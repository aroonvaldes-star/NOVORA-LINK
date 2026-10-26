using NOVORA.Control;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

sealed class TcpPair : IAsyncDisposable
{
    public readonly TcpClient Left, Right;
    private TcpPair(TcpClient left, TcpClient right) { Left = left; Right = right; }
    public static async Task<TcpPair> CreateAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var left = new TcpClient { NoDelay = true };
            var accept = listener.AcceptTcpClientAsync();
            Task connect = left.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
            await connect;
            var right = await accept; right.NoDelay = true;
            return new(left, right);
        }
        finally { listener.Stop(); }
    }
    public ValueTask DisposeAsync() { Left.Dispose(); Right.Dispose(); return ValueTask.CompletedTask; }
}

sealed class MemoryStorage : INLFileReceiveStorageV2
{
    public readonly ConcurrentDictionary<Guid, byte[]> Published = new();
    public readonly TaskCompletionSource<bool> WriteEntered = NewSignal();
    public readonly TaskCompletionSource<bool> OpenEntered = NewSignal();
    public readonly TaskCompletionSource<bool> ReleaseOpen = NewSignal();
    public readonly TaskCompletionSource<bool> CommitEntered = NewSignal();
    public readonly TaskCompletionSource<bool> ReleaseWrite = NewSignal();
    public readonly TaskCompletionSource<bool> ReleaseCommit = NewSignal();
    public bool BlockWrites { get; set; }
    public bool BlockOpen { get; set; }
    public bool BlockCommit { get; set; }
    public bool FailWrite { get; set; }
    public bool FailCommit { get; set; }
    public bool FailAbort { get; set; }
    public Target? Last;
    public int Opens;
    public async ValueTask<INLFileReceiveTargetV2> OpenAsync(Guid id, long generation, NLFileMetadataV2 metadata, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Interlocked.Increment(ref Opens);
        Last = new(this, id, generation, metadata);
        OpenEntered.TrySetResult(true);
        if (BlockOpen) await ReleaseOpen.Task; // Real provider APIs may complete after cancellation.
        return Last;
    }
    public static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public sealed class Target(MemoryStorage owner, Guid id, long generation, NLFileMetadataV2 metadata) : INLFileReceiveTargetV2
    {
        private readonly MemoryStream _partial = new();
        private readonly object _gate = new();
        public bool Aborted, Committed, Disposed;
        public long BytesWritten { get { lock (_gate) return _partial.Length; } }
        public readonly Guid Id = id;
        public readonly long Generation = generation;
        public readonly NLFileMetadataV2 Metadata = metadata;
        public async ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        {
            owner.WriteEntered.TrySetResult(true);
            if (owner.BlockWrites) await owner.ReleaseWrite.Task.WaitAsync(ct);
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (Aborted || Disposed) throw new IOException("Target aborted or disposed.");
                if (owner.FailWrite) throw new IOException("Storage capacity exceeded.");
                if (offset != _partial.Length) throw new InvalidDataException("Provider received noncontiguous write.");
                _partial.Write(bytes.Span);
            }
        }
        public async ValueTask<string?> CommitAsync(long length, ReadOnlyMemory<byte> sha256, CancellationToken ct)
        {
            owner.CommitEntered.TrySetResult(true);
            if (owner.BlockCommit) await owner.ReleaseCommit.Task.WaitAsync(ct);
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (Aborted || Disposed || owner.FailCommit) throw new IOException("Provider cannot atomically publish.");
                byte[] data = _partial.ToArray();
                if (data.LongLength != length || !SHA256.HashData(data).AsSpan().SequenceEqual(sha256.Span)) throw new InvalidDataException("Provider digest mismatch.");
                owner.Published[Id] = data; Committed = true;
                return "test-target";
            }
        }
        public ValueTask AbortAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (Disposed) return ValueTask.CompletedTask;
                if (owner.FailAbort) throw new IOException("Partial cleanup rejected.");
                Aborted = true;
                _partial.SetLength(0);
                owner.Published.TryRemove(Id, out _);
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            lock (_gate) Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

sealed class ManualTime : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = new();
    private long _timerRegistration;
    private TaskCompletionSource<bool> _timerRegistered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _now;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() { lock (_gate) return _now; }
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ManualTimer timer;
        TaskCompletionSource<bool> registered;
        lock (_gate)
        {
            timer = new ManualTimer(this, callback, state, ++_timerRegistration); _timers.Add(timer); timer.Change(dueTime, period);
            registered = _timerRegistered;
            _timerRegistered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        registered.TrySetResult(true);
        return timer;
    }
    public bool HasDeadline(TimeSpan duration)
    {
        lock (_gate) return _timers.Any(t => !t.Disposed && t.Due == _now + duration.Ticks);
    }
    public long TimerRegistration { get { lock (_gate) return _timerRegistration; } }
    public bool HasDeadlineRegisteredAfter(TimeSpan duration, long registration)
    {
        lock (_gate) return _timers.Any(t => t.Registration > registration && !t.Disposed && t.Due == _now + duration.Ticks);
    }
    public async Task WaitForDeadlineRegisteredAfterAsync(TimeSpan duration, long registration, CancellationToken ct = default)
    {
        while (true)
        {
            Task registered;
            lock (_gate)
            {
                if (_timers.Any(t => t.Registration > registration && !t.Disposed && t.Due == _now + duration.Ticks)) return;
                registered = _timerRegistered.Task;
            }
            await registered.WaitAsync(ct);
        }
    }
    public void Advance(TimeSpan duration)
    {
        List<ManualTimer> ready;
        lock (_gate)
        {
            _now += duration.Ticks;
            ready = _timers.Where(t => !t.Disposed && t.Due >= 0 && t.Due <= _now).ToList();
            foreach (var timer in ready) timer.Due = timer.Period <= 0 ? -1 : _now + timer.Period;
        }
        foreach (var timer in ready) timer.Fire();
    }
    private sealed class ManualTimer(ManualTime owner, TimerCallback callback, object? state, long registration) : ITimer
    {
        public readonly long Registration = registration;
        public long Due, Period;
        public bool Disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                if (Disposed) return false;
                Due = dueTime == Timeout.InfiniteTimeSpan ? -1 : owner._now + dueTime.Ticks;
                Period = period == Timeout.InfiniteTimeSpan ? -1 : period.Ticks;
                return true;
            }
        }
        public void Fire() { if (!Disposed) callback(state); }
        public void Dispose() { lock (owner._gate) Disposed = true; }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

sealed class TrackingDuplexStream(Stream inner) : Stream
{
    private int _reading;
    public int PeakConcurrentReads;
    public override bool CanRead => true; public override bool CanWrite => true; public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        int current = Interlocked.Increment(ref _reading); PeakConcurrentReads = Math.Max(PeakConcurrentReads, current);
        try { return await inner.ReadAsync(buffer, ct); } finally { Interlocked.Decrement(ref _reading); }
    }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => inner.WriteAsync(buffer, ct);
    public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
    public override void Flush() => inner.Flush(); public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
