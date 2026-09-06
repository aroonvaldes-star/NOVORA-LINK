using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Recovery;
using NOVORA.VisionEngine.Transport;
using NOVORA.VisionEngine.Video;
using Xunit;

namespace NOVORA.Tests;

public sealed class VisionEngineBlockCTests
{
    [Fact]
    public void MonitorRecoveryVE_escalates_transport_failure_to_session_recovery()
    {
        MonitorRecoveryVE monitor = new(PolicyRecoveryVE.CreateDefaultVE());

        HealthRecoveryVE health = monitor.EvaluateVE(
            StatusVideoVE.CreateInitialVE(),
            StatusAudioVE.CreateInitialVE(),
            StatusControlVE.CreateInitialVE(),
            StatesTransportVE.Failed,
            audioExpected: true,
            controlExpected: true);

        Assert.False(health.IsHealthy);
        Assert.Equal(ScopeRecoveryVE.Session, health.SuggestedScope);
    }

    [Fact]
    public void MonitorRecoveryVE_detects_video_failure_without_disabling_renderer_guard()
    {
        MonitorRecoveryVE monitor = new(PolicyRecoveryVE.CreateDefaultVE());
        StatusVideoVE failed = StatusVideoVE.CreateInitialVE() with
        {
            State = StatesVideoVE.Failed,
            RendererEnabled = false,
            LastError = "decoder"
        };

        HealthRecoveryVE health = monitor.EvaluateVE(
            failed,
            StatusAudioVE.CreateInitialVE(),
            StatusControlVE.CreateInitialVE(),
            StatesTransportVE.Connected,
            audioExpected: false,
            controlExpected: false);

        Assert.False(health.IsHealthy);
        Assert.Equal(ScopeRecoveryVE.Video, health.SuggestedScope);
        Assert.False(failed.RendererEnabled);
    }

    [Fact]
    public void BitratePerformanceVE_reduces_bitrate_under_severe_congestion()
    {
        BitratePerformanceVE policy = new(
            minBitrate: 1_000_000,
            maxBitrate: 20_000_000,
            decreaseFactor: 0.70,
            increaseFactor: 1.08);

        int recommended = policy.RecommendVE(
            currentBitrate: 12_000_000,
            CongestionPerformanceVE.Severe);

        Assert.Equal(8_400_000, recommended);
    }

    [Fact]
    public void QueuePerformanceVE_keeps_critical_messages_when_full()
    {
        QueuePerformanceVE<int> queue = new(capacity: 2);

        Assert.True(queue.TryEnqueueVE(1, PriorityPerformanceVE.Background));
        Assert.True(queue.TryEnqueueVE(2, PriorityPerformanceVE.Normal));
        Assert.True(queue.TryEnqueueVE(3, PriorityPerformanceVE.Critical));

        Assert.True(queue.TryDequeueVE(out int first));
        Assert.Equal(2, first);
        Assert.True(queue.TryDequeueVE(out int second));
        Assert.Equal(3, second);
    }

    [Fact]
    public void ManagerPerformanceVE_flags_decode_errors_as_degraded()
    {
        SnapshotMetricsVE metrics = SnapshotMetricsVE.EmptyVE() with
        {
            Video = VideoMetricsVE.EmptyVE() with
            {
                DecodeErrors = 1,
                FramesDecoded = 100,
                FramesPerSecond = 60
            }
        };

        ManagerPerformanceVE manager = new();
        SnapshotPerformanceVE snapshot = manager.EvaluateVE(metrics);

        Assert.NotEqual(CongestionPerformanceVE.Healthy, snapshot.Congestion);
        Assert.False(snapshot.RendererEnabled);
    }
}
