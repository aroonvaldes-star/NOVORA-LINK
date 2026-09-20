namespace NOVORA.NVIDIA;

public sealed record NLNVIDIACapabilities(
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
        false; // Propiedad histórica: las capacidades no prueban ejecución ni zero-copy.

    public static NLNVIDIACapabilities NoneVE()
        => new(false, false, 0, 0, false, false, 0, false, false, false, "FFmpeg");
}
