using System.IO;
using System.Net;
using System.Security.Cryptography;

namespace NOVORA.Control;

public enum NLControlVeLanChannel { Video, Audio, Control }

public sealed record NLControlVeLanEndpoint(int Port, string Token, string Channel)
{
    internal void Validate(NLControlVeLanChannel expected)
    {
        if (Port is < 1 or > 65535 || Token is not { Length: 64 } || !Token.All(Uri.IsHexDigit) ||
            Channel != expected.ToString().ToUpperInvariant())
            throw new InvalidDataException("Canal VE LAN no válido.");
    }
}

public sealed record NLControlVeLanOffer(
    int Version,
    string SessionId,
    string Host,
    string Fingerprint,
    long ExpiresUnix,
    NLControlVeLanEndpoint Video,
    NLControlVeLanEndpoint Control,
    NLControlVeLanEndpoint? Audio,
    int Bitrate,
    int MaxSize,
    int Fps,
    bool MuteDeviceAudio)
{
    public void Validate(DateTimeOffset now, bool allowLoopback = false)
    {
        if (Version != NLControlProtocol.Version || SessionId is not { Length: >= 8 and <= 128 } ||
            !IPAddress.TryParse(Host, out IPAddress? address) ||
            Fingerprint is not { Length: 64 } || !Fingerprint.All(Uri.IsHexDigit) ||
            ExpiresUnix <= now.ToUnixTimeSeconds() || ExpiresUnix > now.AddSeconds(30).ToUnixTimeSeconds() ||
            Bitrate <= 0 || MaxSize <= 0 || Fps is < 1 or > 240)
            throw new InvalidDataException("Oferta VE LAN no válida.");

        NLControlLanInvitation.ValidateAddress(address, allowLoopback);
        Video.Validate(NLControlVeLanChannel.Video);
        Control.Validate(NLControlVeLanChannel.Control);
        Audio?.Validate(NLControlVeLanChannel.Audio);

        NLControlVeLanEndpoint[] endpoints = Audio is null ? [Video, Control] : [Video, Control, Audio];
        if (endpoints.Select(item => item.Port).Distinct().Count() != endpoints.Length ||
            endpoints.Select(item => item.Token).Distinct(StringComparer.OrdinalIgnoreCase).Count() != endpoints.Length)
            throw new InvalidDataException("Los canales VE LAN deben ser independientes.");
    }
}

public sealed record NLControlVeLanHello(int Version, string SessionId, string Channel, string Token)
{
    public void Validate(string expectedSessionId, NLControlVeLanEndpoint expectedEndpoint)
    {
        if (Version != NLControlProtocol.Version || SessionId != expectedSessionId ||
            Channel != expectedEndpoint.Channel || Token is not { Length: 64 } || !Token.All(Uri.IsHexDigit))
            throw new InvalidDataException("Autorización VE LAN no válida.");

        if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(Token), Convert.FromHexString(expectedEndpoint.Token)))
            throw new InvalidDataException("Autorización VE LAN rechazada.");
    }
}
