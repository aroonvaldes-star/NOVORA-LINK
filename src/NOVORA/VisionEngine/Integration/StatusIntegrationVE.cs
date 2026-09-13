namespace NOVORA.VisionEngine.Integration;

public sealed record StatusIntegrationVE(
    CapabilitiesIntegrationVE Capabilities,
    DateTimeOffset UpdatedAtUtc);
