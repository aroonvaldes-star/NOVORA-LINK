using NOVORA.Control;
using NOVORA.VisionEngine.Performance;

namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Puente entre el contrato compartido de aceleración y los tipos internos de
/// VisionEngine. Un perfil ofrece como máximo dos opciones: hardware del
/// proveedor detectado y software.
/// </summary>
public static class VEAccelerationProfile
{
    public static string Vendor(VEAccelerationCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (capabilities.NvidiaGpu && capabilities.NVDEC) return "Nvidia";
        if (capabilities.IntelGpu && capabilities.IntelQSV) return "Intel";
        if (capabilities.AmdGpu) return "Amd";
        return "Unknown";
    }

    public static VEAccelerationBackend Backend(string? backend) => backend switch
    {
        "NvidiaNvdec" => VEAccelerationBackend.NvidiaNvdec,
        "qsv" => VEAccelerationBackend.Intel,
        "amf" => VEAccelerationBackend.Amd,
        "software" => VEAccelerationBackend.Software,
        _ => VEAccelerationBackend.Software
    };

    public static IReadOnlyList<NLAccelerationChoice> Options(
        VEProfile profile,
        VEAccelerationCapabilities capabilities,
        NLAccelerationCustomProfile? custom = null) =>
        NLAccelerationProfile.OptionsFor("windows", Vendor(capabilities), profile.ToString(), custom);

    /// <summary>Crea un perfil personalizado limitado a los backends implementados en Windows.</summary>
    public static NLAccelerationCustomProfile? CreateCustom(string? vendor, string? backend) =>
        NLAccelerationProfile.CreateCustom("windows", vendor, backend);

    public static VEAccelerationRequest Select(
        VEProfile profile,
        VEAccelerationCapabilities capabilities,
        NLAccelerationMode mode = NLAccelerationMode.Automatic,
        NLAccelerationCustomProfile? custom = null)
    {
        NLAccelerationChoice choice = NLAccelerationProfile.Select(
            "windows", Vendor(capabilities), profile.ToString(), mode, custom);
        VEAccelerationBackend backend = Backend(choice.Backend);
        if (!capabilities.SupportsVE(backend))
            backend = VEAccelerationBackend.Software;
        return new VEAccelerationRequest(backend);
    }
}
