using NOVORA.Control;
using System.Security.Cryptography;

static class TransferTests
{
    private static Action<bool, string> _check = null!;
    public static async Task RunAsync(Action<bool, string> check)
    {
        _check = check;
        await CancelWhileOpenPending();
        await FragmentedSourceUsesBoundedChunks();
        await RealLoopbackBothDirections();
        await LeaseFifoAndTerminalOwnership();
        await CorruptHashAndStorageFailureAbort();
        await SenderWaitsForComplete();
        await DuplicateAckDoesNotCreateCredit();
        await DeadlinesAndPauseWatchdog();
        await PendingSafSixtySecondPause();
        await CancelWhileSafBlocked();
        await CancelWhileCommitBlocked();
        await ReceiverEnforcesCreditAndOffsets();
        await PacingExcludedFromDataDeadline();
    }

    private static async Task RealLoopbackBothDirections()
    {
        await using var pair = await TcpPair.CreateAsync();
        var leftStore = new MemoryStorage(); var rightStore = new MemoryStorage();
        var leftWire = new TrackingDuplexStream(pair.Left.GetStream()); var rightWire = new TrackingDuplexStream(pair.Right.GetStream());
        await using var left = new NLControlFileTransferV2(leftWire, leftStore, 7);
        await using var right = new NLControlFileTransferV2(rightWire, rightStore, 7);
        await left.StartAsync(); await right.StartAsync(); await right.StartAsync();
        foreach (int size in new[] { 0, 65535, 65536, 65537, 1048577, 0, 786433 })
        {
            byte[] bytes = Enumerable.Range(0, size).Select(i => (byte)(i * 29 + 7)).ToArray();
            foreach (bool reverse in new[] { false, true })
            {
                Console.WriteLine("RUN loopback size=" + size + " reverse=" + reverse);
                var sender = reverse ? right : left; var store = reverse ? leftStore : rightStore;
                Guid id = Guid.NewGuid();
                NLFileTransferResultV2 result;
                try { result = await sender.SendAsync(id, new("fixture.bin", size, "Documents/test"), new MemoryStream(bytes)).WaitAsync(TimeSpan.FromSeconds(10)); }
                catch { Console.WriteLine($"DIAG closed={left.IsClosed}/{right.IsClosed} queue={left.PeakQueuedBytes}/{right.PeakQueuedBytes} flight={sender.PeakInFlightBytes} target={store.Last?.BytesWritten} published={store.Last?.Committed}"); throw; }
                _check(result.Length == size && result.Sha256.SequenceEqual(SHA256.HashData(bytes)) && store.Published[id].SequenceEqual(bytes), "LoopbackShaBothDirections" + size);
            }
        }
        _check(left.PeakQueuedBytes <= 262144 && right.PeakQueuedBytes <= 262144 && left.PeakInFlightBytes <= 262144 && right.PeakInFlightBytes <= 262144, "QueueAndFlightStayWithinNegotiatedWindow");
        _check(leftWire.PeakConcurrentReads == 1 && rightWire.PeakConcurrentReads == 1, "ExactlyOneReaderPerReusableBidirectionalSession");
    }

