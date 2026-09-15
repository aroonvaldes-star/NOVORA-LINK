using System;

namespace NOVORA.VisionEngine.Device;

/// <summary>
/// Snapshot observable del dispositivo usado por VisionEngine.
/// </summary>
public sealed record VEDeviceStatus(
    VEDeviceStates State,
    string? Serial,
    VEDeviceCapabilities? Capabilities,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public static VEDeviceStatus CreateInitialVE()
        => new(
            State: VEDeviceStates.Disconnected,
            Serial: null,
            Capabilities: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Message: "VisionEngine no tiene dispositivo.",
            LastError: null);
}
