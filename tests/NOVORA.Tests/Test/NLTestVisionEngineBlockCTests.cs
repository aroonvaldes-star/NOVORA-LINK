using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Recovery;
using NOVORA.VisionEngine.Transport;
using NOVORA.VisionEngine.Video;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestVisionEngineBlockCTests
{
    [Fact]
    public void MonitorRecoveryVE_escalates_transport_failure_to_session_recovery()
    {
        VERecoveryMonitor monitor = new(VERecoveryPolicy.CreateDefaultVE());

        VERecoveryHealth health = monitor.EvaluateVE(
            VEVideoStatus.CreateInitialVE(),
            VEAudioStatus.CreateInitialVE(),
            VEControlStatus.CreateInitialVE(),
            VETransportStates.Failed,
            audioExpected: true,
            controlExpected: true);

        Assert.False(health.IsHealthy);
        Assert.Equal(VERecoveryScope.Session, health.SuggestedScope);
    }

    [Fact]
    public void MonitorRecoveryVE_detects_video_failure_without_disabling_renderer_guard()
    {
        VERecoveryMonitor monitor = new(VERecoveryPolicy.CreateDefaultVE());
        VEVideoStatus failed = VEVideoStatus.CreateInitialVE() with
        {
            State = VEVideoStates.Failed,
            RendererEnabled = false,
            LastError = "decoder"
        };

        VERecoveryHealth health = monitor.EvaluateVE(
            failed,
            VEAudioStatus.CreateInitialVE(),
            VEControlStatus.CreateInitialVE(),
            VETransportStates.Connected,
            audioExpected: false,
            controlExpected: false);

        Assert.False(health.IsHealthy);
        Assert.Equal(VERecoveryScope.Video, health.SuggestedScope);
        Assert.False(failed.RendererEnabled);
    }

    [Fact]
    public void BitratePerformanceVE_reduces_bitrate_under_severe_congestion()
    {
        VEPerformanceBitrate policy = new(
            minBitrate: 1_000_000,
            maxBitrate: 20_000_000,
            decreaseFactor: 0.70,
            increaseFactor: 1.08);

        int recommended = policy.RecommendVE(
            currentBitrate: 12_000_000,
            VEPerformanceCongestion.Severe);

        Assert.Equal(8_400_000, recommended);
    }

    [Fact]
    public void QueuePerformanceVE_keeps_critical_messages_when_full()
    {
        VEPerformanceQueue<int> queue = new(capacity: 2);

        Assert.True(queue.TryEnqueueVE(1, VEPerformancePriority.Background));
        Assert.True(queue.TryEnqueueVE(2, VEPerformancePriority.Normal));
        Assert.True(queue.TryEnqueueVE(3, VEPerformancePriority.Critical));

        Assert.True(queue.TryDequeueVE(out int first));
        Assert.Equal(2, first);
        Assert.True(queue.TryDequeueVE(out int second));
        Assert.Equal(3, second);
    }

    [Fact]
    public void ManagerPerformanceVE_flags_decode_errors_as_degraded()
    {
        VEMetricsSnapshot metrics = VEMetricsSnapshot.EmptyVE() with
        {
            Video = VEMetricsVideo.EmptyVE() with
            {
                DecodeErrors = 1,
                FramesDecoded = 100,
                FramesPerSecond = 60
            }
        };

        VEPerformanceManager manager = new();
        VEPerformanceSnapshot snapshot = manager.EvaluateVE(metrics);

        Assert.NotEqual(VEPerformanceCongestion.Healthy, snapshot.Congestion);
        Assert.False(snapshot.RendererEnabled);
    }
}
