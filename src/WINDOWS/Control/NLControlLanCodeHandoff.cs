using System.IO;

namespace NOVORA.Control;

// Sent only through Android's shell-protected AppControl entry point.
public sealed record NLControlLanCodeHandoff(
    string PcName, string Host, int Port, string Fingerprint,
    string Code, long ExpiresUnix)
{
    public void Validate()
    {
        new NLControlLanPeer(PcName, Host, Port, Fingerprint).Validate();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Code is not { Length: 6 } || !Code.All(char.IsAsciiDigit) ||
            ExpiresUnix <= now || ExpiresUnix > now + 180)
            throw new InvalidDataException("Código LAN inválido o vencido.");
    }
}
