namespace NOVORA.Control;

public sealed record NLControlCapturePolicy(long Generation, long Sequence, int TargetBitrate,
    int TargetFps, int? MaxSize = null);
public sealed record NLControlCapturePolicyAck(long Generation, long Sequence, bool Accepted,
    int RequestedBitrate, int? ObservedBitrate, string? RejectedReason = null);
public sealed record NLControlCaptureTelemetry(long Generation, long Sequence, double WindowSeconds,
    long UsefulBytes, long ProtocolBytes, long QueueBytes, double? QueueAgeP95Ms,
    double? WriteWaitP95Ms, long Dropped);

/// <summary>The live control session owns ordering across encoder replacement and rotation.</summary>
public sealed class NLControlCaptureTelemetrySequencer
{
    private long _sequence;
    public NLControlCaptureTelemetry Stamp(NLControlCaptureTelemetry sample) =>
        sample with { Sequence = Interlocked.Increment(ref _sequence) };
}

/// <summary>Validates commands for one live pipeline. The setters never rebuild capture.</summary>
public sealed class NLControlCapturePolicySession
{
    private readonly object _gate = new();
    private readonly long _generation;
    private readonly int _maximumBitrate, _maximumFps;
    private readonly NLControlNetworkCapabilities _capabilities;
    private readonly Func<int, bool> _setBitrate, _setFps;
    private long _sequence;
    private int _currentBitrate, _currentFps;
    private bool _closed;

    public NLControlCapturePolicySession(long generation, int initialBitrate, int initialFps,
        NLControlNetworkCapabilities capabilities, Func<int, bool> setBitrate, Func<int, bool> setFps)
    {
        _generation = generation;
        _maximumBitrate = _currentBitrate = initialBitrate;
        _maximumFps = _currentFps = initialFps;
        _capabilities = capabilities;
        _setBitrate = setBitrate;
        _setFps = setFps;
    }

    public NLControlCapturePolicyAck Apply(NLControlCapturePolicy policy)
    {
        lock (_gate)
        {
            NLControlCapturePolicyAck Reject(string reason) =>
                new(policy.Generation, policy.Sequence, false, policy.TargetBitrate, null, reason);
            if (_closed || _generation <= 0 || policy.Generation != _generation)
                return Reject("La sesión de captura cambió.");
            if (_capabilities.Version != 1 || !_capabilities.LiveVideoBitrate || !_capabilities.CaptureFramePacing)
                return Reject("La adaptación de captura no fue negociada.");
            if (policy.Sequence <= _sequence || policy.Sequence <= 0)
                return Reject("Orden de captura antigua.");
            if (policy.TargetBitrate is < 250_000 or > 100_000_000 || policy.TargetBitrate > _maximumBitrate ||
                policy.TargetFps is < 10 or > 120 || policy.TargetFps > _maximumFps)
                return Reject("El perfil solicitado excede los límites de la sesión.");
            if (policy.MaxSize is not null)
                return Reject("El cambio vivo de resolución no está habilitado.");
            // Even a rejected actuator request cannot be replayed later after route recovery.
            _sequence = policy.Sequence;
            try
            {
                if (!_setBitrate(policy.TargetBitrate))
                    return Reject("El codificador no aceptó el bitrate.");
                if (!_setFps(policy.TargetFps))
                {
                    _setBitrate(_currentBitrate);
                    return Reject("El compositor no aceptó los FPS.");
                }
                _currentBitrate = policy.TargetBitrate;
                _currentFps = policy.TargetFps;
                // Android accepting SetParameters does not prove a vendor codec obeys it.
                return new(policy.Generation, policy.Sequence, true, policy.TargetBitrate, null);
            }
            catch (Exception)
            {
                try { _setBitrate(_currentBitrate); } catch (Exception) { }
                return Reject("No se pudo aplicar el perfil al codificador activo.");
            }
        }
    }

    public void Close() { lock (_gate) _closed = true; }
}

