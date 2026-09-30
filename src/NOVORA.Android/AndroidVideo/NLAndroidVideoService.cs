using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Hardware.Display;
using Android.Media.Projection;
using Android.OS;
using Android.Views;
using NOVORA.Control;
using NOVORA.AndroidService;
using NOVORA.VisionEngine.Protocol;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using Resource = NOVORA.AndroidApp.Resource;

namespace NOVORA.AndroidVideo;

[Service(Name = "com.novora.appcontrol.VideoService", Exported = false,
    ForegroundServiceType = ForegroundService.TypeMediaProjection)]
public sealed class NLAndroidVideoService : Service
{
    private const string ChannelId = "novora_video";
    private const int NotificationId = 215;
    private const string StopAction = "com.novora.appcontrol.VIDEO_STOP";
    private const string OfferExtra = "novora.video.offer";
    private const string ResultExtra = "novora.video.result";
    private const string ProjectionExtra = "novora.video.projection";
    private CancellationTokenSource? _lifetime;
    private Task _run = Task.CompletedTask;
    private MediaProjection? _projection;
    private NLAndroidProjectionCallback? _projectionCallback;
    private VirtualDisplay? _display;
    private NLAndroidVideoEncoder? _encoder;
    private NLAndroidVideoCompositor? _compositor;
    private TcpClient? _client;
    private TcpClient? _controlClient;
    private TcpClient? _audioClient;
    private Task _controlTask = Task.CompletedTask;
    private NLAndroidAudioCapture? _audioCapture;
    private readonly Channel<bool> _displayChanges = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public static void Start(Context context, Result result, Intent projectionData, NLControlVideoSourceOffer offer)
    {
        var intent = new Intent(context, typeof(NLAndroidVideoService));
        intent.PutExtra(OfferExtra, JsonSerializer.Serialize(offer));
        intent.PutExtra(ResultExtra, (int)result);
        intent.PutExtra(ProjectionExtra, projectionData);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O) context.StartForegroundService(intent);
        else context.StartService(intent);
    }

    public static void Stop(Context context) =>
        context.StartService(new Intent(context, typeof(NLAndroidVideoService)).SetAction(StopAction));

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnConfigurationChanged(Configuration? newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        _displayChanges.Writer.TryWrite(true);
    }

    public override void OnCreate()
    {
        base.OnCreate();
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Video de NOVORA", NotificationImportance.Low)
        { Description = "Captura visible de pantalla para VisionEngine", LockscreenVisibility = NotificationVisibility.Private });
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == StopAction)
        {
            StartService(new Intent(this, typeof(NLAndroidServiceControl)).SetAction(NLAndroidServiceControl.StopVideoAction));
            _ = StopVideoAsync();
            return StartCommandResult.NotSticky;
        }
        StartForeground(NotificationId, BuildNotification());
        if (!_run.IsCompleted) return StartCommandResult.NotSticky;
        string? offerJson = intent?.GetStringExtra(OfferExtra);
#pragma warning disable CS0618, CA1422
        var projectionData = intent?.GetParcelableExtra(ProjectionExtra) as Intent;
