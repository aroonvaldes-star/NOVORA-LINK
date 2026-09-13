using System.Buffers.Binary;
using System.Text;

namespace NOVORA.LinkEngine.Android.Remote;

public static class ProtocolRemoteNV
{
    public const string ProtocolNameNV =
        "NOVORA-REMOTE";

    public const int ProtocolVersionNV =
        2;

    public const int DefaultPortNV =
        27182;

    public const int DefaultFileTransferPortNV =
        27186;

    private const int MaxPayloadBytesNV =
        64 * 1024;

    public static string CreateHelloNV(string sessionToken)
    {
        if (sessionToken is null || sessionToken.Length != 64 || !sessionToken.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Conecta el dispositivo a NOVORA PC para autorizar la sesión.");
        return $"{ProtocolNameNV}|{ProtocolVersionNV}|HELLO|{sessionToken}";
    }

    public static bool IsWelcomeNV(
        string? payload)
    {
        return string.Equals(
            payload?.Trim(),
            $"{ProtocolNameNV}|{ProtocolVersionNV}|WELCOME",
            StringComparison.Ordinal);
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

    public static bool TryParseResultNV(
        string? payload,
        string expectedRequestId,
        out ResultRemoteNV result,
        out string error)
    {
        result =
            ResultRemoteNV.FailNV(
                "Resultado remoto inválido.");

        error =
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                payload))
        {
            error =
                "RESULT vacío.";

            return false;
        }

        string[] parts =
            payload.Trim().Split(
                '|',
                StringSplitOptions.None);

        if (parts.Length != 6)
        {
            error =
                $"RESULT inválido: se esperaban 6 campos y llegaron {parts.Length}.";

            return false;
        }

        if (!string.Equals(
                parts[0],
                ProtocolNameNV,
                StringComparison.Ordinal) ||
            !int.TryParse(
                parts[1],
                out int version) ||
            version != ProtocolVersionNV ||
            !string.Equals(
                parts[2],
                "RESULT",
                StringComparison.Ordinal))
        {
            error =
                "Cabecera RESULT inválida.";

            return false;
        }

        if (!string.Equals(
                parts[3],
                expectedRequestId,
                StringComparison.Ordinal))
        {
            error =
                "RESULT pertenece a otra solicitud.";

            return false;
        }

        bool success;

        if (string.Equals(
                parts[4],
                "OK",
                StringComparison.Ordinal))
        {
            success =
                true;
        }
        else if (string.Equals(
                     parts[4],
                     "FAIL",
                     StringComparison.Ordinal))
        {
            success =
                false;
        }
        else
        {
            error =
                "Estado RESULT inválido.";

            return false;
        }

        try
        {
            string message =
                Encoding.UTF8.GetString(
                    Convert.FromBase64String(
                        parts[5]));

            result =
                new ResultRemoteNV(
                    success,
                    message);

            return true;
        }
        catch (Exception ex)
        {
            error =
                $"Mensaje RESULT inválido: {ex.Message}";

            return false;
        }
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
                    "NOVORA PC cerró el canal remoto.");
            }

            totalRead +=
                read;
        }
    }
}
