using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using NOVORA.Linux.Control;

namespace NOVORA.Linux.VisionEngine;

internal sealed class LinuxVisionLanEngine : IAsyncDisposable
{
    private readonly IPAddress _address;
    private readonly LinuxTrustStore _trustStore;
    private readonly SemaphoreSlim _exclusiveSessionGate;
    private readonly bool _allowLoopback;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LinuxVisionLanSession? _session;
    private CancellationTokenSource? _sessionCancellation;
    private Task _sessionTask = Task.CompletedTask;
    private LinuxSdlVideoRenderer? _renderer;
    private long _videoPacketsReceived;
    private int _ownsExclusiveGate;
    private int _disposed;
    private string _state = "Stopped";
    private string _message = "VisionEngine LAN está detenido.";

    public event EventHandler? StatusChanged;
    public bool IsRunning => _state is "Waiting" or "Streaming";
    public bool HasSession => _session is not null;
    public string State => _state;
    public string Message => _message;
    public LinuxVisionTelemetry Telemetry
    {
        get
        {
            LinuxSdlVideoRenderer? renderer = Volatile.Read(ref _renderer);
            long received = Interlocked.Read(ref _videoPacketsReceived);
            long rendered = renderer?.FramesPresented ?? 0;
            return new LinuxVisionTelemetry(
                renderer?.Backend ?? "Unavailable",
                received,
                rendered,
                Math.Max(0, received - rendered),
                renderer?.OutputWidth ?? 0,
                renderer?.OutputHeight ?? 0);
        }
    }

