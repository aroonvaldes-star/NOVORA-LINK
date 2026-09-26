using NOVORA.VisionEngine.Video;
using Xunit;

namespace NOVORA.Tests.Test;

public sealed class NLTestAccelerationState
{
    [Fact]
    public void Software_fallback_is_selected_and_active_only_after_frames()
    {
        VEAccelerationState opened = VEAccelerationState.FromDecoder("h264", framesDecoded: 0);
        VEAccelerationState active = VEAccelerationState.FromDecoder("h264", framesDecoded: 3);

        Assert.True(opened.Available);
        Assert.True(opened.Selected);
        Assert.False(opened.Active);
        Assert.True(active.Active);
        Assert.Null(active.BenefitMeasured);
    }

    [Fact]
    public void Nvidia_selection_does_not_claim_activity_or_benefit_before_frames()
    {
        VEAccelerationState state = VEAccelerationState.FromDecoder("h264_cuvid", framesDecoded: 0);

        Assert.Equal(VEAccelerationBackend.NvidiaNvdec, state.Backend);
        Assert.True(state.Available);
        Assert.True(state.Selected);
        Assert.False(state.Active);
        Assert.Null(state.BenefitMeasured);
    }

    [Theory]
    [InlineData(VEAccelerationBackend.Amd, "AMD no evaluado")]
    [InlineData(VEAccelerationBackend.Intel, "Intel no evaluado")]
    public void Unimplemented_vendor_backends_remain_unavailable(
        VEAccelerationBackend backend,
        string reason)
    {
        VEAccelerationState state = VEAccelerationState.Unavailable(backend, reason);

        Assert.False(state.Available);
        Assert.False(state.Selected);
        Assert.False(state.Active);
        Assert.Null(state.BenefitMeasured);
        Assert.Equal(reason, state.Evidence);
    }
}
