using System;

namespace NOVORA.VisionEngine.Server;

/// <summary>
/// Snapshot del proceso servidor Android compatible con VisionEngine.
/// </summary>
public sealed record StatusServerVE(
    StatesServerVE State,
    string? Serial,
    int? Scid,
    BackendServerVE Backend,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public static StatusServerVE CreateInitialVE()
        => new(
            State: StatesServerVE.Stopped,
            Serial: null,
            Scid: null,
            Backend: BackendServerVE.Scrcpy41Compatibility,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Message: "VisionEngine Android server detenido.",
            LastError: null);
}
