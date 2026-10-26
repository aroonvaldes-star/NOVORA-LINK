using NOVORA.Control;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading.Channels;

static class CancelSealRaceTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static readonly byte[] Bytes = [2, 7, 19, 31, 43, 61, 89];

    public static async Task RunAsync(Action<bool, string> check)
    {
        await CancelDuringSealAsync(SealBoundary.AfterSeal, check);
        await CancelDuringSealAsync(SealBoundary.BeforeSeal, check);
        await CancelBeforeClaimAsync(check);
        await CloseDuringSealAsync(check);
        await FailedSealAsync(check);
        await StalledSealHasCleanupDeadlineAsync(check);
    }

    private static async Task CancelDuringSealAsync(SealBoundary boundary, Action<bool, string> check)
    {
        var (peer, wire) = CancelSealDuplex.CreatePair();
        var observed = new CancelReadBoundary(wire);
        var storage = new CancelSealStorage(boundary);
        await using var receiver = new NLControlFileTransferV2(observed, storage, 1);
        try
        {
            await receiver.StartAsync();
            Guid id = await CommitFromPeerAsync(peer);
            await storage.BoundaryReached.Task.WaitAsync(Deadline);
            await NLControlFileProtocolV2.WriteAsync(peer, new(NLFileFrameKindV2.Cancel, id, 0, []));
            // The next read starts only after the real receiver handled Cancel.
            await Task.WhenAny(observed.CancelHandled.Task, receiver.Completion).WaitAsync(Deadline);
            storage.ReleaseBoundary.TrySetResult(true);
            NLFileFrameV2? terminal = await ReadTerminalAsync(peer);
            bool retained = storage.Published.TryGetValue(id, out var bytes) && bytes.SequenceEqual(Bytes);
            check(terminal?.Kind == NLFileFrameKindV2.Complete && retained,
                $"Cancel{boundary}AfterCommitReturnsCompleteWithContent(kind={terminal?.Kind},retained={retained})");
        }
        finally { storage.ReleaseBoundary.TrySetResult(true); peer.Dispose(); }
    }

    private static async Task CancelBeforeClaimAsync(Action<bool, string> check)
    {
        var (peer, wire) = CancelSealDuplex.CreatePair();
        var storage = new CancelSealStorage(SealBoundary.CommitReturn);
        await using var receiver = new NLControlFileTransferV2(wire, storage, 1);
        try
        {
            await receiver.StartAsync();
            Guid id = await CommitFromPeerAsync(peer);
            await storage.BoundaryReached.Task.WaitAsync(Deadline);
            await NLControlFileProtocolV2.WriteAsync(peer, new(NLFileFrameKindV2.Cancel, id, 0, []));
            NLFileFrameV2? terminal = await ReadTerminalAsync(peer);
            check(terminal?.Kind == NLFileFrameKindV2.Cancelled && !storage.Published.ContainsKey(id) && storage.PartialLength == 0,
                "CancelBeforeCommitReturnConfirmsCleanup");
            storage.ReleaseBoundary.TrySetResult(true);
            Guid next = await CommitFromPeerAsync(peer);
            terminal = await ReadTerminalAsync(peer);
            check(terminal?.TransferId == next && terminal.Kind == NLFileFrameKindV2.Complete &&
                storage.Published.TryGetValue(next, out var bytes) && bytes.SequenceEqual(Bytes),
                "CancelledCommitReturnReleasesReceiverForNextTransfer");
        }
        finally { storage.ReleaseBoundary.TrySetResult(true); peer.Dispose(); }
    }

    private static async Task CloseDuringSealAsync(Action<bool, string> check)
    {
        var (peer, wire) = CancelSealDuplex.CreatePair();
        var storage = new CancelSealStorage(SealBoundary.BeforeSeal);
        var receiver = new NLControlFileTransferV2(wire, storage, 1);
        try
        {
            await receiver.StartAsync();
            Guid id = await CommitFromPeerAsync(peer);
            await storage.BoundaryReached.Task.WaitAsync(Deadline);
            Task close = receiver.DisposeAsync().AsTask();
            storage.ReleaseBoundary.TrySetResult(true);
            await close.WaitAsync(Deadline);
            check(storage.Published.TryGetValue(id, out var bytes) && bytes.SequenceEqual(Bytes),
                "CloseAfterCompletionClaimWaitsForSealAndPreservesContent");
        }
        finally
        {
            storage.ReleaseBoundary.TrySetResult(true); peer.Dispose();
            await receiver.DisposeAsync().AsTask().WaitAsync(Deadline);
        }
    }

    private static async Task FailedSealAsync(Action<bool, string> check)
    {
        var (peer, wire) = CancelSealDuplex.CreatePair();
        var storage = new CancelSealStorage(SealBoundary.FailSeal);
        var receiver = new NLControlFileTransferV2(wire, storage, 1);
        try
        {
            await receiver.StartAsync();
            Guid id = await CommitFromPeerAsync(peer);
            _ = await ReadTerminalAsync(peer);
            bool closed = receiver.IsClosed;
            await receiver.DisposeAsync().AsTask().WaitAsync(Deadline);
            check(closed && !storage.Published.ContainsKey(id) && storage.PartialLength == 0,
                "FailedSealAfterClaimClosesChannelAndCleansReceiver");
        }
        finally { peer.Dispose(); await receiver.DisposeAsync().AsTask().WaitAsync(Deadline); }
    }

    private static async Task<Guid> CommitFromPeerAsync(Stream peer)
    {
        Guid id = Guid.NewGuid();
        await NLControlFileProtocolV2.WriteAsync(peer, new(NLFileFrameKindV2.Begin, id, 0,
            NLControlFileProtocolV2.EncodeMetadata(new("cancel-seal.bin", Bytes.Length))));
        await RequireAckAsync(peer, id, 0);
        await NLControlFileProtocolV2.WriteAsync(peer, new(NLFileFrameKindV2.Data, id, 0, Bytes));
        await RequireAckAsync(peer, id, Bytes.Length);
        await NLControlFileProtocolV2.WriteAsync(peer, new(NLFileFrameKindV2.Commit, id, Bytes.Length, SHA256.HashData(Bytes)));
        return id;
    }

    private static async Task StalledSealHasCleanupDeadlineAsync(Action<bool, string> check)
    {
        var (peer, wire) = CancelSealDuplex.CreatePair();
        var storage = new CancelSealStorage(SealBoundary.AfterSeal);
        var clock = new ManualTime();
        var receiver = new NLControlFileTransferV2(wire, storage, 1, clock: clock);
        try
        {
            await receiver.StartAsync();
            Guid id = await CommitFromPeerAsync(peer);
            await storage.BoundaryReached.Task.WaitAsync(Deadline);
            Task close = receiver.DisposeAsync().AsTask();
            bool deadlineCreated = clock.HasDeadline(TimeSpan.FromSeconds(30));
            if (deadlineCreated) clock.Advance(TimeSpan.FromSeconds(31));
            bool cleanupFailed = false;
            try { await close.WaitAsync(Deadline); }
            catch (IOException) { cleanupFailed = true; }
            check(deadlineCreated && cleanupFailed && receiver.CleanupFailure is not null &&
                storage.Published.TryGetValue(id, out var bytes) && bytes.SequenceEqual(Bytes),
                "StalledSealHasThirtySecondCleanupDeadlineWithoutFalseSuccess");
        }
        finally
        {
            storage.ReleaseBoundary.TrySetResult(true); peer.Dispose();
            try { await receiver.DisposeAsync().AsTask().WaitAsync(Deadline); } catch (IOException) { }
        }
    }

    private static async Task RequireAckAsync(Stream peer, Guid id, long offset)
    {
        var ack = await NLControlFileProtocolV2.ReadAsync(peer).AsTask().WaitAsync(Deadline);
        if (ack?.Kind != NLFileFrameKindV2.Ack || ack.TransferId != id || ack.Offset != offset)
            throw new InvalidDataException("Fixture expected the real receiver's contiguous acknowledgement.");
    }

    private static async Task<NLFileFrameV2?> ReadTerminalAsync(Stream peer)
    {
        while (true)
        {
            var frame = await NLControlFileProtocolV2.ReadAsync(peer).AsTask().WaitAsync(Deadline);
            if (frame?.Kind != NLFileFrameKindV2.Ack) return frame;
        }
    }
}

