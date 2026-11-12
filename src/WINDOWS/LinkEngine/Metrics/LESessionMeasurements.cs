using NOVORA.LinkEngine.Network;

namespace NOVORA.LinkEngine.Metrics;

/// <summary>Current session only: two test results and one counter sample, never a history.</summary>
public sealed class LESessionMeasurements
{
    private static readonly TimeSpan MinimumCounterInterval = TimeSpan.FromMilliseconds(250);
    private LERelayTrafficSnapshot? _previous;
    public string? SessionKey { get; private set; }
    public string Transport { get; private set; } = "";
    public DateTimeOffset StartedAtUtc { get; private set; }
    public double? UploadMbps { get; private set; }
    public double? DownloadMbps { get; private set; }
    public long UploadedBytes { get; private set; }
    public long DownloadedBytes { get; private set; }
    public LESpeedTestResult? Tunnel { get; private set; }
    public LEInternetSpeedTestResult? Internet { get; private set; }

    public bool Begin(string key, string transport, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (transport is not ("USB" or "LAN")) throw new ArgumentException("Transporte LE no válido.", nameof(transport));
        if (SessionKey == key) return false;
        End();
        SessionKey = key;
        Transport = transport;
        StartedAtUtc = now;
        return true;
    }

    public void End()
    {
        SessionKey = null;
        Transport = "";
        StartedAtUtc = default;
        _previous = null;
        ClearLive();
        UploadedBytes = DownloadedBytes = 0;
        Tunnel = null;
        Internet = null;
    }

    public void ClearLive() => UploadMbps = DownloadMbps = null;

    public bool Observe(string key, LERelayTrafficSnapshot? sample, DateTimeOffset now)
    {
        if (SessionKey is null || key != SessionKey) return false;
        if (sample is null || sample.SampledAtUtc < StartedAtUtc ||
            sample.SampledAtUtc > now || now - sample.SampledAtUtc > TimeSpan.FromSeconds(3) ||
            sample.TxBytes < 0 || sample.RxBytes < 0)
        {
            _previous = null;
            ClearLive();
            return false;
        }
        if (_previous is { } previous)
        {
            if (sample.SampledAtUtc <= previous.SampledAtUtc) return false;
            if (sample.SampledAtUtc - previous.SampledAtUtc < MinimumCounterInterval)
                return false;
            if (sample.TxBytes >= previous.TxBytes && sample.RxBytes >= previous.RxBytes)
            {
                long upload = sample.TxBytes - previous.TxBytes, download = sample.RxBytes - previous.RxBytes;
                double seconds = (sample.SampledAtUtc - previous.SampledAtUtc).TotalSeconds;
                UploadMbps = upload * 8d / seconds / 1_000_000;
                DownloadMbps = download * 8d / seconds / 1_000_000;
                UploadedBytes += upload;
                DownloadedBytes += download;
            }
            else ClearLive(); // A restarted relay starts a new baseline, never negative traffic.
        }
        _previous = sample;
        return UploadMbps.HasValue;
    }

    public bool SetTunnel(string key, LESpeedTestResult result)
    {
        if (SessionKey is null || key != SessionKey || result.Transport != Transport || result.SampledAtUtc < StartedAtUtc) return false;
        Tunnel = result;
        return true;
    }

    public bool SetInternet(string key, LEInternetSpeedTestResult result)
    {
        if (SessionKey is null || key != SessionKey || result.Transport != Transport || result.SampledAtUtc < StartedAtUtc) return false;
        Internet = result;
        return true;
    }
}
