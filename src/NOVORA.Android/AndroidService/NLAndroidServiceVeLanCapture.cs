using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Hardware.Display;
using Android.Media.Projection;
using Android.OS;
using Android.Views;
using NOVORA.AndroidVideo;
using NOVORA.Control;
using NOVORA.AndroidStorage;
using NOVORA.VisionEngine.Protocol;
using System.Text.Json;
using System.Threading.Channels;

namespace NOVORA.AndroidService;

[Service(Name = "com.novora.appcontrol.VeLanCaptureService", Exported = false,
    ForegroundServiceType = ForegroundService.TypeMediaProjection)]
internal sealed class NLAndroidServiceVeLanCapture : Service
{
    internal const string StartAction = "com.novora.appcontrol.VE_LAN_START";
    internal const string StopAction = "com.novora.appcontrol.VE_LAN_STOP";
    internal const string ReconfigureAction = "com.novora.appcontrol.VE_LAN_RECONFIGURE";
    private const string ChannelId = "novora_ve_lan";
    private const int NotificationId = 316;
    private const string OfferExtra = "novora.ve.lan.offer";
    private const string ResultExtra = "novora.ve.lan.result";
    private const string ProjectionExtra = "novora.ve.lan.projection";
    private static Func<NLControlInputCommand, CancellationToken, Task<NLControlInputResponse?>>? _executeControl;
    private static Func<CancellationToken, Task>? _resetPointers;
    private static Func<Task>? _stopRemote;
    private static int _smallIconResource;
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private readonly Channel<bool> _displayChanges = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly Channel<NLControlVeLanOffer> _reconfigurations = Channel.CreateBounded<NLControlVeLanOffer>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private CancellationTokenSource? _lifetime;
    private Task _run = Task.CompletedTask;
    private NLAndroidVideoLanStreams? _streams;
    private MediaProjection? _projection;
    private ProjectionStoppedCallback? _projectionCallback;
    private VirtualDisplay? _display;
    private NLAndroidVideoEncoder? _encoder;
    private NLAndroidVideoCompositor? _compositor;
    private NLControlCapturePolicySession? _capturePolicySession;
    private NLControlNetworkCapabilities? _captureCapabilities;
    private long _captureGeneration;
    private readonly NLControlCaptureTelemetrySequencer _captureTelemetrySequence = new();
    private NLAndroidAudioCapture? _audioCapture;

    internal static void ConfigureControlHandling(
        Func<NLControlInputCommand, CancellationToken, Task<NLControlInputResponse?>> execute,
        Func<CancellationToken, Task> resetPointers,
        Func<Task> stopRemote,
        int smallIconResource)
    {
        _executeControl = execute ?? throw new ArgumentNullException(nameof(execute));
        _resetPointers = resetPointers ?? throw new ArgumentNullException(nameof(resetPointers));
        _stopRemote = stopRemote ?? throw new ArgumentNullException(nameof(stopRemote));
        _smallIconResource = smallIconResource;
    }

