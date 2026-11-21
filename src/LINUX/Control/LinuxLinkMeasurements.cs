using System.Text.Json;
using NOVORA.LinkEngine.Metrics;

namespace NOVORA.Linux.Control;

/// <summary>
/// Windows NLUIWindowMainLinkSpeedTest without the WPF parts: one current-session
/// measurement record (never a history), the SpeedTest server Android opens through the
/// LE VPN (http://10.0.2.2:28085, mapped by the relay to PC loopback) and the traffic
/// sample from the relay's LE_METRIC counters. Shared by the window and both control servers.
/// </summary>
internal sealed class LinuxLinkMeasurements : IDisposable
{
    public const string NoInternet = "Internet: sin medir en esta sesión.";
    public const string NoTunnel = "Enlace PC ↔ Android: sin medir en esta sesión.";
    public const string NoSession = "Sin sesión activa. No se conservan mediciones anteriores.";
    public const string ManualTraffic = "Medición manual disponible para esta sesión. No se sondea tráfico en segundo plano.";

    private readonly object _gate = new();
    private readonly LESessionMeasurements _measurements = new();
    private readonly Func<LESpeedTestServer> _serverFactory;
    private LESpeedTestServer? _server;
    private string? _sourceKey;
    private string? _testSessionKey;
    private string? _testId;
    private string _internetText = NoInternet;
    private string _tunnelText = NoTunnel;
    private string _trafficText = NoSession;
    private bool _disposed;

    public LinuxLinkMeasurements() : this(() => new LESpeedTestServer()) { }

    internal LinuxLinkMeasurements(Func<LESpeedTestServer> serverFactory) => _serverFactory = serverFactory;

    /// <summary>Raised from any thread when a status text changed.</summary>
    public event EventHandler? Changed;

    public string InternetText { get { lock (_gate) return _internetText; } }
    public string TunnelText { get { lock (_gate) return _tunnelText; } }
    public string TrafficText { get { lock (_gate) return _trafficText; } }
    public string? SessionKey { get { lock (_gate) return _measurements.SessionKey; } }
    public string Transport { get { lock (_gate) return _measurements.Transport; } }

    /// <summary>Starts a new measurement record when the connected session changed (Windows SyncLinkMeasurementsLE).</summary>
    public void Sync(ILinuxLinkEngine? active)
    {
        string? key = active is { IsConnected: true } ? active.SessionKey : null;
        bool changed;
        lock (_gate)
        {
            if (_disposed || key == _sourceKey) return;
            _sourceKey = key;
            _server?.Invalidate();
            _testSessionKey = _testId = null;
            _measurements.End();
            _internetText = NoInternet;
            _tunnelText = NoTunnel;
            _trafficText = NoSession;
            if (key is not null && active is not null)
            {
                _measurements.Begin(Guid.NewGuid().ToString("N"), active.Transport, DateTimeOffset.UtcNow);
                _trafficText = ManualTraffic;
            }
            changed = true;
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Windows BeginLinkMeasurementLE: returns the one-use SpeedTest URL for this session.</summary>
    public string Begin(ILinuxLinkEngine active, string? mode = null)
    {
        Sync(active);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_measurements.SessionKey is not { } key || _measurements.Transport != active.Transport)
                throw new InvalidOperationException("Inicia LE en ese transporte antes de medir.");
            LESpeedTestServer server = EnsureServer();
            string url = server.Begin(active.Transport);
            _testSessionKey = key;
            _testId = server.CurrentTestId;
            return mode is null ? url : url + "?mode=" + mode;
        }
    }

    public void SetTunnelStatus(string text) => SetText(ref _tunnelText, text);
    public void SetInternetStatus(string text) => SetText(ref _internetText, text);

    /// <summary>Windows LinkLiveMonitorTick: one manual traffic sample from the relay counters.</summary>
    public void ObserveTraffic(ILinuxLinkEngine? active)
    {
        Sync(active);
        lock (_gate)
        {
            if (_measurements.SessionKey is not { } key || active is null)
            {
                _trafficText = "Inicia LE antes de medir el tráfico.";
            }
            else
            {
                _measurements.Observe(key, active.TrafficSnapshot, DateTimeOffset.UtcNow);
                _trafficText = _measurements.UploadMbps is double up && _measurements.DownloadMbps is double down
                    ? $"Subida desde Android: {up:F2} Mbps · Bajada hacia Android: {down:F2} Mbps\n" +
                      $"Datos observados en esta sesión: ↑ {_measurements.UploadedBytes / 1_000_000d:F2} MB · ↓ {_measurements.DownloadedBytes / 1_000_000d:F2} MB"
                    : "Tráfico actual: sin medir; esperando contadores recientes de esta sesión.";
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>JSON summary of the current session only (Windows ExportLinkSession_Click).</summary>
    public string? ExportJson()
    {
        lock (_gate)
            return _measurements.SessionKey is null ? null
                : JsonSerializer.Serialize(_measurements, new JsonSerializerOptions { WriteIndented = true });
    }

    private LESpeedTestServer EnsureServer()
    {
        if (_server is not null) return _server;
        LESpeedTestServer server = _serverFactory();
        server.ResultReceived += (_, result) =>
        {
            lock (_gate)
            {
                if (_disposed || !IsCurrentTest(result.SessionId) || !_measurements.SetTunnel(_testSessionKey!, result)) return;
                _tunnelText =
                    $"Túnel LE {result.Transport} · ↓ {result.DownloadMbps:F1} Mbps · " +
                    $"↑ {result.UploadMbps:F1} Mbps · RTT HTTP {result.LatencyMs:F1} ms · " +
                    $"variación {result.JitterMs:F1} ms · {result.SampledAtUtc.ToLocalTime():HH:mm:ss}";
            }
            Changed?.Invoke(this, EventArgs.Empty);
        };
        server.InternetResultReceived += (_, result) =>
        {
            lock (_gate)
            {
                if (_disposed || !IsCurrentTest(result.SessionId) || !_measurements.SetInternet(_testSessionKey!, result)) return;
                _internetText =
                    $"Internet por LE {result.Transport} · ↓ {result.DownloadMbps:F1} Mbps · " +
                    $"↑ {result.UploadMbps:F1} Mbps · RTT HTTP {result.LatencyMs:F1} ms · " +
                    $"variación {result.JitterMs:F1} ms · {result.Provider} · {result.SampledAtUtc.ToLocalTime():HH:mm:ss}";
            }
            Changed?.Invoke(this, EventArgs.Empty);
        };
        return _server = server;
    }

    private bool IsCurrentTest(string id) =>
        _measurements.SessionKey is not null && _testSessionKey == _measurements.SessionKey && _testId == id;

    private void SetText(ref string field, string text)
    {
        lock (_gate) field = text;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _measurements.End();
            _server?.Dispose();
            _server = null;
        }
    }
}
