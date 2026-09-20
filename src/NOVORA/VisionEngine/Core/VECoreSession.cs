using System;

namespace NOVORA.VisionEngine.Core;

/// <summary>
/// Sesión lógica de VisionEngine.
///
/// No contiene superficies gráficas ni recursos de renderizado. Sólo
/// identifica el dispositivo y el ciclo de vida de la sesión headless.
/// </summary>
public sealed record VECoreSession(
    Guid SessionId,
    string DeviceSerial,
    VECoreStates State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? StoppedAtUtc,
    string Message,
    string? LastError)
{
    public static VECoreSession CreateVE(
        string deviceSerial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            deviceSerial);

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        return new VECoreSession(
            SessionId: Guid.NewGuid(),
            DeviceSerial: deviceSerial.Trim(),
            State: VECoreStates.Starting,
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            StartedAtUtc: null,
            StoppedAtUtc: null,
            Message: "Preparando sesión VisionEngine.",
            LastError: null);
    }
}
