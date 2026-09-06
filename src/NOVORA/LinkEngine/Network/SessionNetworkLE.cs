using System;

namespace NOVORA.LinkEngine.Network;

public sealed record SessionNetworkLE(
    string Serial,
    StateNetworkLE State,
    bool RelayRunning,
    bool DataReverseConfigured,
    int DataPort,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public static SessionNetworkLE CreateLE(
        string serial,
        int dataPort)
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        return new SessionNetworkLE(
            Serial:
                serial,

            State:
                StateNetworkLE.Stopped,

            RelayRunning:
                false,

            DataReverseConfigured:
                false,

            DataPort:
                dataPort,

            StartedAtUtc:
                now,

            UpdatedAtUtc:
                now,

            Message:
                "ManagerNetworkLE detenido.",

            LastError:
                null);
    }
}