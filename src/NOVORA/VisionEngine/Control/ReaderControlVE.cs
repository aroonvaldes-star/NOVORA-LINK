using System.Buffers.Binary;
using System.Text;

namespace NOVORA.VisionEngine.Control;

/// <summary>
/// Deserializa respuestas Android -> PC del canal de control.
/// </summary>
public sealed class ReaderControlVE
{
    private const int MaxMessageSizeVE = 1 << 18;

    private readonly Stream _stream;

    public ReaderControlVE(Stream stream)
    {
        _stream = stream
            ?? throw new ArgumentNullException(nameof(stream));
    }

    public async Task<MessageDeviceControlVE> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        byte[] typeBuffer = new byte[1];

        await _stream
            .ReadExactlyAsync(typeBuffer, cancellationToken)
            .ConfigureAwait(false);

        TypeDeviceControlVE type =
            (TypeDeviceControlVE)typeBuffer[0];

        return type switch
        {
            TypeDeviceControlVE.Clipboard =>
                await ReadClipboardVE(cancellationToken)
                    .ConfigureAwait(false),

            TypeDeviceControlVE.ClipboardAck =>
                await ReadAckVE(cancellationToken)
                    .ConfigureAwait(false),

            TypeDeviceControlVE.UhidOutput =>
                await ReadUhidOutputVE(cancellationToken)
                    .ConfigureAwait(false),

            _ => throw new InvalidDataException(
                $"Mensaje Android VisionEngine desconocido: {(byte)type}.")
        };
    }

    private async Task<MessageDeviceControlVE> ReadClipboardVE(
        CancellationToken cancellationToken)
    {
        byte[] sizeBuffer = new byte[4];

        await _stream
            .ReadExactlyAsync(sizeBuffer, cancellationToken)
            .ConfigureAwait(false);

        int size = checked(
            (int)BinaryPrimitives.ReadUInt32BigEndian(sizeBuffer));

        if (size < 0 ||
            size > MaxMessageSizeVE - 5)
        {
            throw new InvalidDataException(
                $"Clipboard VisionEngine demasiado grande: {size} bytes.");
        }

        byte[] data = new byte[size];

        await _stream
            .ReadExactlyAsync(data, cancellationToken)
            .ConfigureAwait(false);

        return new MessageDeviceControlVE(
            TypeDeviceControlVE.Clipboard,
            Encoding.UTF8.GetString(data),
            null,
            null,
            null);
    }

    private async Task<MessageDeviceControlVE> ReadAckVE(
        CancellationToken cancellationToken)
    {
        byte[] data = new byte[8];

        await _stream
            .ReadExactlyAsync(data, cancellationToken)
            .ConfigureAwait(false);

        return new MessageDeviceControlVE(
            TypeDeviceControlVE.ClipboardAck,
            null,
            BinaryPrimitives.ReadUInt64BigEndian(data),
            null,
            null);
    }

    private async Task<MessageDeviceControlVE> ReadUhidOutputVE(
        CancellationToken cancellationToken)
    {
        byte[] header = new byte[4];

        await _stream
            .ReadExactlyAsync(header, cancellationToken)
            .ConfigureAwait(false);

        ushort id =
            BinaryPrimitives.ReadUInt16BigEndian(
                header.AsSpan(0, 2));

        ushort size =
            BinaryPrimitives.ReadUInt16BigEndian(
                header.AsSpan(2, 2));

        // El tamaño UHID viene codificado en uint16 por el protocolo.
        // Por definición no puede superar 65535 bytes, que además está
        // por debajo del límite global de 256 KiB de mensajes de control.
        byte[] data = new byte[size];

        await _stream
            .ReadExactlyAsync(data, cancellationToken)
            .ConfigureAwait(false);

        return new MessageDeviceControlVE(
            TypeDeviceControlVE.UhidOutput,
            null,
            null,
            id,
            data);
    }
}
