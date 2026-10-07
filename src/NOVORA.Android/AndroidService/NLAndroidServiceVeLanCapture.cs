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
    private CancellationTokenSource? _lifetime;
    private Task _run = Task.CompletedTask;
    private NLAndroidVideoLanStreams? _streams;
    private MediaProjection? _projection;
    private ProjectionStoppedCallback? _projectionCallback;
    private VirtualDisplay? _display;
    private NLAndroidVideoEncoder? _encoder;
    private NLAndroidVideoCompositor? _compositor;
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
            NLControlVeLanOffer effectiveOffer = CheckSelfPermission(
                Android.Manifest.Permission.RecordAudio) == Permission.Granted
                ? offer
                : offer with { Audio = null };
            _streams = await new NLAndroidVideoLanClient().ConnectAsync(effectiveOffer, lifetime.Token)
                .ConfigureAwait(false);
            var projectionManager = (MediaProjectionManager)GetSystemService(MediaProjectionService)!;
            _projection = projectionManager.GetMediaProjection((int)result, projectionData)
                ?? throw new InvalidOperationException("Android no entregó la captura de pantalla.");
            _projectionCallback = new ProjectionStoppedCallback(() => TryCancel(lifetime));
            _projection.RegisterCallback(_projectionCallback, new Handler(Looper.MainLooper!));

            Task control = MonitorControlAsync(_streams.Control, lifetime.Token);
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
            await StopRemoteAndCaptureAsync().ConfigureAwait(false);
        }
    }

    private async Task RunVideoAsync(
        VEProtocolWriter writer,
        NLControlVeLanOffer offer,
        Task? audioCompletion,
        CancellationToken cancellationToken)
    {
        NLControlVideoSize encodedSize = GetCaptureSize(offer.MaxSize);
        NLControlVideoSize sourceSize = encodedSize;
        int baseRotation = GetDisplayRotationDegrees();
        await writer.WriteVideoSessionAsync(
            VEProtocolCodec.H264,
            new VEProtocolSession(encodedSize.Width, encodedSize.Height, false, 0),
            cancellationToken).ConfigureAwait(false);

        var encoderFailure = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _encoder = new NLAndroidVideoEncoder(
            encodedSize.Width, encodedSize.Height, offer.Bitrate, offer.Fps,
            ex => encoderFailure.TrySetResult(ex));
        _compositor = new NLAndroidVideoCompositor(
            _encoder.InputSurface, encodedSize.Width, encodedSize.Height);
        Surface captureSurface = await _compositor.GetCaptureSurfaceAsync(cancellationToken)
            .ConfigureAwait(false);
        await _compositor.UpdateSourceAsync(
            sourceSize.Width, sourceSize.Height, 0, cancellationToken).ConfigureAwait(false);
        var metrics = Resources?.DisplayMetrics
            ?? throw new InvalidOperationException("Pantalla no disponible.");
        _display = _projection!.CreateVirtualDisplay(
            "NOVORA VisionEngine LAN", sourceSize.Width, sourceSize.Height,
            (int)metrics.DensityDpi, (DisplayFlags)16, captureSurface, null, null)
            ?? throw new InvalidOperationException("Android no creó la pantalla virtual.");
        _encoder.Start();

        IAsyncEnumerator<NLAndroidVideoPacket> packets = _encoder.Packets
            .ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        Task<bool> packetReady = packets.MoveNextAsync().AsTask();
        Task<bool> displayChanged = _displayChanges.Reader.WaitToReadAsync(cancellationToken).AsTask();
        Task audioFailure = audioCompletion ?? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        VEProtocolSession? pendingSession = null;
        long nextPresentationTimeUs = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            Task completed = await Task.WhenAny(
                packetReady, displayChanged, encoderFailure.Task, audioFailure).ConfigureAwait(false);
            if (completed == encoderFailure.Task) throw await encoderFailure.Task.ConfigureAwait(false);
            if (completed == audioFailure)
            {
                await audioFailure.ConfigureAwait(false);
                throw new EndOfStreamException("Terminó el audio VE LAN.");
            }
            if (completed == displayChanged && await displayChanged.ConfigureAwait(false))
            {
                while (_displayChanges.Reader.TryRead(out _)) { }
                displayChanged = _displayChanges.Reader.WaitToReadAsync(cancellationToken).AsTask();
                NLControlVideoSize next = GetCaptureSize(offer.MaxSize);
                int rotation = (GetDisplayRotationDegrees() - baseRotation + 360) % 360;
                sourceSize = next;
                await _compositor.UpdateSourceAsync(
                    next.Width, next.Height, rotation, cancellationToken).ConfigureAwait(false);
                metrics = Resources?.DisplayMetrics
                    ?? throw new InvalidOperationException("Pantalla no disponible.");
                _display.Resize(next.Width, next.Height, (int)metrics.DensityDpi);
                pendingSession = new VEProtocolSession(
                    encodedSize.Width, encodedSize.Height, true, rotation);
                _encoder.RequestKeyFrame();
                continue;
            }
            if (!await packetReady.ConfigureAwait(false))
                throw new EndOfStreamException("El codificador Android cerró el video.");
            NLAndroidVideoPacket packet = packets.Current;
            if (pendingSession is not null)
            {
                if (!packet.KeyFrame)
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
            await writer.WriteVideoPacketAsync(
                packet.Payload, presentationTimeUs, packet.Configuration, packet.KeyFrame,
                cancellationToken).ConfigureAwait(false);
            packetReady = packets.MoveNextAsync().AsTask();
        }
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
            cancellationToken).ConfigureAwait(false);
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
            .SetContentText("NOVORA-LINK comparte la pantalla con tu PC")
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
