using NOVORA.Control;

static class DeadlineTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        await BlockedNetworkWriteHasDeadline(check);
        await BulkBudgetPauseHasBoundedGracefulCancellation(check);
        await CancelledReplyHasIndependentDeadline(check);
        await LivePauseStillHasMaximumDuration(check);
        await CompleteMustMatchHashAndLength(check);
        await ChangedSourcesAndLateAckFailClosed(check);
    }
    private static async Task BlockedNetworkWriteHasDeadline(Action<bool, string> check)
    {
        var clock = new ManualTime(); var stream = new BlockingWriteStream();
        await using var sender = new NLControlFileTransferV2(stream, null, 3, clock: clock);
        Task send = sender.SendAsync(Guid.NewGuid(), new("blocked.bin", 0), new MemoryStream());
        await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntil(() => clock.HasDeadline(TimeSpan.FromSeconds(30)));
        clock.Advance(TimeSpan.FromSeconds(31));
        await MustFail(send, typeof(TimeoutException), check, "NetworkWriteDeadlineClosesUncertainFraming");
        check(sender.IsClosed, "UserCancelNeverWaitsForeverForBlockedNetworkWrite");
    }
    private static async Task CancelledReplyHasIndependentDeadline(Action<bool, string> check)
    {
        await using var pair = await TcpPair.CreateAsync(); var clock = new ManualTime();
        await using var sender = new NLControlFileTransferV2(pair.Left.GetStream(), null, 3, clock: clock);
        using var cancel = new CancellationTokenSource(); Guid id = Guid.NewGuid();
        Task send = sender.SendAsync(id, new("cancelled.bin", 1), new MemoryStream(new byte[1]), ct: cancel.Token);
        var peer = pair.Right.GetStream(); await Read(peer);
        await NLControlFileProtocolV2.WriteAsync(peer, NLControlFileProtocolV2.Ack(id, 0, 0));
        await WaitUntil(() => clock.HasDeadline(TimeSpan.FromSeconds(6)));
        cancel.Cancel(); var request = await Read(peer);
        check(request.Kind == NLFileFrameKindV2.Cancel, "CancelBypassesZeroDataCredit");
        await WaitUntil(() => clock.HasDeadline(TimeSpan.FromSeconds(30)));
        clock.Advance(TimeSpan.FromSeconds(31));
        await MustFail(send, typeof(OperationCanceledException), check, "CancelledDeadlineEndsUserCancellation");
        await MustFail(sender.Completion, typeof(TimeoutException), check, "MissingCancelledClosesFilesAfterThirtySeconds");
    }
    private static async Task BulkBudgetPauseHasBoundedGracefulCancellation(Action<bool, string> check)
    {
        await using var pair = await TcpPair.CreateAsync(); var clock = new ManualTime(); var store = new MemoryStorage();
        var entered = MemoryStorage.NewSignal(); int calls = 0;
        async ValueTask Pacer(int bytes, CancellationToken ct)
        { if (Interlocked.Increment(ref calls) == 1) { entered.TrySetResult(true); await Task.Delay(Timeout.Infinite, ct); } }
        await using var sender = new NLControlFileTransferV2(pair.Left.GetStream(), null, 1, clock: clock, pacer: Pacer);
        await using var receiver = new NLControlFileTransferV2(pair.Right.GetStream(), store, 1, clock: clock);
        await receiver.StartAsync(); Task send = sender.SendAsync(Guid.NewGuid(), new("budget-pause.bin", 1), new MemoryStream(new byte[1]));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntil(() => clock.HasDeadline(TimeSpan.FromSeconds(120)));
        clock.Advance(TimeSpan.FromSeconds(121));
        await MustFail(send, typeof(TimeoutException), check, "BulkBudgetPauseCancelsGracefullyAfterOneHundredTwentySeconds");
        check(store.Last!.Aborted && !sender.IsClosed && !receiver.IsClosed, "BudgetTimeoutUsesCancelledCleanupAndRetainsReusableChannel");
        await sender.SendAsync(Guid.NewGuid(), new("after-budget.bin", 1), new MemoryStream(new byte[] { 4 })).WaitAsync(TimeSpan.FromSeconds(5));
        check(store.Published.Count == 1, "ConfirmedBudgetCancellationReleasesForNextTransfer");
    }
    private static async Task LivePauseStillHasMaximumDuration(Action<bool, string> check)
    {
        await using var pair = await TcpPair.CreateAsync(); var clock = new ManualTime();
        await using var sender = new NLControlFileTransferV2(pair.Left.GetStream(), null, 3, clock: clock);
        Guid id = Guid.NewGuid(); Task send = sender.SendAsync(id, new("paused.bin", 1), new MemoryStream(new byte[1]));
        var peer = pair.Right.GetStream(); await Read(peer);
        await NLControlFileProtocolV2.WriteAsync(peer, NLControlFileProtocolV2.Ack(id, 0, 0));
        for (int tick = 0; tick < 59; tick++)
        {
            TimeSpan remaining = TimeSpan.FromSeconds(Math.Min(6, 120 - clock.GetTimestamp() / TimeSpan.TicksPerSecond));
            await WaitUntil(() => clock.HasDeadline(remaining) || send.IsCompleted);
            if (send.IsCompleted) throw new Exception("Live pause failed before 120 seconds.");
            clock.Advance(TimeSpan.FromSeconds(2));
            await NLControlFileProtocolV2.WriteAsync(peer, NLControlFileProtocolV2.Ack(id, 0, 0));
            await Task.Delay(3);
        }
        check(!send.IsCompleted, "LivePauseSuspendsDataDeadlineWhileHeartbeatArrives");
        clock.Advance(TimeSpan.FromSeconds(3));
        await MustFail(send, typeof(TimeoutException), check, "LivePauseMaximumOneHundredTwentySeconds");
    }
    private static async Task CompleteMustMatchHashAndLength(Action<bool, string> check)
    {
        foreach (bool lengthWrong in new[] { false, true })
        {
            await using var pair = await TcpPair.CreateAsync();
            await using var sender = new NLControlFileTransferV2(pair.Left.GetStream(), null, 3);
            Guid id = Guid.NewGuid(); Task send = sender.SendAsync(id, new("complete.bin", 0), new MemoryStream());
            var peer = pair.Right.GetStream(); await Read(peer);
            await NLControlFileProtocolV2.WriteAsync(peer, NLControlFileProtocolV2.Ack(id, 0, 65536));
            var commit = await Read(peer);
            await NLControlFileProtocolV2.WriteAsync(peer, new(NLFileFrameKindV2.Complete, id, lengthWrong ? 1 : 0, lengthWrong ? commit.Payload : new byte[32]));
            await MustFail(send, typeof(InvalidDataException), check, lengthWrong ? "CompleteLengthMismatchFailsClosed" : "CompleteHashMismatchFailsClosed");
        }
    }
    private static async Task ChangedSourcesAndLateAckFailClosed(Action<bool, string> check)
    {
        foreach (bool longer in new[] { false, true })
        {
            await using var pair = await TcpPair.CreateAsync(); var store = new MemoryStorage();
            await using var receiver = new NLControlFileTransferV2(pair.Right.GetStream(), store, 1);
            await using var sender = new NLControlFileTransferV2(pair.Left.GetStream(), null, 1);
            await receiver.StartAsync();
            await MustFail(sender.SendAsync(Guid.NewGuid(), new("source.bin", longer ? 0 : 1), new MemoryStream(longer ? new byte[1] : Array.Empty<byte>())),
                typeof(InvalidDataException), check, longer ? "SourceGrowingAfterMetadataFailsClosed" : "SourceShrinkingAfterMetadataFailsClosed");
            await WaitUntil(() => store.Last?.Aborted == true);
            check(store.Published.IsEmpty, "SourceChangeLeavesNoPublishedResult");
        }
        await using var latePair = await TcpPair.CreateAsync();
        await using var endpoint = new NLControlFileTransferV2(latePair.Left.GetStream(), new MemoryStorage(), 1);
        await endpoint.StartAsync();
        await NLControlFileProtocolV2.WriteAsync(latePair.Right.GetStream(), NLControlFileProtocolV2.Ack(Guid.NewGuid(), 0, 65536));
        await MustFail(endpoint.Completion, typeof(InvalidDataException), check, "LateAckCannotStartNewTransfer");
    }
    private static async Task MustFail(Task task, Type expected, Action<bool, string> check, string name)
    {
        if (await Task.WhenAny(task, Task.Delay(5000)) != task) throw new Exception("FAIL never completed: " + name);
        try { await task; } catch (Exception ex) when (expected.IsAssignableFrom(ex.GetType()) || expected.IsAssignableFrom(ex.GetBaseException().GetType())) { check(true, name); return; }
        throw new Exception("FAIL expected " + expected.Name + ": " + name);
    }
    private static async Task WaitUntil(Func<bool> condition)
    { for (int i = 0; i < 1000; i++) { if (condition()) return; await Task.Delay(1); } throw new Exception("FAIL expected protocol deadline/state was not installed."); }
    private static async Task<NLFileFrameV2> Read(Stream stream) => await NLControlFileProtocolV2.ReadAsync(stream) ?? throw new EndOfStreamException();
    private sealed class BlockingWriteStream : Stream
    {
        internal readonly TaskCompletionSource<bool> Entered = MemoryStorage.NewSignal();
        public override bool CanRead => true; public override bool CanWrite => true; public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { await Task.Delay(Timeout.Infinite, ct); return 0; }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) { Entered.TrySetResult(true); await Task.Delay(Timeout.Infinite, ct); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException(); public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
    }
}
