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
    bool AudioEnabled,
    int FilesPort = 0, string FilesToken = "", int FilesWireVersion = 0, long CaptureGeneration = 0,
    LinuxCaptureCapabilities? CaptureCapabilities = null, string AudioSource = LinuxAudioSource.Output);

/// <summary>
/// JSON-compatible with NLControlNetworkCapabilities. Only sent for mobile-data captures: frame pacing
/// makes Android drop frames before the encoder, and MobileData unlocks the 100 kbit/s · 1 fps floors.
/// </summary>
internal sealed record LinuxCaptureCapabilities(int Version, bool StabilityTelemetry, bool LiveVideoBitrate,
    bool CaptureFramePacing, bool LiveGeometry, bool MobileData)
{
    public static LinuxCaptureCapabilities Mobile { get; } = new(1, false, false, true, false, true);

    /// <summary>Capture limits shared by the USB and LAN engines.</summary>
    public static void ValidateCapture(int bitrate, int maxSize, int fps, bool mobileData)
    {
        bool size = maxSize is 480 or 720 or 960 or 1080 or 1280 or 1440 or 1600 or 1920 or 2160;
        bool valid = mobileData
            ? bitrate is >= 100_000 and <= 15_000_000 && maxSize is 480 or 720 && fps is >= 1 and <= 30
            : bitrate is >= 2_000_000 and <= 15_000_000 && size && fps is 15 or 24 or 30 or 45 or 60;
        if (!valid)
            throw new InvalidDataException("Los ajustes de captura de VisionEngine no son válidos.");
    }
}

internal sealed record LinuxVisionTelemetry(
    string RendererBackend,
    long VideoPacketsReceived,
    long FramesRendered,
    long FramesDroppedEstimate,
    int OutputWidth,
    int OutputHeight, string DecoderBackend = "Unavailable", bool AudioPlaying = false, string InputStatus = "", string RecordingStatus = "", string FilesStatus = "", long VideoBytesReceived = 0);

internal sealed class LinuxVisionEngine : IAsyncDisposable
{
    public const int DeviceVideoPort = 27215;
    public const int DeviceControlPort = 27216;
    public const int DeviceAudioPort = 27217;
    public const int DeviceFilesPort = 27218;
    private static long _generation = DateTime.UtcNow.Ticks;
    public LinuxVisionRuntime? Runtime { get; private set; }

    private readonly AdbBridge _adb;
    private readonly string _serial;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _exclusiveSessionGate;
    private LinuxAppControlTransport? _videoTransport;
    private LinuxAppControlTransport? _controlTransport;
    private LinuxAppControlTransport? _audioTransport;
    private LinuxAppControlTransport? _filesTransport;
    private AdbBridge.UsbReverseLease? _reverse;
    private CancellationTokenSource? _sessionCancellation;
    private Task _sessionTask = Task.CompletedTask;
    private int _ownsExclusiveGate;
    private string _state = "Stopped";
    private string _message = "VisionEngine Linux está detenido.";
    private int _disposed;

    public LinuxVisionSettings Settings { get; set; } = LinuxVisionSettings.Default;
    public event EventHandler? StatusChanged;
    public bool IsRunning => _state is "Waiting" or "Streaming";
    public bool HasSession => _videoTransport is not null;
    public string State => _state;
    public string Message => _message;
    public LinuxVisionTelemetry Telemetry => Runtime?.Telemetry ?? new("Unavailable",0,0,0,0,0);

