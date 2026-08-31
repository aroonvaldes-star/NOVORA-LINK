using NOVORA.Models;

namespace NOVORA.Services;

/// <summary>Estado dinámico compartido por widgets. Evita polling duplicado y mantiene caché independiente por dispositivo.</summary>
public sealed class DeviceStateService
{
    private readonly NetworkService _network;
    private readonly DeviceMetricsService _metrics;
    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry<NetworkStatus>> _networkCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CacheEntry<DeviceMetrics>> _metricsCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(2);

    public DeviceStateService(NetworkService network, DeviceMetricsService metrics)
    {
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public async Task<NetworkStatus> GetNetworkAsync(DeviceInfo device, CancellationToken ct = default)
    {
        ValidateDevice(device);
        var serial = device.Serial.Trim();
        lock (_gate)
        {
            if (_networkCache.TryGetValue(serial, out var cached) && DateTimeOffset.UtcNow - cached.CreatedAt < CacheLifetime)
                return cached.Value;
        }

        var value = await _network.GetAsync(device, ct);
        lock (_gate) _networkCache[serial] = new CacheEntry<NetworkStatus>(value, DateTimeOffset.UtcNow);
        return value;
    }

    public async Task<DeviceMetrics> GetMetricsAsync(DeviceInfo device, CancellationToken ct = default)
    {
        ValidateDevice(device);
        var serial = device.Serial.Trim();
        lock (_gate)
        {
            if (_metricsCache.TryGetValue(serial, out var cached) && DateTimeOffset.UtcNow - cached.CreatedAt < CacheLifetime)
                return cached.Value;
        }

        var value = await _metrics.GetAsync(device, ct);
        lock (_gate) _metricsCache[serial] = new CacheEntry<DeviceMetrics>(value, DateTimeOffset.UtcNow);
        return value;
    }

    public void Invalidate(string? serial = null)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(serial))
            {
                _networkCache.Clear();
                _metricsCache.Clear();
                return;
            }

            serial = serial.Trim();
            _networkCache.Remove(serial);
            _metricsCache.Remove(serial);
        }
    }

    private static void ValidateDevice(DeviceInfo device)
    {
        if (device is null || !device.Connected || string.IsNullOrWhiteSpace(device.Serial))
            throw new InvalidOperationException("No hay un dispositivo Android conectado.");
    }

    private sealed record CacheEntry<T>(T Value, DateTimeOffset CreatedAt);
}
