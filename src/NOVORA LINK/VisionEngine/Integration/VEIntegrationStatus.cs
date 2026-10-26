namespace NOVORA.VisionEngine.Integration;

public sealed record VEIntegrationStatus(
    VEIntegrationCapabilities Capabilities,
    DateTimeOffset UpdatedAtUtc);
