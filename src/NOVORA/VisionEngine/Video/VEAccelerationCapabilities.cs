namespace NOVORA.VisionEngine.Video;

public sealed record VEAccelerationCapabilities(
    bool D3D11VA,
    bool NVDEC,
    bool IntelQSV,
    bool NvidiaGpu,
    bool IntelGpu,
    bool AmdGpu)
{
    public static VEAccelerationCapabilities NoneVE() => new(false, false, false, false, false, false);

    public bool SupportsVE(VEAccelerationBackend backend) => backend switch
    {
        VEAccelerationBackend.D3D11VA => D3D11VA,
        VEAccelerationBackend.NvidiaNvdec => NVDEC && NvidiaGpu,
        VEAccelerationBackend.Intel => IntelQSV && IntelGpu,
        VEAccelerationBackend.Software => true,
        _ => false
    };
}
