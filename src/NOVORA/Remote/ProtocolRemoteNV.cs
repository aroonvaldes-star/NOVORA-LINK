using System.Buffers.Binary;
using System.Text;
using System.Security.Cryptography;

namespace NOVORA.Remote;

public static class ProtocolRemoteNV
{
    public const string ProtocolNameNV =
        "NOVORA-REMOTE";

    public const int ProtocolVersionNV =
        2;

    public const int DefaultDevicePortNV =
        27182;

    public const int DefaultFileTransferPortNV =
        27186;

    private const int MaxPayloadBytesNV =
        64 * 1024;

    public static string CreateWelcomeNV()
    {
        return
            $"{ProtocolNameNV}|" +
            $"{ProtocolVersionNV}|WELCOME";
    }

    public static bool IsHelloNV(
        string? payload,
        string sessionToken)
    {
        if (sessionToken is null || sessionToken.Length != 64 ||
            !sessionToken.All(Uri.IsHexDigit)) return false;
        string expected = $"{ProtocolNameNV}|{ProtocolVersionNV}|HELLO|{sessionToken}";
        return payload is not null && payload.Length == expected.Length &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(payload), Encoding.UTF8.GetBytes(expected));
    }

    public static string CreateCommandNV(
        string requestId,
        CommandRemoteNV command,
        string? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            requestId);

        string encodedPayload =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    payload ?? string.Empty));

        return
            $"{ProtocolNameNV}|" +
            $"{ProtocolVersionNV}|COMMAND|" +
            $"{requestId.Trim()}|" +
            $"{command}|" +
            encodedPayload;
    }

    public static bool TryParseCommandNV(
        string? payload,
        out string requestId,
        out CommandRemoteNV command,
        out string commandPayload,
        out string error)
    {
        requestId =
            string.Empty;

        command =
            CommandRemoteNV.Status;

        commandPayload =
            string.Empty;

        error =
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                payload))
        {
            error =
                "Comando remoto vacío.";

            return false;
        }

        string[] parts =
            payload.Trim().Split(
                '|',
                StringSplitOptions.None);

        if (parts.Length != 6)
        {
            error =
                $"COMMAND inválido: se esperaban 6 campos y llegaron {parts.Length}.";

            return false;
        }

        if (!string.Equals(
                parts[0],
                ProtocolNameNV,
                StringComparison.Ordinal))
        {
            error =
                "Nombre de protocolo remoto inválido.";

            return false;
        }

        if (!int.TryParse(
                parts[1],
                out int version) ||
            version != ProtocolVersionNV)
        {
            error =
                "Versión de protocolo remoto incompatible.";

            return false;
        }

        if (!string.Equals(
                parts[2],
                "COMMAND",
                StringComparison.Ordinal))
        {
            error =
                "Se esperaba COMMAND.";

            return false;
        }

        if (string.IsNullOrWhiteSpace(
                parts[3]))
        {
            error =
                "COMMAND no contiene RequestId.";

            return false;
        }

        if (!Enum.TryParse(
                parts[4],
                ignoreCase: true,
                out command))
        {
            error =
                $"Comando remoto desconocido: '{parts[4]}'.";

            return false;
        }

        try
        {
            commandPayload =
                Encoding.UTF8.GetString(
                    Convert.FromBase64String(
                        parts[5]));
        }
        catch (Exception ex)
        {
            error =
                $"Payload COMMAND inválido: {ex.Message}";

            return false;
        }

        requestId =
            parts[3].Trim();

        return true;
    }

    public static string CreateResultNV(
        string requestId,
        ResultRemoteNV result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            requestId);

        ArgumentNullException.ThrowIfNull(
            result);

        string state =
            result.Success
                ? "OK"
                : "FAIL";

        string encodedMessage =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    result.Message ?? string.Empty));

        return
            $"{ProtocolNameNV}|" +
            $"{ProtocolVersionNV}|RESULT|" +
            $"{requestId.Trim()}|" +
            $"{state}|" +
            encodedMessage;
    }

    public static async Task<string> ReadFrameNVAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            stream);

        byte[] lengthBuffer =
            new byte[sizeof(int)];

        await ReadExactlyNVAsync(
                stream,
                lengthBuffer,
                cancellationToken)
            .ConfigureAwait(false);

        int length =
            BinaryPrimitives.ReadInt32BigEndian(
                lengthBuffer);

        if (length <= 0 ||
            length > MaxPayloadBytesNV)
        {
            throw new InvalidDataException(
                $"ProtocolRemoteNV recibió longitud inválida: {length}.");
        }

        byte[] frame =
            new byte[length];

        await ReadExactlyNVAsync(
                stream,
                frame,
                cancellationToken)
            .ConfigureAwait(false);

        return Encoding.UTF8.GetString(
            frame);
    }

    public static async Task WriteFrameNVAsync(
        Stream stream,
        string payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            stream);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            payload);

        byte[] payloadBytes =
            Encoding.UTF8.GetBytes(
                payload);

        if (payloadBytes.Length <= 0 ||
            payloadBytes.Length > MaxPayloadBytesNV)
        {
            throw new InvalidOperationException(
                "ProtocolRemoteNV generó un payload fuera de límite.");
        }

        byte[] frame =
            new byte[
                sizeof(int) +
                payloadBytes.Length];

        BinaryPrimitives.WriteInt32BigEndian(
            frame.AsSpan(
                0,
                sizeof(int)),
            payloadBytes.Length);

        payloadBytes.CopyTo(
            frame.AsSpan(
                sizeof(int)));

        await stream
            .WriteAsync(
                frame,
                cancellationToken)
            .ConfigureAwait(false);

        await stream
            .FlushAsync(
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ReadExactlyNVAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        int totalRead =
            0;

        while (totalRead <
               buffer.Length)
        {
            int read =
                await stream
                    .ReadAsync(
                        buffer[totalRead..],
                        cancellationToken)
                    .ConfigureAwait(false);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "El canal remoto se cerró antes de completar el frame.");
            }

            totalRead +=
                read;
        }
    }
}
