using System;

namespace NOVORA.VisionEngine.Device;

/// <summary>
/// Identidad estable del Android validado para una sesión de VisionEngine.
/// </summary>
public sealed record SessionDeviceVE(
    string Serial,
    CapabilitiesDeviceVE Capabilities,
    DateTimeOffset ConnectedAtUtc);