    public LinuxVisionLanEngine(
        IPAddress address,
        LinuxTrustStore trustStore,
        SemaphoreSlim exclusiveSessionGate,
        bool allowLoopbackForTests = false)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
        if (allowLoopbackForTests ? !IPAddress.IsLoopback(address) : !LinuxLanDiscovery.IsPrivateIpv4(address))
            throw new InvalidDataException("VisionEngine LAN requiere una dirección IPv4 privada.");
        _trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
        _exclusiveSessionGate = exclusiveSessionGate ?? throw new ArgumentNullException(nameof(exclusiveSessionGate));
        _allowLoopback = allowLoopbackForTests;
    }

    public async Task<string> StartAsync(bool audioEnabled, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (HasSession)
                throw new InvalidOperationException("Detén la sesión anterior de VisionEngine LAN antes de reiniciarla.");
            if (!await _exclusiveSessionGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("VisionEngine ya tiene una sesión activa por otro transporte.");

            Interlocked.Exchange(ref _ownsExclusiveGate, 1);
            LinuxVisionLanSession? session = null;
            try
            {
                session = new LinuxVisionLanSession(_address, _trustStore.Certificate, _allowLoopback);
                LinuxVisionLanOffer offer = session.Prepare(
                    bitrate: 8_000_000,
                    maxSize: 1920,
                    fps: 60,
                    audioEnabled);
                _session = session;
                _sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Volatile.Write(ref _renderer, null);
                Interlocked.Exchange(ref _videoPacketsReceived, 0);
                _state = "Waiting";
                _message = "VisionEngine LAN espera la autorización de captura y los canales TLS.";
                _sessionTask = RunSessionAsync(session, offer, _sessionCancellation.Token);
                RaiseStatusChanged();
                return JsonSerializer.Serialize(offer);
            }
            catch
            {
                if (session is not null)
                    await session.DisposeAsync().ConfigureAwait(false);
                ReleaseExclusiveGate();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunSessionAsync(
        LinuxVisionLanSession session,
        LinuxVisionLanOffer offer,
        CancellationToken cancellationToken)
    {
        try
        {
            using var acceptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            acceptTimeout.CancelAfter(TimeSpan.FromSeconds(LinuxVisionLanProtocol.OfferLifetimeSeconds));
            await using LinuxVisionLanStreams streams =
                await session.AcceptAsync(acceptTimeout.Token).ConfigureAwait(false);
            using var sessionLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var renderer = new LinuxSdlVideoRenderer();
            Volatile.Write(ref _renderer, renderer);

            LinuxVisionAudioPlayback? audioPlayback = streams.Audio is null
                ? null
                : new LinuxVisionAudioPlayback(streams.Audio);
            var reader = new LinuxVisionProtocolReader(streams.Video);
            (VideoCodec codec, VideoSession videoSession) =
                await reader.OpenAsync(sessionLifetime.Token).ConfigureAwait(false);
            Task? audioTask = audioPlayback?.RunAsync(sessionLifetime.Token);
            LinuxFfmpegVideoDecoder? decoder = null;
            byte[]? configuration = null;
            _state = "Streaming";
            _message = streams.AudioDegraded
                ? $"VisionEngine LAN transmite video sin audio · {videoSession.Width}×{videoSession.Height}."
                : $"VisionEngine LAN transmite video y audio · {videoSession.Width}×{videoSession.Height}.";
            RaiseStatusChanged();
            try
            {
                while (true)
                {
                    if (audioTask?.IsFaulted == true)
                        await audioTask.ConfigureAwait(false);
                    VideoPacket packet = await reader.ReadPacketAsync(
                        videoSession,
                        changed => videoSession = changed,
                        sessionLifetime.Token).ConfigureAwait(false)
                        ?? throw new EndOfStreamException("El flujo de video VE LAN terminó.");

                    if (packet.Session != videoSession)
                        videoSession = packet.Session;
                    if (packet.IsConfiguration)
                        configuration = packet.Data;
                    else
                        Interlocked.Increment(ref _videoPacketsReceived);

                    if (decoder is null || decoder.SessionGeometryChanged(videoSession))
                    {
                        if (decoder is not null)
                            await decoder.DisposeAsync().ConfigureAwait(false);
                        decoder = new LinuxFfmpegVideoDecoder(codec, videoSession, renderer);
                        if (configuration is not null)
                            await decoder.WritePacketAsync(
                                configuration, null, sessionLifetime.Token).ConfigureAwait(false);
                        continue;
                    }

                    await decoder.WritePacketAsync(
                        packet.Data, packet.PresentationTimeUs, sessionLifetime.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                sessionLifetime.Cancel();
                if (audioTask is not null)
                {
                    try { await audioTask.ConfigureAwait(false); }
                    catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
                }
                if (decoder is not null)
                    await decoder.DisposeAsync().ConfigureAwait(false);
                audioPlayback?.Dispose();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            _state = "Failed";
            _message = "VisionEngine LAN agotó el tiempo de espera al abrir los canales.";
            RaiseStatusChanged();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _state = "Failed";
            _message = $"VisionEngine LAN se detuvo: {ex.Message}";
            RaiseStatusChanged();
        }
        finally
        {
            if (_state is "Waiting" or "Streaming")
            {
                _state = "Stopped";
                _message = "El flujo de VisionEngine LAN terminó.";
                RaiseStatusChanged();
            }
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            LinuxVisionLanSession? session = _session;
            CancellationTokenSource? cancellation = _sessionCancellation;
            _session = null;
            _sessionCancellation = null;
            cancellation?.Cancel();

            List<Exception>? errors = null;
            if (session is not null)
            {
                try { await session.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { (errors ??= []).Add(ex); }
            }
            try { await _sessionTask.ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            cancellation?.Dispose();
            ReleaseExclusiveGate();
            _state = errors is null ? "Stopped" : "Failed";
            _message = errors is null
                ? "VisionEngine LAN está detenido."
                : $"No se pudo liberar completamente VisionEngine LAN: {string.Join("; ", errors.Select(error => error.Message))}";
            RaiseStatusChanged();
            if (errors is not null)
                throw new AggregateException(_message, errors);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ReleaseExclusiveGate()
    {
        if (Interlocked.Exchange(ref _ownsExclusiveGate, 0) != 0)
            _exclusiveSessionGate.Release();
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private sealed class LinuxVisionAudioPlayback : IDisposable
    {
        private readonly Stream _stream;
        private readonly LinuxSdlAudioPlayer _player = new();

        public LinuxVisionAudioPlayback(Stream stream) => _stream = stream;

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var reader = new LinuxAudioProtocolReader(_stream);
            AudioCodec? codec = await reader.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (codec is null) return;
            using var decoder = new LinuxFfmpegAudioDecoder(codec.Value);
            while (!cancellationToken.IsCancellationRequested)
            {
                AudioPacket packet = await reader.ReadPacketAsync(cancellationToken).ConfigureAwait(false);
                foreach (byte[] pcm in decoder.Decode(packet))
                    _player.Play(pcm);
            }
        }

        public void Dispose() => _player.Dispose();
    }
}