enum SealBoundary { AfterSeal, BeforeSeal, CommitReturn, FailSeal }

// A real storage boundary with publication, rollback and sealing semantics.
// Only its asynchronous return points are held; protocol/session decisions are production code.
sealed class CancelSealStorage(SealBoundary boundary) : INLFileReceiveStorageV2
{
    private readonly SealBoundary _boundary = boundary;
    public readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte[]> Published = new();
    public readonly TaskCompletionSource<bool> BoundaryReached = Signal();
    public readonly TaskCompletionSource<bool> ReleaseBoundary = Signal();
    private Target? _last;
    private int _opened;
    public long PartialLength => _last?.PartialLength ?? 0;
    public ValueTask<INLFileReceiveTargetV2> OpenAsync(Guid id, long generation, NLFileMetadataV2 metadata, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _last = new Target(this, id, Interlocked.Increment(ref _opened) == 1);
        return ValueTask.FromResult<INLFileReceiveTargetV2>(_last);
    }
    internal static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class Target(CancelSealStorage owner, Guid id, bool first) : INLFileReceiveTargetV2
    {
        private readonly object _gate = new();
        private readonly MemoryStream _partial = new();
        private bool _sealed, _aborted;
        private int _disposals;
        public long PartialLength { get { lock (_gate) return _partial.Length; } }
        public ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_sealed || _aborted || offset != _partial.Length) throw new IOException("Fixture write invalid.");
                _partial.Write(bytes.Span);
            }
            return ValueTask.CompletedTask;
        }
        public async ValueTask<string?> CommitAsync(long length, ReadOnlyMemory<byte> digest, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                byte[] bytes = _partial.ToArray();
                if (_sealed || _aborted || bytes.LongLength != length || !SHA256.HashData(bytes).AsSpan().SequenceEqual(digest.Span))
                    throw new IOException("Fixture commit invalid.");
                owner.Published[id] = bytes;
            }
            if (first && owner._boundary == SealBoundary.CommitReturn)
            {
                owner.BoundaryReached.TrySetResult(true);
                await owner.ReleaseBoundary.Task; // Provider can return after cancellation, without republishing.
            }
            return "fixture-target";
        }
        public ValueTask AbortAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_sealed) return ValueTask.CompletedTask;
                _aborted = true; _partial.SetLength(0); owner.Published.TryRemove(id, out _);
            }
            return ValueTask.CompletedTask;
        }
        public async ValueTask DisposeAsync()
        {
            bool gate = first && Interlocked.Increment(ref _disposals) == 1;
            if (gate && owner._boundary == SealBoundary.FailSeal) throw new IOException("Injected seal failure.");
            if (gate && owner._boundary == SealBoundary.BeforeSeal)
            {
                owner.BoundaryReached.TrySetResult(true); await owner.ReleaseBoundary.Task;
            }
            lock (_gate) _sealed = true;
            if (gate && owner._boundary == SealBoundary.AfterSeal)
            {
                owner.BoundaryReached.TrySetResult(true); await owner.ReleaseBoundary.Task;
            }
        }
    }
}

