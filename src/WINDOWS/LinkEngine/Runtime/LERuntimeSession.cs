using System;
using NOVORA.LinkEngine.Device;
using NOVORA.LinkEngine.Recovery;
using NOVORA.LinkEngine.Transport;

namespace NOVORA.LinkEngine.Runtime;

/// <summary>
/// Snapshot inmutable del estado observable del runtime.
/// </summary>
public sealed record LERuntimeSession(
    string Serial,
    LERuntimeState State,
    bool DeviceOnline,
    LEDeviceConnection ConnectionType,
    bool TransportOpen,
    LETransportState TransportState,
    bool ReverseConfigured,
    bool ReverseVerified,
    bool ListenerStarted,
    bool ClientConnected,
    bool HandshakeVerified,
    bool SessionHealthy,
    string? ClientId,
    long HeartbeatSequence,
    long HeartbeatCount,
    LERecoveryMonitorState RecoveryState,
    long RecoveryAttemptCount,
    long RecoverySuccessCount,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ConnectedAtUtc,
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? LastRecoveryAtUtc,
    string Message,
    string? LastError)
{
    public static LERuntimeSession CreateInitialLE(
        string serial)
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        return new LERuntimeSession(
            Serial:
                serial,

            State:
                LERuntimeState.Stopped,

            DeviceOnline:
                false,

            ConnectionType:
                LEDeviceConnection.Unknown,

            TransportOpen:
                false,

            TransportState:
                LETransportState.Closed,

            ReverseConfigured:
                false,

            ReverseVerified:
                false,

            ListenerStarted:
                false,

            ClientConnected:
                false,

            HandshakeVerified:
                false,

            SessionHealthy:
                false,

            ClientId:
                null,

            HeartbeatSequence:
                0,

            HeartbeatCount:
                0,

            RecoveryState:
                LERecoveryMonitorState.Stopped,

            RecoveryAttemptCount:
                0,

            RecoverySuccessCount:
                0,

            StartedAtUtc:
                now,

            UpdatedAtUtc:
                now,

            ConnectedAtUtc:
                null,

            LastHeartbeatAtUtc:
                null,

            LastRecoveryAtUtc:
                null,

            Message:
                "LinkEngine Runtime detenido.",

            LastError:
                null);
    }
}