    internal static void Start(
        Context context,
        Result result,
        Intent projectionData,
        NLControlVeLanOffer offer)
    {
        var intent = new Intent(context, typeof(NLAndroidServiceVeLanCapture))
            .SetAction(StartAction)
            .PutExtra(OfferExtra, JsonSerializer.Serialize(offer))
            .PutExtra(ResultExtra, (int)result)
            .PutExtra(ProjectionExtra, projectionData);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O) context.StartForegroundService(intent);
        else context.StartService(intent);
    }

    internal static void Stop(Context context) => context.StartService(
        new Intent(context, typeof(NLAndroidServiceVeLanCapture)).SetAction(StopAction));

    internal static void Reconfigure(Context context, NLControlVeLanOffer offer) => context.StartService(
        new Intent(context, typeof(NLAndroidServiceVeLanCapture))
            .SetAction(ReconfigureAction)
            .PutExtra(OfferExtra, JsonSerializer.Serialize(offer)));

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel(
            ChannelId, "VisionEngine por LAN", NotificationImportance.Low)
        {
            Description = "Captura visible de pantalla para NOVORA VisionEngine",
            LockscreenVisibility = NotificationVisibility.Private
        });
    }

    public override void OnConfigurationChanged(Configuration? newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        _displayChanges.Writer.TryWrite(true);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == StopAction)
        {
            TryCancel(_lifetime);
            _ = StopRemoteAndCaptureAsync();
            return StartCommandResult.NotSticky;
        }
        if (intent?.Action == ReconfigureAction)
        {
            string? reconfigureOfferJson = intent.GetStringExtra(OfferExtra);
            NLControlVeLanOffer? reconfigureOffer = string.IsNullOrWhiteSpace(reconfigureOfferJson)
                ? null : JsonSerializer.Deserialize<NLControlVeLanOffer>(reconfigureOfferJson);
            if (reconfigureOffer is not null && !_run.IsCompleted)
                _reconfigurations.Writer.TryWrite(reconfigureOffer);
            return StartCommandResult.NotSticky;
        }
        if (intent?.Action != StartAction || !_run.IsCompleted)
            return StartCommandResult.NotSticky;

        StartForeground(NotificationId, BuildNotification());
        string? offerJson = intent.GetStringExtra(OfferExtra);
#pragma warning disable CS0618, CA1422
        var projectionData = intent.GetParcelableExtra(ProjectionExtra) as Intent;
