using NOVORA.Model;

namespace NOVORA.Service;

/// <summary>Estado dinámico compartido con caché independiente por dispositivo.</summary>
public sealed class NLServiceDeviceState : IDisposable
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(2);
    private readonly NLServiceNetwork _network;
    private readonly NLServiceDeviceMetrics _metrics;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _networkGate = new(1, 1);
    private readonly SemaphoreSlim _metricsGate = new(1, 1);
    private readonly Dictionary<string, NLServiceCacheEntry<NLServiceNetworkStatus>> _networkCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NLServiceCacheEntry<NLServiceDeviceMetricsSnapshot>> _metricsCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public NLServiceDeviceState(NLServiceNetwork network, NLServiceDeviceMetrics metrics)
    {
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public async Task<NLServiceNetworkStatus> GetNetworkAsync(NLModelDeviceInfo device, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var serial = RequireSerial(device);
        if (TryGetFresh(_networkCache, serial, out var cached)) return cached;

        await _networkGate.WaitAsync(ct);
        try
        {
            if (TryGetFresh(_networkCache, serial, out cached)) return cached;
            var value = await _network.GetAsync(device, ct);
            lock (_gate) _networkCache[serial] = new NLServiceCacheEntry<NLServiceNetworkStatus>(value, DateTimeOffset.UtcNow);
            return value;
        }
        finally { _networkGate.Release(); }
    }

    public async Task<NLServiceDeviceMetricsSnapshot> GetMetricsAsync(NLModelDeviceInfo device, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var serial = RequireSerial(device);
        if (TryGetFresh(_metricsCache, serial, out var cached)) return cached;

        await _metricsGate.WaitAsync(ct);
        try
        {
            if (TryGetFresh(_metricsCache, serial, out cached)) return cached;
            var value = await _metrics.GetAsync(device, ct);
            lock (_gate) _metricsCache[serial] = new NLServiceCacheEntry<NLServiceDeviceMetricsSnapshot>(value, DateTimeOffset.UtcNow);
            return value;
        }
        finally { _metricsGate.Release(); }
    }

    public void Invalidate(string? serial = null)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(serial))
            {
                _networkCache.Clear();
                _metricsCache.Clear();
                return;
            }
            _networkCache.Remove(serial.Trim());
            _metricsCache.Remove(serial.Trim());
        }
    }

    private bool TryGetFresh<T>(Dictionary<string, NLServiceCacheEntry<T>> cache, string serial, out T value)
    {
        lock (_gate)
        {
            if (cache.TryGetValue(serial, out var entry) && DateTimeOffset.UtcNow - entry.CreatedAt < CacheLifetime)
            {
                value = entry.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    private static string RequireSerial(NLModelDeviceInfo device)
    {
        if (device is null || !device.Connected || string.IsNullOrWhiteSpace(device.Serial))
            throw new InvalidOperationException("No hay un dispositivo Android conectado.");
        return device.Serial.Trim();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _networkGate.Dispose();
        _metricsGate.Dispose();
        lock (_gate)
        {
            _networkCache.Clear();
            _metricsCache.Clear();
        }
        GC.SuppressFinalize(this);
    }

    private sealed record NLServiceCacheEntry<T>(T Value, DateTimeOffset CreatedAt);
}