    private static async Task LeaseFifoAndTerminalOwnership()
    {
        var clock = new ManualTime();
        using var leases = new NLControlFileTransferLease(4, clock);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var one = await leases.AcquireAsync(a, 4, NLFileTransferDirectionV2.PcToAndroid);
        leases.ConfirmBegin(one);
        Task<NLFileLease> second = leases.AcquireAsync(b, 4, NLFileTransferDirectionV2.AndroidToPc);
        Task<NLFileLease> third = leases.AcquireAsync(c, 4, NLFileTransferDirectionV2.PcToAndroid);
        _check(!second.IsCompleted && !third.IsCompleted && leases.Active == one, "SimultaneousRequestsOneOwner");
        _check(!leases.NotifyTerminal(one, NLFileFrameKindV2.Commit) && !leases.NotifyTerminal(one, NLFileFrameKindV2.Cancel) && !second.IsCompleted, "LeaseHeldAfterCommitAndCancel");
        clock.Advance(TimeSpan.FromSeconds(40));
        _check(leases.IsCurrent(one) && !second.IsCompleted, "ActiveTransferNeverPreempted");
        _check(leases.NotifyTerminal(one, NLFileFrameKindV2.Complete), "LeaseCompleteReleases");
        var two = await second;
        _check(two.TransferId == b && !third.IsCompleted, "PendingLeaseFifo");
        leases.ConfirmBegin(two);
        _check(leases.NotifyTerminal(two, NLFileFrameKindV2.Cancelled), "ConfirmedCancelledReleases");
        var three = await third; leases.ConfirmBegin(three);
        var pending = leases.AcquireAsync(Guid.NewGuid(), 4, NLFileTransferDirectionV2.AndroidToPc);
        leases.CloseGeneration(4);
        await ExpectFailure(pending, typeof(IOException), "PendingRequestFailsOnGenerationClose");
        _check(!leases.IsCurrent(three), "GenerationCloseInvalidatesActive");
        using var expiring = new NLControlFileTransferLease(9, clock);
        var beforeBegin = await expiring.AcquireAsync(Guid.NewGuid(), 9, NLFileTransferDirectionV2.PcToAndroid);
        var next = expiring.AcquireAsync(Guid.NewGuid(), 9, NLFileTransferDirectionV2.AndroidToPc);
        clock.Advance(TimeSpan.FromSeconds(31));
        _check(!expiring.IsCurrent(beforeBegin) && next.IsCompletedSuccessfully, "LeaseExpiresOnlyBeforeBegin");
    }

    private static async Task CorruptHashAndStorageFailureAbort()
    {
        await using var pair = await TcpPair.CreateAsync();
        var store = new MemoryStorage();
        await using var receiver = new NLControlFileTransferV2(pair.Right.GetStream(), store, 2);
        await receiver.StartAsync(); var peer = pair.Left.GetStream();
        Guid id = Guid.NewGuid();
        await Begin(peer, id, 3); _ = await Read(peer);
        await Write(peer, NLFileFrameKindV2.Data, id, 0, new byte[] { 1, 2, 3 });
        _check((await Read(peer)).Offset == 3, "ReceiverAckAfterContiguousWrite");
        await Write(peer, NLFileFrameKindV2.Commit, id, 3, new byte[32]);
        _check((await Read(peer)).Kind == NLFileFrameKindV2.Error && store.Last!.Aborted && store.Published.IsEmpty, "CorruptHashAbortsPartial");
        store.FailWrite = true; Guid next = Guid.NewGuid();
        await Begin(peer, next, 1); _ = await Read(peer);
        await Write(peer, NLFileFrameKindV2.Data, next, 0, new byte[] { 4 });
        _check((await Read(peer)).Kind == NLFileFrameKindV2.Error && store.Last!.Aborted, "DiskFullOrRevokedPermissionAbortsPartial");
    }

    private static async Task SenderWaitsForComplete()
    {
        await using var pair = await TcpPair.CreateAsync();
        var store = new MemoryStorage { BlockCommit = true };
        await using var sender = new NLControlFileTransferV2(pair.Left.GetStream(), null, 2);
        await using var receiver = new NLControlFileTransferV2(pair.Right.GetStream(), store, 2);
        await receiver.StartAsync(); Guid id = Guid.NewGuid();
        var pending = sender.SendAsync(id, new("empty.bin", 0), new MemoryStream());
        await store.CommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _check(!pending.IsCompleted && store.Published.IsEmpty, "LastAckAndCommitAreNotSuccess");
        await ExpectFailure(sender.SendAsync(Guid.NewGuid(), new("next.bin", 0), new MemoryStream()), typeof(InvalidOperationException), "EngineHoldsOwnerUntilComplete");
        store.ReleaseCommit.TrySetResult(true); await pending.WaitAsync(TimeSpan.FromSeconds(5));
        _check(store.Published[id].Length == 0, "EmptyFileRequiresComplete");
    }