sealed class CancelReadBoundary(Stream inner) : Stream
{
    private bool _cancelRead;
    private readonly byte[] _header = new byte[NLControlFileProtocolV2.HeaderBytes];
    private int _headerRead, _payloadRemaining;
    public readonly TaskCompletionSource<bool> CancelHandled = CancelSealStorage.Signal();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_cancelRead) CancelHandled.TrySetResult(true);
        int count = await inner.ReadAsync(buffer, ct);
        if (_payloadRemaining != 0) _payloadRemaining -= count;
        else if (count != 0)
        {
            buffer.Span[..count].CopyTo(_header.AsSpan(_headerRead));
            _headerRead += count;
            if (_headerRead == _header.Length)
            {
                _payloadRemaining = checked((int)BinaryPrimitives.ReadUInt32BigEndian(_header.AsSpan(8)));
                _cancelRead = _header[4] == (byte)NLFileFrameKindV2.Cancel;
                _headerRead = 0;
            }
        }
        return count;
    }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct = default) => inner.WriteAsync(bytes, ct);
    public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
    public override bool CanRead => true; public override bool CanWrite => true; public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
    public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    public override long Seek(long o, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}

sealed class CancelSealDuplex : Stream
{
    private readonly Channel<byte[]> _incoming, _outgoing;
    private byte[]? _current;
    private int _position;
    private CancelSealDuplex(Channel<byte[]> incoming, Channel<byte[]> outgoing) { _incoming = incoming; _outgoing = outgoing; }
    public static (CancelSealDuplex Left, CancelSealDuplex Right) CreatePair()
    {
        var left = Channel.CreateUnbounded<byte[]>(); var right = Channel.CreateUnbounded<byte[]>();
        return (new(left, right), new(right, left));
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_current is null || _position == _current.Length)
        {
            try { _current = await _incoming.Reader.ReadAsync(ct); } catch (ChannelClosedException) { return 0; }
            _position = 0;
        }
        int count = Math.Min(buffer.Length, _current.Length - _position);
        _current.AsMemory(_position, count).CopyTo(buffer); _position += count; return count;
    }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_outgoing.Writer.TryWrite(bytes.ToArray())) throw new IOException("Fixture transport closed.");
        return ValueTask.CompletedTask;
    }
    public override Task FlushAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public override bool CanRead => true; public override bool CanWrite => true; public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
    public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    public override long Seek(long o, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _incoming.Writer.TryComplete(); _outgoing.Writer.TryComplete(); }
        base.Dispose(disposing);
    }
}
