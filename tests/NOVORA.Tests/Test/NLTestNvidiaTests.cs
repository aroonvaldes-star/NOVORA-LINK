using NOVORA.NVIDIA;
using NOVORA.Service;
using NOVORA.VisionEngine.Video;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestNvidiaTests
{
    [Fact]
    public void Active_decode_survives_requested_profile_change_until_session_stops()
    {
        var manager = new NLNVIDIAManager(new NLServiceNovoraPaths());
        Assert.Equal(NLNVIDIAProfile.Automatic, manager.BeginSessionVE());
        var streaming = VEVideoStatus.CreateInitialVE() with
        {
            State = VEVideoStates.Streaming, DecoderName = "h264_cuvid", NvdecActive = true
        };
        manager.UpdateDecoderVE(streaming);
        Assert.False(manager.StatusVE.Pipeline.UseNvdec); // Todavía no hay frames.
        manager.UpdateDecoderVE(streaming with { Stats = new VEVideoStats(1, 100, 1, 1, 1, 0) });
        Assert.True(manager.StatusVE.Pipeline.UseNvdec);
        Assert.False(manager.StatusVE.Pipeline.UseZeroCopy);
        manager.SetProfileVE(NLNVIDIAProfile.Disabled);
        Assert.True(manager.StatusVE.Pipeline.UseNvdec); // Cambio solicitado para la próxima sesión.
        Assert.Equal(NLNVIDIAProfile.Automatic, manager.StatusVE.Pipeline.Profile);
        manager.UpdateDecoderVE(VEVideoStatus.CreateInitialVE());
        Assert.False(manager.StatusVE.Pipeline.UseNvdec);
        Assert.Equal("Sin decoder", manager.StatusVE.Capabilities.Backend);
        Assert.Equal(NLNVIDIAProfile.Disabled, manager.BeginSessionVE());
    }

    [Fact]
    public void Undefined_profile_is_rejected_before_detection()
    {
        var manager = new NLNVIDIAManager(new NLServiceNovoraPaths());
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.SetProfileVE((NLNVIDIAProfile)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => NLNVIDIAPolicy.BuildVE((NLNVIDIAProfile)999, NLNVIDIACapabilities.NoneVE()));
    }
    [Theory]
    [InlineData(NLNVIDIAProfile.Automatic)]
    [InlineData(NLNVIDIAProfile.Disabled)]
    [InlineData(NLNVIDIAProfile.Competitive)]
    [InlineData(NLNVIDIAProfile.Balanced)]
    [InlineData(NLNVIDIAProfile.VisionPlus)]
    [InlineData(NLNVIDIAProfile.Smooth)]
    [InlineData(NLNVIDIAProfile.Stream)]
    public void Available_libraries_and_bridge_file_do_not_activate_unimplemented_features(NLNVIDIAProfile profile)
    {
        var capabilities = new NLNVIDIACapabilities(true, true, 1, 12000,
            true, true, 1, true, true, true, "FFmpeg software");
        var pipeline = NLNVIDIAPolicy.BuildVE(profile, capabilities);
        Assert.False(pipeline.UseNvdec);
        Assert.False(pipeline.UseNvenc);
        Assert.False(pipeline.UseZeroCopy);
        Assert.False(pipeline.UseRtxVideo);
        Assert.False(pipeline.UseFruc);
    }
    [Fact]
    public void Disabled_profile_publishes_software_fallback_without_native_component()
    {
        var manager = new NLNVIDIAManager(new NLServiceNovoraPaths());
        NLNVIDIAStatus? published = null;
        manager.StatusChangedVE += (_, status) => published = status;
        manager.SetProfileVE(NLNVIDIAProfile.Disabled);
        Assert.Same(manager.StatusVE, published);
        Assert.Equal("FFmpeg", published!.Capabilities.Backend);
        Assert.False(published.Pipeline.UseNvdec);
        Assert.False(published.Pipeline.UseNvenc);
        Assert.False(published.Capabilities.NativeBridgeAvailable);
    }

    [Theory]
    [InlineData(NLNVIDIAProfile.Automatic)]
    [InlineData(NLNVIDIAProfile.Competitive)]
    [InlineData(NLNVIDIAProfile.Stream)]
    public void Missing_native_bridge_preserves_fallback_even_with_gpu_capabilities(NLNVIDIAProfile profile)
    {
        var capabilities = new NLNVIDIACapabilities(true, true, 1, 12000,
            true, true, 1, false, false, false, "FFmpeg software");
        var pipeline = NLNVIDIAPolicy.BuildVE(profile, capabilities);
        Assert.Equal(NLNVIDIAProfile.Disabled, pipeline.Profile);
        Assert.False(pipeline.UseNvdec);
        Assert.False(pipeline.UseNvenc);
    }

    [Fact]
    public void Native_component_resolves_under_application_nvidia_directory()
    {
        var root = System.IO.Path.GetFullPath("nvidia-path-test");
        Assert.Equal(System.IO.Path.Combine(root, "NVIDIA", "Native", "NOVORA.NVIDIA.Native.dll"),
            NLNVIDIAPaths.NativeBridge(root));
    }
}
