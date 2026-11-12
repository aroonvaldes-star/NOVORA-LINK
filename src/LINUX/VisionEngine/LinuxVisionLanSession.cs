using System.Net;
using System.Security.Cryptography.X509Certificates;
using NOVORA.Linux.Control;

namespace NOVORA.Linux.VisionEngine;

internal sealed class LinuxVisionLanStreams(
    Stream video,
    Stream control,
    Stream? audio,
    bool audioDegraded, Stream? files = null) : IAsyncDisposable
{
    public Stream? Files { get; } = files;
    public Stream Video { get; } = video;
    public Stream Control { get; } = control;
    public Stream? Audio { get; } = audio;
    public bool AudioDegraded { get; } = audioDegraded;

    public async ValueTask DisposeAsync()
    {
        if (Files is not null) await Files.DisposeAsync().ConfigureAwait(false);
        await Video.DisposeAsync().ConfigureAwait(false);
        await Control.DisposeAsync().ConfigureAwait(false);
        if (Audio is not null)
            await Audio.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class LinuxVisionLanSession : IAsyncDisposable
{
    private static readonly TimeSpan OptionalChannelGrace = TimeSpan.FromSeconds(3);

    private readonly IPAddress _address;
    private readonly X509Certificate2 _certificate;
    private readonly bool _allowLoopback;
    private readonly LinuxVisionLanChannel _video;
    private readonly LinuxVisionLanChannel _control;
    private readonly LinuxVisionLanChannel _audio;
    private readonly LinuxVisionLanChannel _files;
    private readonly CancellationTokenSource _stop = new();
    private LinuxVisionLanOffer? _offer;
    private DateTimeOffset _expiresAt;
    private bool _disposed;

    public LinuxVisionLanSession(IPAddress address, X509Certificate2 certificate, bool allowLoopback = false)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
        if (allowLoopback ? !IPAddress.IsLoopback(address) : !LinuxLanDiscovery.IsPrivateIpv4(address))
            throw new InvalidDataException("VisionEngine LAN requiere una dirección IPv4 privada.");
        _certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
        _allowLoopback = allowLoopback;
        _video = new LinuxVisionLanChannel(address, certificate, LinuxVisionLanChannelKind.Video, allowLoopback);
        _control = new LinuxVisionLanChannel(address, certificate, LinuxVisionLanChannelKind.Control, allowLoopback);
        _audio = new LinuxVisionLanChannel(address, certificate, LinuxVisionLanChannelKind.Audio, allowLoopback);
        _files = new LinuxVisionLanChannel(address, certificate, LinuxVisionLanChannelKind.Files, allowLoopback);
    }

    public LinuxVisionLanOffer Prepare(int bitrate, int maxSize, int fps, bool audioEnabled, long generation = 0,
        bool mobileData = false, string audioSource = LinuxAudioSource.Output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_offer is not null)
            throw new InvalidOperationException("La sesión VE LAN ya fue preparada.");
        if (bitrate <= 0 || maxSize <= 0 || fps is < 1 or > 240)
            throw new InvalidDataException("Perfil VE LAN no válido.");

        string sessionId = Guid.NewGuid().ToString("N");
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(LinuxVisionLanProtocol.OfferLifetimeSeconds);
        LinuxVisionLanEndpoint video = _video.Prepare(sessionId, _expiresAt);
        LinuxVisionLanEndpoint control = _control.Prepare(sessionId, _expiresAt);
        LinuxVisionLanEndpoint? audio = audioEnabled ? _audio.Prepare(sessionId, _expiresAt) : null;
        _offer = new LinuxVisionLanOffer(
            ControlProtocol.Version,
            sessionId,
            _address.ToString(),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_certificate.RawData)),
            _expiresAt.ToUnixTimeSeconds(),
            video,
            control,
            audio,
            bitrate,
            maxSize,
            fps,
            MuteDeviceAudio: audioEnabled,
            Files: generation > 0 ? _files.Prepare(sessionId, _expiresAt) : null,
            FilesWireVersion: generation > 0 ? 2 : 0, CaptureGeneration: generation,
            CaptureCapabilities: mobileData ? LinuxCaptureCapabilities.Mobile : null,
            AudioSource: audioSource);
        _offer.Validate(DateTimeOffset.UtcNow, _allowLoopback);
        return _offer;
    }

    public async Task<LinuxVisionLanStreams> AcceptAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LinuxVisionLanOffer offer = _offer ??
            throw new InvalidOperationException("Prepara la oferta VE LAN primero.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);

        Task<Stream> video = _video.AcceptAsync(offer.SessionId, offer.Video, _expiresAt, linked.Token);
        Task<Stream> control = _control.AcceptAsync(offer.SessionId, offer.Control, _expiresAt, linked.Token);
        Task<Stream>? files = offer.Files is null ? null : _files.AcceptAsync(offer.SessionId, offer.Files, _expiresAt, linked.Token);
        Task<Stream>? audio = offer.Audio is null
            ? null
            : _audio.AcceptAsync(offer.SessionId, offer.Audio, _expiresAt, linked.Token);

        try
        {
            await Task.WhenAll(files is null ? [video, control] : new[] { video, control, files }).ConfigureAwait(false);
            Stream? audioStream = null;
            bool audioDegraded = false;
            if (audio is not null)
            {
                try
                {
                    audioStream = await audio.WaitAsync(OptionalChannelGrace, linked.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is TimeoutException or IOException or InvalidDataException ||
                                           ex is OperationCanceledException && !linked.IsCancellationRequested)
                {
                    audioDegraded = true;
                    await _audio.DisposeAsync().ConfigureAwait(false);
                    await ObserveFailureAsync(audio).ConfigureAwait(false);
                }
            }

            return new LinuxVisionLanStreams(
                await video.ConfigureAwait(false),
                await control.ConfigureAwait(false),
                audioStream,
                audioDegraded, files is null ? null : await files.ConfigureAwait(false));
        }
        catch
        {
            linked.Cancel();
            await _video.DisposeAsync().ConfigureAwait(false);
            await _control.DisposeAsync().ConfigureAwait(false);
            await _audio.DisposeAsync().ConfigureAwait(false);
            await _files.DisposeAsync().ConfigureAwait(false);
            if (files is not null) await ObserveFailureAsync(files).ConfigureAwait(false);
            await ObserveFailureAsync(video).ConfigureAwait(false);
            await ObserveFailureAsync(control).ConfigureAwait(false);
            if (audio is not null)
                await ObserveFailureAsync(audio).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        await _video.DisposeAsync().ConfigureAwait(false);
        await _control.DisposeAsync().ConfigureAwait(false);
        await _audio.DisposeAsync().ConfigureAwait(false);
        await _files.DisposeAsync().ConfigureAwait(false);
        _stop.Dispose();
    }

    private static async Task ObserveFailureAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or
                                   InvalidDataException or ObjectDisposedException or
                                   System.Security.Authentication.AuthenticationException) { }
    }
}
