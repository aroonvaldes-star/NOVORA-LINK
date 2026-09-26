using NOVORA.Contracts.Stability;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Video;

namespace NOVORA.VisionEngine.Metrics;

public static class VEStabilitySnapshotAdapter
{
    public static NLStabilityVisionSnapshot CaptureVE(VECoreRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        VEMetricsSnapshot metrics = new VEMetricsCollector().CaptureVE(
            runtime.VideoVE.StatusVE, runtime.AudioVE.StatusVE, runtime.ControlVE.StatusVE,
            runtime.TransportVE.StateVE, runtime.TransportSessionVE);
        VEPerformanceSnapshot performance = runtime.PerformanceVE.ObserveVE(metrics);

        return new NLStabilityVisionSnapshot(
            metrics.CapturedAtUtc, runtime.IsRunningVE,
            runtime.VideoVE.StatusVE.State == VEVideoStates.Streaming,
            metrics.Video.FramesDecoded, metrics.Video.FramesPerSecond, metrics.Video.DecodeErrors,
            metrics.Audio.DecodeErrors, metrics.Audio.PlaybackErrors, metrics.Control.Errors,
            metrics.Transport.VideoConnected, metrics.WorkingSetBytes, metrics.ProcessCpuPercent,
            metrics.RendererEnabled, MapSeverityVE(performance.Congestion),
            performance.RecommendedVideoBitrate, performance.ShouldReduceTelemetry, performance.Reason);
    }

    private static NLStabilitySeverity MapSeverityVE(VEPerformanceCongestion congestion)
        => congestion switch
        {
            VEPerformanceCongestion.Mild => NLStabilitySeverity.Watch,
            VEPerformanceCongestion.Moderate => NLStabilitySeverity.Degraded,
            VEPerformanceCongestion.Severe or VEPerformanceCongestion.Critical => NLStabilitySeverity.Critical,
            _ => NLStabilitySeverity.Healthy
        };
}
