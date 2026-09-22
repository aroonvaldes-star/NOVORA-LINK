using NOVORA.VisionEngine.Metrics;

namespace NOVORA.VisionEngine.Performance;

public sealed class VEPerformanceManager
{
    private readonly VEPerformanceBitrate _bitrateVE;
    private int _currentBitrateVE;
    private VEPerformanceProfile _profileVE = VEPerformanceProfile.Gaming;

    public VEPerformanceManager(
        int initialBitrate = 8_000_000,
        VEPerformanceBitrate? bitrate = null)
    {
        _bitrateVE = bitrate ?? new VEPerformanceBitrate();
        _currentBitrateVE = Math.Clamp(initialBitrate, _bitrateVE.MinBitrateVE, _bitrateVE.MaxBitrateVE);
    }

    public VEPerformanceProfile ProfileVE => _profileVE;

    public void SetProfileVE(VEPerformanceProfile profile)
        => _profileVE = profile;

    public VEPerformanceOptions GetProfileOptionsVE(
        IEnumerable<NOVORA.VisionEngine.Protocol.VEProtocolCodec>? supportedCodecs = null)
        => VEPerformanceOptions.CreateVE(_profileVE, supportedCodecs);

    public VEPerformanceSnapshot EvaluateVE(VEMetricsSnapshot metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        VEPerformanceCongestion congestion = DetectCongestionVE(metrics);
        _currentBitrateVE = _bitrateVE.RecommendVE(_currentBitrateVE, congestion);

        string reason = congestion switch
        {
            VEPerformanceCongestion.Healthy => "Pipeline estable.",
            VEPerformanceCongestion.Mild => "Carga elevada pero estable.",
            VEPerformanceCongestion.Moderate => "Se detectó degradación del pipeline.",
            VEPerformanceCongestion.Severe => "Congestión severa: reducir trabajo no crítico.",
            VEPerformanceCongestion.Critical => "Congestión crítica: priorizar video/audio/control esencial.",
            _ => "Estado desconocido."
        };

        return new VEPerformanceSnapshot(
            DateTimeOffset.UtcNow,
            congestion,
            _currentBitrateVE,
            congestion >= VEPerformanceCongestion.Severe,
            metrics.RendererEnabled,
            reason);
    }

    private static VEPerformanceCongestion DetectCongestionVE(VEMetricsSnapshot metrics)
    {
        // RendererEnabled es funcionamiento normal en Block D.
        if (metrics.Video.DecodeErrors > 0 || metrics.Audio.DecodeErrors > 0 || metrics.Control.Errors > 0)
            return VEPerformanceCongestion.Moderate;

        if (metrics.ProcessCpuPercent >= 95 || metrics.WorkingSetBytes >= 1_500_000_000)
            return VEPerformanceCongestion.Critical;

        if (metrics.ProcessCpuPercent >= 85 || metrics.WorkingSetBytes >= 1_000_000_000)
            return VEPerformanceCongestion.Severe;

        if (metrics.ProcessCpuPercent >= 70 || metrics.WorkingSetBytes >= 700_000_000)
            return VEPerformanceCongestion.Moderate;

        if (metrics.ProcessCpuPercent >= 55)
            return VEPerformanceCongestion.Mild;

        return VEPerformanceCongestion.Healthy;
    }
}
