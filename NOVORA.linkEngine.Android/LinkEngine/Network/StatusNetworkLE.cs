using System;

namespace NOVORA.LinkEngine.Android.LinkEngine;

public sealed record StatusNetworkLE(
    StateNetworkLE State,
    string Message,
    bool ControlConnected,
    bool HandshakeVerified,
    bool VpnActive,
    bool DataConnected,
    int? RelayClientId,
    DateTimeOffset UpdatedAtUtc)
{
    public static StatusNetworkLE CreateInitialLE()
    {
        return new StatusNetworkLE(
            State:
                StateNetworkLE.Stopped,

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