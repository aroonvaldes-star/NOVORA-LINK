using System;

namespace NOVORA.LinkEngine.Network;

public sealed record LENetworkSession(
    string Serial,
    LENetworkState State,
    bool RelayRunning,
    bool DataReverseConfigured,
    int DataPort,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public static LENetworkSession CreateLE(
        string serial,
        int dataPort)
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        return new LENetworkSession(
            Serial:
                serial,

            State:
                LENetworkState.Stopped,

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
                "LENetworkManager detenido.",

            LastError:
                null);
    }
}