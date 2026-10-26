using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NOVORA.Control;

public sealed record NLFileTransferResultV2(Guid TransferId, long Length, byte[] Sha256, string? Destination = null);

/// <summary>The provider confines metadata to the selected local root/SAF tree; remote metadata is never an absolute path.</summary>
public interface INLFileReceiveStorageV2
{
    ValueTask<INLFileReceiveTargetV2> OpenAsync(Guid transferId, long generation, NLFileMetadataV2 metadata, CancellationToken ct);
}

/// <summary>A temporary target bound to the supplied transfer id/generation. Abort must invalidate publication even
/// while a Write/Commit is pending. Commit must check cancellation immediately before atomic publication and must
/// reject providers without a reliable publish operation. Abort may run concurrently with a cancelled storage call.
/// Disposal after a successful Commit seals publication; later Abort must not remove that final result.</summary>
public interface INLFileReceiveTargetV2 : IAsyncDisposable
{
    ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> data, CancellationToken ct);
    ValueTask<string?> CommitAsync(long length, ReadOnlyMemory<byte> sha256, CancellationToken ct);
    ValueTask AbortAsync(CancellationToken ct);
}

/// <summary>A reusable, bidirectional FILES v2 session with exactly one network reader. The caller authenticates
/// hello and negotiates wire2 before construction, and grants a FIFO lease before SendAsync. Owns the duplex stream.</summary>
public sealed class NLControlFileTransferV2 : IAsyncDisposable
{
    private sealed class BudgetPauseTimeoutException(Exception inner)
        : TimeoutException("FILES bulk budget pause exceeded 120 seconds.", inner);
    private sealed class Sender(Guid id, long length)
    {
        public readonly Guid Id = id;
        public readonly long Length = length;
        public readonly Channel<NLFileFrameV2> Replies = Channel.CreateBounded<NLFileFrameV2>(new BoundedChannelOptions(64)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    }
    private sealed class Receiver(Guid id, NLFileMetadataV2 metadata, CancellationToken sessionToken)
    {
        public readonly Guid Id = id;
        public readonly NLFileMetadataV2 Metadata = metadata;
        public readonly CancellationTokenSource Stop = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
        public readonly Channel<NLFileFrameV2> Work = Channel.CreateBounded<NLFileFrameV2>(new BoundedChannelOptions(32)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        public INLFileReceiveTargetV2? Target;
        public readonly TaskCompletionSource<INLFileReceiveTargetV2?> Opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> SealFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? Cleanup;
        public Task? Worker;
        public long Accepted, Written, QueuedBytes, WriteStarted;
        public bool Ending, Completing, Writing, Paused;
        public long PauseStarted;
    }
    private sealed record Response(NLFileFrameV2 Frame, TaskCompletionSource<bool> Written);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PauseHeartbeat = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PauseWatchdog = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan MaxPause = TimeSpan.FromSeconds(120);
    private readonly object _gate = new();
    private readonly Stream _stream;
    private readonly INLFileReceiveStorageV2? _storage;
    private readonly long _generation;
    private readonly int _window;
    private readonly TimeProvider _clock;
    private readonly Func<int, CancellationToken, ValueTask>? _pacer;
    private readonly Func<Guid, long, bool>? _receiveAuthorization;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly Channel<Response> _responses = Channel.CreateBounded<Response>(new BoundedChannelOptions(64)
        { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _cleanupDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Sender? _sender;
    private Receiver? _receiver;
    private Task? _readTask, _responseTask, _heartbeatTask;
    private Exception? _failure;
    private Exception? _cleanupFailure;
    private bool _started, _closed;
    private long _peakQueued, _peakInFlight;

    public NLControlFileTransferV2(Stream duplex, INLFileReceiveStorageV2? storage, long generation,
        int windowBytes = NLControlFileProtocolV2.InitialWindowBytes, TimeProvider? clock = null,
        Func<int, CancellationToken, ValueTask>? pacer = null, Func<Guid, long, bool>? receiveAuthorization = null)
    {
        ArgumentNullException.ThrowIfNull(duplex);
        if (!duplex.CanRead || !duplex.CanWrite) throw new ArgumentException("FILES requires a duplex stream.", nameof(duplex));
        if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
        if (windowBytes < NLControlFileProtocolV2.ChunkBytes || windowBytes > NLControlFileProtocolV2.MaxWindowBytes)
            throw new ArgumentOutOfRangeException(nameof(windowBytes));
        _stream = duplex; _storage = storage; _generation = generation; _window = windowBytes;
        _clock = clock ?? TimeProvider.System; _pacer = pacer; _receiveAuthorization = receiveAuthorization;
    }

    public Task Completion => _completion.Task;
    public Task CleanupCompletion => _cleanupDone.Task;
    public long PeakQueuedBytes => Interlocked.Read(ref _peakQueued);
    public long PeakInFlightBytes => Interlocked.Read(ref _peakInFlight);
    public bool IsClosed { get { lock (_gate) return _closed; } }
    public Exception? CleanupFailure { get { lock (_gate) return _cleanupFailure; } }
    public event Action<NLFileTransferResultV2>? Received;
    public event Action<Guid, NLFileFrameKindV2>? TerminalReceived;
    public event Action<Guid, NLFileFrameKindV2>? TerminalSent;
    public event Action<Guid>? BeginConfirmed;
    public event Action<Guid>? BeginSending;

    /// <summary>Starts the sole reader and independent storage-response/keepalive paths. Idempotent.</summary>
    public Task StartAsync()
    {
        lock (_gate)
        {
            ThrowIfClosed();
            if (_started) return Task.CompletedTask;
            _started = true;
            _responseTask = Task.Run(WriteResponsesAsync);
            _heartbeatTask = Task.Run(HeartbeatAsync);
            _readTask = Task.Run(ReadLoopAsync);
            return Task.CompletedTask;
        }
    }

    public async Task<NLFileTransferResultV2> SendAsync(Guid transferId, NLFileMetadataV2 metadata, Stream source,
        IProgress<long>? progress = null, CancellationToken ct = default)
    {
        byte[] begin = NLControlFileProtocolV2.EncodeMetadata(metadata);
        if (transferId == Guid.Empty) throw new InvalidDataException("Empty FILES transfer id.");
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("FILES source is not readable.", nameof(source));
        ct.ThrowIfCancellationRequested();
        await StartAsync().ConfigureAwait(false);
        var sender = new Sender(transferId, metadata.Length);
        lock (_gate)
        {
            ThrowIfClosed();
            if (_sender is not null || _receiver is not null) throw new InvalidOperationException("FILES already has an active owner.");
            _sender = sender;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        bool began = false;
        try
        {
            await WriteSenderAsync(new(NLFileFrameKindV2.Begin, transferId, 0, begin), false, linked.Token).ConfigureAwait(false);
            began = true;
            NLFileFrameV2 initial = await ReadReplyAsync(sender, ResponseTimeout, linked.Token).ConfigureAwait(false);
            if (initial.Kind == NLFileFrameKindV2.Error) { ReceivedTerminal(sender, initial.Kind); throw new IOException("FILES receiver rejected Begin: " + NLControlFileProtocolV2.ReadError(initial)); }
            if (initial.Kind != NLFileFrameKindV2.Ack || initial.Offset != 0) throw new InvalidDataException("Expected Begin acknowledgement.");
            int credit = ValidateCredit(initial);
            BeginConfirmed?.Invoke(transferId);
            long sent = 0, acknowledged = 0, lastProgress = _clock.GetTimestamp();
            long pauseStarted = lastProgress, lastPauseLive = lastProgress;
            bool paused = credit == 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[NLControlFileProtocolV2.ChunkBytes];
            while (sent < metadata.Length || acknowledged < sent)
            {
                linked.Token.ThrowIfCancellationRequested();
                while (sender.Replies.Reader.TryRead(out var ready)) ApplyAck(ready);
                if (sent == metadata.Length && acknowledged == sent) break;
                long room = credit - (sent - acknowledged);
                if (!paused && sent < metadata.Length && room > 0)
                {
                    int count = (int)Math.Min(Math.Min(buffer.Length, room), metadata.Length - sent);
                    int read = 0;
                    while (read < count)
                    {
                        int part = await source.ReadAsync(buffer.AsMemory(read, count - read), linked.Token).AsTask().WaitAsync(linked.Token).ConfigureAwait(false);
                        if (part == 0) throw new InvalidDataException("FILES source became shorter than its declared length.");
                        read += part;
                    }
                    byte[] payload = buffer.AsSpan(0, read).ToArray();
                    TimeSpan waited = await WriteSenderAsync(new(NLFileFrameKindV2.Data, transferId, sent, payload), true, linked.Token).ConfigureAwait(false);
                    // Voluntary budget waits are excluded; ACKs received during that wait are still consumed next.
                    lastProgress += (long)(waited.TotalSeconds * _clock.TimestampFrequency);
                    if (acknowledged == sent) lastProgress = _clock.GetTimestamp();
                    hash.AppendData(payload); sent += read;
                    UpdatePeak(ref _peakInFlight, sent - acknowledged);
                    continue;
                }
                TimeSpan timeout = paused ? Min(Remaining(lastPauseLive, PauseWatchdog), Remaining(pauseStarted, MaxPause))
                    : Remaining(lastProgress, ResponseTimeout);
                ApplyAck(await ReadReplyAsync(sender, timeout, linked.Token).ConfigureAwait(false));
            }
            // Catch a source growing after metadata was captured, including declared-empty files.
            if (await source.ReadAsync(buffer.AsMemory(0, 1), linked.Token).ConfigureAwait(false) != 0)
                throw new InvalidDataException("FILES source became longer than its declared length.");
            byte[] digest = hash.GetHashAndReset();
            await WriteSenderAsync(new(NLFileFrameKindV2.Commit, transferId, sent, digest), false, linked.Token).ConfigureAwait(false);
            long committedAt = _clock.GetTimestamp();
            while (true)
            {
                var final = await ReadReplyAsync(sender, Remaining(committedAt, ResponseTimeout), linked.Token).ConfigureAwait(false);
                if (final.Kind == NLFileFrameKindV2.Ack) { ApplyAck(final); continue; }
                if (final.Kind == NLFileFrameKindV2.Error) { ReceivedTerminal(sender, final.Kind); throw new IOException("FILES commit rejected: " + NLControlFileProtocolV2.ReadError(final)); }
                if (final.Kind != NLFileFrameKindV2.Complete || final.Offset != metadata.Length ||
                    !CryptographicOperations.FixedTimeEquals(final.Payload, digest))
                    throw new InvalidDataException("FILES Complete does not match the committed file.");
                ReceivedTerminal(sender, final.Kind);
                return new(transferId, metadata.Length, digest);
            }

            void ApplyAck(NLFileFrameV2 frame)
            {
                if (frame.Kind == NLFileFrameKindV2.Error) { ReceivedTerminal(sender, frame.Kind); throw new IOException("FILES receiver failed: " + NLControlFileProtocolV2.ReadError(frame)); }
                if (frame.Kind != NLFileFrameKindV2.Ack || frame.Offset < acknowledged || frame.Offset > sent)
                    throw new InvalidDataException("FILES acknowledgement is out of sequence.");
                int nextCredit = ValidateCredit(frame);
                long now = _clock.GetTimestamp();
                if (frame.Offset > acknowledged) { acknowledged = frame.Offset; lastProgress = now; progress?.Report(acknowledged); }
                if (nextCredit == 0)
                {
                    if (!paused) pauseStarted = now;
                    paused = true; lastPauseLive = now;
                }
                else if (paused) { paused = false; lastProgress = now; }
                credit = nextCredit; // Absolute limit relative to ACK offset; duplicated ACKs never add credit.
            }
        }
        catch (Exception ex)
        {
            bool current; lock (_gate) current = !_closed && _sender == sender;
            if (current && began && (ex is BudgetPauseTimeoutException || ex is not (InvalidDataException or TimeoutException)))
            {
                try { await CancelSenderAsync(sender).ConfigureAwait(false); }
                catch (Exception cancelError) { Close(cancelError); }
            }
            else if (current) Close(ex);
            if (!ct.IsCancellationRequested && ex is OperationCanceledException or ChannelClosedException)
            {
                lock (_gate) { if (_closed && _failure is not null) throw new IOException("FILES channel failed.", _failure); }
            }
            throw;
        }
    }

    private int ValidateCredit(NLFileFrameV2 frame)
    {
        int credit = NLControlFileProtocolV2.ReadCredit(frame);
        if (credit > _window) throw new InvalidDataException("FILES credit exceeds the negotiated window.");
        return credit;
    }

    private async Task CancelSenderAsync(Sender sender)
    {
        await WriteSenderAsync(new(NLFileFrameKindV2.Cancel, sender.Id, 0, Array.Empty<byte>()), false, _stop.Token).ConfigureAwait(false);
        long since = _clock.GetTimestamp();
        while (true)
        {
            var frame = await ReadReplyAsync(sender, Remaining(since, ResponseTimeout), _stop.Token).ConfigureAwait(false);
            if (frame.Kind == NLFileFrameKindV2.Ack) continue;
            if (frame.Kind == NLFileFrameKindV2.Cancelled) { ReceivedTerminal(sender, frame.Kind); return; }
            if (frame.Kind == NLFileFrameKindV2.Error) { ReceivedTerminal(sender, frame.Kind); throw new IOException("FILES cancellation cleanup failed."); }
            throw new InvalidDataException("Expected FILES Cancelled.");
        }
    }

    private void ReleaseSender(Sender sender)
    { lock (_gate) { if (_sender == sender) { _sender = null; sender.Replies.Writer.TryComplete(); } } }

    private void ReceivedTerminal(Sender sender, NLFileFrameKindV2 kind)
    { ReleaseSender(sender); TerminalReceived?.Invoke(sender.Id, kind); }

    private async Task<NLFileFrameV2> ReadReplyAsync(Sender sender, TimeSpan timeout, CancellationToken ct)
    {
        if (timeout <= TimeSpan.Zero) throw new TimeoutException("FILES response/progress deadline exceeded.");
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<NLFileFrameV2> read = sender.Replies.Reader.ReadAsync(wait.Token).AsTask();
        Task deadline = Task.Delay(timeout, _clock, wait.Token);
        Task winner = await Task.WhenAny(read, deadline).ConfigureAwait(false);
        if (winner != read) { wait.Cancel(); ct.ThrowIfCancellationRequested(); throw new TimeoutException("FILES response/progress deadline exceeded."); }
        wait.Cancel();
        return await read.ConfigureAwait(false);
    }

    private async Task<TimeSpan> WriteSenderAsync(NLFileFrameV2 frame, bool budget, CancellationToken ct)
    {
        long beforePacer = _clock.GetTimestamp();
        if (budget && _pacer is not null)
        {
            using var pauseDeadline = new CancellationTokenSource(MaxPause, _clock);
            using var pause = CancellationTokenSource.CreateLinkedTokenSource(ct, pauseDeadline.Token);
            try { await _pacer(NLControlFileProtocolV2.HeaderBytes + frame.Payload.Length, pause.Token).AsTask().WaitAsync(pause.Token).ConfigureAwait(false); }
            catch (OperationCanceledException ex) when (pauseDeadline.IsCancellationRequested && !ct.IsCancellationRequested)
            { throw new BudgetPauseTimeoutException(ex); }
        }
        TimeSpan waited = _clock.GetElapsedTime(beforePacer);
        ct.ThrowIfCancellationRequested();
        if (frame.Kind == NLFileFrameKindV2.Begin) BeginSending?.Invoke(frame.TransferId);
        // Once a frame begins, finish it with the session token. User cancellation is sent as a separate whole frame.
        await WriteWireAsync(frame, _stop.Token).ConfigureAwait(false);
        return waited;
    }

    private async Task WriteWireAsync(NLFileFrameV2 frame, CancellationToken ct)
    {
        using var deadline = new CancellationTokenSource(ResponseTimeout, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        bool acquired = false;
        try
        {
            await _writer.WaitAsync(linked.Token).ConfigureAwait(false); acquired = true;
            await NLControlFileProtocolV2.WriteAsync(_stream, frame, linked.Token).AsTask().WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            var timeout = new TimeoutException("FILES network write deadline exceeded; framing is uncertain.", ex);
            Close(timeout); throw timeout;
        }
        catch (Exception ex) { Close(ex); throw; }
        finally { if (acquired) _writer.Release(); }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var frame = await NLControlFileProtocolV2.ReadAsync(_stream, _stop.Token).ConfigureAwait(false)
                    ?? throw new EndOfStreamException("FILES channel disconnected.");
                lock (_gate)
                {
                    ThrowIfClosed();
                    if (_sender is { } sender)
                    {
                        if (frame.TransferId != sender.Id || frame.Kind is not (NLFileFrameKindV2.Ack or NLFileFrameKindV2.Complete or NLFileFrameKindV2.Cancelled or NLFileFrameKindV2.Error))
                            throw new InvalidDataException("FILES response has a stale id or wrong direction.");
                        if (!sender.Replies.Writer.TryWrite(frame)) throw new InvalidDataException("FILES response queue exceeded its bound.");
                    }
                    else HandleReceiverFrame(frame);
                }
            }
        }
        catch (Exception ex) { Close(ex); }
    }

    private void HandleReceiverFrame(NLFileFrameV2 frame)
    {
        if (_receiver is null)
        {
            if (frame.Kind != NLFileFrameKindV2.Begin || _storage is null) throw new InvalidDataException("No FILES receive owner/provider.");
            if (_receiveAuthorization is not null && !_receiveAuthorization(frame.TransferId, _generation))
                throw new InvalidDataException("FILES Begin has no current receive lease.");
            var receiver = new Receiver(frame.TransferId, NLControlFileProtocolV2.DecodeMetadata(frame.Payload), _stop.Token);
            _receiver = receiver;
            receiver.Worker = Task.Run(() => ReceiveWorkerAsync(receiver));
            return;
        }
        var current = _receiver;
        if (frame.TransferId != current.Id) throw new InvalidDataException("FILES frame has a stale transfer id.");
        if (current.Ending)
        {
            // Commit already won the terminal claim. A late Cancel cannot promise rollback.
            if (current.Completing && frame.Kind == NLFileFrameKindV2.Cancel) return;
            throw new InvalidDataException("FILES transfer already ended.");
        }
        if (frame.Kind == NLFileFrameKindV2.Cancel)
        {
            current.Ending = true; current.Work.Writer.TryComplete();
            _ = Task.Run(() => FinishReceiveAsync(current, cancelled: true, error: null));
            return;
        }
        if (frame.Kind == NLFileFrameKindV2.Data)
        {
            // A policy change mid-transfer stops writing at the next block, not only at Commit.
            if (_receiveAuthorization is not null && !_receiveAuthorization(frame.TransferId, _generation))
                throw new InvalidDataException("FILES Data is no longer authorized.");
            if (frame.Offset != current.Accepted || frame.Payload.Length > current.Metadata.Length - current.Accepted ||
                frame.Payload.Length > _window - current.QueuedBytes)
                throw new InvalidDataException("FILES Data exceeds sequence, declared length or receive credit.");
            current.Accepted += frame.Payload.Length; current.QueuedBytes += frame.Payload.Length;
            UpdatePeak(ref _peakQueued, current.QueuedBytes);
        }
        else if (frame.Kind == NLFileFrameKindV2.Commit)
        {
            if (_receiveAuthorization is not null && !_receiveAuthorization(frame.TransferId, _generation))
                throw new InvalidDataException("FILES Commit is no longer authorized.");
            if (frame.Offset != current.Metadata.Length || current.Written != current.Metadata.Length || current.QueuedBytes != 0)
                throw new InvalidDataException("FILES Commit arrived before all Data were acknowledged.");
        }
        else throw new InvalidDataException("Invalid FILES receiver frame direction.");
        if (!current.Work.Writer.TryWrite(frame)) throw new InvalidDataException("FILES work queue exceeded its bound.");
    }

    private async Task ReceiveWorkerAsync(Receiver receiver)
    {
        bool completionClaimed = false;
        try
        {
            // Open may be a provider operation that completes late. Cancelled cannot confirm cleanup before it resolves.
            INLFileReceiveTargetV2 target = await _storage!.OpenAsync(receiver.Id, _generation, receiver.Metadata, receiver.Stop.Token).ConfigureAwait(false);
            lock (_gate) { receiver.Target = target; }
            receiver.Opened.TrySetResult(target);
            if (!IsCurrent(receiver)) { await CleanupReceiverAsync(receiver, target).ConfigureAwait(false); return; }
            lock (_gate) QueueResponse(NLControlFileProtocolV2.Ack(receiver.Id, receiver.Written, _window));
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await foreach (var frame in receiver.Work.Reader.ReadAllAsync(receiver.Stop.Token).ConfigureAwait(false))
            {
                receiver.Stop.Token.ThrowIfCancellationRequested();
                if (frame.Kind == NLFileFrameKindV2.Data)
                {
                    lock (_gate) { receiver.Writing = true; receiver.WriteStarted = _clock.GetTimestamp(); }
                    await target.WriteAsync(frame.Offset, frame.Payload, receiver.Stop.Token).AsTask().WaitAsync(receiver.Stop.Token).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (!IsCurrent(receiver)) return;
                        hash.AppendData(frame.Payload); receiver.Written += frame.Payload.Length;
                        receiver.QueuedBytes -= frame.Payload.Length; receiver.Writing = false; receiver.Paused = false;
                        QueueResponse(NLControlFileProtocolV2.Ack(receiver.Id, receiver.Written, _window));
                    }
                    continue;
                }
                byte[] digest = hash.GetHashAndReset();
                if (receiver.Written != receiver.Metadata.Length || !CryptographicOperations.FixedTimeEquals(digest, frame.Payload))
                    throw new InvalidDataException("FILES SHA-256/length verification failed.");
                receiver.Stop.Token.ThrowIfCancellationRequested();
                if (!IsCurrent(receiver)) return;
                string? destination = await target.CommitAsync(receiver.Metadata.Length, digest, receiver.Stop.Token)
                    .AsTask().WaitAsync(receiver.Stop.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (!IsCurrent(receiver)) return;
                    receiver.Ending = true; receiver.Completing = true; completionClaimed = true;
                }
                // Complete can reach the peer before the transport write finishes locally.
                // Claim the terminal outcome before sealing; Close waits for this provider
                // boundary outside _gate, so it cannot roll a claimed completion back.
                try { await target.DisposeAsync().ConfigureAwait(false); }
                finally { receiver.SealFinished.TrySetResult(true); }
                Task sent;
                lock (_gate)
                {
                    if (_closed || _receiver != receiver) return;
                    sent = QueueResponse(new(NLFileFrameKindV2.Complete, receiver.Id, receiver.Written, digest));
                }
                await sent.ConfigureAwait(false);
                lock (_gate) { if (_receiver == receiver) _receiver = null; }
                receiver.Stop.Cancel(); receiver.Work.Writer.TryComplete();
                receiver.Stop.Dispose();
                Received?.Invoke(new(receiver.Id, receiver.Written, digest, destination));
                return;
            }
        }
        catch (Exception ex)
        {
            receiver.Opened.TrySetResult(null);
            if (completionClaimed)
            {
                // A failed seal/Complete write cannot leave an Ending receiver attached.
                // Close owns cleanup; a successfully sealed target preserves its final file.
                Close(new IOException("FILES completion failed after its terminal claim.", ex));
                return;
            }
            bool finish;
            lock (_gate) { finish = !_closed && _receiver == receiver && !receiver.Ending; if (finish) receiver.Ending = true; }
            if (finish) await FinishReceiveAsync(receiver, false, ex).ConfigureAwait(false);
        }
    }

    private bool IsCurrent(Receiver receiver)
    { lock (_gate) return !_closed && _receiver == receiver && !receiver.Ending && !receiver.Stop.IsCancellationRequested; }

    private async Task FinishReceiveAsync(Receiver receiver, bool cancelled, Exception? error)
    {
        receiver.Stop.Cancel(); receiver.Work.Writer.TryComplete();
        try
        {
            using var openDeadline = new CancellationTokenSource(ResponseTimeout, _clock);
            var target = await receiver.Opened.Task.WaitAsync(openDeadline.Token).ConfigureAwait(false);
            if (target is not null) await CleanupReceiverAsync(receiver, target).ConfigureAwait(false);
            Task terminal;
            lock (_gate)
            {
                if (_closed || _receiver != receiver) return;
                terminal = QueueResponse(cancelled && error is null
                    ? new(NLFileFrameKindV2.Cancelled, receiver.Id, receiver.Written, Array.Empty<byte>())
                    : NLControlFileProtocolV2.Error(receiver.Id, receiver.Written, "FILES storage or integrity failed; partial aborted."));
            }
            await terminal.ConfigureAwait(false);
            lock (_gate) { if (_receiver == receiver) _receiver = null; }
            receiver.Stop.Dispose();
        }
        catch (Exception cleanup) { Close(new IOException("FILES partial cleanup failed.", cleanup)); }
    }

    private async Task CleanupAsync(INLFileReceiveTargetV2 target)
    {
        using var cleanup = new CancellationTokenSource(ResponseTimeout, _clock);
        await target.AbortAsync(cleanup.Token).AsTask().WaitAsync(cleanup.Token).ConfigureAwait(false);
        await target.DisposeAsync().AsTask().WaitAsync(cleanup.Token).ConfigureAwait(false);
    }

    private Task CleanupReceiverAsync(Receiver receiver, INLFileReceiveTargetV2 target)
    {
        // Provider Abort/Dispose may perform synchronous I/O before their first await.
        // Reserve the shared cleanup task under _gate, but execute it outside that lock.
        lock (_gate) return receiver.Cleanup ??= Task.Run(() => CleanupAsync(target));
    }

    private Task QueueResponse(NLFileFrameV2 frame)
    {
        var written = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_responses.Writer.TryWrite(new(frame, written))) throw new InvalidDataException("FILES response queue exceeded its bound.");
        return written.Task;
    }

