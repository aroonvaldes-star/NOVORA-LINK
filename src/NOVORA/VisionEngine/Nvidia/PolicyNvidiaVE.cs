namespace NOVORA.VisionEngine.Nvidia;

public static class PolicyNvidiaVE
{
    public static PipelineNvidiaVE BuildVE(
        ProfileNvidiaVE profile,
        CapabilitiesNvidiaVE capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        if (profile == ProfileNvidiaVE.Disabled || !capabilities.FastPathReadyVE)
            return PipelineNvidiaVE.FallbackVE();

        ProfileNvidiaVE effective = profile == ProfileNvidiaVE.Automatic
            ? ProfileNvidiaVE.Competitive
            : profile;

        return effective switch
        {
            ProfileNvidiaVE.Competitive => new(effective, true, true, false, false, false, 2),
            ProfileNvidiaVE.Balanced => new(effective, true, true, false, false, false, 2),
            ProfileNvidiaVE.VisionPlus => new(effective, true, true, capabilities.RtxVideoAvailable, false, false, 2),
            ProfileNvidiaVE.Smooth => new(effective, true, true, false, capabilities.FrucAvailable, false, 2),
            ProfileNvidiaVE.Stream => new(effective, true, true, false, false, capabilities.NvencApiAvailable, 2),
            _ => PipelineNvidiaVE.FallbackVE()
        };
    }
}