#pragma warning restore CS0618, CA1422
        int result = intent.GetIntExtra(ResultExtra, (int)Result.Canceled);
        NLControlVeLanOffer? offer = string.IsNullOrWhiteSpace(offerJson)
            ? null : JsonSerializer.Deserialize<NLControlVeLanOffer>(offerJson);
        if (offer is null || projectionData is null || result != (int)Result.Ok)
        {
            _ = StopRemoteAndCaptureAsync();
            return StartCommandResult.NotSticky;
        }

        _lifetime = new CancellationTokenSource();
        _run = RunAsync((Result)result, projectionData, offer, _lifetime);
        return StartCommandResult.NotSticky;
    }

    private async Task RunAsync(
        Result result,
        Intent projectionData,
        NLControlVeLanOffer offer,
        CancellationTokenSource lifetime)
    {
        try
        {
            _captureGeneration = offer.CaptureGeneration;
            _captureCapabilities = offer.CaptureCapabilities;
            NLControlVeLanOffer effectiveOffer = CheckSelfPermission(
                Android.Manifest.Permission.RecordAudio) == Permission.Granted
                ? offer
                : offer with { Audio = null };
            bool canReceiveLanFiles = NLAndroidLanFileReceiver.TryCreate(this,
                out NLAndroidLanFileReceiver? fileReceiver);
            var lanClient = new NLAndroidVideoLanClient();
            _streams = await lanClient.ConnectAsync(
                effectiveOffer, canReceiveLanFiles || offer.FilesWireVersion == 2, lifetime.Token)
                .ConfigureAwait(false);
            if (lanClient.FilesConnectionError is { } fileError)
                Android.Util.Log.Warn("NOVORA-VE-LAN", "Canal FILES opcional no disponible: " + fileError);
            var projectionManager = (MediaProjectionManager)GetSystemService(MediaProjectionService)!;
            _projection = projectionManager.GetMediaProjection((int)result, projectionData)
                ?? throw new InvalidOperationException("Android no entregó la captura de pantalla.");
            _projectionCallback = new ProjectionStoppedCallback(() => TryCancel(lifetime));
            _projection.RegisterCallback(_projectionCallback, new Handler(Looper.MainLooper!));

            Task control = MonitorControlAsync(_streams.Control, lifetime.Token);
            if (offer.FilesWireVersion == 2 && _streams.Files is not null)
            {
                try
                {
                    await NLAndroidServiceFilesV2.AttachAsync(this, _streams.Files,
                        offer.CaptureGeneration, lifetime.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!lifetime.IsCancellationRequested)
                {
                    Android.Util.Log.Warn("NOVORA-VE-LAN", "Canal FILES v2 no disponible: " + ex.Message);
                    try { await NLAndroidServiceFilesV2.DetachAsync(offer.CaptureGeneration).ConfigureAwait(false); } catch { }
                    try { await _streams.Files.DisposeAsync().ConfigureAwait(false); } catch { }
                }
            }
            else if (offer.FilesWireVersion == 1 && fileReceiver is not null && _streams.Files is not null)
                _ = fileReceiver.RunAsync(_streams.Files, lifetime.Token).ContinueWith(task =>
                {
                    if (task.Exception is not null)
                        Android.Util.Log.Warn("NOVORA-VE-LAN", "Canal FILES cerrado: " + task.Exception.GetBaseException().Message);
                }, TaskContinuationOptions.OnlyOnFaulted);
            Task? audio = null;
            if (_streams.Audio is not null)
            {
                if (!OperatingSystem.IsAndroidVersionAtLeast(29))
                    throw new NotSupportedException("El audio de pantalla requiere Android 10 o posterior.");
                _audioCapture = new NLAndroidAudioCapture(this, _projection, offer.MuteDeviceAudio);
                await _audioCapture.StartAsync(_streams.Audio, lifetime.Token).ConfigureAwait(false);
                audio = _audioCapture.Completion;
            }
            Task video = RunVideoAsync(
                new VEProtocolWriter(_streams.Video), offer, audio, lifetime.Token);
            Task ended = await Task.WhenAny(video, control).ConfigureAwait(false);
            await ended.ConfigureAwait(false);
            throw new EndOfStreamException("Terminó un canal VE LAN.");
        }
        catch (System.OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Android.Util.Log.Error("NOVORA-VE-LAN", ex.ToString());
        }
        finally
        {
            // The FILES channel is optional. Its loss must not interrupt video/control.
            await StopRemoteAndCaptureAsync().ConfigureAwait(false);
        }
    }

    private async Task RunVideoAsync(
        VEProtocolWriter writer,
        NLControlVeLanOffer offer,
        Task? audioCompletion,
        CancellationToken cancellationToken)
    {
        bool initialSession = true;
        NLControlVeLanOffer activeOffer = offer;
        while (!cancellationToken.IsCancellationRequested)
        {
            var encoderFailure = new TaskCompletionSource<Exception>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            NLControlVideoSize encodedSize = await ConfigureVideoPipelineAsync(
                activeOffer, ex => encoderFailure.TrySetResult(ex), cancellationToken).ConfigureAwait(false);
            if (initialSession)
            {
                await writer.WriteVideoSessionAsync(
                    VEProtocolCodec.H264,
                    new VEProtocolSession(encodedSize.Width, encodedSize.Height, false, 0),
                    cancellationToken).ConfigureAwait(false);
            }

            NLAndroidVideoEncoder encoder = _encoder
                ?? throw new InvalidOperationException("El codificador VE LAN no se inicializó.");
            await using IAsyncEnumerator<NLAndroidVideoPacket> packets = encoder.Packets
                .ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            Task<bool> packetReady = packets.MoveNextAsync().AsTask();
            Task<bool> displayChanged = _displayChanges.Reader.WaitToReadAsync(cancellationToken).AsTask();
            Task<NLControlVeLanOffer> reconfigured = _reconfigurations.Reader.ReadAsync(cancellationToken).AsTask();
            Task audioFailure = audioCompletion ?? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            VEProtocolSession? pendingSession = initialSession
                ? null : new VEProtocolSession(encodedSize.Width, encodedSize.Height, true, 0);
            long nextPresentationTimeUs = 0;
            NLControlVeLanOffer? nextOffer = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                Task completed = await Task.WhenAny(
                    packetReady, displayChanged, reconfigured, encoderFailure.Task, audioFailure).ConfigureAwait(false);
                if (completed == encoderFailure.Task) throw await encoderFailure.Task.ConfigureAwait(false);
                if (completed == audioFailure)
                {
                    await audioFailure.ConfigureAwait(false);
                    throw new EndOfStreamException("Terminó el audio VE LAN.");
                }
                if (completed == reconfigured)
                {
                    nextOffer = await reconfigured.ConfigureAwait(false);
                    while (_reconfigurations.Reader.TryRead(out NLControlVeLanOffer? latest))
                        if (latest is not null) nextOffer = latest;
                    break;
                }
                if (completed == displayChanged && await displayChanged.ConfigureAwait(false))
                {
                    while (_displayChanges.Reader.TryRead(out _)) { }
                    displayChanged = _displayChanges.Reader.WaitToReadAsync(cancellationToken).AsTask();
                    NLControlVideoSize next = GetCaptureSize(activeOffer.MaxSize);
                    int rotation = GetDisplayRotationDegrees();
                    await _compositor!.UpdateSourceAsync(
                        next.Width, next.Height, rotation, cancellationToken).ConfigureAwait(false);
                    var metrics = Resources?.DisplayMetrics
                        ?? throw new InvalidOperationException("Pantalla no disponible.");
                    _display!.Resize(next.Width, next.Height, (int)metrics.DensityDpi);
                    pendingSession = new VEProtocolSession(
                        encodedSize.Width, encodedSize.Height, true, rotation);
                    encoder.RequestKeyFrame();
                    continue;
                }
                if (!await packetReady.ConfigureAwait(false))
                    throw new EndOfStreamException("El codificador Android cerró el video.");
                NLAndroidVideoPacket packet = packets.Current;
                encoder.PacketDequeued(packet);
                if (pendingSession is not null)
                {
                    // A size change also changes the codec configuration. Publish the
                    // session before that configuration reaches the receiver.
                    if (!packet.Configuration && !packet.KeyFrame)
                    {
                        packetReady = packets.MoveNextAsync().AsTask();
                        continue;
                    }
                    await writer.WriteSessionUpdateAsync(pendingSession, cancellationToken)
                        .ConfigureAwait(false);
                    pendingSession = null;
                }
                long? presentationTimeUs = packet.PresentationTimeUs;
                if (presentationTimeUs is long sourcePts)
                {
                    presentationTimeUs = Math.Max(nextPresentationTimeUs, sourcePts);
                    nextPresentationTimeUs = presentationTimeUs.Value + 1;
                }
                long writeStarted = TimeProvider.System.GetTimestamp();
                encoder.Telemetry.BeginWrite();
                await writer.WriteVideoPacketAsync(
                    packet.Payload, presentationTimeUs, packet.Configuration, packet.KeyFrame,
                    cancellationToken).ConfigureAwait(false);
                encoder.Telemetry.RecordWrite(packet.Payload.Length, packet.Payload.Length + VEProtocolConstants.PacketHeaderSizeVE,
                    TimeProvider.System.GetElapsedTime(writeStarted));
                packetReady = packets.MoveNextAsync().AsTask();
            }

            if (nextOffer is null) return;
            await DisposeVideoPipelineAsync(releaseDisplay: false).ConfigureAwait(false);
            activeOffer = nextOffer;
            initialSession = false;
        }
    }

    private async Task<NLControlVideoSize> ConfigureVideoPipelineAsync(
        NLControlVeLanOffer offer, Action<Exception> failed, CancellationToken cancellationToken)
    {
        NLControlVideoSize encodedSize = GetCaptureSize(offer.MaxSize);
        _captureGeneration = offer.CaptureGeneration;
        _captureCapabilities = offer.CaptureCapabilities;
        _encoder = new NLAndroidVideoEncoder(
            encodedSize.Width, encodedSize.Height, offer.Bitrate, offer.Fps,
            failed, offer.CaptureGeneration);
        _compositor = new NLAndroidVideoCompositor(
            _encoder.InputSurface, encodedSize.Width, encodedSize.Height,
            offer.CaptureGeneration > 0 && offer.CaptureCapabilities?.CaptureFramePacing == true ? offer.Fps : 0);
        Surface captureSurface = await _compositor.GetCaptureSurfaceAsync(cancellationToken)
            .ConfigureAwait(false);
        await _compositor.UpdateSourceAsync(
            encodedSize.Width, encodedSize.Height, 0, cancellationToken).ConfigureAwait(false);
        var metrics = Resources?.DisplayMetrics
            ?? throw new InvalidOperationException("Pantalla no disponible.");
        if (_display is null)
        {
            _display = _projection!.CreateVirtualDisplay(
                "NOVORA VisionEngine LAN", encodedSize.Width, encodedSize.Height,
                (int)metrics.DensityDpi, (DisplayFlags)16, captureSurface, null, null)
                ?? throw new InvalidOperationException("Android no creó la pantalla virtual.");
        }
        else
        {
            _display.Resize(encodedSize.Width, encodedSize.Height, (int)metrics.DensityDpi);
            _display.Surface = captureSurface;
        }
        _encoder.Start();
        var activeEncoder = _encoder;
        var activeCompositor = _compositor;
        _capturePolicySession = new NLControlCapturePolicySession(offer.CaptureGeneration, offer.Bitrate,
            offer.Fps, offer.CaptureCapabilities ?? new(), activeEncoder.TrySetTargetBitrate,
            activeCompositor.TrySetTargetFps);
        return encodedSize;
    }

    private async Task DisposeVideoPipelineAsync(bool releaseDisplay)
    {
        Interlocked.Exchange(ref _capturePolicySession, null)?.Close();
        if (_display is not null)
        {
            try { _display.Surface = null; } catch { }
            if (releaseDisplay)
            {
                try { _display.Release(); } catch { }
                _display = null;
            }
        }
        if (_compositor is not null) await _compositor.DisposeAsync().ConfigureAwait(false);
        _compositor = null;
        if (_encoder is not null) await _encoder.DisposeAsync().ConfigureAwait(false);
        _encoder = null;
    }

    private async Task MonitorControlAsync(Stream stream, CancellationToken cancellationToken)
    {
        await NLControlInputLoop.RunDuplexAsync(
            stream,
            async (command, token) =>
            {
                if (command.Type == 17)
                {
                    _encoder?.RequestKeyFrame();
                    return null;
                }
                if (command.Type == 23)
                {
                    NLControlCapturePolicy? policy = command.CapturePolicy;
                    var ack = policy is not null && _capturePolicySession is { } session
                        ? session.Apply(policy) : new NLControlCapturePolicyAck(
                            policy?.Generation ?? _captureGeneration, policy?.Sequence ?? 0, false,
                            policy?.TargetBitrate ?? 0, null, "La captura no está disponible.");
                    return new NLControlInputResponse(NLControlInputResponse.CapturePolicyAckType,
                        CapturePolicyAck: ack);
                }
                if (command.Type == 24 && _captureGeneration > 0 && _captureCapabilities?.StabilityTelemetry == true)
                    return new NLControlInputResponse(NLControlInputResponse.NetworkEchoType, Sequence: command.Sequence,
                        // CLOCK_MONOTONIC, the same base as the SurfaceTexture/MediaCodec timestamps.
                        DeviceClockUs: Java.Lang.JavaSystem.NanoTime() / 1000);
                return await (_executeControl ?? throw new InvalidOperationException(
                    "No se configuró el control de accesibilidad para VisionEngine LAN."))
                    (command, token).ConfigureAwait(false);
            },
            async (command, exception, token) =>
            {
                Android.Util.Log.Warn("NOVORA-VE-LAN",
                    $"Orden {command.Type} descartada: {exception.Message}");
                if (command.Type == 2)
                    await (_resetPointers ?? (_ => Task.CompletedTask))(token)
                        .ConfigureAwait(false);
            },
            TimeSpan.FromSeconds(2),
            cancellationToken, _captureGeneration > 0 && _captureCapabilities?.StabilityTelemetry == true
                ? CaptureTelemetryResponse : null).ConfigureAwait(false);
    }

    private NLControlInputResponse? CaptureTelemetryResponse()
    {
        NLControlCaptureTelemetry? sample = _encoder?.TakeTelemetry();
        return sample is null ? null : new(NLControlInputResponse.CaptureTelemetryType,
            CaptureTelemetry: _captureTelemetrySequence.Stamp(sample));
    }

    private NLControlVideoSize GetCaptureSize(int maxSize)
    {
        var metrics = Resources?.DisplayMetrics
            ?? throw new InvalidOperationException("Pantalla no disponible.");
        return NLControlVideoSize.Fit(
            Math.Max(2, metrics.WidthPixels), Math.Max(2, metrics.HeightPixels), maxSize);
    }

    private int GetDisplayRotationDegrees()
    {
        var manager = (DisplayManager?)GetSystemService(DisplayService);
        return manager?.GetDisplay(Android.Views.Display.DefaultDisplay)?.Rotation switch
        {
            SurfaceOrientation.Rotation90 => 90,
            SurfaceOrientation.Rotation180 => 180,
            SurfaceOrientation.Rotation270 => 270,
            _ => 0
        };
    }

    private Notification BuildNotification()
    {
        var stop = new Intent(this, typeof(NLAndroidServiceVeLanCapture)).SetAction(StopAction);
        var pending = PendingIntent.GetService(
            this, NotificationId, stop,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        return new Notification.Builder(this, ChannelId)
            .SetSmallIcon(_smallIconResource != 0 ? _smallIconResource : Android.Resource.Drawable.IcMenuView)
            .SetContentTitle("VisionEngine LAN activo")
            .SetContentText("LINK Control comparte la pantalla con tu PC")
            .SetOngoing(true)
            .AddAction(new Notification.Action.Builder(null, "Detener", pending).Build())
            .Build();
    }

    private async Task StopRemoteAndCaptureAsync()
    {
        await _stopGate.WaitAsync().ConfigureAwait(false);
        try
        {
            CancellationTokenSource? lifetime = Interlocked.Exchange(ref _lifetime, null);
            TryCancel(lifetime);
            Interlocked.Exchange(ref _capturePolicySession, null)?.Close();
            try { await NLAndroidServiceFilesV2.DetachAsync(_captureGeneration).ConfigureAwait(false); }
            catch (Exception ex) { Android.Util.Log.Warn("NOVORA-VE-LAN", "Cierre FILES v2: " + ex.Message); }
            try { _display?.Release(); } catch { }
            _display = null;
            if (_compositor is not null) await _compositor.DisposeAsync().ConfigureAwait(false);
            _compositor = null;
            if (_projection is not null && _projectionCallback is not null)
            {
                try { _projection.UnregisterCallback(_projectionCallback); } catch { }
            }
            _projectionCallback = null;
            try { _projection?.Stop(); } catch { }
            _projection = null;
            if (_encoder is not null) await _encoder.DisposeAsync().ConfigureAwait(false);
            _encoder = null;
            if (_audioCapture is not null && OperatingSystem.IsAndroidVersionAtLeast(29))
                await _audioCapture.DisposeAsync().ConfigureAwait(false);
            _audioCapture = null;
            if (_streams is not null) await _streams.DisposeAsync().ConfigureAwait(false);
            _streams = null;
            lifetime?.Dispose();
            try
            {
                if (_stopRemote is not null) await _stopRemote().ConfigureAwait(false);
            }
            catch { }
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
        }
        finally
        {
            _stopGate.Release();
        }
    }

    public override void OnDestroy()
    {
        TryCancel(_lifetime);
        base.OnDestroy();
    }

    private static void TryCancel(CancellationTokenSource? source)
    {
        if (source is null) return;
        try { source.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private sealed class ProjectionStoppedCallback(Action stopped) : MediaProjection.Callback
    {
        public override void OnStop() => stopped();
    }
}
