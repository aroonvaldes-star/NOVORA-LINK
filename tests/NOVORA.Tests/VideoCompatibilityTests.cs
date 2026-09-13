using NOVORA.VisionEngine.Performance;
using NOVORA.VisionEngine.Protocol;
using Xunit;

namespace NOVORA.Tests;

public sealed class VideoCompatibilityTests
{
    [Fact]
    public void Selected_codec_reaches_server_options()
    {
        var options = NOVORA.VisionEngine.Server.OptionsServerVE.CreateForProfileVE(
            ProfilePerformanceVE.Video, [CodecProtocolVE.H264, CodecProtocolVE.H265]);
        options.ValidateVE();
        Assert.Equal(CodecProtocolVE.H265, options.VideoCodec);
        Assert.Equal(6_000_000, options.VideoBitRate);
        Assert.Equal(CodecProtocolVE.H264,
            NOVORA.VisionEngine.Server.OptionsServerVE.CreateForProfileVE(ProfilePerformanceVE.Video).VideoCodec);
    }

    [Theory]
    [InlineData(ProfilePerformanceVE.Video)]
    [InlineData(ProfilePerformanceVE.Gaming)]
    [InlineData(ProfilePerformanceVE.Balanced)]
    [InlineData(ProfilePerformanceVE.Battery)]
    public void Unknown_capabilities_always_use_h264(ProfilePerformanceVE profile)
    {
        Assert.Equal(CodecProtocolVE.H264, OptionsPerformanceVE.CreateVE(profile).PreferredCodec);
        Assert.Equal(CodecProtocolVE.H264, OptionsPerformanceVE.CreateVE(profile, []).PreferredCodec);
    }

    [Fact]
    public void Gaming_preserves_h264_when_advanced_formats_are_available()
    {
        Assert.Equal(CodecProtocolVE.H264, OptionsPerformanceVE.CreateVE(ProfilePerformanceVE.Gaming,
            [CodecProtocolVE.Av1, CodecProtocolVE.H264, CodecProtocolVE.H265]).PreferredCodec);
    }

    [Fact]
    public void Audio_only_capabilities_are_not_treated_as_video_support()
    {
        Assert.Throws<ArgumentException>(() => OptionsPerformanceVE.CreateVE(ProfilePerformanceVE.Video,
            [CodecProtocolVE.Opus]));
    }

    [Fact]
    public void Manager_passes_verified_capabilities_to_profile_selection()
    {
        var manager = new ManagerPerformanceVE();
        manager.SetProfileVE(ProfilePerformanceVE.Video);
        Assert.Equal(CodecProtocolVE.H265, manager.GetProfileOptionsVE([CodecProtocolVE.H265]).PreferredCodec);
        Assert.Equal(CodecProtocolVE.H264, manager.GetProfileOptionsVE().PreferredCodec);
    }
}
