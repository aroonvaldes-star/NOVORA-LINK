using System;

namespace NOVORA.VisionEngine.Device;

/// <summary>
/// Identidad estable del Android validado para una sesión de VisionEngine.
/// </summary>
public sealed record VEDeviceSession(
    string Serial,
    VEDeviceCapabilities Capabilities,
    DateTimeOffset ConnectedAtUtc);
