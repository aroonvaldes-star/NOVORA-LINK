using NOVORA.VisionEngine.Metrics;

namespace NOVORA.VisionEngine.Performance;

public sealed class ManagerPerformanceVE
{
    private readonly BitratePerformanceVE _bitrateVE;
    private int _currentBitrateVE;
    private ProfilePerformanceVE _profileVE = ProfilePerformanceVE.Gaming;

    public ManagerPerformanceVE(
        int initialBitrate = 4_000_000,
        BitratePerformanceVE? bitrate = null)
    {
        _bitrateVE = bitrate ?? new BitratePerformanceVE();
        _currentBitrateVE = Math.Clamp(initialBitrate, _bitrateVE.MinBitrateVE, _bitrateVE.MaxBitrateVE);
    }

    public ProfilePerformanceVE ProfileVE => _profileVE;

    public void SetProfileVE(ProfilePerformanceVE profile)
        => _profileVE = profile;

    public OptionsPerformanceVE GetProfileOptionsVE(
        IEnumerable<NOVORA.VisionEngine.Protocol.CodecProtocolVE>? supportedCodecs = null)
        => OptionsPerformanceVE.CreateVE(_profileVE, supportedCodecs);

    public SnapshotPerformanceVE EvaluateVE(SnapshotMetricsVE metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        CongestionPerformanceVE congestion = DetectCongestionVE(metrics);
        _currentBitrateVE = _bitrateVE.RecommendVE(_currentBitrateVE, congestion);

        string reason = congestion switch
        {
            CongestionPerformanceVE.Healthy => "Pipeline estable.",
            CongestionPerformanceVE.Mild => "Carga elevada pero estable.",
            CongestionPerformanceVE.Moderate => "Se detectó degradación del pipeline.",
            CongestionPerformanceVE.Severe => "Congestión severa: reducir trabajo no crítico.",
            CongestionPerformanceVE.Critical => "Congestión crítica: priorizar video/audio/control esencial.",
            _ => "Estado desconocido."
        };

        return new SnapshotPerformanceVE(
            DateTimeOffset.UtcNow,
            congestion,
            _currentBitrateVE,
            congestion >= CongestionPerformanceVE.Severe,
            metrics.RendererEnabled,
            reason);
    }

    private static CongestionPerformanceVE DetectCongestionVE(SnapshotMetricsVE metrics)
    {
        // RendererEnabled es funcionamiento normal en Block D.
        if (metrics.Video.DecodeErrors > 0 || metrics.Audio.DecodeErrors > 0 || metrics.Control.Errors > 0)
            return CongestionPerformanceVE.Moderate;

        if (metrics.ProcessCpuPercent >= 95 || metrics.WorkingSetBytes >= 1_500_000_000)
            return CongestionPerformanceVE.Critical;

        if (metrics.ProcessCpuPercent >= 85 || metrics.WorkingSetBytes >= 1_000_000_000)
            return CongestionPerformanceVE.Severe;

        if (metrics.ProcessCpuPercent >= 70 || metrics.WorkingSetBytes >= 700_000_000)
            return CongestionPerformanceVE.Moderate;

        if (metrics.ProcessCpuPercent >= 55)
            return CongestionPerformanceVE.Mild;

        return CongestionPerformanceVE.Healthy;
    }
}
