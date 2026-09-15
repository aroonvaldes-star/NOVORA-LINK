using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Systems;
using NOVORA.Control;
using NOVORA.AndroidUI;
using System.Net.Sockets;
using System.IO;
using Resource = NOVORA.AndroidApp.Resource;

namespace NOVORA.AndroidVpn;

[Service(Name = "com.novora.appcontrol.VpnService", Exported = false,
    Permission = "android.permission.BIND_VPN_SERVICE", ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
[IntentFilter(["android.net.VpnService"])]
[MetaData("android.net.VpnService.SUPPORTS_ALWAYS_ON", Value = "false")]
public sealed class NLAndroidVpnService : VpnService
{
    private const string StartAction = "com.novora.appcontrol.START_USB_VPN";
    private const string StopAction = "com.novora.appcontrol.STOP_USB_VPN";
    private const string Channel = "novora_usb_vpn";
    private const int NotificationId = 216;
    private readonly object _gate = new();
    private CancellationTokenSource? _run;
    private TcpClient? _control, _data;
    private ParcelFileDescriptor? _tun;
    private Java.IO.FileDescriptor[]? _wake;
    private bool _wakeSent;
    private long _epoch;
    private static long _requestedEpoch;
    private static readonly object RequestGate = new();
    private Handler _main = null!;
    private bool _destroyed;
    private static volatile bool _running;
    private static string _status = "VPN USB detenida.";
    public static bool IsRunning => _running;
    public static string Status => Volatile.Read(ref _status);
    public static event EventHandler? StatusChanged;

    public static void Start(Context context)
    {
        long epoch;
        lock (RequestGate)
        {
            if (_running) return;
            epoch = Interlocked.Increment(ref _requestedEpoch);
            _running = true;
            Volatile.Write(ref _status, "Preparando VPN USB…");
        }
        try { context.StartForegroundService(new Intent(context, typeof(NLAndroidVpnService)).SetAction(StartAction).PutExtra("epoch", epoch)); }
        catch
        {
            lock (RequestGate) { if (epoch == _requestedEpoch) { _running = false; Volatile.Write(ref _status, "No se pudo iniciar la VPN USB."); } }
            StatusChanged?.Invoke(null, EventArgs.Empty);
            throw;
        }
    }
    public static void Stop(Context context)
    {
        lock (RequestGate)
        {
            Interlocked.Increment(ref _requestedEpoch);
            _running = false;
            Volatile.Write(ref _status, "VPN USB detenida.");
        }
        StatusChanged?.Invoke(null, EventArgs.Empty);
        context.StopService(new Intent(context, typeof(NLAndroidVpnService)));
    }
    public override void OnCreate()
    {
        base.OnCreate();
        _main = new Handler(Looper.MainLooper!);
        ((NotificationManager)GetSystemService(NotificationService)!).CreateNotificationChannel(
            new NotificationChannel(Channel, "Internet USB de NOVORA", NotificationImportance.Low)
            { Description = "Estado del túnel USB y opción de detener", LockscreenVisibility = NotificationVisibility.Private });
    }
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == StopAction) { Stop(this); CloseRun(); StopSelf(); return StartCommandResult.NotSticky; }
        if (intent?.Action != StartAction) { StopSelf(startId); return StartCommandResult.NotSticky; }
        long requested = intent.GetLongExtra("epoch", -1);
        if (requested != Interlocked.Read(ref _requestedEpoch)) { StopSelf(startId); return StartCommandResult.NotSticky; }
        lock (_gate)
        {
            if (_run is not null)
            {
                if (_epoch != requested)
                {
                    _epoch = requested;
                    CloseRun();
                    SetStatus("La VPN anterior se está cerrando. Vuelve a iniciar en un momento.", false);
                    StopSelf();
                }
                return StartCommandResult.NotSticky;
            }
            _epoch = requested;
            if (Prepare(this) is not null) { SetStatus("Autoriza la VPN desde NOVORA antes de iniciar.", false); StopSelf(); return StartCommandResult.NotSticky; }
            var run = new CancellationTokenSource();
            _run = run;
            SetStatus("Preparando Internet USB. Esperando LinkEngine en PC…", true);
            try
            {
                StartForeground(NotificationId, BuildNotification());
                _ = Task.Run(() => RunAsync(run));
            }
            catch (Exception)
            {
                run.Cancel(); run.Dispose();
                SetStatus("Android no permitió iniciar el servicio VPN. Revisa sus permisos y vuelve a intentar.", false);
                StopSelf();
            }
        }
        return StartCommandResult.NotSticky;
    }
    private Notification BuildNotification()
    {
        var open = PendingIntent.GetActivity(this, 216, new Intent(this, typeof(NLAndroidUIActivity)), PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var stop = PendingIntent.GetService(this, 217, new Intent(this, typeof(NLAndroidVpnService)).SetAction(StopAction), PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        return new Notification.Builder(this, Channel).SetSmallIcon(Resource.Drawable.novora_notification)!
            .SetContentTitle("NOVORA · Internet USB")!.SetContentText(Status)!.SetContentIntent(open)!
            .SetOngoing(true)!.SetOnlyAlertOnce(true)!.AddAction(new Notification.Action.Builder(null, "Detener VPN", stop).Build())!.Build();
    }
    private void SetStatus(string message, bool running)
    {
        lock (_gate)
        {
            lock (RequestGate)
            {
                if (_epoch != Interlocked.Read(ref _requestedEpoch)) return;
                if (running && (_destroyed || _run?.IsCancellationRequested != false)) return;
                Volatile.Write(ref _status, message);
                _running = running;
            }
        }
        _main.Post(() =>
        {
            if (_epoch != Interlocked.Read(ref _requestedEpoch)) return;
            StatusChanged?.Invoke(null, EventArgs.Empty);
            if (!_destroyed && _running)
                ((NotificationManager)GetSystemService(NotificationService)!).Notify(NotificationId, BuildNotification());
        });
    }
    private async Task<TcpClient> ConnectAsync(int port, CancellationToken token)
    {
        // Only the phone's ADB reverse endpoint. Never accept an arbitrary LAN host.
        for (int attempt = 0; attempt < 25; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
            try
            {
                if (!Protect(checked((int)client.Client.Handle))) throw new InvalidOperationException("No se pudo proteger el socket USB.");
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                await client.ConnectAsync(System.Net.IPAddress.Loopback, port, deadline.Token);
                return client;
            }
            catch (Exception ex) when (ex is SocketException or System.OperationCanceledException)
            {
                client.Dispose();
                token.ThrowIfCancellationRequested();
                if (attempt == 24) throw new IOException("PC no preparó el canal USB a tiempo.", ex);
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
            catch { client.Dispose(); throw; }
        }
        throw new IOException("Canal USB no disponible.");
    }
    private async Task RunAsync(CancellationTokenSource run)
    {
        Task? heartbeat = null, outbound = null, inbound = null;
        string terminal = "VPN USB detenida.";
        try
        {
            using var control = await ConnectAsync(27183, run.Token);
            lock (_gate) { run.Token.ThrowIfCancellationRequested(); _control = control; }
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(run.Token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                await NLControlVpnProtocol.WriteControlAsync(control.GetStream(), $"NOVORA-LINK|1|HELLO|android-{Guid.NewGuid():N}", deadline.Token);
                if (await NLControlVpnProtocol.ReadControlAsync(control.GetStream(), deadline.Token) != "NOVORA-LINK|1|ACK")
                    throw new InvalidDataException("PC no confirmó el protocolo de LinkEngine.");
            }
            heartbeat = NLControlVpnProtocol.HeartbeatAsync(control.GetStream(), run.Token);
            Task<TcpClient> connectData = ConnectAsync(27184, run.Token);
            if (await Task.WhenAny(connectData, heartbeat) == heartbeat)
            {
                run.Cancel();
                try { (await connectData).Dispose(); } catch { }
                await heartbeat;
            }
            using var data = await connectData;
            lock (_gate) { run.Token.ThrowIfCancellationRequested(); _data = data; }
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(run.Token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                // RelayCore sends one 32-bit ID before its raw IPv4 packet stream.
                await NLControlVpnProtocol.ReadRelayIdAsync(data.GetStream(), deadline.Token);
            }
            lock (_gate)
            {
                run.Token.ThrowIfCancellationRequested();
                using var builder = new Builder(this);
                builder.SetSession("NOVORA Internet USB")!.SetMtu(1500)!.AddAddress("10.0.0.2", 32)!
                    .AddRoute("0.0.0.0", 0)!.AddDnsServer("8.8.8.8")!.SetBlocking(false)!
                    .AddDisallowedApplication(PackageName!);
                // No IPv6 address/route or allowFamily: Android blocks this unsupported family.
                _tun = builder.Establish() ?? throw new InvalidOperationException("Android no autorizó el túnel VPN.");
                _wake = Os.Pipe() ?? throw new IOException("No se pudo preparar cancelación del túnel.");
                var descriptor = _tun.FileDescriptor!;
                var wake = _wake[0];
                outbound = Task.Run(async () =>
                {
                    byte[] packet = new byte[65535];
                    while (!run.IsCancellationRequested)
                    {
                        WaitTun(descriptor, wake, (short)OsConstants.Pollin, run.Token);
                        int count;
                        try { count = Os.Read(descriptor, packet, 0, packet.Length); }
                        catch (ErrnoException ex) when (ex.Errno == OsConstants.Eagain) { continue; }
                        if (count <= 0) throw new EndOfStreamException("Túnel cerrado.");
                        // Android may deliver local IPv6 packets even when this VPN only routes IPv4.
                        // Keep unsupported IPv6 inside the tunnel instead of aborting the IPv4 session.
                        if (count >= 40 && packet[0] >> 4 == 6) continue;
                        if (count < 20 || packet[0] >> 4 != 4)
                            throw new InvalidDataException($"Lectura TUN no IPv4: longitud={count}, version={packet[0] >> 4}, primerByte={packet[0]:X2}");
                        if (NLControlVpnProtocol.ValidateIpv4(packet.AsSpan(0, count)) != count)
                            throw new InvalidDataException("Paquete TUN inconsistente.");
                        await data.GetStream().WriteAsync(packet.AsMemory(0, count), run.Token);
                    }
                });
                inbound = Task.Run(async () =>
                {
                    byte[] packet = new byte[65535];
                    while (!run.IsCancellationRequested)
                    {
                        int count = await NLControlVpnProtocol.ReadPacketAsync(data.GetStream(), packet, run.Token);
                        while (true)
                        {
                            WaitTun(descriptor, wake, (short)OsConstants.Pollout, run.Token);
                            try
                            {
                                if (Os.Write(descriptor, packet, 0, count) != count) throw new IOException("Escritura TUN incompleta.");
                                break;
                            }
                            catch (ErrnoException ex) when (ex.Errno == OsConstants.Eagain) { }
                        }
                    }
                });
            }
            SetStatus("Túnel USB activo. El acceso a Internet depende de la conexión de PC.", true);
            await await Task.WhenAny(heartbeat, outbound, inbound);
        }
        catch (System.OperationCanceledException) when (run.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Android.Util.Log.Error("NOVORA-VPN", ex.GetType().Name + ": " + ex.Message);
            terminal = "Internet USB se interrumpió. Revisa el cable y LinkEngine en PC; vuelve a iniciar desde NOVORA.";
        }
        finally
        {
            CloseRun();
            try { await Task.WhenAll(new[] { heartbeat, outbound, inbound }.OfType<Task>()); } catch { }
            lock (_gate)
            {
                try { _tun?.Close(); } catch { }
                if (_wake is not null) foreach (var descriptor in _wake) { try { Os.Close(descriptor); } catch { } descriptor.Dispose(); }
                _wake = null; _tun = null;
            }
            run.Dispose();
            SetStatus(terminal, false);
            _main.Post(() => { if (!_destroyed && _epoch == Interlocked.Read(ref _requestedEpoch)) { StopForeground(StopForegroundFlags.Remove); StopSelf(); } });
        }
    }
    private static void WaitTun(Java.IO.FileDescriptor descriptor, Java.IO.FileDescriptor wake, short events, CancellationToken token)
    {
        using var tunPoll = new StructPollfd { Fd = descriptor, Events = events };
        using var wakePoll = new StructPollfd { Fd = wake, Events = (short)OsConstants.Pollin };
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { Os.Poll(new[] { tunPoll, wakePoll }, -1); }
            catch (ErrnoException ex) when (ex.Errno == OsConstants.Eintr) { continue; }
            token.ThrowIfCancellationRequested();
            if (wakePoll.Revents != 0) throw new System.OperationCanceledException(token);
            if ((tunPoll.Revents & events) != 0) return;
            if (tunPoll.Revents != 0) throw new IOException("Túnel no disponible.");
        }
    }
    private void CloseRun()
    {
        lock (_gate)
        {
            try { _run?.Cancel(); } catch (ObjectDisposedException) { }
            try { _control?.Dispose(); } catch { }
            try { _data?.Dispose(); } catch { }
            if (!_wakeSent && _wake is not null)
            {
                _wakeSent = true;
                try { Os.Write(_wake[1], new byte[] { 1 }, 0, 1); } catch { }
            }
            _control = _data = null;
        }
    }
    public override void OnRevoke() { CloseRun(); SetStatus("Android revocó la VPN de NOVORA.", false); StopSelf(); base.OnRevoke(); }
    public override void OnDestroy()
    {
        _destroyed = true;
        CloseRun();
        SetStatus("VPN USB detenida.", false);
        base.OnDestroy();
    }
}
