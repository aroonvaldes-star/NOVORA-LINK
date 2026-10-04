using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using NOVORA.Control;

namespace NOVORA.LinkClient;

internal sealed record NovoraVeLanStreams(Stream Video, Stream Control, Stream? Audio) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await DisposeOneAsync(Audio).ConfigureAwait(false);
        await DisposeOneAsync(Control).ConfigureAwait(false);
        await DisposeOneAsync(Video).ConfigureAwait(false);
    }

    private static async ValueTask DisposeOneAsync(Stream? stream)
    {
        if (stream is null) return;
        try { await stream.DisposeAsync().ConfigureAwait(false); }
        catch { }
    }
}

/// <summary>Connects Android capture streams to a short-lived, certificate-pinned VE LAN offer.</summary>
internal sealed class NovoraVeLanClient
{
    private readonly bool _allowLoopback;

    internal NovoraVeLanClient(bool allowLoopback = false) => _allowLoopback = allowLoopback;

    internal async Task<NovoraVeLanStreams> ConnectAsync(
        NLControlVeLanOffer offer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        offer.Validate(DateTimeOffset.UtcNow, _allowLoopback);
        IPAddress address = IPAddress.Parse(offer.Host);
        byte[] fingerprint = Convert.FromHexString(offer.Fingerprint);
        Stream? video = null;
        Stream? control = null;
        Stream? audio = null;
        try
        {
            video = await ConnectChannelAsync(
                address, fingerprint, offer, offer.Video, cancellationToken).ConfigureAwait(false);
            control = await ConnectChannelAsync(
                address, fingerprint, offer, offer.Control, cancellationToken).ConfigureAwait(false);
            if (offer.Audio is not null)
                audio = await ConnectChannelAsync(
                    address, fingerprint, offer, offer.Audio, cancellationToken).ConfigureAwait(false);
            return new NovoraVeLanStreams(video, control, audio);
        }
        catch
        {
            await DisposeOneAsync(audio).ConfigureAwait(false);
            await DisposeOneAsync(control).ConfigureAwait(false);
            await DisposeOneAsync(video).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<Stream> ConnectChannelAsync(
        IPAddress address,
        byte[] expectedFingerprint,
        NLControlVeLanOffer offer,
        NLControlVeLanEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        var socket = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        SslStream? tls = null;
        try
        {
            await socket.ConnectAsync(address, endpoint.Port, cancellationToken).ConfigureAwait(false);
            tls = new SslStream(socket.GetStream(), leaveInnerStreamOpen: false,
                (_, certificate, _, _) => certificate is not null &&
                    CryptographicOperations.FixedTimeEquals(
                        SHA256.HashData(certificate.GetRawCertData()), expectedFingerprint));
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "NOVORA",
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }, cancellationToken).ConfigureAwait(false);
            await NLControlProtocol.WriteAsync(tls,
                new NLControlVeLanHello(
                    offer.Version, offer.SessionId, endpoint.Channel, endpoint.Token),
                cancellationToken).ConfigureAwait(false);
            return tls;
        }
        catch
        {
            if (tls is not null) await tls.DisposeAsync().ConfigureAwait(false);
            socket.Dispose();
            throw;
        }
    }

    private static async ValueTask DisposeOneAsync(Stream? stream)
    {
        if (stream is null) return;
        try { await stream.DisposeAsync().ConfigureAwait(false); }
        catch { }
    }
}