    private static async Task DuplicateAckDoesNotCreateCredit()
    {
        await using var pair = await TcpPair.CreateAsync();
        await using var sender = new NLControlFileTransferV2(pair.Left.GetStream(), null, 1);
        Guid id = Guid.NewGuid(); byte[] bytes = new byte[262145];
        Task transfer = sender.SendAsync(id, new("window.bin", bytes.Length), new MemoryStream(bytes));
        var peer = pair.Right.GetStream(); _ = await Read(peer);
        await NLControlFileProtocolV2.WriteAsync(peer, NLControlFileProtocolV2.Ack(id, 0, 262144));
        long received = 0;
        while (received < 262144) received += (await Read(peer)).Payload.Length;
        for (int i = 0; i < 3; i++) await NLControlFileProtocolV2.WriteAsync(peer, NLControlFileProtocolV2.Ack(id, 0, 262144));
        Task<NLFileFrameV2> next = Read(peer);
        await Task.Delay(30);
        _check(!next.IsCompleted && sender.PeakInFlightBytes <= 262144, "DuplicateAckDoesNotAddCredit");
        await NLControlFileProtocolV2.WriteAsync(peer, NLControlFileProtocolV2.Ack(id, 262144, 262144));
        var last = await next.WaitAsync(TimeSpan.FromSeconds(5));
        _check(last.Kind == NLFileFrameKindV2.Data && last.Offset == 262144 && last.Payload.Length == 1, "CreditResumesAtCumulativeOffset");
        await NLControlFileProtocolV2.WriteAsync(peer, NLControlFileProtocolV2.Ack(id, 262145, 262144));
        var commit = await Read(peer);
        await Write(peer, NLFileFrameKindV2.Complete, id, bytes.Length, commit.Payload);
        await transfer.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task DeadlinesAndPauseWatchdog()
    {
        foreach (string stage in new[] { "Begin", "Data", "Complete", "Paused", "WrongId", "ExcessiveOffset", "ExcessiveCredit" })
        {
            await using var pair = await TcpPair.CreateAsync(); var clock = new ManualTime();
            var flushGate = stage is "Begin" or "Data" or "Complete" ? new DeadlineFlushGateStream(pair.Left.GetStream(),
                stage == "Begin" ? NLFileFrameKindV2.Begin : stage == "Data" ? NLFileFrameKindV2.Data : NLFileFrameKindV2.Commit, clock) : null;
            await using var sender = new NLControlFileTransferV2(flushGate ?? (Stream)pair.Left.GetStream(), null, 1, clock: clock);
            Guid id = Guid.NewGuid(); byte[] bytes = stage is "Data" or "Paused" ? new byte[1] : Array.Empty<byte>();
            Task pending = sender.SendAsync(id, new("deadline.bin", bytes.Length), new MemoryStream(bytes));
            var peer = pair.Right.GetStream(); _ = await Read(peer);
            if (stage != "Begin")
            {
                Guid responseId = stage == "WrongId" ? Guid.NewGuid() : id;
                int credit = stage == "Paused" ? 0 : stage == "ExcessiveCredit" ? 1048576 : 262144;
                long offset = stage == "ExcessiveOffset" ? 1 : 0;
                await NLControlFileProtocolV2.WriteAsync(peer, NLControlFileProtocolV2.Ack(responseId, offset, credit));
            }
            if (stage is "Data" or "Complete") _ = await Read(peer);
            if (flushGate is not null)
            {
                await flushGate.FlushEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                _check(clock.HasDeadline(TimeSpan.FromSeconds(30)) &&
                    (stage != "Data" || sender.PeakInFlightBytes == 0), stage + "PeerReceiptPrecedesLocalWriteReturn");
            }
            if (stage is "Begin" or "Data" or "Complete")
            {
                // Peer receipt does not establish local Write/Flush completion. The
                // held write already owns a 30s timer, so wait for a later registration.
                flushGate!.ReleaseFlush.TrySetResult(true);
                await clock.WaitForDeadlineRegisteredAfterAsync(TimeSpan.FromSeconds(30), flushGate.DeadlineRegistration)
                    .WaitAsync(TimeSpan.FromSeconds(5));
                _check(clock.HasDeadlineRegisteredAfter(TimeSpan.FromSeconds(30), flushGate.DeadlineRegistration) &&
                    (stage != "Data" || sender.PeakInFlightBytes == bytes.Length),
                    stage + "ResponseDeadlineExcludesPendingWriteDeadline");
                clock.Advance(TimeSpan.FromSeconds(31));
            }
            if (stage == "Paused") { await WaitUntil(() => clock.HasDeadline(TimeSpan.FromSeconds(6))); clock.Advance(TimeSpan.FromSeconds(7)); }
            await ExpectFailure(pending, stage is "WrongId" or "ExcessiveOffset" or "ExcessiveCredit" ? typeof(InvalidDataException) : typeof(TimeoutException), stage + "FailsClosed");
            _check(sender.IsClosed, stage + "ClosesOnlyFilesChannel");
        }
    }

    private static async Task PendingSafSixtySecondPause()
    {
        await using var pair = await TcpPair.CreateAsync(); var clock = new ManualTime();
        var store = new MemoryStorage { BlockWrites = true };
        await using var sender = new NLControlFileTransferV2(pair.Left.GetStream(), null, 1, clock: clock);
        await using var receiver = new NLControlFileTransferV2(pair.Right.GetStream(), store, 1, clock: clock);
        await receiver.StartAsync(); Guid id = Guid.NewGuid(); byte[] data = new byte[400000];
        var transfer = sender.SendAsync(id, new("saf.bin", data.Length), new MemoryStream(data));
        await store.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int i = 0; i < 30; i++)
        {
            await WaitUntil(() => clock.HasDeadline(TimeSpan.FromSeconds(2)));
            clock.Advance(TimeSpan.FromSeconds(2));
            await Task.Delay(10);
            _check(!transfer.IsCompleted, "SafPauseKeepsPendingDataAlive" + (i + 1));
        }
        _check(receiver.PeakQueuedBytes <= 262144 && sender.PeakInFlightBytes <= 262144, "SafPauseMemoryBounded");
        store.BlockWrites = false; store.ReleaseWrite.TrySetResult(true);
        await transfer.WaitAsync(TimeSpan.FromSeconds(5));
        _check(store.Published[id].SequenceEqual(data), "SafSixtySecondResumeIntegrity");
    }

