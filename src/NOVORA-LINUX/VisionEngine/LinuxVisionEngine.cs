using System.Net.Sockets;
using System.Text.Json;
using NOVORA.Linux.Control;

namespace NOVORA.Linux.VisionEngine;

internal sealed record LinuxVideoSourceOffer(
    int Port,
    string Token,
    int Bitrate,
    int MaxSize,
    int Fps,
    int ControlPort,
    string ControlToken,
    int AudioPort,
    string AudioToken,
    bool MuteDeviceAudio,
    bool AudioEnabled);

internal sealed record LinuxVisionTelemetry(
    string RendererBackend,
    long VideoPacketsReceived,
    long FramesRendered,
    long FramesDroppedEstimate,
    int OutputWidth,
    int OutputHeight);

internal sealed class LinuxVisionEngine : IAsyncDisposable
{
    public const int DeviceVideoPort = 27215;
    public const int DeviceControlPort = 27216;
    public const int DeviceAudioPort = 27217;

    private readonly AdbBridge _adb;
    private readonly string _serial;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _exclusiveSessionGate;
    private LinuxAppControlTransport? _videoTransport;
    private LinuxAppControlTransport? _controlTransport;
    private LinuxAppControlTransport? _audioTransport;
    private AdbBridge.UsbReverseLease? _reverse;
    private CancellationTokenSource? _sessionCancellation;
    private Task _sessionTask = Task.CompletedTask;
    private LinuxSdlVideoRenderer? _renderer;
    private long _videoPacketsReceived;
    private int _ownsExclusiveGate;
    private string _state = "Stopped";
    private string _message = "VisionEngine Linux está detenido.";
    private int _disposed;

    public event EventHandler? StatusChanged;
    public bool IsRunning => _state is "Waiting" or "Streaming";
    public bool HasSession => _videoTransport is not null;
    public string State => _state;
    public string Message => _message;
    public LinuxVisionTelemetry Telemetry
    {
        get
        {
            LinuxSdlVideoRenderer? renderer = Volatile.Read(ref _renderer);
            long rendered = renderer?.FramesPresented ?? 0;
            long received = Interlocked.Read(ref _videoPacketsReceived);
            return new LinuxVisionTelemetry(
                renderer?.Backend ?? "Unavailable",
                received,
                rendered,
                Math.Max(0, received - rendered),
                renderer?.OutputWidth ?? 0,
                renderer?.OutputHeight ?? 0);
        }
    }

