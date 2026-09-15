using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Protocol;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestVideoCompatibilityTests
{
    [Fact]
    public void Selected_codec_reaches_server_options()
    {
        var options = NOVORA.VisionEngine.Server.VEServerOptions.CreateForProfileVE(
            VEPerformanceProfile.Video, [VEProtocolCodec.H264, VEProtocolCodec.H265]);
        options.ValidateVE();
        Assert.Equal(VEProtocolCodec.H265, options.VideoCodec);
        Assert.Equal(6_000_000, options.VideoBitRate);
        Assert.Equal(VEProtocolCodec.H264,
            NOVORA.VisionEngine.Server.VEServerOptions.CreateForProfileVE(VEPerformanceProfile.Video).VideoCodec);
    }

    [Theory]
    [InlineData(VEPerformanceProfile.Video)]
    [InlineData(VEPerformanceProfile.Gaming)]
    [InlineData(VEPerformanceProfile.Balanced)]
    [InlineData(VEPerformanceProfile.Battery)]
    public void Unknown_capabilities_always_use_h264(VEPerformanceProfile profile)
    {
        Assert.Equal(VEProtocolCodec.H264, VEPerformanceOptions.CreateVE(profile).PreferredCodec);
        Assert.Equal(VEProtocolCodec.H264, VEPerformanceOptions.CreateVE(profile, []).PreferredCodec);
    }

    [Fact]
    public void Gaming_preserves_h264_when_advanced_formats_are_available()
    {
        Assert.Equal(VEProtocolCodec.H264, VEPerformanceOptions.CreateVE(VEPerformanceProfile.Gaming,
            [VEProtocolCodec.Av1, VEProtocolCodec.H264, VEProtocolCodec.H265]).PreferredCodec);
    }

    [Fact]
    public void Audio_only_capabilities_are_not_treated_as_video_support()
    {
        Assert.Throws<ArgumentException>(() => VEPerformanceOptions.CreateVE(VEPerformanceProfile.Video,
            [VEProtocolCodec.Opus]));
    }

    [Fact]
    public void Manager_passes_verified_capabilities_to_profile_selection()
    {
        var manager = new VEPerformanceManager();
        manager.SetProfileVE(VEPerformanceProfile.Video);
        Assert.Equal(VEProtocolCodec.H265, manager.GetProfileOptionsVE([VEProtocolCodec.H265]).PreferredCodec);
        Assert.Equal(VEProtocolCodec.H264, manager.GetProfileOptionsVE().PreferredCodec);
    }
}
