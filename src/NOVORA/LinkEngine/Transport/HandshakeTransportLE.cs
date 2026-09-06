using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.LinkEngine.Transport;

public static class HandshakeTransportLE
{
    public const string ProtocolNameLE = "NOVORA-LINK";
    public const int ProtocolVersionLE = 1;

    private const int MaxPayloadLengthLE = 1024;

    public static byte[] CreateHelloLE(
        string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        return CreateFrameLE(
            $"{ProtocolNameLE}|{ProtocolVersionLE}|HELLO|{clientId.Trim()}");
    }

    public static byte[] CreateAckLE()
    {
        return CreateFrameLE(
            $"{ProtocolNameLE}|{ProtocolVersionLE}|ACK");
    }

    public static string CreateAckPayloadLE()
    {
        return $"{ProtocolNameLE}|{ProtocolVersionLE}|ACK";
    }

    public static bool TryParseHelloLE(
        string payload,
        out HandshakeHelloLE? hello,
        out string? error)
    {
        hello = null;
        error = null;

        if (string.IsNullOrWhiteSpace(payload))
        {
            error = "HELLO vacío.";
            return false;
        }

        string[] parts =
            payload.Split(
                '|',
                StringSplitOptions.None);

        if (parts.Length != 4)
        {
            error =
                "HELLO inválido. Se esperaban 4 campos.";
            return false;
        }

        if (!string.Equals(
                parts[0],
                ProtocolNameLE,
                StringComparison.Ordinal))
        {
            error =
                $"Protocolo inválido: {parts[0]}.";
            return false;
        }

        if (!int.TryParse(
                parts[1],
                out int version))
        {
            error =
                "La versión del protocolo no es válida.";
            return false;
        }

        if (version != ProtocolVersionLE)
        {
            error =
                $"Versión incompatible: {version}. " +
                $"NOVORA requiere {ProtocolVersionLE}.";
            return false;
        }

        if (!string.Equals(
                parts[2],
                "HELLO",
                StringComparison.Ordinal))
        {
            error =
                $"Mensaje inesperado: {parts[2]}.";
            return false;
        }

        string clientId =
            parts[3].Trim();

        if (string.IsNullOrWhiteSpace(clientId))
        {
            error =
                "HELLO no contiene ClientIdLE.";
            return false;
        }

        hello =
            new HandshakeHelloLE(
                ProtocolNameLE,
                version,
                clientId);

        return true;
    }

    public static bool IsAckLE(
        string payload)
    {
        return string.Equals(
            payload?.Trim(),
            CreateAckPayloadLE(),
            StringComparison.Ordinal);
    }

    public static async Task<string> ReadFrameLEAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] lengthBuffer =
            new byte[sizeof(int)];

        await ReadExactlyLEAsync(
                stream,
                lengthBuffer,
                cancellationToken)
            .ConfigureAwait(false);

        int length =
            BinaryPrimitives.ReadInt32BigEndian(
                lengthBuffer);

        if (length <= 0 ||
            length > MaxPayloadLengthLE)
        {
            throw new InvalidDataException(
                $"HandshakeTransportLE recibió una longitud inválida: {length}.");
        }

        byte[] payload =
            new byte[length];

        await ReadExactlyLEAsync(
                stream,
                payload,
                cancellationToken)
            .ConfigureAwait(false);

        return Encoding.UTF8.GetString(
            payload);
    }

    public static async Task WriteFrameLEAsync(
        Stream stream,
        string payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        byte[] frame =
            CreateFrameLE(payload);

        await stream.WriteAsync(
                frame,
                cancellationToken)
            .ConfigureAwait(false);

        await stream
            .FlushAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static byte[] CreateFrameLE(
        string payload)
    {
        byte[] payloadBytes =
            Encoding.UTF8.GetBytes(payload);

        if (payloadBytes.Length <= 0 ||
            payloadBytes.Length > MaxPayloadLengthLE)
        {
            throw new InvalidOperationException(
                "HandshakeTransportLE generó un payload fuera del límite permitido.");
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
            frame.AsSpan(sizeof(int)));

        return frame;
    }

    private static async Task ReadExactlyLEAsync(
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
                await stream.ReadAsync(
                        buffer[totalRead..],
                        cancellationToken)
                    .ConfigureAwait(false);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "HandshakeTransportLE perdió el canal antes de recibir el frame completo.");
            }

            totalRead +=
                read;
        }
    }
}

public sealed record HandshakeHelloLE(
    string Protocol,
    int Version,
    string ClientId);