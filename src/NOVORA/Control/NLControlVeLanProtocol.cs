using System.IO;
using System.Net;
using System.Security.Cryptography;

namespace NOVORA.Control;

public enum NLControlVeLanChannel { Video, Audio, Control, Files }

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
    bool MuteDeviceAudio,
    NLControlVeLanEndpoint? Files = null, int FilesWireVersion = 1,
    long CaptureGeneration = 0, NLControlNetworkCapabilities? CaptureCapabilities = null)
{
    public const int OfferLifetimeSeconds = 120;
    // PC and Android clocks may differ slightly even on the same Wi-Fi network.
    private const int ClockSkewSeconds = 5;

    public void Validate(DateTimeOffset now, bool allowLoopback = false)
    {
        if (Version != NLControlProtocol.Version)
            throw new InvalidDataException("Oferta VE LAN: versión no válida.");
        if (SessionId is not { Length: >= 8 and <= 128 })
            throw new InvalidDataException("Oferta VE LAN: sesión no válida.");
        if (!IPAddress.TryParse(Host, out IPAddress? address))
            throw new InvalidDataException("Oferta VE LAN: dirección no válida.");
        if (Fingerprint is not { Length: 64 } || !Fingerprint.All(Uri.IsHexDigit))
            throw new InvalidDataException("Oferta VE LAN: huella no válida.");
        if (ExpiresUnix <= now.ToUnixTimeSeconds() ||
            ExpiresUnix > now.AddSeconds(OfferLifetimeSeconds + ClockSkewSeconds).ToUnixTimeSeconds())
            throw new InvalidDataException("Oferta VE LAN: caducidad no válida.");
        if (Bitrate <= 0 || MaxSize <= 0 || Fps is < 1 or > 240)
            throw new InvalidDataException("Oferta VE LAN: perfil no válido.");
        if (FilesWireVersion is not (1 or 2) || CaptureGeneration < 0)
            throw new InvalidDataException("Oferta VE LAN: capacidad no válida.");

        NLControlLanInvitation.ValidateAddress(address, allowLoopback);
        Video.Validate(NLControlVeLanChannel.Video);
        Control.Validate(NLControlVeLanChannel.Control);
        Audio?.Validate(NLControlVeLanChannel.Audio);
        Files?.Validate(NLControlVeLanChannel.Files);

        var endpoints = new List<NLControlVeLanEndpoint> { Video, Control };
        if (Audio is not null) endpoints.Add(Audio);
        if (Files is not null) endpoints.Add(Files);
        if (endpoints.Select(item => item.Port).Distinct().Count() != endpoints.Count ||
            endpoints.Select(item => item.Token).Distinct(StringComparer.OrdinalIgnoreCase).Count() != endpoints.Count)
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