    private static async Task CancelWhileSafBlocked()
    {
        await using var pair = await TcpPair.CreateAsync(); var store = new MemoryStorage { BlockWrites = true };
        await using var sender = new NLControlFileTransferV2(pair.Left.GetStream(), null, 3);
        await using var receiver = new NLControlFileTransferV2(pair.Right.GetStream(), store, 3);
        await receiver.StartAsync(); using var cancel = new CancellationTokenSource();
        var transfer = sender.SendAsync(Guid.NewGuid(), new("cancel.bin", 400000), new MemoryStream(new byte[400000]), ct: cancel.Token);
        await store.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel();
        await ExpectFailure(transfer, typeof(OperationCanceledException), "CancelBypassesBlockedSafAndDataCredit");
        _check(store.Last!.Aborted && store.Published.IsEmpty && !sender.IsClosed, "CancelledConfirmsPartialCleanup");
        store.BlockWrites = false;
        await sender.SendAsync(Guid.NewGuid(), new("after.bin", 1), new MemoryStream(new byte[] { 8 })).WaitAsync(TimeSpan.FromSeconds(5));
        _check(store.Published.Count == 1, "ChannelReusableAfterConfirmedCancelled");
    }

    private static async Task CancelWhileCommitBlocked()
    {
        await using var pair = await TcpPair.CreateAsync(); var store = new MemoryStorage { BlockCommit = true };
        await using var sender = new NLControlFileTransferV2(pair.Left.GetStream(), null, 3);
        await using var receiver = new NLControlFileTransferV2(pair.Right.GetStream(), store, 3);
        await receiver.StartAsync(); using var cancel = new CancellationTokenSource();
        var transfer = sender.SendAsync(Guid.NewGuid(), new("commit.bin", 0), new MemoryStream(), ct: cancel.Token);
        await store.CommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel();
        await ExpectFailure(transfer, typeof(OperationCanceledException), "CancelDuringCommit");
        store.ReleaseCommit.TrySetResult(true); await Task.Delay(10);
        _check(store.Published.IsEmpty && store.Last!.Aborted, "LateCommitCannotPublishAfterCancellation");
    }

