namespace NOVORA.VisionEngine.Metrics;

/// <summary>Bounds optional display sampling; it does not control the media pipeline.</summary>
public sealed class VEOnDemandTelemetry<T> where T : class
{
    private readonly object _gate = new();
    private bool _requested;
    private long? _lastRefreshMs, _lastSampleMs;
    private T? _sample;

    public bool IsRequested { get { lock (_gate) return _requested; } }

    public bool SetRequested(bool requested)
    {
        lock (_gate)
        {
            if (_requested == requested) return false;
            _requested = requested;
            ResetCore();
            return true;
        }
    }

    public bool TryRequestRefresh(long nowMs)
    {
        lock (_gate)
        {
            if (!_requested || !Due(nowMs, _lastRefreshMs)) return false;
            _lastRefreshMs = nowMs;
            return true;
        }
    }

    public T? GetSample(long nowMs, Func<T?> capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        lock (_gate)
        {
            if (!_requested) return null;
            if (Due(nowMs, _lastSampleMs))
            {
                _sample = capture();
                _lastSampleMs = nowMs;
            }
            return _sample;
        }
    }

    public void Reset() { lock (_gate) ResetCore(); }
    private void ResetCore() { _sample = null; _lastRefreshMs = _lastSampleMs = null; }
    private static bool Due(long nowMs, long? previousMs) =>
        previousMs is null || nowMs < previousMs.Value || nowMs - previousMs.Value >= 1000;
}
