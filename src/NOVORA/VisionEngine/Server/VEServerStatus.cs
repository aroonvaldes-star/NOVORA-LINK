using System;

namespace NOVORA.VisionEngine.Server;

/// <summary>
/// Snapshot del proceso servidor Android compatible con VisionEngine.
/// </summary>
public sealed record VEServerStatus(
    VEServerStates State,
    string? Serial,
    int? Scid,
    VEServerBackend Backend,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public static VEServerStatus CreateInitialVE()
        => new(
            State: VEServerStates.Stopped,
            Serial: null,
            Scid: null,
            Backend: VEServerBackend.Scrcpy41Compatibility,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Message: "VisionEngine Android server detenido.",
            LastError: null);
}