#pragma warning restore CS0618, CA1422
        int result = intent?.GetIntExtra(ResultExtra, (int)Result.Canceled) ?? (int)Result.Canceled;
        var offer = string.IsNullOrWhiteSpace(offerJson)
            ? null : JsonSerializer.Deserialize<NLControlVideoSourceOffer>(offerJson);
        if (offer is null || projectionData is null || result != (int)Result.Ok)
        {
            StopSelf(startId);
            return StartCommandResult.NotSticky;
        }
        _lifetime = new CancellationTokenSource();
        _run = RunAsync((Result)result, projectionData, offer, _lifetime);
        return StartCommandResult.NotSticky;
    }

    private async Task RunAsync(Result result, Intent projectionData, NLControlVideoSourceOffer offer,
        CancellationTokenSource lifetime)
    {
        try
        {
            Validate(offer);
            _client = new TcpClient { NoDelay = true };
            await _client.ConnectAsync("127.0.0.1", offer.Port, lifetime.Token);
            NetworkStream stream = _client.GetStream();
            await stream.WriteAsync(Convert.FromHexString(offer.Token), lifetime.Token);
            _controlClient = new TcpClient { NoDelay = true };
            await _controlClient.ConnectAsync("127.0.0.1", offer.ControlPort, lifetime.Token);
            NetworkStream controlStream = _controlClient.GetStream();
            await controlStream.WriteAsync(Convert.FromHexString(offer.ControlToken), lifetime.Token);
            _controlTask = RunControlAsync(controlStream, lifetime.Token);
            var writer = new VEProtocolWriter(stream);
            var projectionManager = (MediaProjectionManager)GetSystemService(MediaProjectionService)!;
            _projection = projectionManager.GetMediaProjection((int)result, projectionData)
                ?? throw new InvalidOperationException("Android no entregó la captura de pantalla.");
            _projectionCallback = new NLAndroidProjectionCallback(() => TryCancel(lifetime));
            _projection.RegisterCallback(_projectionCallback, new Handler(Looper.MainLooper!));
            if (offer.AudioEnabled)
            {
                if (!OperatingSystem.IsAndroidVersionAtLeast(29))
                    throw new NotSupportedException("Audio AppControl requiere Android 10 o posterior.");
                _audioClient = new TcpClient { NoDelay = true };
                await _audioClient.ConnectAsync("127.0.0.1", offer.AudioPort, lifetime.Token);
                NetworkStream audioStream = _audioClient.GetStream();
                await audioStream.WriteAsync(Convert.FromHexString(offer.AudioToken), lifetime.Token);
                _audioCapture = new NLAndroidAudioCapture(this, _projection, offer.MuteDeviceAudio);
                await _audioCapture.StartAsync(audioStream, lifetime.Token);
            }
            await RunVideoAsync(writer, offer, _audioCapture?.Completion, lifetime.Token);
        }
        catch (System.OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Android.Util.Log.Error("NOVORA-VE", ex.ToString());
        }
        finally { await StopVideoAsync(); }
    }

    private async Task RunVideoAsync(
        VEProtocolWriter writer,
        NLControlVideoSourceOffer offer,
        Task? audioCompletion,
        CancellationToken cancellationToken)
    {
        NLControlVideoSize encodedSize = GetCaptureSize(offer.MaxSize);
        NLControlVideoSize sourceSize = encodedSize;
        int baseRotation = GetDisplayRotationDegrees();
        await writer.WriteVideoSessionAsync(
            VEProtocolCodec.H264,
            new VEProtocolSession(encodedSize.Width, encodedSize.Height, false, 0),
            cancellationToken);

        long nextPresentationTimeUs = 0;
        while (_displayChanges.Reader.TryRead(out _)) { }
        var encoderFailure = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _encoder = new NLAndroidVideoEncoder(
            encodedSize.Width,
            encodedSize.Height,
            offer.Bitrate,
            offer.Fps,
            ex => encoderFailure.TrySetResult(ex));
        _compositor = new NLAndroidVideoCompositor(
            _encoder.InputSurface, encodedSize.Width, encodedSize.Height);
        Surface captureSurface = await _compositor.GetCaptureSurfaceAsync(cancellationToken);
        await _compositor.UpdateSourceAsync(sourceSize.Width, sourceSize.Height, 0, cancellationToken);

        var metrics = Resources?.DisplayMetrics
            ?? throw new InvalidOperationException("Pantalla no disponible.");
        _display = _projection!.CreateVirtualDisplay(
            "NOVORA VisionEngine",
            sourceSize.Width,
            sourceSize.Height,
            (int)metrics.DensityDpi,
            (Android.Views.DisplayFlags)16,
            captureSurface,
            null,
            null)
            ?? throw new InvalidOperationException("Android no creó la pantalla virtual.");
        _encoder.Start();

        // ChannelReader.ReadAllAsync devuelve un enumerador cuya liberación
        // asíncrona no está implementada en este runtime Android. Los recursos
        // reales pertenecen al encoder y se liberan en StopVideoAsync.
        IAsyncEnumerator<NLAndroidVideoPacket> packets =
            _encoder.Packets.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        Task<bool> packetReady = packets.MoveNextAsync().AsTask();
        Task<bool> displayChanged = _displayChanges.Reader
            .WaitToReadAsync(cancellationToken).AsTask();
        Task audioFailure = audioCompletion ?? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        VEProtocolSession? pendingSession = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            Task completed = await Task.WhenAny(
                packetReady, displayChanged, encoderFailure.Task, audioFailure);
            if (completed == encoderFailure.Task)
                throw await encoderFailure.Task;
            if (completed == audioFailure)
            {
                await audioFailure;
                throw new EndOfStreamException("La captura de audio Android terminó inesperadamente.");
            }

            if (completed == displayChanged && await displayChanged)
            {
                while (_displayChanges.Reader.TryRead(out _)) { }
                displayChanged = _displayChanges.Reader.WaitToReadAsync(cancellationToken).AsTask();
                NLControlVideoSize next = GetCaptureSize(offer.MaxSize);
                int rotation = (GetDisplayRotationDegrees() - baseRotation + 360) % 360;
                sourceSize = next;
                await _compositor.UpdateSourceAsync(next.Width, next.Height, rotation, cancellationToken);
                metrics = Resources?.DisplayMetrics
                    ?? throw new InvalidOperationException("Pantalla no disponible.");
                _display.Resize(next.Width, next.Height, (int)metrics.DensityDpi);
                pendingSession = new VEProtocolSession(
                    encodedSize.Width,
                    encodedSize.Height,
                    true,
                    rotation);
                _encoder.RequestKeyFrame();
                continue;
            }

            if (!await packetReady)
                throw new EndOfStreamException("El encoder Android cerró el video inesperadamente.");
            NLAndroidVideoPacket packet = packets.Current;
            if (pendingSession is not null)
            {
                if (!packet.KeyFrame)
                {
                    packetReady = packets.MoveNextAsync().AsTask();
                    continue;
                }

                await writer.WriteSessionUpdateAsync(pendingSession, cancellationToken);
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
                cancellationToken);
            packetReady = packets.MoveNextAsync().AsTask();
        }
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

    private NLControlVideoSize GetCaptureSize(int maxSize)
    {
        var metrics = Resources?.DisplayMetrics
            ?? throw new InvalidOperationException("Pantalla no disponible.");
        return NLControlVideoSize.Fit(
            Math.Max(2, metrics.WidthPixels),
            Math.Max(2, metrics.HeightPixels),
            maxSize);
    }

    private static void Validate(NLControlVideoSourceOffer offer)
    {
        if (offer.Port is < 1 or > 65535 || offer.Token.Length != 64 ||
            offer.ControlPort is < 1 or > 65535 || offer.ControlToken.Length != 64 ||
            (offer.AudioEnabled &&
                (offer.AudioPort is < 1 or > 65535 || offer.AudioToken.Length != 64)) ||
            offer.Bitrate is < 250_000 or > 100_000_000 || offer.MaxSize is < 240 or > 4320 ||
            offer.Fps is < 10 or > 120)
            throw new InvalidDataException("Oferta de video AppControl inválida.");
        _ = Convert.FromHexString(offer.Token);
        _ = Convert.FromHexString(offer.ControlToken);
        if (offer.AudioEnabled) _ = Convert.FromHexString(offer.AudioToken);
    }

    private static async Task RunControlAsync(Stream stream, CancellationToken cancellationToken)
    {
        await NLControlInputLoop.RunDuplexAsync(
            stream,
            NLAndroidControlAccessibilityService.ExecuteWithResponseAsync,
            async (command, exception, token) =>
            {
                Android.Util.Log.Warn("NOVORA-VE",
                    $"Orden de control descartada sin detener VisionEngine: {exception.Message}");
                if (command.Type == 2)
                    await NLAndroidControlAccessibilityService.ResetPointersAsync(token);
            },
            TimeSpan.FromSeconds(2),
            cancellationToken);
    }

    private Notification BuildNotification()
    {
        var stop = new Intent(this, typeof(NLAndroidVideoService)).SetAction(StopAction);
        var pending = PendingIntent.GetService(this, 215, stop, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        return new Notification.Builder(this, ChannelId)
            .SetSmallIcon(Resource.Drawable.novora_notification)
            .SetContentTitle("VisionEngine activo")
            .SetContentText("NOVORA comparte la pantalla con tu PC")
            .SetOngoing(true)
            .AddAction(new Notification.Action.Builder(null, "Detener", pending).Build())
            .Build();
    }

    private async Task StopVideoAsync()
    {
        var lifetime = Interlocked.Exchange(ref _lifetime, null);
        TryCancel(lifetime);
        try { _display?.Release(); } catch { }
        _display = null;
        if (_compositor is not null) await _compositor.DisposeAsync();
        _compositor = null;
        if (_projection is not null && _projectionCallback is not null)
        {
            try { _projection.UnregisterCallback(_projectionCallback); } catch { }
        }
        _projectionCallback = null;
        try { _projection?.Stop(); } catch { }
        _projection = null;
        if (_encoder is not null) await _encoder.DisposeAsync();
        _encoder = null;
        if (_audioCapture is not null && OperatingSystem.IsAndroidVersionAtLeast(29))
            await _audioCapture.DisposeAsync();
        _audioCapture = null;
        try { _client?.Dispose(); } catch { }
        _client = null;
        try { _controlClient?.Dispose(); } catch { }
        _controlClient = null;
        try { _audioClient?.Dispose(); } catch { }
        _audioClient = null;
        try { await _controlTask.ConfigureAwait(false); }
        catch (Exception ex) when (ex is System.OperationCanceledException or IOException or ObjectDisposedException) { }
        _controlTask = Task.CompletedTask;
        lifetime?.Dispose();
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
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

    private sealed class NLAndroidProjectionCallback(Action stopped) : MediaProjection.Callback
    {
        public override void OnStop() => stopped();
    }
}
