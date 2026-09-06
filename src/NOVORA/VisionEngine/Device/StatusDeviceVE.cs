using System;

namespace NOVORA.VisionEngine.Device;

/// <summary>
/// Snapshot observable del dispositivo usado por VisionEngine.
/// </summary>
public sealed record StatusDeviceVE(
    StatesDeviceVE State,
    string? Serial,
    CapabilitiesDeviceVE? Capabilities,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public static StatusDeviceVE CreateInitialVE()
        => new(
            State: StatesDeviceVE.Disconnected,
            Serial: null,
            Capabilities: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Message: "VisionEngine no tiene dispositivo.",
            LastError: null);
}
