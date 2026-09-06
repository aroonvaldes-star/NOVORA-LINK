using System;

namespace NOVORA.LinkEngine.Core;

public sealed record StatusCoreLE(
    string Serial,
    StatesCoreLE State,
    bool DeviceConnected,
    bool TransportOpen,
    bool InternetActive,
    bool Healthy,
    DateTimeOffset UpdatedAtUtc);