    private static async Task CancelWhileOpenPending()
    {
        await using var pair = await TcpPair.CreateAsync(); var store = new MemoryStorage { BlockOpen = true };
        await using var sender = new NLControlFileTransferV2(pair.Left.GetStream(), null, 6);
        await using var receiver = new NLControlFileTransferV2(pair.Right.GetStream(), store, 6);
        await receiver.StartAsync(); using var cancel = new CancellationTokenSource();
        var transfer = sender.SendAsync(Guid.NewGuid(), new("open.bin", 0), new MemoryStream(), ct: cancel.Token);
        await store.OpenEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel(); await Task.Delay(30);
        _check(!transfer.IsCompleted && !store.Last!.Aborted, "CancelledMustWaitForPendingOpenCleanup");
        store.ReleaseOpen.TrySetResult(true);
        await ExpectFailure(transfer, typeof(OperationCanceledException), "CancelledAfterLateOpenAborted");
        _check(store.Last!.Aborted && store.Published.IsEmpty, "LateOpenNeverLeavesPartialOnConfirmedCancelled");
    }

    private static async Task FragmentedSourceUsesBoundedChunks()
    {
        await using var pair = await TcpPair.CreateAsync(); var store = new MemoryStorage { BlockWrites = true };
        await using var sender = new NLControlFileTransferV2(pair.Left.GetStream(), null, 3);
        await using var receiver = new NLControlFileTransferV2(pair.Right.GetStream(), store, 3);
        await receiver.StartAsync(); byte[] data = new byte[65537]; Guid id = Guid.NewGuid();
        Task send = sender.SendAsync(id, new("short-read.bin", data.Length), new FragmentedReadStream(new MemoryStream(data)));
        await store.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); await Task.Delay(30);
        store.BlockWrites = false; store.ReleaseWrite.TrySetResult(true);
        await send.WaitAsync(TimeSpan.FromSeconds(5));
        _check(store.Published[id].SequenceEqual(data) && receiver.PeakQueuedBytes <= 262144, "ShortSourceReadsCoalesceIntoBoundedDataChunks");
    }

    private static async Task ReceiverEnforcesCreditAndOffsets()
    {
        foreach (bool wrongOffset in new[] { false, true })
        {
            await using var pair = await TcpPair.CreateAsync(); var store = new MemoryStorage { BlockWrites = true };
            await using var receiver = new NLControlFileTransferV2(pair.Right.GetStream(), store, 1, windowBytes: 65536);
            await receiver.StartAsync(); var peer = pair.Left.GetStream(); Guid id = Guid.NewGuid();
            await Begin(peer, id, 65537); _ = await Read(peer);
            await Write(peer, NLFileFrameKindV2.Data, id, 0, new byte[65536]);
            await store.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Write(peer, NLFileFrameKindV2.Data, id, wrongOffset ? 0 : 65536, new byte[1]);
            await ExpectFailure(receiver.Completion, typeof(InvalidDataException), wrongOffset ? "ReceiverRejectsReorderedOffset" : "ReceiverRejectsBeyondGrantedWindow");
            await WaitUntil(() => store.Last!.Aborted);
            _check(receiver.PeakQueuedBytes <= 65536 && store.Published.IsEmpty, "CreditViolationCleansWithoutPublication");
        }
    }

    private static async Task PacingExcludedFromDataDeadline()
    {
        await using var pair = await TcpPair.CreateAsync(); var clock = new ManualTime();
        var budgetEntered = MemoryStorage.NewSignal(); var release = MemoryStorage.NewSignal(); var store = new MemoryStorage();
        int waits = 0;
        async ValueTask Pacer(int bytes, CancellationToken ct)
        { if (Interlocked.Increment(ref waits) == 1) { budgetEntered.TrySetResult(true); await release.Task.WaitAsync(ct); } }
        await using var sender = new NLControlFileTransferV2(pair.Left.GetStream(), null, 2, clock: clock, pacer: Pacer);
        await using var receiver = new NLControlFileTransferV2(pair.Right.GetStream(), store, 2, clock: clock);
        await receiver.StartAsync(); Task transfer = sender.SendAsync(Guid.NewGuid(), new("budget.bin", 1), new MemoryStream(new byte[] { 1 }));
        await budgetEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); clock.Advance(TimeSpan.FromSeconds(35));
        _check(!transfer.IsCompleted && store.Opens == 1 && store.Last!.BytesWritten == 0, "EssentialBeginBypassesBulkBudgetAndDataWaitExcludesDeadline");
        release.TrySetResult(true); await transfer.WaitAsync(TimeSpan.FromSeconds(5));
        _check(store.Published.Count == 1 && waits == 1, "BudgetWaitThirtyFiveSecondsStillCommitsWithEssentialCommitUnpaced");
    }

    private static async Task ExpectFailure(Task task, Type type, string name)
    {
        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5))) != task) throw new Exception("FAIL task did not finish: " + name);
        try { await task; }
        catch (Exception ex) when (type.IsAssignableFrom(ex.GetType()) || type.IsAssignableFrom(ex.GetBaseException().GetType())) { _check(true, name); return; }
        throw new Exception("FAIL expected " + type.Name + ": " + name);
    }
    private static async Task WaitUntil(Func<bool> condition)
    { for (int i = 0; i < 1000; i++) { if (condition()) return; await Task.Delay(1); } throw new Exception("Condition did not become true."); }
    private static Task Begin(Stream stream, Guid id, long length) => Write(stream, NLFileFrameKindV2.Begin, id, 0, NLControlFileProtocolV2.EncodeMetadata(new("fixture.bin", length)));
    private static async Task Write(Stream stream, NLFileFrameKindV2 kind, Guid id, long offset, byte[] data) => await NLControlFileProtocolV2.WriteAsync(stream, new(kind, id, offset, data));
    private static async Task<NLFileFrameV2> Read(Stream stream) => await NLControlFileProtocolV2.ReadAsync(stream) ?? throw new EndOfStreamException();
}

