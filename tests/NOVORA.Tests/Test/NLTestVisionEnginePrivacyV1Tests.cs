using NOVORA.VisionEngine.Audio;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Privacy;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Recovery;
using NOVORA.VisionEngine.Transport;
using NOVORA.VisionEngine.Video;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestVisionEnginePrivacyV1Tests
{
    [Fact]
    public void RendererEnabled_is_not_congestion()
    {
        VEMetricsSnapshot metrics =
            VEMetricsSnapshot.EmptyVE() with
            {
                RendererEnabled = true
            };

        VEPerformanceManager performance =
            new();

        VEPerformanceSnapshot snapshot =
            performance.EvaluateVE(
                metrics);

        Assert.Equal(
            VEPerformanceCongestion.Healthy,
            snapshot.Congestion);

        Assert.True(
            snapshot.RendererEnabled);
    }

    [Fact]
    public void RendererEnabled_is_not_recovery_condition()
    {
        VERecoveryMonitor monitor =
            new(
                VERecoveryPolicy.CreateDefaultVE());

        VEVideoStatus video =
            VEVideoStatus.CreateInitialVE() with
            {
                RendererEnabled = true
            };

        VERecoveryHealth health =
            monitor.EvaluateVE(
                video,
                VEAudioStatus.CreateInitialVE(),
                VEControlStatus.CreateInitialVE(),
                VETransportStates.Connected,
                audioExpected: false,
                controlExpected: false);

        Assert.True(
            health.IsHealthy);
    }

    [Fact]
    public void SecureInput_enables_privacy_shield()
    {
        VEPrivacyManager privacy =
            new();

        privacy.SetSecureInputVE(
            true);

        Assert.True(
            privacy.IsProtectedVE);

        Assert.False(
            privacy.CanExposeVideoVE);

        Assert.False(
            privacy.CanExposeAudioVE);

        Assert.False(
            privacy.CanSendControlVE(VEControlType.InjectText));

        Assert.False(
            privacy.CanUseClipboardVE);

        Assert.False(
            privacy.CanExchangeFilesVE);
    }

    [Fact]
    public void Sensitive_package_is_local_policy()
    {
        VEPrivacyManager privacy =
            new();

        privacy.SetSensitivePackagesVE(
            new[]
            {
                "com.example.bank"
            });

        privacy.SetForegroundPackageVE(
            "com.example.bank");

        Assert.True(
            privacy.IsProtectedVE);

        Assert.Equal(
            VEPrivacyClassification.SensitiveApplication,
            privacy.StatusVE.Classification);
    }

    [Fact]
    public void Privacy_does_not_keep_package_after_session_end()
    {
        VEPrivacyManager privacy =
            new();

        privacy.BeginSessionVE();

        privacy.SetForegroundPackageVE(
            "com.example.private");

        privacy.EndSessionVE();

        Assert.Null(
            privacy.ContextVE.ForegroundPackage);

        Assert.False(
            privacy.IsProtectedVE);
    }

    [Fact]
    public void Video_profile_uses_safe_h264_without_capability_data()
    {
        var options =
            VEPerformanceOptions.CreateVE(
                VEPerformanceProfile.Video);

        Assert.Equal(
            VEProtocolCodec.H264,
            options.PreferredCodec);
    }

    [Fact]
    public void Video_profile_can_use_h265_when_reported()
    {
        var options =
            VEPerformanceOptions.CreateVE(
                VEPerformanceProfile.Video,
                new[]
                {
                    VEProtocolCodec.H264,
                    VEProtocolCodec.H265
                });

        Assert.Equal(
            VEProtocolCodec.H265,
            options.PreferredCodec);
    }

    [Fact]
    public void Video_profile_prefers_av1_only_when_reported()
    {
        var options =
            VEPerformanceOptions.CreateVE(
                VEPerformanceProfile.Video,
                new[]
                {
                    VEProtocolCodec.H264,
                    VEProtocolCodec.H265,
                    VEProtocolCodec.Av1
                });

        Assert.Equal(
            VEProtocolCodec.Av1,
            options.PreferredCodec);
    }
}