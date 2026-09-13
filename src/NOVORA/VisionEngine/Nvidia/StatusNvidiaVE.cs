namespace NOVORA.VisionEngine.Nvidia;

public sealed record StatusNvidiaVE(
    CapabilitiesNvidiaVE Capabilities,
    PipelineNvidiaVE Pipeline,
    DateTimeOffset UpdatedAtUtc,
    string Message);
