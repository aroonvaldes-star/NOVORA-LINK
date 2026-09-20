using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Metrics;
using NOVORA.STEngine.Core;
using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Transport;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestSTEngineTests
{
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
}
