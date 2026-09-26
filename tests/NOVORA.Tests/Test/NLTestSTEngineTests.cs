using NOVORA.ExInEngine;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Metrics;
using NOVORA.Contracts.Stability;
using NOVORA.STEngine.Core;
using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Transport;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestSTEngineTests
{
    [Fact]
    public void STEngine_classifies_neutral_snapshots_without_engine_runtimes()
    {
        STCoreEngine engine =
            new();

        NLStabilityVisionSnapshot vision =
            NLStabilityVisionSnapshot.Empty() with
            {
                IsRunning = true,
                IsStreaming = true,
                FramesDecoded = 240,
                FramesPerSecond = 60,
                RendererEnabled = true
            };

        NLStabilityLinkSnapshot link =
            new(
                "device-neutral",
                NLStabilityEngineState.Running,
                Healthy: true,
                LatencyMs: 18,
                DnsFailures: 0,
                RecoveryAttempts: 0,
                SuccessfulRecoveries: 0);

        NLStabilityExInSnapshot exIn =
            new(
                NLStabilityEngineState.Running,
                ConnectedGamepads: 1,
                LastError: null,
                HasUncorrectableCalibration: false,
                HasLowBattery: false);

        STCoreSnapshot snapshot =
            engine.AnalyzeST(vision);

        STCoreSnapshotNovora novora =
            engine.AnalyzeNovoraST(
                snapshot,
                [link],
                exIn);

        Assert.Equal(
            STCoreState.Healthy,
            novora.State);
    }

    [Fact]
    public void STEngine_performance_observation_does_not_mutate_vision_bitrate_policy()
    {
        VEPerformanceManager performance =
            new(initialBitrate: 8_000_000);

        VEMetricsSnapshot critical =
            VEMetricsSnapshot.EmptyVE() with
            {
                ProcessCpuPercent = 96
            };

        VEMetricsSnapshot moderate =
            VEMetricsSnapshot.EmptyVE() with
            {
                ProcessCpuPercent = 75
            };

        VEPerformanceSnapshot observation =
            performance.ObserveVE(critical);

        VEPerformanceSnapshot applied =
            performance.EvaluateVE(moderate);

        Assert.Equal(
            4_800_000,
            observation.RecommendedVideoBitrate);

        Assert.Equal(
            7_200_000,
            applied.RecommendedVideoBitrate);
    }

    [Fact]
    public void STEngine_reports_healthy_stream_when_metrics_are_clean()
    {
        STCoreEngine engine =
            new();

        VEMetricsSnapshot metrics =
            VEMetricsSnapshot.EmptyVE() with
            {
                Video = VEMetricsVideo.EmptyVE() with
                {
                    FramesDecoded = 240,
                    FramesPerSecond = 60,
                    DecodeErrors = 0
                },
                Transport = new VEMetricsTransport(
                    VETransportStates.Connected,
                    VETransportMode.Reverse,
                    VideoConnected: true,
                    AudioConnected: true,
                    ControlConnected: true,
                    Uptime: TimeSpan.FromSeconds(12))
            };

        VEPerformanceSnapshot performance =
            new(
                DateTimeOffset.UtcNow,
                VEPerformanceCongestion.Healthy,
                8_000_000,
                false,
                true,
                "Pipeline estable.");

        STCoreSnapshot snapshot =
            engine.AnalyzeST(
                metrics,
                performance);

        Assert.Equal(
            STCoreState.Healthy,
            snapshot.State);

        Assert.False(
            snapshot.ShouldReduceNonCriticalWork);
    }

    [Fact]
    public void STEngine_marks_decode_errors_as_degraded()
    {
        STCoreEngine engine =
            new();

        VEMetricsSnapshot metrics =
            VEMetricsSnapshot.EmptyVE() with
            {
                Video = VEMetricsVideo.EmptyVE() with
                {
                    FramesDecoded = 120,
                    FramesPerSecond = 55,
                    DecodeErrors = 1
                }
            };

        VEPerformanceSnapshot performance =
            new(
                DateTimeOffset.UtcNow,
                VEPerformanceCongestion.Moderate,
                6_000_000,
                false,
                true,
                "Se detecto degradacion del pipeline.");

        STCoreSnapshot snapshot =
            engine.AnalyzeST(
                metrics,
                performance);

        Assert.Equal(
            STCoreState.Degraded,
            snapshot.State);

        Assert.True(
            snapshot.ShouldReduceNonCriticalWork);

        Assert.Contains(
            snapshot.Observations,
            item =>
                item.Contains(
                    "decodificacion",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void STEngine_marks_severe_congestion_as_critical()
    {
        STCoreEngine engine =
            new();

        VEPerformanceSnapshot performance =
            new(
                DateTimeOffset.UtcNow,
                VEPerformanceCongestion.Severe,
                3_500_000,
                true,
                true,
                "Congestion severa.");

        STCoreSnapshot snapshot =
            engine.AnalyzeST(
                VEMetricsSnapshot.EmptyVE(),
                performance);

        Assert.Equal(
            STCoreState.Critical,
            snapshot.State);

        Assert.True(
            snapshot.ShouldReduceNonCriticalWork);
    }

    [Fact]
    public void STEngine_reports_novora_healthy_when_vision_and_linkengine_are_clean()
    {
        STCoreEngine engine =
            new();

        STCoreSnapshot vision =
            engine.AnalyzeST(
                VEMetricsSnapshot.EmptyVE(),
                new VEPerformanceSnapshot(
                    DateTimeOffset.UtcNow,
                    VEPerformanceCongestion.Healthy,
                    8_000_000,
                    false,
                    true,
                    "Pipeline estable."));

        LEMetricsDeviceMetricsSnapshot link =
            new(
                "device-1",
                LECoreStates.Online,
                true,
                BytesSent: 1024,
                BytesReceived: 2048,
                LatencyMs: 20,
                ThroughputMbps: 80,
                TcpSessions: 4,
                UdpSessions: 1,
                DnsFailures: 0,
                RecoveryAttempts: 0,
                SuccessfulRecoveries: 0,
                Healthy: true,
                LastActivityUtc: DateTimeOffset.UtcNow);

        STCoreSnapshotNovora snapshot =
            engine.AnalyzeNovoraST(
                vision,
                [link]);

        Assert.Equal(
            STCoreState.Healthy,
            snapshot.State);

        Assert.False(
            snapshot.ShouldReduceNonCriticalWork);

        Assert.Equal(
            1,
            snapshot.LinkDeviceCount);
    }

    [Fact]
    public void STEngine_marks_novora_degraded_when_linkengine_latency_is_high()
    {
        STCoreEngine engine =
            new();

        STCoreSnapshot vision =
            engine.AnalyzeST(
                VEMetricsSnapshot.EmptyVE(),
                new VEPerformanceSnapshot(
                    DateTimeOffset.UtcNow,
                    VEPerformanceCongestion.Healthy,
                    8_000_000,
                    false,
                    true,
                    "Pipeline estable."));

        LEMetricsDeviceMetricsSnapshot link =
            new(
                "device-2",
                LECoreStates.Online,
                true,
                BytesSent: 1024,
                BytesReceived: 2048,
                LatencyMs: 320,
                ThroughputMbps: 10,
                TcpSessions: 2,
                UdpSessions: 0,
                DnsFailures: 0,
                RecoveryAttempts: 0,
                SuccessfulRecoveries: 0,
                Healthy: false,
                LastActivityUtc: DateTimeOffset.UtcNow);

        STCoreSnapshotNovora snapshot =
            engine.AnalyzeNovoraST(
                vision,
                [link]);

        Assert.Equal(
            STCoreState.Degraded,
            snapshot.State);

        Assert.True(
            snapshot.ShouldReduceNonCriticalWork);

        Assert.Contains(
            snapshot.Observations,
            item =>
                item.Contains(
                    "latencia alta",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void STEngine_marks_novora_critical_when_linkengine_failed()
    {
        STCoreEngine engine =
            new();

        STCoreSnapshot vision =
            STCoreSnapshot.EmptyST();

        LEMetricsDeviceMetricsSnapshot link =
            new(
                "device-3",
                LECoreStates.Failed,
                false,
                BytesSent: 0,
                BytesReceived: 0,
                LatencyMs: 0,
                ThroughputMbps: 0,
                TcpSessions: 0,
                UdpSessions: 0,
                DnsFailures: 0,
                RecoveryAttempts: 1,
                SuccessfulRecoveries: 0,
                Healthy: false,
                LastActivityUtc: DateTimeOffset.UtcNow);

        STCoreSnapshotNovora snapshot =
            engine.AnalyzeNovoraST(
                vision,
                [link]);

        Assert.Equal(
            STCoreState.Critical,
            snapshot.State);

        Assert.True(
            snapshot.ShouldReduceNonCriticalWork);
    }

    [Fact]
    public void STEngine_marks_novora_degraded_when_exin_failed_without_recovering_other_engines()
    {
        STCoreEngine engine =
            new();

        STCoreSnapshot vision =
            engine.AnalyzeST(
                VEMetricsSnapshot.EmptyVE(),
                new VEPerformanceSnapshot(
                    DateTimeOffset.UtcNow,
                    VEPerformanceCongestion.Healthy,
                    8_000_000,
                    false,
                    true,
                    "Pipeline estable."));

        ExInStatus exIn =
            new(
                ExInStates.Failed,
                ConnectedGamepads: 1,
                ReportsSent: 42,
                DateTimeOffset.UtcNow,
                "ExInEngine fallo.",
                "Salida UHID no disponible.",
                [],
                []);

        STCoreSnapshotNovora snapshot =
            engine.AnalyzeNovoraST(
                vision,
                [],
                exIn);

        Assert.Equal(
            STCoreState.Degraded,
            snapshot.State);

        Assert.True(
            snapshot.ShouldReduceNonCriticalWork);

        Assert.Equal(
            NLStabilityEngineState.Failed,
            snapshot.ExIn?.State);

        Assert.Equal(
            exIn.LastError,
            snapshot.ExIn?.LastError);

        Assert.Contains(
            snapshot.Observations,
            item =>
                item.Contains(
                    "ExInEngine esta en fallo",
                    StringComparison.OrdinalIgnoreCase));
    }
}

internal static class NLTestSTEngineCompatibility
{
    public static STCoreSnapshot AnalyzeST(
        this STCoreEngine engine,
        VEMetricsSnapshot metrics,
        VEPerformanceSnapshot performance)
        => engine.AnalyzeST(
            new NLStabilityVisionSnapshot(
                metrics.CapturedAtUtc,
                IsRunning: true,
                IsStreaming: true,
                metrics.Video.FramesDecoded,
                metrics.Video.FramesPerSecond,
                metrics.Video.DecodeErrors,
                metrics.Audio.DecodeErrors,
                metrics.Audio.PlaybackErrors,
                metrics.Control.Errors,
                metrics.Transport.VideoConnected,
                metrics.WorkingSetBytes,
                metrics.ProcessCpuPercent,
                metrics.RendererEnabled,
                performance.Congestion switch
                {
                    VEPerformanceCongestion.Mild => NLStabilitySeverity.Watch,
                    VEPerformanceCongestion.Moderate => NLStabilitySeverity.Degraded,
                    VEPerformanceCongestion.Severe or VEPerformanceCongestion.Critical => NLStabilitySeverity.Critical,
                    _ => NLStabilitySeverity.Healthy
                },
                performance.RecommendedVideoBitrate,
                performance.ShouldReduceTelemetry,
                performance.Reason));

    public static STCoreSnapshotNovora AnalyzeNovoraST(
        this STCoreEngine engine,
        STCoreSnapshot vision,
        IReadOnlyList<LEMetricsDeviceMetricsSnapshot> links,
        ExInStatus? exIn = null)
        => engine.AnalyzeNovoraST(
            vision,
            links.Select(link =>
                new NLStabilityLinkSnapshot(
                    link.Serial,
                    link.State switch
                    {
                        LECoreStates.Connecting => NLStabilityEngineState.Starting,
                        LECoreStates.Online => NLStabilityEngineState.Running,
                        LECoreStates.Degraded => NLStabilityEngineState.Degraded,
                        LECoreStates.Recovering => NLStabilityEngineState.Recovering,
                        LECoreStates.Failed => NLStabilityEngineState.Failed,
                        _ => NLStabilityEngineState.Stopped
                    },
                    link.Healthy,
                    link.LatencyMs,
                    link.DnsFailures,
                    link.RecoveryAttempts,
                    link.SuccessfulRecoveries))
                .ToArray(),
            ExInStabilitySnapshotAdapter.CaptureExIn(exIn));
}
