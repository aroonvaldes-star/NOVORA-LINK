using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Globalization;

namespace NOVORA.Control;

/// <summary>Existing LinkEngine CONTROL framing and RelayCore raw IPv4 DATA stream.</summary>
public static class NLControlVpnProtocol
{
    public static async Task<uint> ReadRelayIdAsync(Stream stream, CancellationToken token)
    {
        byte[] id = new byte[4];
        await stream.ReadExactlyAsync(id, token);
        return BinaryPrimitives.ReadUInt32BigEndian(id);
    }
    public static async Task WriteControlAsync(Stream stream, string text, CancellationToken token)
    {
        byte[] payload = Encoding.UTF8.GetBytes(text);
        if (payload.Length is < 1 or > 1024) throw new InvalidDataException("CONTROL fuera de límite.");
        byte[] frame = new byte[payload.Length + 4];
        BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        await stream.WriteAsync(frame, token);
    }
    public static async Task<string> ReadControlAsync(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 1 or > 1024) throw new InvalidDataException("CONTROL fuera de límite.");
        byte[] body = new byte[length];
        await stream.ReadExactlyAsync(body, token);
        return new UTF8Encoding(false, true).GetString(body);
    }
    public static async Task HeartbeatAsync(Stream stream, CancellationToken token)
    {
        for (long sequence = 1; ; sequence++)
        {
            token.ThrowIfCancellationRequested();
            long sent = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            await WriteControlAsync(stream, FormattableString.Invariant($"NOVORA-LINK|1|HEARTBEAT|{sequence}|{sent}"), timeout.Token);
            string[] ack = (await ReadControlAsync(stream, timeout.Token)).Split('|');
            if (ack.Length != 6 || ack[0] != "NOVORA-LINK" || ack[1] != "1" || ack[2] != "HEARTBEAT_ACK" ||
                ack[3] != sequence.ToString(CultureInfo.InvariantCulture) || ack[4] != sent.ToString(CultureInfo.InvariantCulture) ||
                !long.TryParse(ack[5], NumberStyles.None, CultureInfo.InvariantCulture, out long serverTime) || serverTime <= 0)
                throw new InvalidDataException("LinkEngine no confirmó el heartbeat.");
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
    }
    public static int ValidateIpv4(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 20 || packet[0] >> 4 != 4) throw new InvalidDataException("El relay requiere IPv4.");
        int headerLength = (packet[0] & 15) * 4;
        int length = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2));
        if (headerLength < 20 || length < headerLength || length > packet.Length)
            throw new InvalidDataException("Paquete IPv4 truncado o inválido.");
        return length;
    }
    public static async Task<int> ReadPacketAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        if (buffer.Length < 65535) throw new ArgumentException("Se requiere un buffer IPv4 completo.", nameof(buffer));
        await stream.ReadExactlyAsync(buffer.AsMemory(0, 20), token);
        int length = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2, 2));
        int headerLength = (buffer[0] & 15) * 4;
        if (buffer[0] >> 4 != 4 || headerLength < 20 || length < headerLength)
            throw new InvalidDataException("Cabecera IPv4 inválida en DATA.");
        await stream.ReadExactlyAsync(buffer.AsMemory(20, length - 20), token);
        return length;
    }
}