/// <summary>Called after consuming the newest texture; skipped frames never accumulate.</summary>
public sealed class NLControlCaptureFramePacer
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly int _maximumFps;
    private int _targetFps;
    private long? _lastPresentation;
    private double _nextPresentation;
    private long _seen, _presented;
    public NLControlCaptureFramePacer(int initialFps, TimeProvider? clock = null)
    {
        if (initialFps is < 1 or > 240) throw new ArgumentOutOfRangeException(nameof(initialFps));
        _maximumFps = _targetFps = initialFps;
        _clock = clock ?? TimeProvider.System;
    }
    public long FramesSeen { get { lock (_gate) return _seen; } }
    public long FramesPresented { get { lock (_gate) return _presented; } }
    public long FramesSkipped { get { lock (_gate) return _seen - _presented; } }
    public bool SetTargetFps(int fps)
    {
        lock (_gate)
        {
            if (fps < 1 || fps > _maximumFps) return false;
            _targetFps = fps;
            if (_lastPresentation is long last)
                _nextPresentation = last + (double)_clock.TimestampFrequency / fps;
            return true;
        }
    }
    /// <summary>
    /// Vsync jitter allowance: a frame arriving this early still takes its slot. Capped at
    /// 2 ms so that a lower target (e.g. 45 from 60 Hz) keeps its cadence.
    /// </summary>
    private double Tolerance(double interval) => Math.Min(interval * 0.25, _clock.TimestampFrequency * 0.002);

    /// <summary>Time until a skipped frame may be presented; zero when it already may.</summary>
    public TimeSpan UntilNextPresentation
    {
        get
        {
            lock (_gate)
            {
                if (_lastPresentation is null) return TimeSpan.Zero;
                double interval = (double)_clock.TimestampFrequency / _targetFps;
                double remaining = _nextPresentation - Tolerance(interval) - _clock.GetTimestamp();
                // Round up: a caller that waits exactly this long must find the slot open.
                return remaining <= 0 ? TimeSpan.Zero
                    : TimeSpan.FromTicks((long)Math.Ceiling(remaining * TimeSpan.TicksPerSecond / _clock.TimestampFrequency));
            }
        }
    }

    /// <summary>
    /// <paramref name="deferred"/> re-presents a frame already counted as seen, so a skipped
    /// last frame of a screen change still reaches the encoder.
    /// </summary>
    public bool TryPresent(bool deferred = false)
    {
        lock (_gate)
        {
            if (!deferred) _seen++;
            long now = _clock.GetTimestamp();
            double interval = (double)_clock.TimestampFrequency / _targetFps;
            if (_lastPresentation is not null && now < _nextPresentation - Tolerance(interval)) return false;
            // Preserve cadence on a faster source (e.g. 45 from 60 Hz), but never catch up a backlog.
            _nextPresentation = _lastPresentation is null || now - _nextPresentation >= interval
                ? now + interval : _nextPresentation + interval;
            _lastPresentation = now;
            _presented++;
            return true;
        }
    }
}

/// <summary>Coalesces repeated recovery requests, retrying at most once per second.</summary>
public sealed class NLControlKeyframeGate(TimeProvider? clock = null)
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private long? _lastRequested;
    private bool _pending;
    public bool TryRequest()
    {
        lock (_gate)
        {
            long now = _clock.GetTimestamp();
            if (_lastRequested is long previous && _clock.GetElapsedTime(previous, now) < TimeSpan.FromSeconds(1))
                return false;
            // A codec may ignore the request. One expired pending slot is replaced, never queued.
            _lastRequested = now;
            _pending = true;
            return true;
        }
    }
    public void ObserveKeyFrame() { lock (_gate) _pending = false; }
    public bool Pending { get { lock (_gate) return _pending; } }
}

/// <summary>Sender-local video measurements; protocol bytes exclude TLS/TCP overhead.</summary>
public sealed class NLControlCaptureTelemetryWindow
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly long _generation;
    private readonly Queue<double> _writes = new(), _ages = new();
    private long _windowStart, _sequence, _useful, _protocol, _queue, _drops;
    private long? _writeStarted;
    public NLControlCaptureTelemetryWindow(long generation, TimeProvider? clock = null)
    {
        _generation = generation;
        _clock = clock ?? TimeProvider.System;
        _windowStart = _clock.GetTimestamp();
    }
    public void BeginWrite() { lock (_gate) _writeStarted = _clock.GetTimestamp(); }
    public void RecordWrite(int usefulBytes, int protocolBytes, TimeSpan wait)
    {
        if (usefulBytes < 0 || protocolBytes < usefulBytes || wait < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(usefulBytes));
        lock (_gate)
        {
            _useful += usefulBytes;
            _protocol += protocolBytes;
            Add(_writes, wait.TotalMilliseconds);
            _writeStarted = null;
        }
    }
    public void RecordQueueAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero) return;
        lock (_gate) Add(_ages, age.TotalMilliseconds);
    }
    public void SetQueueBytes(long bytes) { lock (_gate) _queue = Math.Max(0, bytes); }
    public void RecordDrop() { lock (_gate) _drops++; }
    public NLControlCaptureTelemetry? Take()
    {
        lock (_gate)
        {
            long now = _clock.GetTimestamp();
            double seconds = _clock.GetElapsedTime(_windowStart, now).TotalSeconds;
            if (_generation <= 0 || seconds < 1) return null;
            if (_writeStarted is long started)
                Add(_writes, _clock.GetElapsedTime(started, now).TotalMilliseconds);
            var sample = new NLControlCaptureTelemetry(_generation, ++_sequence, seconds,
                _useful, _protocol, _queue, P95(_ages), P95(_writes), _drops);
            _windowStart = now;
            _useful = _protocol = _drops = 0;
            _ages.Clear(); _writes.Clear();
            return sample;
        }
    }
    private static void Add(Queue<double> queue, double value)
    {
        if (queue.Count == 128) queue.Dequeue();
        queue.Enqueue(value);
    }
    private static double? P95(Queue<double> values)
    {
        if (values.Count == 0) return null;
        double[] sorted = values.ToArray(); Array.Sort(sorted);
        return sorted[(int)Math.Ceiling(sorted.Length * .95) - 1];
    }
}
