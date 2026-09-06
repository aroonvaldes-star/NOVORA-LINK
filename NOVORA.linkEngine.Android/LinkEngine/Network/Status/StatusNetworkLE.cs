using System;

namespace NOVORA.LinkEngine.Android.LinkEngine;

public sealed record NetworkStatusLE(
    NetworkStateLE State,
    string Message,
    bool ControlConnected,
    bool HandshakeVerified,
    bool VpnActive,
    bool DataConnected,
    int? RelayClientId,
    DateTimeOffset UpdatedAtUtc)
{
    public static NetworkStatusLE CreateInitialLE()
    {
        return new NetworkStatusLE(
            State:
                NetworkStateLE.Stopped,

            Message:
                "LinkEngine VPN detenido.",

            ControlConnected:
                false,

            HandshakeVerified:
                false,

            VpnActive:
                false,

            DataConnected:
                false,

            RelayClientId:
                null,

            UpdatedAtUtc:
                DateTimeOffset.UtcNow);
    }
}