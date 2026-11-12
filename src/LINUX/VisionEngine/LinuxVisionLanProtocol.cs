using System.Security.Cryptography;
using System.Net;
using NOVORA.Linux.Control;

namespace NOVORA.Linux.VisionEngine;

internal enum LinuxVisionLanChannelKind
{
    Video,
    Audio,
    Control,
    Files
}

internal sealed record LinuxVisionLanEndpoint(int Port, string Token, string Channel)
{
    public void Validate(LinuxVisionLanChannelKind expected)
    {
        if (Port is < 1 or > 65535 ||
            Token is not { Length: 64 } || !Token.All(Uri.IsHexDigit) ||
            Channel != expected.ToString().ToUpperInvariant())
            throw new InvalidDataException("Canal VE LAN no válido.");
    }
}

internal sealed record LinuxVisionLanOffer(
    int Version,
    string SessionId,
    string Host,
    string Fingerprint,
    long ExpiresUnix,
    LinuxVisionLanEndpoint Video,
    LinuxVisionLanEndpoint Control,
    LinuxVisionLanEndpoint? Audio,
    int Bitrate,
    int MaxSize,
    int Fps,
    bool MuteDeviceAudio,
    LinuxVisionLanEndpoint? Files = null,
    int FilesWireVersion = 0,
    long CaptureGeneration = 0,
    LinuxCaptureCapabilities? CaptureCapabilities = null,
    string AudioSource = LinuxAudioSource.Output);

internal static class LinuxVisionLanProtocol
{
    public const int OfferLifetimeSeconds = 120;
    private const int ClockSkewSeconds = 60;

    public static void Validate(this LinuxVisionLanOffer offer, DateTimeOffset now, bool allowLoopback = false)
    {
        ArgumentNullException.ThrowIfNull(offer);
        if (offer.Version != ControlProtocol.Version)
            throw new InvalidDataException("Oferta VE LAN: versión no válida.");
        if (offer.SessionId is not { Length: >= 8 and <= 128 })
            throw new InvalidDataException("Oferta VE LAN: sesión no válida.");
        if (!IPAddress.TryParse(offer.Host, out IPAddress? address))
            throw new InvalidDataException("Oferta VE LAN: dirección no válida.");
        if (offer.Fingerprint is not { Length: 64 } || !offer.Fingerprint.All(Uri.IsHexDigit))
            throw new InvalidDataException("Oferta VE LAN: huella no válida.");
        if (offer.ExpiresUnix <= now.ToUnixTimeSeconds() ||
            offer.ExpiresUnix > now.AddSeconds(OfferLifetimeSeconds + ClockSkewSeconds).ToUnixTimeSeconds())
            throw new InvalidDataException("Oferta VE LAN: caducidad no válida.");
        if (offer.Bitrate <= 0 || offer.MaxSize <= 0 || offer.Fps is < 1 or > 240)
            throw new InvalidDataException("Oferta VE LAN: perfil no válido.");
        if (offer.FilesWireVersion is not (0 or 1 or 2) ||
            offer.FilesWireVersion == 2 && (offer.CaptureGeneration <= 0 || offer.Files is null))
            throw new InvalidDataException("Oferta VE LAN: capacidad no válida.");

        if (allowLoopback ? !IPAddress.IsLoopback(address) :
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !LinuxLanDiscovery.IsPrivateIpv4(address))
            throw new InvalidDataException("Oferta VE LAN: dirección no permitida.");

        offer.Video.Validate(LinuxVisionLanChannelKind.Video);
        offer.Control.Validate(LinuxVisionLanChannelKind.Control);
        offer.Audio?.Validate(LinuxVisionLanChannelKind.Audio);
        offer.Files?.Validate(LinuxVisionLanChannelKind.Files);
        var endpoints = new List<LinuxVisionLanEndpoint> { offer.Video, offer.Control };
        if (offer.Audio is not null) endpoints.Add(offer.Audio);
        if (offer.Files is not null) endpoints.Add(offer.Files);
        if (endpoints.Select(item => item.Port).Distinct().Count() != endpoints.Count ||
            endpoints.Select(item => item.Token).Distinct(StringComparer.OrdinalIgnoreCase).Count() != endpoints.Count)
            throw new InvalidDataException("Los canales VE LAN deben ser independientes.");
    }
}

internal sealed record LinuxVisionLanHello(int Version, string SessionId, string Channel, string Token)
{
    public void Validate(string expectedSessionId, LinuxVisionLanEndpoint endpoint)
    {
        if (Version != ControlProtocol.Version ||
            SessionId != expectedSessionId ||
            Channel != endpoint.Channel ||
            Token is not { Length: 64 } || !Token.All(Uri.IsHexDigit) ||
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(Token), Convert.FromHexString(endpoint.Token)))
            throw new InvalidDataException("Autorización VE LAN rechazada.");
    }
}
