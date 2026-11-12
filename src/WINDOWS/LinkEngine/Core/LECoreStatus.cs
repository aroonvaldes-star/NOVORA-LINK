using System;

namespace NOVORA.LinkEngine.Core;

public sealed record LECoreStatus(
    string Serial,
    LECoreStates State,
    bool DeviceConnected,
    bool TransportOpen,
    bool InternetActive,
    bool Healthy,
    DateTimeOffset UpdatedAtUtc);
