using System.IO;
using System.Net;
using System.Net.Sockets;

namespace NOVORA.Control;

public sealed record NLControlLanInvitation(int Version, string Host, int Port, string Fingerprint, string Secret, long ExpiresUnix)
{
    private const int MaximumClientClockSkewSeconds = 300;

    public void Validate(bool allowLoopback = false)
    {
        if (Version != 1 || Port is < 1 or > 65535 || !IPAddress.TryParse(Host, out var address))
            throw new InvalidDataException("Invitación no válida.");
        ValidateAddress(address, allowLoopback);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (ExpiresUnix <= now || ExpiresUnix > now + MaximumClientClockSkewSeconds || !IsHex(Fingerprint) || !IsHex(Secret))
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
}
