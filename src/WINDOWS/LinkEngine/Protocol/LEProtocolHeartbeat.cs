using System;
using System.Globalization;
using NOVORA.LinkEngine.Transport;

namespace NOVORA.LinkEngine.Protocol;

public static class LEProtocolHeartbeat
{
    public const string HeartbeatMessageLE =
        "HEARTBEAT";

    public const string HeartbeatAckMessageLE =
        "HEARTBEAT_ACK";

    public static readonly TimeSpan ExpectedIntervalLE =
        TimeSpan.FromSeconds(2);

    public static readonly TimeSpan HealthTimeoutLE =
        TimeSpan.FromSeconds(5);

    public static string CreateAckPayloadLE(
        long sequence,
        long clientUnixMilliseconds)
    {
        long serverUnixMilliseconds =
            DateTimeOffset.UtcNow
                .ToUnixTimeMilliseconds();

        return
            $"{LETransportHandshake.ProtocolNameLE}|" +
            $"{LETransportHandshake.ProtocolVersionLE}|" +
            $"{HeartbeatAckMessageLE}|" +
            $"{sequence.ToString(CultureInfo.InvariantCulture)}|" +
            $"{clientUnixMilliseconds.ToString(CultureInfo.InvariantCulture)}|" +
            $"{serverUnixMilliseconds.ToString(CultureInfo.InvariantCulture)}";
    }

    public static bool TryParseHeartbeatLE(
        string? payload,
        out long sequence,
        out long clientUnixMilliseconds,
        out string error)
    {
        sequence = 0;
        clientUnixMilliseconds = 0;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(payload))
        {
            error =
                "HEARTBEAT vacio.";

            return false;
        }

        string[] parts =
            payload
                .Trim()
                .Split(
                    '|',
                    StringSplitOptions.None);

        if (parts.Length != 5)
        {
            error =
                $"HEARTBEAT invalido: se esperaban 5 campos y llegaron {parts.Length}.";

            return false;
        }

        if (!string.Equals(
                parts[0],
                LETransportHandshake.ProtocolNameLE,
                StringComparison.Ordinal))
        {
            error =
                $"Protocolo heartbeat invalido: '{parts[0]}'.";

            return false;
        }

        if (!int.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int version) ||
            version !=
            LETransportHandshake.ProtocolVersionLE)
        {
            error =
                $"Version heartbeat incompatible: '{parts[1]}'.";

            return false;
        }

        if (!string.Equals(
                parts[2],
                HeartbeatMessageLE,
                StringComparison.Ordinal))
        {
            error =
                $"Mensaje inesperado durante sesion persistente: '{parts[2]}'.";

            return false;
        }

        if (!long.TryParse(
                parts[3],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out sequence) ||
            sequence <= 0)
        {
            error =
                $"Secuencia heartbeat invalida: '{parts[3]}'.";

            return false;
        }

        if (!long.TryParse(
                parts[4],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out clientUnixMilliseconds) ||
            clientUnixMilliseconds <= 0)
        {
            error =
                $"Timestamp heartbeat invalido: '{parts[4]}'.";

            return false;
        }

        return true;
    }
}
