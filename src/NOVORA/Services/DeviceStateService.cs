using NOVORA.Models;

namespace NOVORA.Services;

/// <summary>Estado dinámico compartido por widgets. Evita que cada widget sondee Android por separado.</summary>
public sealed class DeviceStateService
{
    private readonly NetworkService _network;
    private readonly DeviceMetricsService _metrics;
    private readonly object _gate = new();
    private string? _serial;
    private NetworkStatus? _networkCache;
    private DeviceMetrics? _metricsCache;
    private DateTimeOffset _networkAt;
    private DateTimeOffset _metricsAt;

    public DeviceStateService(NetworkService network, DeviceMetricsService metrics)
    { _network = network; _metrics = metrics; }

    public async Task<NetworkStatus> GetNetworkAsync(DeviceInfo device, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_serial == device.Serial && _networkCache is not null && DateTimeOffset.UtcNow - _networkAt < TimeSpan.FromSeconds(2)) return _networkCache;
        }
        var value = await _network.GetAsync(device, ct);
        lock (_gate) { _serial = device.Serial; _networkCache = value; _networkAt = DateTimeOffset.UtcNow; }
        return value;
    }

    public async Task<DeviceMetrics> GetMetricsAsync(DeviceInfo device, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_serial == device.Serial && _metricsCache is not null && DateTimeOffset.UtcNow - _metricsAt < TimeSpan.FromSeconds(2)) return _metricsCache;
        }
        var value = await _metrics.GetAsync(device, ct);
        lock (_gate) { _serial = device.Serial; _metricsCache = value; _metricsAt = DateTimeOffset.UtcNow; }
        return value;
    }

    public void Invalidate(string? serial = null)
    {
        lock (_gate) { if (serial is null || string.Equals(_serial, serial, StringComparison.OrdinalIgnoreCase)) { _networkCache = null; _metricsCache = null; _serial = serial; } }
    }
}