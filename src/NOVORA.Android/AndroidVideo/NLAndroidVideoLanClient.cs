using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using NOVORA.Control;

namespace NOVORA.AndroidVideo;

internal sealed record NLAndroidVideoLanStreams(Stream Video, Stream Control, Stream? Audio, Stream? Files) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await DisposeOneAsync(Audio).ConfigureAwait(false);
        await DisposeOneAsync(Files).ConfigureAwait(false);
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
internal sealed class NLAndroidVideoLanClient
{
    private readonly bool _allowLoopback;
    internal string? FilesConnectionError { get; private set; }

    internal NLAndroidVideoLanClient(bool allowLoopback = false) => _allowLoopback = allowLoopback;

    internal async Task<NLAndroidVideoLanStreams> ConnectAsync(
        NLControlVeLanOffer offer,
        bool connectFiles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        offer.Validate(DateTimeOffset.UtcNow, _allowLoopback);
        FilesConnectionError = null;
        IPAddress address = IPAddress.Parse(offer.Host);
        byte[] fingerprint = Convert.FromHexString(offer.Fingerprint);
        Stream? video = null;
        Stream? control = null;
        Stream? audio = null;
        Stream? files = null;
        try
        {
            video = await ConnectChannelAsync(
                address, fingerprint, offer, offer.Video, cancellationToken).ConfigureAwait(false);
            control = await ConnectChannelAsync(
                address, fingerprint, offer, offer.Control, cancellationToken).ConfigureAwait(false);
            if (offer.Audio is not null)
                audio = await ConnectChannelAsync(
                    address, fingerprint, offer, offer.Audio, cancellationToken).ConfigureAwait(false);
            if (connectFiles && offer.Files is not null)
            {
                using var filesDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                filesDeadline.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    files = await ConnectChannelAsync(
                        address, fingerprint, offer, offer.Files, filesDeadline.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                    ex is IOException or SocketException or AuthenticationException or OperationCanceledException)
                {
                    // Optional FILES remains absent. The mandatory pinned video/control streams stay live.
                    FilesConnectionError = ex.Message;
                }
            }
            return new NLAndroidVideoLanStreams(video, control, audio, files);
        }
        catch
        {
            await DisposeOneAsync(audio).ConfigureAwait(false);
            await DisposeOneAsync(files).ConfigureAwait(false);
            await DisposeOneAsync(control).ConfigureAwait(false);
            await DisposeOneAsync(video).ConfigureAwait(false);
            throw;
        }
    }

    private const int VideoSendBufferBytes = 512 * 1024;

    private static async Task<Stream> ConnectChannelAsync(
        IPAddress address,
        byte[] expectedFingerprint,
        NLControlVeLanOffer offer,
        NLControlVeLanEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        var socket = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        // Kernel autotuning can grow the send buffer to megabytes: WriteAsync then returns
        // while hundreds of ms of video wait unseen by the packet queue's age guard. 128 KiB
        // still carries ~100 Mbps at a 10 ms Wi-Fi RTT.
        if (ReferenceEquals(endpoint, offer.Video)) socket.SendBufferSize = VideoSendBufferBytes;
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
