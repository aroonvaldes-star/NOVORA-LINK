using System;

namespace NOVORA.LinkEngine.Device;

public sealed record LEDeviceSession(
    string Serial,
    LEDeviceState State,
    LEDeviceConnection ConnectionType,
    bool AdbOnline,
    DateTimeOffset ConnectedAtUtc,
    DateTimeOffset LastSeenUtc,
    string? LastError);