    private async Task WriteResponsesAsync()
    {
        try
        {
            await foreach (var response in _responses.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                try
                {
                    await WriteWireAsync(response.Frame, _stop.Token).ConfigureAwait(false);
                    if (response.Frame.Kind is NLFileFrameKindV2.Complete or NLFileFrameKindV2.Cancelled or NLFileFrameKindV2.Error)
                    {
                        lock (_gate) { if (_receiver?.Id == response.Frame.TransferId && _receiver.Ending) _receiver = null; }
                        TerminalSent?.Invoke(response.Frame.TransferId, response.Frame.Kind);
                    }
                    response.Written.TrySetResult(true);
                }
                catch (Exception ex) { response.Written.TrySetException(ex); throw; }
            }
        }
        catch (Exception ex) { Close(ex); }
        finally { while (_responses.Reader.TryRead(out var pending)) pending.Written.TrySetException(_failure ?? new IOException("FILES closed.")); }
    }

    private async Task HeartbeatAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await Task.Delay(PauseHeartbeat, _clock, _stop.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_receiver is not { Ending: false, Writing: true } receiver || _clock.GetElapsedTime(receiver.WriteStarted) < PauseHeartbeat) continue;
                    if (!receiver.Paused) { receiver.Paused = true; receiver.PauseStarted = _clock.GetTimestamp(); }
                    if (_clock.GetElapsedTime(receiver.PauseStarted) >= MaxPause)
                        throw new TimeoutException("FILES storage pause exceeded 120 seconds.");
                    QueueResponse(NLControlFileProtocolV2.Ack(receiver.Id, receiver.Written, 0));
                }
            }
        }
        catch (Exception ex) { Close(ex); }
    }

    private TimeSpan Remaining(long since, TimeSpan duration) => duration - _clock.GetElapsedTime(since);
    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
    private static void UpdatePeak(ref long peak, long value)
    { long previous; do { previous = Interlocked.Read(ref peak); if (value <= previous) return; } while (Interlocked.CompareExchange(ref peak, value, previous) != previous); }
    private void ThrowIfClosed() { if (_closed) throw new IOException("FILES channel is closed.", _failure); }

    private void Close(Exception reason)
    {
        Receiver? receiver;
        lock (_gate)
        {
            if (_closed) return;
            _closed = true; _failure = reason; receiver = _receiver; _receiver = null;
            _sender?.Replies.Writer.TryComplete(reason); _sender = null;
            _responses.Writer.TryComplete(reason); _stop.Cancel();
            if (receiver is not null) { receiver.Ending = true; receiver.Stop.Cancel(); receiver.Work.Writer.TryComplete(reason); }
            try { _stream.Dispose(); } catch (IOException) { }
            _completion.TrySetException(reason);
            if (receiver is null) _cleanupDone.TrySetResult(true);
        }
        if (receiver is not null) _ = CleanupAfterCloseAsync(receiver);
    }

    private async Task CleanupAfterCloseAsync(Receiver receiver)
    {
        try
        {
            using var deadline = new CancellationTokenSource(ResponseTimeout, _clock);
            var target = await receiver.Opened.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (receiver.Completing) await receiver.SealFinished.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (target is not null) await CleanupReceiverAsync(receiver, target).ConfigureAwait(false);
            receiver.Stop.Dispose();
            _cleanupDone.TrySetResult(true);
        }
        catch (Exception ex)
        {
            var failure = new IOException("FILES partial cleanup failed after channel close.", ex);
            lock (_gate) _cleanupFailure = failure;
            _cleanupDone.TrySetException(failure);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Close(new ObjectDisposedException(nameof(NLControlFileTransferV2)));
        if (_readTask is not null) await _readTask.ConfigureAwait(false);
        if (_responseTask is not null) await _responseTask.ConfigureAwait(false);
        if (_heartbeatTask is not null) await _heartbeatTask.ConfigureAwait(false);
        await _cleanupDone.Task.ConfigureAwait(false);
    }
}
