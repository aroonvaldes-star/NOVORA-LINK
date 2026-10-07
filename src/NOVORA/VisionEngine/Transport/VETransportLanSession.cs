using System.IO;
using System.Net;
using NOVORA.Control;

namespace NOVORA.VisionEngine.Transport;

public sealed class VETransportLanAcceptedStreams : IAsyncDisposable
{
    internal VETransportLanAcceptedStreams(Stream video, Stream control, Stream? audio, bool degraded)
    {
        Video = video;
        Control = control;
        Audio = audio;
        Degraded = degraded;
    }

    public Stream Video { get; }
    public Stream Control { get; }
    public Stream? Audio { get; }
    public bool Degraded { get; }

    public async ValueTask DisposeAsync()
    {
        await Video.DisposeAsync().ConfigureAwait(false);
        await Control.DisposeAsync().ConfigureAwait(false);
        if (Audio is not null) await Audio.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Owns the one-shot video, control, and optional audio listeners for one VE LAN offer.</summary>
public sealed class VETransportLanSession : IAsyncDisposable
{
    private readonly VETransportLanChannel _video;
    private readonly VETransportLanChannel _control;
    private readonly VETransportLanChannel? _audio;
    private readonly NLControlTrustStore _trustStore;
    private readonly IPAddress _address;
    private readonly bool _allowLoopback;
    private readonly TimeSpan _optionalAudioGrace;
    private readonly CancellationTokenSource _stop = new();
    private NLControlVeLanOffer? _offer;
    private DateTimeOffset _expiresAt;
    private bool _disposed;

    internal VETransportLanSession(
        IPAddress address,
        NLControlTrustStore trustStore,
        bool allowLoopback = false,
        TimeSpan? optionalAudioGrace = null,
        bool prepareAudio = true)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
        NLControlLanInvitation.ValidateAddress(address, allowLoopback);
        _trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
        _allowLoopback = allowLoopback;
        _optionalAudioGrace = optionalAudioGrace ?? TimeSpan.FromSeconds(3);
        if (_optionalAudioGrace <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(optionalAudioGrace));
        _video = new(address, trustStore, NLControlVeLanChannel.Video, allowLoopback);
        _control = new(address, trustStore, NLControlVeLanChannel.Control, allowLoopback);
        if (prepareAudio) _audio = new(address, trustStore, NLControlVeLanChannel.Audio, allowLoopback);
    }

    public NLControlVeLanOffer PrepareVE(
        int bitrate,
        int maxSize,
        int fps,
        bool muteDeviceAudio,
        bool audioEnabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_offer is not null) throw new InvalidOperationException("La sesión VE LAN ya fue preparada.");
        string sessionId = Guid.NewGuid().ToString("N");
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddSeconds(30);
        _expiresAt = expiresAt;
        NLControlVeLanEndpoint video = _video.PrepareVE(sessionId, expiresAt);
        NLControlVeLanEndpoint control = _control.PrepareVE(sessionId, expiresAt);
        NLControlVeLanEndpoint? audio = audioEnabled
            ? (_audio ?? throw new InvalidOperationException("El canal de audio VE LAN no está disponible."))
                .PrepareVE(sessionId, expiresAt)
            : null;
        _offer = new NLControlVeLanOffer(
            NLControlProtocol.Version,
            sessionId,
            _address.ToString(),
            _trustStore.Fingerprint,
            expiresAt.ToUnixTimeSeconds(),
            video,
            control,
            audio,
            bitrate,
            maxSize,
            fps,
            muteDeviceAudio);
        _offer.Validate(DateTimeOffset.UtcNow, _allowLoopback);
        return _offer;
    }

    public async Task<VETransportLanAcceptedStreams> AcceptAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NLControlVeLanOffer offer = _offer ?? throw new InvalidOperationException("Prepara la oferta VE LAN primero.");
        DateTimeOffset expiresAt = _expiresAt;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        Task<Stream> video = _video.AcceptAsync(offer.SessionId, offer.Video, expiresAt, linked.Token);
        Task<Stream> control = _control.AcceptAsync(offer.SessionId, offer.Control, expiresAt, linked.Token);
        Task<Stream>? audio = offer.Audio is null || _audio is null
            ? null
            : _audio.AcceptAsync(offer.SessionId, offer.Audio, expiresAt, linked.Token);

        await Task.WhenAll(video, control).ConfigureAwait(false);
        Stream? audioStream = null;
        bool degraded = false;
        if (audio is not null)
        {
            try { audioStream = await audio.WaitAsync(_optionalAudioGrace, linked.Token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is TimeoutException or IOException or InvalidDataException or
                OperationCanceledException && !linked.IsCancellationRequested)
            {
                degraded = true;
                await _audio!.DisposeAsync().ConfigureAwait(false);
            }
        }
        return new VETransportLanAcceptedStreams(await video.ConfigureAwait(false),
            await control.ConfigureAwait(false), audioStream, degraded);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        await _video.DisposeAsync().ConfigureAwait(false);
        await _control.DisposeAsync().ConfigureAwait(false);
        if (_audio is not null) await _audio.DisposeAsync().ConfigureAwait(false);
        _stop.Dispose();
    }
}
