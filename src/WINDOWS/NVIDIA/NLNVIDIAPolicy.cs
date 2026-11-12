namespace NOVORA.NVIDIA;

public static class NLNVIDIAPolicy
{
    public static NLNVIDIAPipeline BuildVE(NLNVIDIAProfile profile, NLNVIDIACapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!Enum.IsDefined(profile)) throw new ArgumentOutOfRangeException(nameof(profile));
        // La detección sola no demuestra frames NVDEC; el manager recibe evidencia de VE.
        // Presencia de DLL/API no autoriza anunciar funciones activas.
        return NLNVIDIAPipeline.FallbackVE();
    }
}