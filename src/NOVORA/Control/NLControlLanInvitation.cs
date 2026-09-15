using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace NOVORA.Control;

public sealed record NLControlLanInvitation(int Version, string Host, int Port, string Fingerprint, string Secret, long ExpiresUnix)
{
    public void Validate(bool allowLoopback = false)
    {
        if (Version != 1 || Port is < 1 or > 65535 || !IPAddress.TryParse(Host, out var address))
            throw new InvalidDataException("Invitación no válida.");
        ValidateAddress(address, allowLoopback);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (ExpiresUnix <= now || ExpiresUnix > now + 120 || !IsHex(Fingerprint) || !IsHex(Secret))
            throw new InvalidDataException("Invitación vencida o incompleta.");
    }
    private static bool IsHex(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    public static void ValidateAddress(IPAddress address, bool allowLoopback = false)
    {
        byte[] b = address.GetAddressBytes();
        if (address.AddressFamily != AddressFamily.InterNetwork || !(allowLoopback && IPAddress.IsLoopback(address) ||
            b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168))
            throw new InvalidDataException("Selecciona una dirección IPv4 de la red local privada.");
    }
    public string Encode()
    {
        Validate();
        return "novora://pair?data=" + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
    public static NLControlLanInvitation Parse(string text, bool allowLoopback = false)
    {
        const string prefix = "novora://pair?data=";
        if (text is null || text.Length > 2048 || !text.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("QR de NOVORA no válido.");
        try
        {
            string data = text[prefix.Length..].Replace('-', '+').Replace('_', '/');
            data = data.PadRight((data.Length + 3) / 4 * 4, '=');
            var invitation = JsonSerializer.Deserialize<NLControlLanInvitation>(Convert.FromBase64String(data), new JsonSerializerOptions { MaxDepth = 4 })
                ?? throw new InvalidDataException("QR vacío.");
            invitation.Validate(allowLoopback);
            return invitation;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        { throw new InvalidDataException("QR de NOVORA no válido.", ex); }
    }
}
