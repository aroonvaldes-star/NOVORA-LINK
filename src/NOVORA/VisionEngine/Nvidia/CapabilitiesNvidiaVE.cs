namespace NOVORA.VisionEngine.Nvidia;

public sealed record CapabilitiesNvidiaVE(
    bool CudaDriverLoaded,
    bool CudaInitialized,
    int CudaDeviceCount,
    int CudaDriverVersion,
    bool NvdecApiAvailable,
    bool NvencApiAvailable,
    uint NvencMaxApiVersion,
    bool NativeBridgeAvailable,
    bool RtxVideoAvailable,
    bool FrucAvailable,
    string Backend)
{
    public bool FastPathReadyVE =>
        CudaInitialized &&
        CudaDeviceCount > 0 &&
        NvdecApiAvailable &&
        NativeBridgeAvailable;

    public static CapabilitiesNvidiaVE NoneVE()
        => new(false, false, 0, 0, false, false, 0, false, false, false, "FFmpeg");
}
