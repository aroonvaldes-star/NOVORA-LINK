using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Display;
using Android.Media.Projection;
using Android.OS;
using NOVORA.Control;
using NOVORA.AndroidService;
using NOVORA.VisionEngine.Protocol;
using System.Net.Sockets;
using System.Text.Json;
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
    private TcpClient? _client;
    private TcpClient? _controlClient;
    private TcpClient? _audioClient;
    private Task _controlTask = Task.CompletedTask;
    private NLAndroidAudioCapture? _audioCapture;

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
            var metrics = Resources?.DisplayMetrics ?? throw new InvalidOperationException("Pantalla no disponible.");
            int sourceWidth = Math.Max(2, metrics.WidthPixels);
            int sourceHeight = Math.Max(2, metrics.HeightPixels);
            double scale = Math.Min(1d, offer.MaxSize / (double)Math.Max(sourceWidth, sourceHeight));
            int width = Math.Max(2, ((int)(sourceWidth * scale)) & ~1);
            int height = Math.Max(2, ((int)(sourceHeight * scale)) & ~1);

            _client = new TcpClient { NoDelay = true };
            await _client.ConnectAsync("127.0.0.1", offer.Port, lifetime.Token);
            NetworkStream stream = _client.GetStream();
            await stream.WriteAsync(Convert.FromHexString(offer.Token), lifetime.Token);
            _controlClient = new TcpClient { NoDelay = true };
            await _controlClient.ConnectAsync("127.0.0.1", offer.ControlPort, lifetime.Token);
            NetworkStream controlStream = _controlClient.GetStream();
            await controlStream.WriteAsync(Convert.FromHexString(offer.ControlToken), lifetime.Token);
            _controlTask = RunControlAsync(controlStream, lifetime.Token);
            _audioClient = new TcpClient { NoDelay = true };
            await _audioClient.ConnectAsync("127.0.0.1", offer.AudioPort, lifetime.Token);
            NetworkStream audioStream = _audioClient.GetStream();
            await audioStream.WriteAsync(Convert.FromHexString(offer.AudioToken), lifetime.Token);
            var writer = new VEProtocolWriter(stream);
            await writer.WriteVideoSessionAsync(VEProtocolCodec.H264,
                new VEProtocolSession(width, height, false), lifetime.Token);

            Exception? encoderFailure = null;
            _encoder = new NLAndroidVideoEncoder(width, height, offer.Bitrate, offer.Fps,
                ex => { encoderFailure = ex; lifetime.Cancel(); });
            var projectionManager = (MediaProjectionManager)GetSystemService(MediaProjectionService)!;
            _projection = projectionManager.GetMediaProjection((int)result, projectionData)
                ?? throw new InvalidOperationException("Android no entregó la captura de pantalla.");
            _projectionCallback = new NLAndroidProjectionCallback(() => lifetime.Cancel());
            _projection.RegisterCallback(_projectionCallback, new Handler(Looper.MainLooper!));
            if (!OperatingSystem.IsAndroidVersionAtLeast(29))
                throw new NotSupportedException("Audio AppControl requiere Android 10 o posterior.");
            _audioCapture = new NLAndroidAudioCapture(_projection);
            await _audioCapture.StartAsync(audioStream, lifetime.Token);
            // VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR is 16; the .NET binding exposes a legacy enum here.
            _display = _projection.CreateVirtualDisplay("NOVORA VisionEngine", width, height,
                (int)metrics.DensityDpi, (Android.Views.DisplayFlags)16, _encoder.InputSurface, null, null)
                ?? throw new InvalidOperationException("Android no creó la pantalla virtual.");
            _encoder.Start();
            await foreach (NLAndroidVideoPacket packet in _encoder.Packets.ReadAllAsync(lifetime.Token))
                await writer.WriteVideoPacketAsync(packet.Payload, packet.PresentationTimeUs,
                    packet.Configuration, packet.KeyFrame, lifetime.Token);
            if (encoderFailure is not null) throw encoderFailure;
        }
        catch (System.OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Android.Util.Log.Error("NOVORA-VE", ex.ToString());
        }
        finally { await StopVideoAsync(); }
    }

    private static void Validate(NLControlVideoSourceOffer offer)
    {
        if (offer.Port is < 1 or > 65535 || offer.Token.Length != 64 ||
            offer.ControlPort is < 1 or > 65535 || offer.ControlToken.Length != 64 ||
            offer.AudioPort is < 1 or > 65535 || offer.AudioToken.Length != 64 ||
            offer.Bitrate is < 250_000 or > 100_000_000 || offer.MaxSize is < 240 or > 4320 ||
            offer.Fps is < 10 or > 120)
            throw new InvalidDataException("Oferta de video AppControl inválida.");
        _ = Convert.FromHexString(offer.Token);
        _ = Convert.FromHexString(offer.ControlToken);
        _ = Convert.FromHexString(offer.AudioToken);
    }

    private static async Task RunControlAsync(Stream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NLControlInputCommand command = await NLControlProtocol.ReadAsync<NLControlInputCommand>(
                stream, cancellationToken);
            await NLAndroidControlAccessibilityService.ExecuteAsync(command, cancellationToken);
        }
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
        lifetime?.Cancel();
        try { _display?.Release(); } catch { }
        _display = null;
        try { _projection?.Stop(); } catch { }
        _projection = null;
        _projectionCallback = null;
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
        _lifetime?.Cancel();
        base.OnDestroy();
    }

    private sealed class NLAndroidProjectionCallback(Action stopped) : MediaProjection.Callback
    {
        public override void OnStop() => stopped();
    }
}
