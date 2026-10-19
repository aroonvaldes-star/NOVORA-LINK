using NOVORA.Control;
using System.Security.Cryptography;

static class CompletionRaceTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        await using var pair = await TcpPair.CreateAsync();
        var store = new MemoryStorage();
        var completeGate = new CompletePayloadGateStream(pair.Right.GetStream());
        await using var sender = new NLControlFileTransferV2(pair.Left.GetStream(), null, generation: 1);
        await using var receiver = new NLControlFileTransferV2(completeGate, store, generation: 1);
        byte[] bytes = Enumerable.Range(0, 70_017).Select(index => (byte)(index * 29 + 11)).ToArray();
        byte[] digest = SHA256.HashData(bytes);
        Guid id = Guid.NewGuid();
        try
        {
            await receiver.StartAsync();
            using var source = new MemoryStream(bytes, writable: false);
            var result = await sender.SendAsync(id, new("completion-race.bin", bytes.Length), source)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await completeGate.PayloadDelivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(result.TransferId == id && result.Length == bytes.Length && result.Sha256.SequenceEqual(digest) &&
                store.Published.TryGetValue(id, out var before) && before.SequenceEqual(bytes),
                "SenderConfirmsCompleteWhileReceiverWriteRemainsPending");

            // Complete is already validated by the real peer. Closing the receiver
            // before its local write continuation runs must not undo those bytes.
            await receiver.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            check(store.Published.TryGetValue(id, out var after) && after.SequenceEqual(bytes) &&
                SHA256.HashData(after).SequenceEqual(digest), "CloseAfterVisibleCompletePreservesPublishedContent");
        }
        finally { completeGate.ReleaseWrite.TrySetResult(true); }
    }
}

sealed class CompletePayloadGateStream(Stream inner) : Stream
{
    private bool _completePayload;
    public readonly TaskCompletionSource<bool> PayloadDelivered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource<bool> ReleaseWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        bool gateThisWrite = _completePayload;
        _completePayload = buffer.Length == NLControlFileProtocolV2.HeaderBytes &&
            buffer.Span[0] == 'N' && buffer.Span[1] == 'L' && buffer.Span[2] == 'F' && buffer.Span[3] == '2' &&
            buffer.Span[4] == (byte)NLFileFrameKindV2.Complete;
        await inner.WriteAsync(buffer, ct);
        if (gateThisWrite)
        {
            PayloadDelivered.TrySetResult(true);
            await ReleaseWrite.Task;
        }
    }
    public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
