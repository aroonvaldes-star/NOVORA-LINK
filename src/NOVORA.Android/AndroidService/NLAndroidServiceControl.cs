using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using AndroidNetNetwork = Android.Net.Network;
using Android.OS;
using NOVORA.Control;
using NOVORA.AndroidUI;
using NOVORA.AndroidStorage;
using NOVORA.AndroidVpn;
using System.Security.Authentication;
using Resource = NOVORA.AndroidApp.Resource;

namespace NOVORA.AndroidService;

[Service(Name = "com.linkcontrol.novora.ControlService", Exported = false,
    ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public sealed partial class NLAndroidServiceControl : Service
{
    private const string Channel = "novora_control";
    private const int NotificationId = 214;
    public const string DisconnectAction = "com.linkcontrol.novora.DISCONNECT";
    public const string StopVideoAction = "com.linkcontrol.novora.STOP_VIDEO";
    public const string UsbBootstrapAction = "com.linkcontrol.novora.USB_BOOTSTRAP";
    public const string UsbBootstrapExtra = "novora.usb";
    public const string SendFilesAction = "com.linkcontrol.novora.SEND_FILES";
    public NLControlSession Session { get; } = new();
    public string InstanceId { get; } = Guid.NewGuid().ToString("N");
    private NLAndroidStorageTrustedPcs _trustedStore = null!;
    private NLControlTrustedPc? _recoveryPeer;
    private NLControlTrustedPc? _connectingPeer;
    private int _launchPresenceRequested;
    private NLControlLanInvitation? _freshInvitation;
    private NLControlLanPeer? _freshLanPeer;
    private NLControlLanInvitation? _usbInvitation;
    public void AcceptUsbBootstrap(string text)
    {
        if (text.Length > 4096) throw new InvalidOperationException("Enlace USB no válido.");
        var invitation = NLAndroidServiceUsbBootstrap.Parse(text).Invitation;
        invitation.Validate(true);
        if (invitation.Host != "127.0.0.1" || invitation.Port != NLControlProtocol.Port) throw new InvalidOperationException("Destino USB no válido.");
        _usbInvitation = invitation;
    }
    private CancellationTokenSource? _recovery;
    private bool _retrying;
    private bool _internetOwned, _internetStarting, _internetStopSending;
    private long _internetStopGeneration = -1;
    private int _internetStopAttempts;
    private long _operation;
    public string? RecoveryMessage { get; private set; }
    public event EventHandler? StatusChanged;
    private NLAndroidServiceControllerNotifications? _controllerNotifications;
    private readonly NLAndroidNotificationPolicy _notificationPolicy = new();
    public bool TransferInProgress { get; private set; }
    public bool BeginFileTransfer()
    {
        if (TransferInProgress || Session.Current.Busy || Session.Current.Phase != NLControlSessionPhase.Connected) return false;
        TransferInProgress = true;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
    public void EndFileTransfer()
    {
        TransferInProgress = false;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
    public bool CanRememberCurrentPc => (_freshInvitation is not null || _freshLanPeer is not null) &&
        Session.Current.Phase == NLControlSessionPhase.Connected;
    public IReadOnlyList<NLControlTrustedPc> GetSavedPcs() => _trustedStore.Read();
    private Handler _main = null!;
    private bool _foreground;
    private volatile bool _destroyed;
    private readonly object _lifecycle = new();
    private ConnectivityManager? _connectivity;
    private NLAndroidServiceNetworkWatch? _networkWatch;
    private AndroidNetNetwork? _network;
    private int _internetControlHandoff;
    public sealed class NLAndroidServiceBinder(NLAndroidServiceControl owner) : Binder
    { public NLAndroidServiceControl Owner { get; } = owner; }

    public override void OnCreate()
    {
        base.OnCreate();
        _trustedStore = new NLAndroidStorageTrustedPcs(this);
        _main = new Handler(Looper.MainLooper!);
        _controllerNotifications = new NLAndroidServiceControllerNotifications(this);
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel(Channel, "Control de NOVORA PC", NotificationImportance.Low)
        { Description = "Conexión y controles de NOVORA PC", LockscreenVisibility = NotificationVisibility.Private });
        Session.Changed += SessionChanged;
        NLAndroidVpnService.StatusChanged += InternetStatusChanged;
        _connectivity = (ConnectivityManager?)GetSystemService(ConnectivityService);
        _network = _connectivity?.ActiveNetwork;
        _networkWatch = new NLAndroidServiceNetworkWatch(this);
        _connectivity?.RegisterDefaultNetworkCallback(_networkWatch);
    }
    public override IBinder OnBind(Intent? intent) => new NLAndroidServiceBinder(this);
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == DisconnectAction) _ = DisconnectFromNotificationAsync();
        else if (intent?.Action == StopVideoAction) _ = SendNotificationCommandAsync("stopVideo");
        else if (intent?.Action == SendFilesAction) StartBackgroundFileTransfer(intent);
        else if (intent?.Action == UsbBootstrapAction)
        {
            string text = intent.GetStringExtra(UsbBootstrapExtra) ?? "";
            intent.RemoveExtra(UsbBootstrapExtra);
            _ = ConnectAutomaticUsbAsync(text);
        }
        else if (!_foreground && Session.Current.Phase is NLControlSessionPhase.Disconnected or NLControlSessionPhase.Lost)
            StopSelf(startId);
        return StartCommandResult.NotSticky;
    }
    public async Task<NLControlReply> StartInternetAsync(long generation)
    {
        var state = Session.Current;
        if (_internetOwned || state.Generation != generation || state.Transport is not ("USB" or "LAN") ||
            state.Phase != NLControlSessionPhase.Connected || state.Busy || state.Snapshot?.Engines?.LinkCanStart != true)
            throw new InvalidOperationException("Se requiere una sesión USB o LAN autorizada y disponible.");
        if (VpnService.Prepare(this) is not null) throw new InvalidOperationException("Falta el permiso VPN de Android.");
        _internetStopGeneration = -1;
        _internetOwned = _internetStarting = true;
        try
        {
            NLControlReply reply;
            if (state.Transport == "LAN")
            {
                reply = await Session.SendAsync("startLink");
                if (!reply.Success || string.IsNullOrWhiteSpace(reply.Value))
                    throw new InvalidOperationException(reply.Message);
                var offer = System.Text.Json.JsonSerializer.Deserialize<NLControlLinkOffer>(reply.Value)
                    ?? throw new InvalidDataException("PC no entregó el transporte DATA LAN.");
                NLAndroidVpnService.Start(this, offer);
            }
            else
            {
                NLAndroidVpnService.Start(this);
                reply = await Session.SendAsync("startLink");
            }
            if (!reply.Success || Session.Current.Generation != generation) StopLocalInternet();
            return reply;
        }
        catch { StopLocalInternet(); throw; }
        finally { _internetStarting = false; }
    }
    public async Task<NLControlReply> StopInternetAsync()
    {
        StopLocalInternet();
        return await Session.SendAsync("stopLink");
    }
    private void StopLocalInternet()
    {
        _internetOwned = false;
        NLAndroidVpnService.Stop(this);
    }
    private void InternetStatusChanged(object? sender, EventArgs args) => Post(() =>
    {
        StatusChanged?.Invoke(this, EventArgs.Empty);
        if (_internetOwned && !NLAndroidVpnService.IsRunning)
        {
            _internetOwned = false;
            _internetStopGeneration = Session.Current.Generation;
            _internetStopAttempts = 0;
            _ = StopRemoteInternetAfterFailureAsync();
        }
    });
    private async Task StopRemoteInternetAfterFailureAsync()
    {
        var state = Session.Current;
        if (_internetStopGeneration < 0 || _internetStopSending) return;
        if (state.Generation != _internetStopGeneration || state.Phase != NLControlSessionPhase.Connected ||
            state.Transport is not ("USB" or "LAN"))
        { _internetStopGeneration = -1; return; }
        if (state.Busy) return; // Resume on the next session event, without polling.
        if (state.Snapshot?.Engines?.LinkCanStop != true) return;
        if (_internetStopAttempts >= 3) { _internetStopGeneration = -1; return; }
        _internetStopSending = true;
        _internetStopAttempts++;
        try
        {
            var reply = await Session.SendAsync("stopLink");
            if (reply.Success) _internetStopGeneration = -1;
        }
        catch (Exception) { /* A lost control session invokes PC ownership cleanup. */ }
        finally { _internetStopSending = false; }
    }
    // NOVORA_AUTOUSB_V1 - same session/engine ownership; only the trigger changes.
    public Task ConnectUsbAsync() => ConnectUsbCoreAsync(automatic: false);
    private async Task ConnectUsbCoreAsync(bool automatic)
    {
        _automaticUsbOrigin = automatic ? "AutomaticUsb" : "ManualUsb";
        var invitation = _usbInvitation;
        if (invitation is null || invitation.ExpiresUnix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
            var saved = GetSavedPcs().Where(p => p.Transport == "USB").ToArray();
            if (saved.Length == 1) { await ConnectTrustedAsync(saved[0]); return; }
            throw new InvalidOperationException("Pulsa Preparar control USB en PC o elige una PC USB guardada.");
        }
        StopLocalInternet();
        CancelRecovery();
        _freshInvitation = null;
        _freshLanPeer = null;
        BeginForeground();
        long operation = _operation;
        try { await Session.ConnectUsbAsync(invitation); if (operation == _operation) { _freshInvitation = invitation; _usbInvitation = null; } }
        finally { RenderNotification(); }
    }
    public async Task ConnectLanAsync(NLControlLanPeer peer, string pairingCode)
    {
        _automaticUsbOrigin = "ManualLan";
        StopLocalInternet();
        CancelRecovery();
        _freshInvitation = null;
        _freshLanPeer = null;
        long operation = _operation;
        BeginForeground();
        _network = _connectivity?.ActiveNetwork;
        try
        {
            await Session.ConnectLanAsync(peer, pairingCode);
            if (operation == _operation)
                _freshLanPeer = peer;
        }
        finally { RenderNotification(); }
    }
    /// <summary>
    /// Connects AppControl to the LAN PC explicitly selected for automatic entry.
    /// USB remains owned by its protected ADB bootstrap flow.
    /// </summary>
    public async Task<bool> AnnounceTrustedLanPcOnLaunchAsync(string fingerprint)
    {
        if (Interlocked.Exchange(ref _launchPresenceRequested, 1) != 0 || _destroyed ||
            Session.Current.Phase is NLControlSessionPhase.Connecting or NLControlSessionPhase.Connected)
            return false;

        try
        {
            NLControlTrustedPc? saved = GetSavedPcs().SingleOrDefault(p =>
                p.Transport == "LAN" && p.Fingerprint == fingerprint);
            if (saved is null) return false;

            await ConnectTrustedCoreAsync(saved, "LaunchTrustedLan");
            return Session.Current.Phase == NLControlSessionPhase.Connected;
        }
        finally
        {
            // Startup may race the PC listener. Permit the bounded UI recovery
            // attempts when this one did not establish a trusted LAN session.
            if (Session.Current.Phase != NLControlSessionPhase.Connected)
                Interlocked.Exchange(ref _launchPresenceRequested, 0);
        }
    }

    public Task ConnectTrustedAsync(NLControlTrustedPc peer) =>
        ConnectTrustedCoreAsync(peer, "ManualTrusted");

    private async Task ConnectTrustedCoreAsync(NLControlTrustedPc peer, string origin)
    {
        _automaticUsbOrigin = origin;
        StopLocalInternet();
        peer.Validate();
        // Reload protected storage: stale dialogs cannot reconnect a forgotten PC.
        if (!GetSavedPcs().Any(p => p == peer)) throw new InvalidOperationException("Esta PC ya no está guardada.");
        StopLocalInternet();
        CancelRecovery();
        _freshInvitation = null;
        _freshLanPeer = null;
        long operation = _operation;
        _connectingPeer = peer;
        BeginForeground();
        _network = _connectivity?.ActiveNetwork;
        try
        {
            NLControlTrustedPc connectedPeer = await ConnectTrustedAtCurrentAddressAsync(peer, operation);
            if (operation == _operation) _recoveryPeer = connectedPeer.Transport == "LAN" ? connectedPeer : null;
        }
        finally { if (operation == _operation) _connectingPeer = null; RenderNotification(); }
    }

    private async Task<NLControlTrustedPc> ConnectTrustedAtCurrentAddressAsync(
        NLControlTrustedPc peer, long operation, CancellationToken cancellationToken = default)
    {
        try
        {
            await Session.ConnectTrustedAsync(peer);
            return peer;
        }
        catch (Exception ex) when (peer.Transport == "LAN" &&
                                   ex is IOException or System.Net.Sockets.SocketException or
                                       System.OperationCanceledException or AuthenticationException)
        {
            // A saved LAN address is only a hint. Discovery identifies the PC by its
            // pinned certificate; resume still authenticates with the saved token.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(4));
            var discovered = await NLControlLanDiscovery.SearchAsync(deadline.Token);
            NLControlTrustedPc? updated = peer.AtDiscoveredLanAddress(discovered);
            if (updated is null) throw;
            if (operation != _operation || !GetSavedPcs().Any(saved => saved == peer))
                throw new System.OperationCanceledException("La reconexión LAN fue cancelada.");
            await Session.ConnectTrustedAsync(updated);
            if (operation == _operation && GetSavedPcs().Any(saved => saved == peer))
                _trustedStore.Save(updated);
            return updated;
        }
    }
    public async Task RememberCurrentPcAsync()
    {
        var invitation = _freshInvitation;
        var lanPeer = _freshLanPeer;
        var state = Session.Current;
        if ((invitation is null && lanPeer is null) || state.Phase != NLControlSessionPhase.Connected)
            throw new InvalidOperationException("Prepara un enlace USB o LAN nuevo para recordar esta PC.");
        string expectedFingerprint = invitation?.Fingerprint ?? lanPeer!.Fingerprint;
        long operation = _operation;
        var reply = await Session.SendAsync("trust.enroll", SafeName(Build.Model ?? "Android"));
        if (!reply.Success || reply.TrustedPc is not { } peer) throw new InvalidOperationException(reply.Message);
        if (operation != _operation || Session.Current.Generation != state.Generation) throw new System.OperationCanceledException();
        peer.Validate();
        string expectedHost = invitation?.Host ?? lanPeer!.Host;
        int expectedPort = invitation?.Port ?? lanPeer!.Port;
        if (peer.Fingerprint != expectedFingerprint || peer.Host != expectedHost || peer.Port != expectedPort || peer.Transport != state.Transport)
            throw new AuthenticationException("La identidad recibida no coincide con el código LAN autorizado.");
        _trustedStore.Save(peer);
        _recoveryPeer = peer.Transport == "LAN" ? peer : null;
        _freshInvitation = null;
        _freshLanPeer = null;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
    public async Task ForgetPcAsync(string fingerprint)
    {
        if (Session.Current.Transport == "USB" && GetSavedPcs().Any(p => p.Fingerprint == fingerprint && p.Transport == "USB")) await DisconnectAsync();
        if (_connectingPeer?.Fingerprint == fingerprint || _recoveryPeer?.Fingerprint == fingerprint ||
            _freshInvitation?.Fingerprint == fingerprint || _freshLanPeer?.Fingerprint == fingerprint)
            await DisconnectAsync();
        _trustedStore.Forget(fingerprint);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
    private void CancelRecovery()
    {
        _operation++;
        _recovery?.Cancel();
        _recovery = null;
        _retrying = false;
        _recoveryPeer = null;
        _connectingPeer = null;
        RecoveryMessage = null;
    }
    public async Task DisconnectAsync()
    {
        StopLocalInternet();
        CancelRecovery();
        _freshInvitation = null;
        _freshLanPeer = null;
        await Session.DisconnectAsync();
        RenderNotification();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SynchronizeExInAsync()
    {
        NLControlSessionState state = Session.Current;
        if (state.Phase != NLControlSessionPhase.Connected ||
            state.Snapshot?.ExIn is not { Detected: true, Transitioning: false }) return;

        try
        {
            await Session.SendAsync("exin.synchronize");
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or
                                   System.OperationCanceledException or ObjectDisposedException)
        {
            // El recovery de la sesión conserva la propiedad de una pérdida real de transporte.
        }
    }

    private async Task RecoverAsync(NLControlTrustedPc peer, CancellationTokenSource cancellation, long operation)
    {
        try
        {
            int[] delays = [1, 3, 8];
            for (int attempt = 0; attempt < delays.Length; attempt++)
            {
                RecoveryMessage = $"Reconectando con {SafeName(peer.PcName)} · intento {attempt + 1}/3";
                RenderNotification();
                StatusChanged?.Invoke(this, EventArgs.Empty);
                await Task.Delay(TimeSpan.FromSeconds(delays[attempt]), cancellation.Token);
                if (operation != _operation || !_foreground || _destroyed) return;
                try
                {
                    peer = await ConnectTrustedAtCurrentAddressAsync(peer, operation, cancellation.Token);
                    if (operation == _operation) RecoveryMessage = null;
                    return;
                }
                catch (AuthenticationException)
                {
                    RecoveryMessage = "PC rechazó la autorización o cambió su identidad. Vuelve a enlazar mediante código LAN.";
                    return;
                }
                catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or System.OperationCanceledException or ObjectDisposedException)
                { if (cancellation.IsCancellationRequested) return; }
            }
            RecoveryMessage = "No se recuperó el enlace después de 3 intentos. Revisa la red y conecta desde PC guardadas.";
        }
        catch (System.OperationCanceledException) { }
        catch (Exception)
        { if (operation == _operation) RecoveryMessage = "No se pudo recuperar la sesión. Revisa la PC y vuelve a enlazar mediante código LAN."; }
        finally
        {
            if (operation == _operation)
            {
                _retrying = false;
                _recoveryPeer = Session.Current.Phase == NLControlSessionPhase.Connected ? peer : null;
                _recovery = null;
                RenderNotification();
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
            cancellation.Dispose();
        }
    }
    private void BeginForeground()
    {
        if (_destroyed) throw new ObjectDisposedException(nameof(NLAndroidServiceControl));
        StartForegroundService(new Intent(this, typeof(NLAndroidServiceControl)));
        _notificationPolicy.Reset();
        var presentation = CaptureNotificationState("Preparando conexión con NOVORA PC…", true);
        var notification = BuildNotification(presentation);
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(NotificationId, notification, ForegroundService.TypeConnectedDevice);
        else StartForeground(NotificationId, notification);
        _foreground = true;
        _notificationPolicy.MarkPublished(presentation);
    }
    private async Task DisconnectFromNotificationAsync()
    {
        try { await DisconnectAsync(); }
        finally
        {
            Post(() =>
            {
                RenderNotification();
                if (Session.Current.Phase is NLControlSessionPhase.Disconnected or NLControlSessionPhase.Lost)
                    StopSelf();
            });
        }
    }
    private void Post(Action callback)
    {
        lock (_lifecycle)
        {
            if (_destroyed) return;
            _main.Post(() => { if (!_destroyed) callback(); });
        }
    }
    private void SessionChanged(object? sender, NLControlSessionState state)
    {
        if (!_notificationPolicy.TryQueue()) return;
        Post(() =>
        {
            _notificationPolicy.BeginDrain();
            var current = Session.Current;
            TryAnnounceNetworkCapabilities(current);
            PublishAutomaticUsbDiagnostics();
            _controllerNotifications?.Update(current.Generation, current.Snapshot?.ExIn);
            if (_internetStopGeneration >= 0) _ = StopRemoteInternetAfterFailureAsync();
            if (_internetOwned && Volatile.Read(ref _internetControlHandoff) == 0 &&
                current.ShouldStopOwnedInternet(_internetStarting)) StopLocalInternet();
            if (Session.Current.Phase == NLControlSessionPhase.Lost && !_retrying && _foreground && _recoveryPeer is { } peer)
            {
                _retrying = true;
                _recovery = new CancellationTokenSource();
                _ = RecoverAsync(peer, _recovery, _operation);
            }
            RenderNotification();
        });
    }
    private static string SafeName(string name)
    {
        string safe = new(name.Take(48).Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "NOVORA PC" : safe.Trim();
    }
    private static string Describe(NLControlSessionState state)
    {
        if (state.Phase != NLControlSessionPhase.Connected || state.Snapshot is not { } snapshot)
            return "Preparando conexión con NOVORA PC…";
        string transport = state.Transport == "USB" ? "USB" : "LAN";
        string connection = $"{SafeName(snapshot.PcName)} · {transport}\nVideo {(snapshot.VideoRunning ? "activo" : "detenido")} · Bitrate {SafeName(snapshot.Bitrate)}";
        if (snapshot.Media is not { } media) return connection;
        string recording = media.Starting ? "Preparando grabación…" : media.Recording
            ? media.AudioIncluded ? "Grabando · video y audio del teléfono · sin micrófono" : "Grabando · esperando audio del teléfono · sin micrófono"
            : "Grabación inactiva";
        return $"{recording}\n{media.Message}\n{connection}";
    }
    private void RenderNotification()
    {
        if (_destroyed) return;
        var state = Session.Current;
        bool running = _retrying || state.Phase is NLControlSessionPhase.Connecting or NLControlSessionPhase.Connected;
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        if (running)
        {
            if (_foreground)
            {
                var presentation = CaptureNotificationState(RecoveryMessage ?? Describe(state), true);
                if (_notificationPolicy.NeedsPublish(presentation))
                {
                    manager.Notify(NotificationId, BuildNotification(presentation));
                    _notificationPolicy.MarkPublished(presentation);
                }
            }
            return;
        }
        if (state.Phase == NLControlSessionPhase.Disconnected)
        {
            manager.Cancel(NotificationId);
            _notificationPolicy.Reset();
        }
        if (!_foreground) return;
        StopForeground(StopForegroundFlags.Remove);
        _foreground = false;
        StopSelf();
        // A lost session can remain visible as an ordinary notification, never as a running service.
        if (state.Phase == NLControlSessionPhase.Lost && NotificationsAllowed())
        {
            var presentation = CaptureNotificationState(RecoveryMessage ?? "Conexión perdida. Abre LINK Control para preparar un nuevo enlace.", false);
            manager.Notify(NotificationId, BuildNotification(presentation));
            _notificationPolicy.MarkPublished(presentation);
        }
    }
    private bool NotificationsAllowed() => !OperatingSystem.IsAndroidVersionAtLeast(33) ||
        CheckSelfPermission(Android.Manifest.Permission.PostNotifications) == Permission.Granted;
    private NLAndroidNotificationState CaptureNotificationState(string message, bool ongoing)
    {
        var state = Session.Current;
        var media = state.Phase == NLControlSessionPhase.Connected ? state.Snapshot?.Media : null;
        string title = media?.Starting == true ? "Preparando grabación" :
            media?.Recording == true ? "Grabando la pantalla" :
            state.Phase == NLControlSessionPhase.Lost ? "Conexión con PC perdida" : "Conexión con PC";
        string summary = NLAndroidNotificationPolicy.PublicSummary(state.Transport,
            state.Phase == NLControlSessionPhase.Connected, state.Phase == NLControlSessionPhase.Lost,
            state.Snapshot?.VideoRunning == true, state.Snapshot?.Engines?.LinkRunning == true,
            media?.Recording == true, media?.Starting == true);
        return new(title, message, summary, ongoing, state.Snapshot?.Engines?.VideoCanStop == true, state.Generation);
    }
    private Notification BuildNotification(NLAndroidNotificationState presentation)
    {
        var open = PendingIntent.GetActivity(this, 214,
            new Intent(this, typeof(NLAndroidUIActivity)).AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        var disconnect = PendingIntent.GetService(this, 215,
            new Intent(this, typeof(NLAndroidServiceControl)).SetAction(DisconnectAction),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        var generic = new Notification.Builder(this, Channel)
            .SetSmallIcon(Resource.Drawable.novora_notification).SetContentTitle(presentation.Title)
            .SetContentText(presentation.PublicMessage)
            .SetStyle(new Notification.BigTextStyle().BigText(presentation.PublicMessage))
            .SetContentIntent(open).SetVisibility(NotificationVisibility.Public)
            .SetOnlyAlertOnce(true).SetShowWhen(false).Build();
        var builder = new Notification.Builder(this, Channel)
            .SetSmallIcon(Resource.Drawable.novora_notification).SetContentTitle(presentation.Title)
            .SetContentText(presentation.Message).SetStyle(new Notification.BigTextStyle().BigText(presentation.Message))
            .SetContentIntent(open).SetOnlyAlertOnce(true)
            .SetShowWhen(false)
            .SetOngoing(presentation.Ongoing).SetAutoCancel(!presentation.Ongoing).SetVisibility(NotificationVisibility.Private)
            .SetPublicVersion(generic);
        if (presentation.Ongoing)
        {
            if (presentation.CanStopVideo)
                builder.AddAction(NotificationServiceAction(216, StopVideoAction, "Detener pantalla"));
            builder.AddAction(new Notification.Action.Builder(null, "Desconectar", disconnect).Build());
        }
        return builder.Build();
    }
    private Notification.Action NotificationServiceAction(int requestCode, string action, string label,
        string? extraName = null, string? extraValue = null)
    {
        var intent = new Intent(this, typeof(NLAndroidServiceControl)).SetAction(action);
        if (extraName is not null) intent.PutExtra(extraName, extraValue);
        var pending = PendingIntent.GetService(this, requestCode, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        return new Notification.Action.Builder(null, label, pending).Build();
    }
    private async Task SendNotificationCommandAsync(string action, string? value = null)
    {
        var state = Session.Current;
        if (state.Phase != NLControlSessionPhase.Connected || state.Busy) return;
        try
        {
            await Session.SendAsync(action, value);
        }
        catch (Exception)
        {
            Post(RenderNotification);
        }
    }
    private sealed class NLAndroidServiceNetworkWatch(NLAndroidServiceControl owner) : ConnectivityManager.NetworkCallback
    {
        public override void OnAvailable(AndroidNetNetwork network) => owner.Post(() =>
        {
            if (owner._destroyed) return;
            var previous = owner._network;
            owner._network = network;
            if (previous is not null && !previous.Equals(network)) owner.LoseLan();
        });
        public override void OnLost(AndroidNetNetwork network) => owner.Post(() =>
        {
            if (owner._destroyed || !network.Equals(owner._network)) return;
            owner._network = null;
            owner.LoseLan();
        });
    }
    private void LoseLan()
    {
        var state = Session.Current;
        if (state.Transport == "LAN" && state.Phase is NLControlSessionPhase.Connecting or NLControlSessionPhase.Connected)
            _ = Session.LoseAsync("La red cambió o se perdió. Comprobando la sesión LAN.");
    }
    public override void OnDestroy()
    {
        CancelBackgroundFileTransfer();
        _controllerNotifications = null;
        NLAndroidVpnService.StatusChanged -= InternetStatusChanged;
        StopLocalInternet();
        CancelRecovery();
        lock (_lifecycle)
        {
            _destroyed = true;
            Session.Changed -= SessionChanged;
            _main.RemoveCallbacksAndMessages(null);
            _main.Dispose();
        }
        if (_networkWatch is not null)
        {
            try { _connectivity?.UnregisterNetworkCallback(_networkWatch); }
            catch (Java.Lang.IllegalArgumentException) { /* Callback was already detached by Android. */ }
        }
        _ = Session.DisposeAsync();
        base.OnDestroy();
    }
}
