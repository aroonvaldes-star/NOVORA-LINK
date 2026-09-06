using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.LinkEngine.Android.LinkEngine;

public static class HandshakeLE
{
    public const string ProtocolNameLE =
        "NOVORA-LINK";

    public const int ProtocolVersionLE =
        1;

    private const int MaxPayloadLengthLE =
        1024;

    public static string CreateHelloPayloadLE(
        string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            clientId);

        return
            $"{ProtocolNameLE}|" +
            $"{ProtocolVersionLE}|" +
            $"HELLO|" +
            $"{clientId.Trim()}";
    }

    public static string CreateAckPayloadLE()
    {
        return
            $"{ProtocolNameLE}|" +
            $"{ProtocolVersionLE}|" +
            "ACK";
    }

    public static bool IsValidAckLE(
        string? payload)
    {
        if (string.IsNullOrWhiteSpace(
                payload))
        {
            return false;
        }

        return string.Equals(
            payload.Trim(),
            CreateAckPayloadLE(),
            StringComparison.Ordinal);
    }

    public static async Task<string> ReadFrameLEAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            stream);

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
                $"HandshakeLE recibió una longitud inválida: {length}.");
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
        ArgumentNullException.ThrowIfNull(
            stream);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            payload);

        byte[] frame =
            CreateFrameLE(
                payload);

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

    private static byte[] CreateFrameLE(
        string payload)
    {
        byte[] payloadBytes =
            Encoding.UTF8.GetBytes(
                payload);

        if (payloadBytes.Length <= 0 ||
            payloadBytes.Length > MaxPayloadLengthLE)
        {
            throw new InvalidOperationException(
                "HandshakeLE generó un payload fuera del límite permitido.");
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
                await stream
                    .ReadAsync(
                        buffer[totalRead..],
                        cancellationToken)
                    .ConfigureAwait(false);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "HandshakeLE perdió el canal antes de recibir el frame completo.");
            }

            totalRead +=
                read;
        }
    }
}
