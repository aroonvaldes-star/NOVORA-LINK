using System.IO;
using System.Net;
namespace NOVORA.Control;
public sealed record NLControlTrustedPc(string PcName, string Host, int Port, string Fingerprint, string DeviceId, string Token)
{
    public void Validate(bool allowLoopback = false)
    {
        if (string.IsNullOrWhiteSpace(PcName) || PcName.Length > 128 || Port is < 1 or > 65535 || !IPAddress.TryParse(Host, out var ip) ||
            Fingerprint is not { Length: 64 } || !Fingerprint.All(Uri.IsHexDigit) || Token is not { Length: 64 } || !Token.All(Uri.IsHexDigit) ||
            !Guid.TryParseExact(DeviceId, "N", out _)) throw new InvalidDataException("PC de confianza no válida.");
        NLControlLanInvitation.ValidateAddress(ip, allowLoopback);
    }
}
