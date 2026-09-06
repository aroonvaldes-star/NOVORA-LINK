using System;

namespace NOVORA.LinkEngine.Device;

public sealed record SessionDeviceLE(
    string Serial,
    StateDeviceLE State,
    ConnectionDeviceLE ConnectionType,
    bool AdbOnline,
    DateTimeOffset ConnectedAtUtc,
    DateTimeOffset LastSeenUtc,
    string? LastError);
