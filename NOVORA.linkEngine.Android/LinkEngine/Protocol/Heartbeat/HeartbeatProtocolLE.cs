using System;
using System.Globalization;

namespace NOVORA.LinkEngine.Android.LinkEngine;

public static class HeartbeatLE
{
    public const string HeartbeatMessageLE =
        "HEARTBEAT";

    public const string HeartbeatAckMessageLE =
        "HEARTBEAT_ACK";

    public static readonly TimeSpan IntervalLE =
        TimeSpan.FromSeconds(2);

    public static readonly TimeSpan AckTimeoutLE =
        TimeSpan.FromSeconds(5);

    public static string CreateHeartbeatPayloadLE(
        long sequence,
        long clientUnixMilliseconds)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence));
        }

        if (clientUnixMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(clientUnixMilliseconds));
        }

        return
            $"{HandshakeLE.ProtocolNameLE}|" +
            $"{HandshakeLE.ProtocolVersionLE}|" +
            $"{HeartbeatMessageLE}|" +
            $"{sequence.ToString(CultureInfo.InvariantCulture)}|" +
            $"{clientUnixMilliseconds.ToString(CultureInfo.InvariantCulture)}";
    }

    public static bool TryParseAckLE(
        string? payload,
        long expectedSequence,
        long expectedClientUnixMilliseconds,
        out long serverUnixMilliseconds,
        out string error)
    {
        serverUnixMilliseconds =
            0;

        error =
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                payload))
        {
            error =
                "HEARTBEAT_ACK vacío.";

            return false;
        }

        string[] parts =
            payload
                .Trim()
                .Split(
                    '|',
                    StringSplitOptions.None);

        if (parts.Length != 6)
        {
            error =
                $"HEARTBEAT_ACK inválido: se esperaban 6 campos y llegaron {parts.Length}.";

            return false;
        }

        if (!string.Equals(
                parts[0],
                HandshakeLE.ProtocolNameLE,
                StringComparison.Ordinal))
        {
            error =
                $"Protocolo inválido: '{parts[0]}'.";

            return false;
        }

        if (!int.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int version) ||
            version !=
            HandshakeLE.ProtocolVersionLE)
        {
            error =
                $"Versión incompatible: '{parts[1]}'.";

            return false;
        }

        if (!string.Equals(
                parts[2],
                HeartbeatAckMessageLE,
                StringComparison.Ordinal))
        {
            error =
                $"Se esperaba HEARTBEAT_ACK y llegó '{parts[2]}'.";

            return false;
        }

        if (!long.TryParse(
                parts[3],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long sequence) ||
            sequence !=
            expectedSequence)
        {
            error =
                $"Secuencia HEARTBEAT_ACK incorrecta. " +
                $"Esperada={expectedSequence}, recibida='{parts[3]}'.";

            return false;
        }

        if (!long.TryParse(
                parts[4],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long echoedClientUnixMilliseconds) ||
            echoedClientUnixMilliseconds !=
            expectedClientUnixMilliseconds)
        {
            error =
                "HEARTBEAT_ACK no corresponde al HEARTBEAT enviado.";

            return false;
        }

        if (!long.TryParse(
                parts[5],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out serverUnixMilliseconds) ||
            serverUnixMilliseconds <= 0)
        {
            error =
                $"Timestamp Windows inválido: '{parts[5]}'.";

            return false;
        }

        return true;
    }
}
