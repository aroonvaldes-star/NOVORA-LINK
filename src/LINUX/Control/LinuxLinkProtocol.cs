using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace NOVORA.Linux.Control;

/// <summary>
/// CONTROL framing and DATA authentication shared by the USB and LAN LinkEngine
/// transports. The relay expects the same NOVORA-LINK v2 frames described by
/// Android's NLControlVpnProtocol.
/// </summary>
internal static class LinuxLinkProtocol
{
    public const string Name = "NOVORA-LINK";
    public const int Version = 2;
    public const string DataAuthenticationAck = "NOVORA-LINK|2|DATA|ACK";

    public static bool IsToken(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    public static string CreateDataAuthentication(string token)
    {
        if (!IsToken(token))
            throw new ArgumentException("Token de sesión inválido.", nameof(token));
        return $"{Name}|{Version}|DATA|{token}";
    }

    public static bool IsDataAuthenticationAck(string? payload) =>
        string.Equals(payload?.Trim(), DataAuthenticationAck, StringComparison.Ordinal);

    public static async Task<string> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 1 or > 1024)
            throw new InvalidDataException("Frame CONTROL de LinkEngine fuera de límite.");
        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return new UTF8Encoding(false, true).GetString(payload);
    }

    public static async Task WriteFrameAsync(Stream stream, string payload, CancellationToken cancellationToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(payload);
        if (body.Length is < 1 or > 1024)
            throw new InvalidDataException("Frame CONTROL de LinkEngine fuera de límite.");
        byte[] frame = new byte[sizeof(int) + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, body.Length);
        body.CopyTo(frame, sizeof(int));
        await stream.WriteAsync(frame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
