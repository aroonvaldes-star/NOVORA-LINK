namespace NOVORA.VisionEngine.Video;

public enum VEAccelerationBackend
{
    Automatic,
    D3D11VA,
    NvidiaNvdec,
    Amd,
    Intel,
    Software
}

public sealed record VEAccelerationState(
    VEAccelerationBackend Backend,
    bool Available,
    bool Selected,
    bool Active,
    bool? BenefitMeasured,
    string Evidence)
{
    public static VEAccelerationState FromDecoder(string decoderName, long framesDecoded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decoderName);
        VEAccelerationBackend backend = decoderName.EndsWith("_cuvid", StringComparison.Ordinal)
            ? VEAccelerationBackend.NvidiaNvdec
            : decoderName.EndsWith("_amf", StringComparison.Ordinal)
                ? VEAccelerationBackend.Amd
                : decoderName.EndsWith("_qsv", StringComparison.Ordinal)
                    ? VEAccelerationBackend.Intel
                    : VEAccelerationBackend.Software;
        bool selected = !string.Equals(decoderName, "Sin decoder", StringComparison.Ordinal);
        return new(
            backend,
            Available: selected,
            Selected: selected,
            Active: selected && framesDecoded > 0,
            BenefitMeasured: null,
            Evidence: selected ? $"Decoder abierto: {decoderName}." : "Sin decoder abierto.");
    }

    public static VEAccelerationState Unavailable(VEAccelerationBackend backend, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(backend, false, false, false, null, reason);
    }
}
