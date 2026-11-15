namespace NOVORA.NVIDIA;

public sealed record NLNVIDIAPipeline(
    NLNVIDIAProfile Profile,
    bool UseNvdec,
    bool UseZeroCopy,
    bool UseRtxVideo,
    bool UseFruc,
    bool UseNvenc,
    int MaxReadyFrames)
{
    public static NLNVIDIAPipeline FallbackVE()
        => new(NLNVIDIAProfile.Disabled, false, false, false, false, false, 2);
}
