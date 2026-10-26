namespace NOVORA.NVIDIA;

public sealed record NLNVIDIAStatus(
    NLNVIDIACapabilities Capabilities,
    NLNVIDIAPipeline Pipeline,
    DateTimeOffset UpdatedAtUtc,
    string Message);
