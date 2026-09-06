using System;

namespace NOVORA.LinkEngine.Android.LinkEngine;

public sealed record StatusTransportLE(
    StateTransportLE State,
    string Message,
    string Host,
    int Port,
    string ClientId,
    bool SocketConnected,
    bool HandshakeVerified,
    DateTimeOffset UpdatedAtUtc,
    bool SessionHealthy = false,
    long HeartbeatSequence = 0,
    long HeartbeatCount = 0,
    DateTimeOffset? LastHeartbeatAtUtc = null,
    DateTimeOffset? LastHeartbeatAckAtUtc = null,
    double? RoundTripMilliseconds = null)
{
    public static StatusTransportLE CreateInitialLE(
        string host,
        int port,
        string clientId)
    {
        return new StatusTransportLE(
            State:
                StateTransportLE.Stopped,

            Message:
                "LinkEngine Android detenido.",

            Host:
                host,

            Port:
                port,

            ClientId:
                clientId,

            SocketConnected:
                false,

            HandshakeVerified:
                false,

            UpdatedAtUtc:
                DateTimeOffset.UtcNow,

            SessionHealthy:
                false,

            HeartbeatSequence:
                0,

            HeartbeatCount:
                0,

            LastHeartbeatAtUtc:
                null,

            LastHeartbeatAckAtUtc:
                null,

            RoundTripMilliseconds:
                null);
    }
}