namespace NOVORA.VisionEngine.Nvidia;

public sealed record PipelineNvidiaVE(
    ProfileNvidiaVE Profile,
    bool UseNvdec,
    bool UseZeroCopy,
    bool UseRtxVideo,
    bool UseFruc,
    bool UseNvenc,
    int MaxReadyFrames)
{
    public static PipelineNvidiaVE FallbackVE()
        => new(ProfileNvidiaVE.Disabled, false, false, false, false, false, 2);
}
