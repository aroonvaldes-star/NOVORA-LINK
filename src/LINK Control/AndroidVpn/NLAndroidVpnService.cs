using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using AndroidNetNetwork = Android.Net.Network;
using Android.OS;
using Android.Systems;
using NOVORA.Control;
using NOVORA.AndroidUI;
using System.Net.Sockets;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.IO;
using System.Text.Json;
using Resource = NOVORA.AndroidApp.Resource;

namespace NOVORA.AndroidVpn;

[Service(Name = "com.linkcontrol.novora.VpnService", Exported = false,
    Permission = "android.permission.BIND_VPN_SERVICE", ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
[IntentFilter(["android.net.VpnService"])]
[MetaData("android.net.VpnService.SUPPORTS_ALWAYS_ON", Value = "false")]
public sealed class NLAndroidVpnService : VpnService
{
    private const string StartAction = "com.linkcontrol.novora.START_USB_VPN";
    private const string StopAction = "com.linkcontrol.novora.STOP_USB_VPN";
    private const string Channel = "novora_usb_vpn";
    private const int NotificationId = 216;
    private readonly object _gate = new();
    private CancellationTokenSource? _run;
    private TcpClient? _control, _data;
    private Stream? _dataStream;
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
    private static NLControlLinkOffer? _lanOffer;
    private static string? _usbToken;
    private static long _vpnTxBytes, _vpnRxBytes, _vpnTxPackets, _vpnRxPackets;
    private static volatile bool _tunnelReady;
    private static string _activeTransport = "USB";
    private static readonly object MonitorGate = new();
    private static CancellationTokenSource? _monitorRun;
    private static NLLinkMonitorSample? _monitorSample;
    public sealed record NLLinkMonitorSample(DateTimeOffset SampledAtUtc, string Transport,
        double TxMbps, double RxMbps, long TxBytes, long RxBytes,
        long TxPackets, long RxPackets, string SessionId);
    public static bool IsRunning => _running;
    public static bool IsTunnelReady => _tunnelReady;
    public static bool IsMonitoring { get { lock (MonitorGate) return _monitorRun is not null; } }
    public static NLLinkMonitorSample? MonitorSample => Volatile.Read(ref _monitorSample);
    public static event EventHandler? MonitorChanged;
    public static string Status => Volatile.Read(ref _status);
    public static event EventHandler? StatusChanged;

    public static void Start(Context context, string token)
    {
        if (!NLControlVpnProtocol.IsValidSessionToken(token))
            throw new ArgumentException("Token de sesión USB inválido.", nameof(token));
        _lanOffer = null;
        _usbToken = token;
        StartCore(context);
    }
    private static void StartCore(Context context)
    {
        long epoch;
        lock (RequestGate)
        {
            if (_running) return;
            epoch = Interlocked.Increment(ref _requestedEpoch);
            _running = true;
            _activeTransport = _lanOffer is null ? "USB" : "LAN";
            Volatile.Write(ref _status, _activeTransport == "USB" ? "Preparando VPN USB…" : "Preparando VPN Wi-Fi…");
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
            new NotificationChannel(Channel, "Internet de LinkEngine", NotificationImportance.Low)
            { Description = "Estado del túnel USB o Wi-Fi y opción de detener", LockscreenVisibility = NotificationVisibility.Private });
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
            SetStatus(_activeTransport == "USB" ? "Preparando Internet USB. Esperando LinkEngine en PC…" :
                "Preparando Internet Wi-Fi. Esperando LinkEngine en PC…", true);
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
        string route = _activeTransport == "USB" ? "USB" : "Wi-Fi";
        string title = $"LinkEngine · Internet {route}";
        string summary = _tunnelReady ? $"VPN de LinkEngine activa por {route}." : $"Preparando la VPN de LinkEngine por {route}.";
        var publicVersion = new Notification.Builder(this, Channel).SetSmallIcon(Resource.Drawable.novora_notification)!
            .SetContentTitle(title)!.SetContentText(summary)!.SetVisibility(NotificationVisibility.Public)!.Build();
        return new Notification.Builder(this, Channel).SetSmallIcon(Resource.Drawable.novora_notification)!
            .SetContentTitle(title)!.SetContentText(Status)!.SetStyle(new Notification.BigTextStyle().BigText(Status))!
            .SetVisibility(NotificationVisibility.Private)!.SetPublicVersion(publicVersion)!.SetShowWhen(false)!.SetContentIntent(open)!
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

    private async Task<(TcpClient Client, Stream Stream)> ConnectLanAsync(
        NLControlLinkOffer offer, CancellationToken token)
    {
        if (offer.Transport != "LAN" || !IPAddress.TryParse(offer.Host, out IPAddress? address))
            throw new InvalidDataException("Oferta DATA LAN inválida.");
        NLControlLanInvitation.ValidateAddress(address);
        if (offer.Port is < 1 or > 65535 || offer.Fingerprint is not { Length: 64 } ||
            !offer.Fingerprint.All(System.Uri.IsHexDigit) || offer.Token is not { Length: 64 } ||
            !offer.Token.All(System.Uri.IsHexDigit))
            throw new InvalidDataException("Oferta DATA LAN incompleta.");

        var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        try
        {
            if (!Protect(checked((int)client.Client.Handle)))
                throw new InvalidOperationException("No se pudo proteger el socket LAN de la VPN.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            await client.ConnectAsync(address, offer.Port, deadline.Token);
            var tls = new SslStream(client.GetStream(), false, (_, certificate, _, _) =>
                certificate is not null && CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(certificate.GetRawCertData()), Convert.FromHexString(offer.Fingerprint)));
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "NOVORA",
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }, deadline.Token);
            await NLControlProtocol.WriteAsync(tls,
                new NLControlLinkHello(NLControlProtocol.Version, offer.Token), deadline.Token);
            return (client, tls);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
    private async Task RunAsync(CancellationTokenSource run)
    {
        Task? heartbeat = null, outbound = null, inbound = null;
        NLControlLinkOffer? lanOffer = _lanOffer;
        string? usbToken = _usbToken;
        string terminal = lanOffer is null ? "VPN USB detenida." : "VPN LAN detenida.";
        try
        {
            if (lanOffer is null)
            {
                if (!NLControlVpnProtocol.IsValidSessionToken(usbToken))
                    throw new InvalidDataException("La VPN USB no recibió un token de sesión válido.");
                TcpClient control = await ConnectAsync(27183, run.Token);
                lock (_gate) { run.Token.ThrowIfCancellationRequested(); _control = control; }
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                await NLControlVpnProtocol.WriteControlAsync(control.GetStream(),
                    NLControlVpnProtocol.CreateControlHello(
                        $"android-{Guid.NewGuid():N}", usbToken!), deadline.Token);
                if (await NLControlVpnProtocol.ReadControlAsync(control.GetStream(), deadline.Token) !=
                    NLControlVpnProtocol.CreateControlAck())
                    throw new InvalidDataException("PC no confirmó el protocolo de LinkEngine.");
                heartbeat = NLControlVpnProtocol.HeartbeatAsync(control.GetStream(), run.Token);
            }
            else
            {
                heartbeat = Task.Delay(Timeout.InfiniteTimeSpan, run.Token);
            }

            TcpClient data;
            Stream dataStream;
            if (lanOffer is null)
            {
                Task<TcpClient> connectData = ConnectAsync(27184, run.Token);
                if (await Task.WhenAny(connectData, heartbeat) == heartbeat)
                {
                    run.Cancel();
                    try { (await connectData).Dispose(); } catch { }
                    await heartbeat;
                }
                data = await connectData;
                dataStream = data.GetStream();
            }
            else
            {
                (data, dataStream) = await ConnectLanAsync(lanOffer, run.Token);
            }

            lock (_gate)
            {
                run.Token.ThrowIfCancellationRequested();
                _data = data;
                _dataStream = dataStream;
            }
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(run.Token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                if (lanOffer is null)
                {
                    await NLControlVpnProtocol.WriteControlAsync(
                        dataStream,
                        NLControlVpnProtocol.CreateDataAuthentication(usbToken!),
                        deadline.Token);
                    if (!NLControlVpnProtocol.IsDataAuthenticationAck(
                            await NLControlVpnProtocol.ReadControlAsync(
                                dataStream,
                                deadline.Token)))
                        throw new InvalidDataException("El relay de PC rechazó la autenticación DATA USB.");
                }
                await NLControlVpnProtocol.ReadRelayIdAsync(dataStream, deadline.Token);
            }
            lock (_gate)
            {
                run.Token.ThrowIfCancellationRequested();
                using var builder = new Builder(this);
                builder.SetSession(lanOffer is null ? "NOVORA Internet USB" : "NOVORA Internet LAN")!
                    .SetMtu(1500)!.AddAddress("10.0.0.2", 32)!.AddRoute("0.0.0.0", 0)!
                    .AddDnsServer("8.8.8.8")!.SetBlocking(false)!.AddDisallowedApplication(PackageName!);
                if (OperatingSystem.IsAndroidVersionAtLeast(29)) builder.SetMetered(false);
                AndroidNetNetwork? underlying = FindUnderlyingNetwork();
                if (underlying is not null) builder.SetUnderlyingNetworks([underlying]);
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
                        if (count >= 40 && packet[0] >> 4 == 6) continue;
                        if (count < 20 || packet[0] >> 4 != 4)
                            throw new InvalidDataException($"Lectura TUN no IPv4: longitud={count}, version={packet[0] >> 4}, primerByte={packet[0]:X2}");
                        if (NLControlVpnProtocol.ValidateIpv4(packet.AsSpan(0, count)) != count)
                            throw new InvalidDataException("Paquete TUN inconsistente.");
                        await dataStream.WriteAsync(packet.AsMemory(0, count), run.Token);
                        Interlocked.Add(ref _vpnTxBytes, count);
                        Interlocked.Increment(ref _vpnTxPackets);
                    }
                });
                inbound = Task.Run(async () =>
                {
                    byte[] packet = new byte[65535];
                    while (!run.IsCancellationRequested)
                    {
                        int count = await NLControlVpnProtocol.ReadPacketAsync(dataStream, packet, run.Token);
                        while (true)
                        {
                            WaitTun(descriptor, wake, (short)OsConstants.Pollout, run.Token);
                            try
                            {
                                if (Os.Write(descriptor, packet, 0, count) != count)
                                    throw new IOException("Escritura TUN incompleta.");
                                Interlocked.Add(ref _vpnRxBytes, count);
                                Interlocked.Increment(ref _vpnRxPackets);
                                break;
                            }
                            catch (ErrnoException ex) when (ex.Errno == OsConstants.Eagain) { }
                        }
                    }
                });
            }
            _activeTransport = lanOffer is null ? "USB" : "LAN";
            _tunnelReady = true;
            SetStatus(lanOffer is null ? "Conectado a Internet con LinkEngine USB" :
                "Conectado a Internet con LinkEngine LAN", true);
            await await Task.WhenAny(heartbeat, outbound, inbound);
        }
        catch (System.OperationCanceledException) when (run.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Android.Util.Log.Error("NOVORA-VPN", ex.GetType().Name + ": " + ex.Message);
            terminal = lanOffer is null
                ? "Internet USB se interrumpió. Revisa el cable y LinkEngine en PC; vuelve a iniciar desde NOVORA."
                : "Internet LAN se interrumpió. NOVORA conserva la PC vinculada para reconectar.";
        }
        finally
        {
            _tunnelReady = false;
            StopMonitor();
            CloseRun();
            try { await Task.WhenAll(new[] { heartbeat, outbound, inbound }.OfType<Task>()); } catch { }
            lock (_gate)
            {
                try { _tun?.Close(); } catch { }
                if (_wake is not null)
                    foreach (var descriptor in _wake) { try { Os.Close(descriptor); } catch { } descriptor.Dispose(); }
                _wake = null;
                _tun = null;
                _dataStream = null;
            }
            run.Dispose();
            SetStatus(terminal, false);
            _main.Post(() =>
            {
                if (!_destroyed && _epoch == Interlocked.Read(ref _requestedEpoch))
                { StopForeground(StopForegroundFlags.Remove); StopSelf(); }
            });
        }
    }
    public static bool StartMonitor(Context context)
    {
        lock (MonitorGate)
        {
            if (!_tunnelReady) return false;
            if (_monitorRun is not null) return true;
            var run = new CancellationTokenSource();
            _monitorRun = run;
            Volatile.Write(ref _monitorSample, null);
            string sessionId = Guid.NewGuid().ToString("N");
            string path = Path.Combine(context.FilesDir!.AbsolutePath!,
                $"linkengine-monitor-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{sessionId}.jsonl");
            _ = Task.Run(() => RecordMonitorAsync(path, sessionId, run.Token));
            MonitorChanged?.Invoke(null, EventArgs.Empty);
            return true;
        }
    }

    public static void StopMonitor()
    {
        CancellationTokenSource? run;
        lock (MonitorGate)
        {
            run = _monitorRun;
            _monitorRun = null;
        }
        run?.Cancel();
        MonitorChanged?.Invoke(null, EventArgs.Empty);
    }

    private static async Task RecordMonitorAsync(string path, string sessionId, CancellationToken token)
    {
        long lastTx = Interlocked.Read(ref _vpnTxBytes);
        long lastRx = Interlocked.Read(ref _vpnRxBytes);
        long previousTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.Read, 4096, FileOptions.Asynchronous);
            await using var writer = new StreamWriter(file);
            while (!token.IsCancellationRequested && _tunnelReady)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);
                long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                double seconds = (nowTicks - previousTicks) /
                    (double)System.Diagnostics.Stopwatch.Frequency;
                previousTicks = nowTicks;
                long tx = Interlocked.Read(ref _vpnTxBytes);
                long rx = Interlocked.Read(ref _vpnRxBytes);
                var sample = new NLLinkMonitorSample(DateTimeOffset.UtcNow, _activeTransport,
                    (tx - lastTx) * 8 / seconds / 1_000_000,
                    (rx - lastRx) * 8 / seconds / 1_000_000,
                    tx, rx, Interlocked.Read(ref _vpnTxPackets),
                    Interlocked.Read(ref _vpnRxPackets), sessionId);
                lastTx = tx;
                lastRx = rx;
                await writer.WriteLineAsync(JsonSerializer.Serialize(sample));
                await writer.FlushAsync(token);
                Volatile.Write(ref _monitorSample, sample);
                MonitorChanged?.Invoke(null, EventArgs.Empty);
            }
        }
        catch (System.OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Android.Util.Log.Error("NOVORA-VPN", "Monitor LE: " + ex.Message);
            StopMonitor();
        }
    }
    public static void Start(Context context, NLControlLinkOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        _lanOffer = offer;
        _usbToken = null;
        StartCore(context);
    }

    private AndroidNetNetwork? FindUnderlyingNetwork()
    {
        var connectivity =
            (ConnectivityManager?)GetSystemService(ConnectivityService);

        if (connectivity is null)
        {
            return null;
        }

#pragma warning disable CA1422
        foreach (AndroidNetNetwork network in connectivity.GetAllNetworks())
#pragma warning restore CA1422
        {
            NetworkCapabilities? capabilities =
                connectivity.GetNetworkCapabilities(network);

            if (capabilities is not null &&
                !capabilities.HasTransport(TransportType.Vpn) &&
                capabilities.HasCapability(NetCapability.Validated))
            {
                return network;
            }
        }

        return null;
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
            try { _dataStream?.Dispose(); } catch { }
            if (!_wakeSent && _wake is not null)
            {
                _wakeSent = true;
                try { Os.Write(_wake[1], new byte[] { 1 }, 0, 1); } catch { }
            }
            _control = _data = null;
            _dataStream = null;
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