// Bytes are delivered to the peer while local Flush remains pending, as real
// asynchronous transports can do. This keeps the write-deadline window deterministic.
sealed class DeadlineFlushGateStream(Stream inner, NLFileFrameKindV2 heldKind, ManualTime clock) : Stream
{
    private NLFileFrameKindV2 _frameKind;
    private int _payloadRemaining;
    private bool _held;
    public readonly TaskCompletionSource<bool> FlushEntered = MemoryStorage.NewSignal();
    public readonly TaskCompletionSource<bool> ReleaseFlush = MemoryStorage.NewSignal();
    public long DeadlineRegistration { get; private set; }
    public override ValueTask<int> ReadAsync(Memory<byte> bytes, CancellationToken ct = default) => inner.ReadAsync(bytes, ct);
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
    {
        if (_payloadRemaining == 0)
        {
            if (bytes.Length != NLControlFileProtocolV2.HeaderBytes) throw new InvalidDataException("Fixture expected a complete header.");
            _frameKind = (NLFileFrameKindV2)bytes.Span[4];
            _payloadRemaining = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.Span[8..]));
        }
        else _payloadRemaining -= bytes.Length;
        await inner.WriteAsync(bytes, ct);
    }
    public override async Task FlushAsync(CancellationToken ct)
    {
        await inner.FlushAsync(ct);
        if (_frameKind == heldKind && !_held)
        {
            _held = true; DeadlineRegistration = clock.TimerRegistration; FlushEntered.TrySetResult(true);
            await ReleaseFlush.Task.WaitAsync(ct);
        }
    }
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
