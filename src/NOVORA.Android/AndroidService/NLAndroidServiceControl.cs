using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using NOVORA.Control;
using NOVORA.AndroidUI;
using NOVORA.AndroidStorage;
using NOVORA.AndroidVpn;
using System.Security.Authentication;
using Resource = NOVORA.AndroidApp.Resource;

namespace NOVORA.AndroidService;

[Service(Name = "com.novora.appcontrol.ControlService", Exported = false,
    ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public sealed class NLAndroidServiceControl : Service
{
    private const string Channel = "novora_control";
    private const int NotificationId = 214;
    public const string DisconnectAction = "com.novora.appcontrol.DISCONNECT";
    public NLControlSession Session { get; } = new();
    public string InstanceId { get; } = Guid.NewGuid().ToString("N");
    private NLAndroidStorageTrustedPcs _trustedStore = null!;
    private NLControlTrustedPc? _recoveryPeer;
    private NLControlTrustedPc? _connectingPeer;
    private NLControlLanInvitation? _freshInvitation;
    private CancellationTokenSource? _recovery;
    private bool _retrying;
    private bool _internetOwned, _internetStarting, _internetStopSending;
    private long _internetStopGeneration = -1;
    private int _internetStopAttempts;
    private long _operation;
    public string? RecoveryMessage { get; private set; }
    public event EventHandler? StatusChanged;
    private NLAndroidUIFloatingController? _floating;
    private int _visibleUi;
    public bool TransferInProgress { get; private set; }
    public bool BeginFileTransfer()
    {
        if (TransferInProgress || Session.Current.Busy || Session.Current.Phase != NLControlSessionPhase.Connected) return false;
        TransferInProgress = true;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        _floating?.Update(Session.Current);
        return true;
    }
    public void EndFileTransfer()
    {
        TransferInProgress = false;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        _floating?.Update(Session.Current);
    }
    public bool CanRememberCurrentPc => _freshInvitation is not null && Session.Current is { Transport: "LAN", Phase: NLControlSessionPhase.Connected };
    public void SetUiForeground(bool foreground)
    {
        _visibleUi = Math.Max(0, _visibleUi + (foreground ? 1 : -1));
        if (foreground) _floating?.RefreshPreferences();
        _floating?.SetForeground(_visibleUi > 0);
        _floating?.Update(Session.Current);
    }
    public IReadOnlyList<NLControlTrustedPc> GetSavedPcs() => _trustedStore.Read();
    private Handler _main = null!;
    private bool _foreground;
    private volatile bool _destroyed;
    private readonly object _lifecycle = new();
    private ConnectivityManager? _connectivity;
    private NLAndroidServiceNetworkWatch? _networkWatch;
    private Network? _network;
    public sealed class NLAndroidServiceBinder(NLAndroidServiceControl owner) : Binder
    { public NLAndroidServiceControl Owner { get; } = owner; }

    public override void OnCreate()
    {
        base.OnCreate();
        _trustedStore = new NLAndroidStorageTrustedPcs(this);
        _main = new Handler(Looper.MainLooper!);
        _floating = new NLAndroidUIFloatingController(this, this);
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
        else if (!_foreground && Session.Current.Phase is NLControlSessionPhase.Disconnected or NLControlSessionPhase.Lost)
            StopSelf(startId);
        return StartCommandResult.NotSticky;
    }
    public async Task<NLControlReply> StartInternetAsync(long generation)
    {
        var state = Session.Current;
        if (_internetOwned || state.Generation != generation || state.Transport != "USB" || state.Phase != NLControlSessionPhase.Connected || state.Busy || state.Snapshot?.Engines?.LinkCanStart != true)
            throw new InvalidOperationException("Se requiere una sesión USB autorizada y disponible.");
        if (VpnService.Prepare(this) is not null) throw new InvalidOperationException("Falta el permiso VPN de Android.");
        _internetStopGeneration = -1;
        _internetOwned = _internetStarting = true;
        try
        {
            NLAndroidVpnService.Start(this);
            var reply = await Session.SendAsync("startLink");
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
        if (state.Generation != _internetStopGeneration || state.Phase != NLControlSessionPhase.Connected || state.Transport != "USB")
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
    public async Task ConnectUsbAsync(string code)
    {
        StopLocalInternet();
        CancelRecovery();
        _freshInvitation = null;
        BeginForeground();
        try { await Session.ConnectUsbAsync(code); }
        finally { RenderNotification(); }
    }
    public async Task ConnectLanAsync(NLControlLanInvitation invitation)
    {
        StopLocalInternet();
        CancelRecovery();
        _freshInvitation = null;
        long operation = _operation;
        BeginForeground();
        _network = _connectivity?.ActiveNetwork;
        try
        {
            await Session.ConnectLanAsync(invitation);
            if (operation == _operation) _freshInvitation = invitation;
        }
        finally { RenderNotification(); }
    }
    public async Task ConnectTrustedAsync(NLControlTrustedPc peer)
    {
        StopLocalInternet();
        peer.Validate();
        // Reload protected storage: stale dialogs cannot reconnect a forgotten PC.
        if (!GetSavedPcs().Any(p => p == peer)) throw new InvalidOperationException("Esta PC ya no está guardada.");
        StopLocalInternet();
        CancelRecovery();
        _freshInvitation = null;
        long operation = _operation;
        _connectingPeer = peer;
        BeginForeground();
        _network = _connectivity?.ActiveNetwork;
        try
        {
            await Session.ConnectTrustedAsync(peer);
            if (operation == _operation) _recoveryPeer = peer;
        }
        finally { if (operation == _operation) _connectingPeer = null; RenderNotification(); }
    }
    public async Task RememberCurrentPcAsync()
    {
        var invitation = _freshInvitation;
        var state = Session.Current;
        if (invitation is null || state.Transport != "LAN" || state.Phase != NLControlSessionPhase.Connected)
            throw new InvalidOperationException("Conecta primero mediante un QR nuevo para recordar esta PC.");
        // Check corruption/capacity before creating any credential in PC.
        var existing = GetSavedPcs();
        if (existing.Count >= 16 && !existing.Any(p => p.Fingerprint == invitation.Fingerprint))
            throw new InvalidOperationException("Olvida una PC antes de agregar otra; el máximo es 16.");
        long operation = _operation;
        var reply = await Session.SendAsync("trust.enroll", SafeName(Build.Model ?? "Android"));
        if (!reply.Success || reply.TrustedPc is not { } peer) throw new InvalidOperationException(reply.Message);
        if (operation != _operation || Session.Current.Generation != state.Generation) throw new System.OperationCanceledException();
        peer.Validate();
        if (peer.Fingerprint != invitation.Fingerprint || peer.Host != invitation.Host || peer.Port != invitation.Port)
            throw new AuthenticationException("La identidad recibida no coincide con el QR autorizado.");
        _trustedStore.Save(peer);
        _recoveryPeer = peer;
        _freshInvitation = null;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
    public async Task ForgetPcAsync(string fingerprint)
    {
        if (_connectingPeer?.Fingerprint == fingerprint || _recoveryPeer?.Fingerprint == fingerprint || _freshInvitation?.Fingerprint == fingerprint)
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
        await Session.DisconnectAsync();
        RenderNotification();
        StatusChanged?.Invoke(this, EventArgs.Empty);
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
                    await Session.ConnectTrustedAsync(peer);
                    if (operation == _operation) RecoveryMessage = null;
                    return;
                }
                catch (AuthenticationException)
                {
                    RecoveryMessage = "PC rechazó la autorización o cambió su identidad. Vuelve a enlazar mediante QR.";
                    return;
                }
                catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or System.OperationCanceledException or ObjectDisposedException)
                { if (cancellation.IsCancellationRequested) return; }
            }
            RecoveryMessage = "No se recuperó el enlace después de 3 intentos. Revisa la red y conecta desde PC guardadas.";
        }
        catch (System.OperationCanceledException) { }
        catch (Exception)
        { if (operation == _operation) RecoveryMessage = "No se pudo recuperar la sesión. Revisa la PC y vuelve a enlazar por QR."; }
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
        var notification = BuildNotification("Preparando conexión con NOVORA PC…", true);
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(NotificationId, notification, ForegroundService.TypeConnectedDevice);
        else StartForeground(NotificationId, notification);
        _foreground = true;
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
    private void SessionChanged(object? sender, NLControlSessionState state) => Post(() =>
    {
        var current = Session.Current;
        _floating?.Update(current);
        if (_internetStopGeneration >= 0) _ = StopRemoteInternetAfterFailureAsync();
        if (_internetOwned && (current.Transport != "USB" || current.Phase != NLControlSessionPhase.Connected ||
            (!_internetStarting && current.Snapshot?.Engines is { LinkCanStart: true, LinkCanStop: false }))) StopLocalInternet();
        if (Session.Current.Phase == NLControlSessionPhase.Lost && !_retrying && _foreground && _recoveryPeer is { } peer)
        {
            _retrying = true;
            _recovery = new CancellationTokenSource();
            _ = RecoverAsync(peer, _recovery, _operation);
        }
        RenderNotification();
    });
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
        return $"{SafeName(snapshot.PcName)} · {transport}\nVideo {(snapshot.VideoRunning ? "activo" : "detenido")} · Bitrate {SafeName(snapshot.Bitrate)}";
    }
    private void RenderNotification()
    {
        if (_destroyed) return;
        var state = Session.Current;
        bool running = _retrying || state.Phase is NLControlSessionPhase.Connecting or NLControlSessionPhase.Connected;
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        if (running)
        {
            if (_foreground) manager.Notify(NotificationId, BuildNotification(RecoveryMessage ?? Describe(state), true));
            return;
        }
        if (state.Phase == NLControlSessionPhase.Disconnected) manager.Cancel(NotificationId);
        if (!_foreground) return;
        StopForeground(StopForegroundFlags.Remove);
        _foreground = false;
        StopSelf();
        // A lost session can remain visible as an ordinary notification, never as a running service.
        if (state.Phase == NLControlSessionPhase.Lost && NotificationsAllowed())
            manager.Notify(NotificationId, BuildNotification(RecoveryMessage ?? "Conexión perdida. Abre NOVORA para preparar un nuevo enlace.", false));
    }
    private bool NotificationsAllowed() => !OperatingSystem.IsAndroidVersionAtLeast(33) ||
        CheckSelfPermission(Android.Manifest.Permission.PostNotifications) == Permission.Granted;
    private Notification BuildNotification(string message, bool ongoing)
    {
        var open = PendingIntent.GetActivity(this, 214,
            new Intent(this, typeof(NLAndroidUIActivity)).AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        var disconnect = PendingIntent.GetService(this, 215,
            new Intent(this, typeof(NLAndroidServiceControl)).SetAction(DisconnectAction),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        var generic = new Notification.Builder(this, Channel)
            .SetSmallIcon(Resource.Drawable.novora_notification).SetContentTitle("NOVORA")
            .SetContentText("Control de PC").SetContentIntent(open).Build();
        var builder = new Notification.Builder(this, Channel)
            .SetSmallIcon(Resource.Drawable.novora_notification).SetContentTitle("NOVORA · Control PC")
            .SetContentText(message).SetStyle(new Notification.BigTextStyle().BigText(message))
            .SetContentIntent(open).SetOnlyAlertOnce(true)
            .SetOngoing(ongoing).SetAutoCancel(!ongoing).SetVisibility(NotificationVisibility.Private)
            .SetPublicVersion(generic).AddAction(new Notification.Action.Builder(null, "Abrir controles", open).Build());
        if (ongoing) builder.AddAction(new Notification.Action.Builder(null, "Desconectar", disconnect).Build());
        return builder.Build();
    }
    private sealed class NLAndroidServiceNetworkWatch(NLAndroidServiceControl owner) : ConnectivityManager.NetworkCallback
    {
        public override void OnAvailable(Network network) => owner.Post(() =>
        {
            if (owner._destroyed) return;
            var previous = owner._network;
            owner._network = network;
            if (previous is not null && !previous.Equals(network)) owner.LoseLan();
        });
        public override void OnLost(Network network) => owner.Post(() =>
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
        _floating?.Dispose();
        _floating = null;
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