    public LinuxVisionEngine(AdbBridge adb, string serial, SemaphoreSlim? exclusiveSessionGate = null)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _serial = string.IsNullOrWhiteSpace(serial)
            ? throw new ArgumentException("El serial ADB es obligatorio.", nameof(serial))
            : serial;
        _exclusiveSessionGate = exclusiveSessionGate ?? new SemaphoreSlim(1, 1);
    }

    public async Task<string> StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning)
                throw new InvalidOperationException("VisionEngine ya tiene una sesión activa.");
            if (HasSession)
                throw new InvalidOperationException("Detén la sesión anterior de VisionEngine antes de reiniciarla.");
            if (!await _exclusiveSessionGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("VisionEngine ya tiene una sesión activa por otro transporte.");
            Interlocked.Exchange(ref _ownsExclusiveGate, 1);

            var video = new LinuxAppControlTransport();
            var control = new LinuxAppControlTransport();
            var audio = new LinuxAppControlTransport();
            AdbBridge.UsbReverseLease? reverse = null;
            try
            {
                AppControlStreamOffer videoOffer = video.Prepare();
                AppControlStreamOffer controlOffer = control.Prepare();
                AppControlStreamOffer audioOffer = audio.Prepare();
                reverse = await _adb.PrepareUsbReverseMappingsAsync(
                    _serial,
                    [
                        (DeviceVideoPort, videoOffer.Port),
                        (DeviceControlPort, controlOffer.Port),
                        (DeviceAudioPort, audioOffer.Port)
                    ],
                    cancellationToken).ConfigureAwait(false);

                _videoTransport = video;
                _controlTransport = control;
                _audioTransport = audio;
                Volatile.Write(ref _renderer, null);
                Interlocked.Exchange(ref _videoPacketsReceived, 0);
                _reverse = reverse;
                reverse = null;
                _sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _state = "Waiting";
                _message = "VisionEngine espera la autorización de captura y el flujo de AppControl.";
                _sessionTask = RunSessionAsync(
                    video, videoOffer, control, controlOffer, audio, audioOffer,
                    _sessionCancellation.Token);
                RaiseStatusChanged();

                var offer = new LinuxVideoSourceOffer(
                    Port: DeviceVideoPort,
                    Token: videoOffer.Token,
                    Bitrate: 8_000_000,
                    MaxSize: 1920,
                    Fps: 60,
                    ControlPort: DeviceControlPort,
                    ControlToken: controlOffer.Token,
                    AudioPort: DeviceAudioPort,
                    AudioToken: audioOffer.Token,
                    MuteDeviceAudio: true,
                    AudioEnabled: true);
                return JsonSerializer.Serialize(offer);
            }
            catch
            {
                try
                {
                    if (reverse is not null)
                        await reverse.DisposeAsync().ConfigureAwait(false);
                    await video.DisposeAsync().ConfigureAwait(false);
                    await control.DisposeAsync().ConfigureAwait(false);
                    await audio.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    ReleaseExclusiveGate();
                }
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunSessionAsync(
        LinuxAppControlTransport videoTransport,
        AppControlStreamOffer videoOffer,
        LinuxAppControlTransport controlTransport,
        AppControlStreamOffer controlOffer,
        LinuxAppControlTransport audioTransport,
        AppControlStreamOffer audioOffer,
        CancellationToken cancellationToken)
    {
        try
        {
            using var acceptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            acceptTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            Task<Stream> videoAccept = videoTransport.AcceptAsync(
                videoOffer, TimeSpan.FromSeconds(30), acceptTimeout.Token);
            Task<Stream> controlAccept = controlTransport.AcceptAsync(
                controlOffer, TimeSpan.FromSeconds(30), acceptTimeout.Token);
            Task<Stream> audioAccept = audioTransport.AcceptAsync(
                audioOffer, TimeSpan.FromSeconds(30), acceptTimeout.Token);
            await Task.WhenAll(videoAccept, controlAccept, audioAccept).ConfigureAwait(false);
            await using Stream video = await videoAccept.ConfigureAwait(false);
            await using Stream control = await controlAccept.ConfigureAwait(false);
            await using Stream audio = await audioAccept.ConfigureAwait(false);
            using var sessionLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var renderer = new LinuxSdlVideoRenderer();
            Volatile.Write(ref _renderer, renderer);
            using var audioPlayer = new LinuxSdlAudioPlayer();
            var reader = new LinuxVisionProtocolReader(video);
            (VideoCodec codec, VideoSession session) =
                await reader.OpenAsync(sessionLifetime.Token).ConfigureAwait(false);
            Task audioTask = RunAudioAsync(audio, audioPlayer, sessionLifetime.Token);

            LinuxFfmpegVideoDecoder? decoder = new(codec, session, renderer);
            byte[]? configuration = null;
            _state = "Streaming";
            _message = $"VisionEngine transmite video {codec} y audio por USB · {session.Width}×{session.Height}.";
            RaiseStatusChanged();
            try
            {
                while (!sessionLifetime.IsCancellationRequested)
                {
                    if (audioTask.IsFaulted)
                        await audioTask.ConfigureAwait(false);
                    VideoPacket packet = await reader.ReadPacketAsync(
                        session,
                        changed => session = changed,
                        sessionLifetime.Token).ConfigureAwait(false)
                        ?? throw new EndOfStreamException("El flujo de video terminó.");

                    if (packet.Session != session)
                        session = packet.Session;

                    if (packet.IsConfiguration)
                        configuration = packet.Data;
                    else
                        Interlocked.Increment(ref _videoPacketsReceived);

                    if (decoder is null || decoder.SessionGeometryChanged(session))
                    {
                        if (decoder is not null)
                            await decoder.DisposeAsync().ConfigureAwait(false);
                        decoder = new LinuxFfmpegVideoDecoder(codec, session, renderer);
                        if (configuration is not null)
                            await decoder.WritePacketAsync(configuration, null, sessionLifetime.Token).ConfigureAwait(false);
                        continue;
                    }

                    await decoder.WritePacketAsync(
                        packet.Data, packet.PresentationTimeUs, sessionLifetime.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                sessionLifetime.Cancel();
                try
                {
                    await audioTask.ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
                {
                }
                if (decoder is not null)
                    await decoder.DisposeAsync().ConfigureAwait(false);
            }
            _ = control;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            _state = "Failed";
            _message = "VisionEngine agotó el tiempo de espera al abrir los canales AppControl.";
            RaiseStatusChanged();
        }

        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _state = "Failed";
            _message = $"VisionEngine se detuvo: {ex.Message}";
            RaiseStatusChanged();
        }
        finally
        {
            if (_state is "Waiting" or "Streaming")
            {
                _state = "Stopped";
                _message = "El flujo de VisionEngine terminó.";
                RaiseStatusChanged();
            }
        }
    }

    private static async Task RunAudioAsync(
        Stream stream, LinuxSdlAudioPlayer player, CancellationToken cancellationToken)
    {
        var reader = new LinuxAudioProtocolReader(stream);
        AudioCodec? codec = await reader.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (codec is null)
            return;
        using var decoder = new LinuxFfmpegAudioDecoder(codec.Value);
        while (!cancellationToken.IsCancellationRequested)
        {
            AudioPacket packet = await reader.ReadPacketAsync(cancellationToken).ConfigureAwait(false);
            foreach (byte[] pcm in decoder.Decode(packet))
                player.Play(pcm);
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            CancellationTokenSource? cancellation = _sessionCancellation;
            _sessionCancellation = null;
            cancellation?.Cancel();

            LinuxAppControlTransport? video = _videoTransport;
            LinuxAppControlTransport? control = _controlTransport;
            LinuxAppControlTransport? audio = _audioTransport;
            _videoTransport = null;
            _controlTransport = null;
            _audioTransport = null;
            AdbBridge.UsbReverseLease? reverse = _reverse;
            _reverse = null;
            List<Exception>? errors = null;
            try
            {
                if (video is not null) await video.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            try
            {
                if (control is not null) await control.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            try
            {
                if (audio is not null) await audio.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            try
            {
                await _sessionTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            ReleaseExclusiveGate();
            cancellation?.Dispose();
            try
            {
                if (reverse is not null)
                    await reverse.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            _state = errors is null ? "Stopped" : "Failed";
            _message = errors is null
                ? "VisionEngine Linux está detenido."
                : $"No se pudo liberar completamente VisionEngine: {string.Join("; ", errors.Select(error => error.Message))}";
            RaiseStatusChanged();
            if (errors is not null)
                throw new AggregateException(_message, errors);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    private void ReleaseExclusiveGate()
    {
        if (Interlocked.Exchange(ref _ownsExclusiveGate, 0) != 0)
            _exclusiveSessionGate.Release();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