    public LinuxVisionEngine(AdbBridge adb, string serial, SemaphoreSlim? exclusiveSessionGate = null)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _serial = string.IsNullOrWhiteSpace(serial)
            ? throw new ArgumentException("El serial ADB es obligatorio.", nameof(serial))
            : serial;
        _exclusiveSessionGate = exclusiveSessionGate ?? new SemaphoreSlim(1, 1);
    }

    public async Task<string> StartAsync(
        CancellationToken cancellationToken,
        int bitrate = 8_000_000,
        int maxSize = 1920,
        int fps = 60,
        bool mobileData = false)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        LinuxCaptureCapabilities.ValidateCapture(bitrate, maxSize, fps, mobileData);
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
            var files = new LinuxAppControlTransport();
            AdbBridge.UsbReverseLease? reverse = null;
            try
            {
                AppControlStreamOffer videoOffer = video.Prepare();
                AppControlStreamOffer controlOffer = control.Prepare();
                AppControlStreamOffer audioOffer = audio.Prepare();
                AppControlStreamOffer filesOffer = files.Prepare();
                reverse = await _adb.PrepareUsbReverseMappingsAsync(
                    _serial,
                    [
                        (DeviceVideoPort, videoOffer.Port),
                        (DeviceControlPort, controlOffer.Port),
                        (DeviceAudioPort, audioOffer.Port),
                        (DeviceFilesPort, filesOffer.Port)
                    ],
                    cancellationToken).ConfigureAwait(false);

                _videoTransport = video;
                _controlTransport = control;
                _audioTransport = audio;
                _filesTransport = files;
                Runtime = new LinuxVisionRuntime(Settings, Interlocked.Increment(ref _generation));
                Runtime.Changed += RaiseStatusChanged;
                _reverse = reverse;
                reverse = null;
                _sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _state = "Waiting";
                _message = "VisionEngine espera la autorización de captura y el flujo de AppControl.";
                _sessionTask = RunSessionAsync(
                    video, videoOffer, control, controlOffer, audio, audioOffer, files, filesOffer, Runtime,
                    _sessionCancellation.Token);
                RaiseStatusChanged();

                var offer = new LinuxVideoSourceOffer(
                    Port: DeviceVideoPort,
                    Token: videoOffer.Token,
                    Bitrate: bitrate,
                    MaxSize: maxSize,
                    Fps: fps,
                    ControlPort: DeviceControlPort,
                    ControlToken: controlOffer.Token,
                    AudioPort: DeviceAudioPort,
                    AudioToken: audioOffer.Token,
                    MuteDeviceAudio: true,
                    AudioEnabled: Settings.AudioEnabled && !mobileData, FilesPort: DeviceFilesPort, FilesToken: filesOffer.Token,
                    FilesWireVersion: 2, CaptureGeneration: Runtime.Generation,
                    CaptureCapabilities: mobileData ? LinuxCaptureCapabilities.Mobile : null,
                    AudioSource: Settings.AudioSource);
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
                    await files.DisposeAsync().ConfigureAwait(false);
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
        LinuxAppControlTransport videoTransport, AppControlStreamOffer videoOffer,
        LinuxAppControlTransport controlTransport, AppControlStreamOffer controlOffer,
        LinuxAppControlTransport audioTransport, AppControlStreamOffer audioOffer,
        LinuxAppControlTransport filesTransport, AppControlStreamOffer filesOffer,
        LinuxVisionRuntime runtime, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            Task<Stream> video=videoTransport.AcceptAsync(videoOffer,TimeSpan.FromSeconds(30),timeout.Token);
            Task<Stream> control=controlTransport.AcceptAsync(controlOffer,TimeSpan.FromSeconds(30),timeout.Token);
            Task<Stream> files=filesTransport.AcceptAsync(filesOffer,TimeSpan.FromSeconds(30),timeout.Token);
            Task<Stream>? audio=Settings.AudioEnabled ? audioTransport.AcceptAsync(audioOffer,TimeSpan.FromSeconds(30),timeout.Token) : null;
            await Task.WhenAll(audio is null ? [video,control,files] : new[]{video,control,files,audio}).ConfigureAwait(false);
            _state="Streaming"; _message="VisionEngine USB conectado: video, control y archivos autenticados."; RaiseStatusChanged();
            await runtime.RunAsync(await video,await control,audio is null ? null : await audio,await files,cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { _state="Failed"; _message=$"VisionEngine USB se detuvo: {ex.Message}"; RaiseStatusChanged(); }
        finally
        {
            _sessionCancellation?.Cancel();
            var reverse=Interlocked.Exchange(ref _reverse,null);
            try
            {
                await LinuxVisionCleanup.RunAsync(()=>runtime.DisposeAsync().AsTask(),
                    ()=>videoTransport.DisposeAsync().AsTask(),()=>controlTransport.DisposeAsync().AsTask(),
                    ()=>audioTransport.DisposeAsync().AsTask(),()=>filesTransport.DisposeAsync().AsTask(),
                    ()=>reverse is null ? Task.CompletedTask : reverse.DisposeAsync().AsTask()).ConfigureAwait(false);
            }
            catch (Exception ex) { _state="Failed"; _message="VE USB cerró con errores: "+ex.GetBaseException().Message; }
            finally
            {
                _videoTransport=null; _controlTransport=null; _audioTransport=null; _filesTransport=null;
                ReleaseExclusiveGate();
                if (_state is "Waiting" or "Streaming") { _state="Stopped"; _message="VisionEngine USB detenido."; }
                RaiseStatusChanged();
            }
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
            LinuxAppControlTransport? files = _filesTransport;
            _videoTransport = null;
            _controlTransport = null;
            _audioTransport = null;
            _filesTransport = null;
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
                if (files is not null) await files.DisposeAsync().ConfigureAwait(false);
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
