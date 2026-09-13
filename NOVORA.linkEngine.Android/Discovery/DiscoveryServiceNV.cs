using Android.App;
using Android.Content;
using Android.OS;

namespace NOVORA.LinkEngine.Android.Discovery;

[Service(
    Name = "com.novora.linkengine.DiscoveryServiceNV",
    Enabled = true,
    Exported = false)]
public sealed class DiscoveryServiceNV :
    Service
{
    public const string ActionStartNV =
        "com.novora.linkengine.action.DISCOVERY_START";

    public const string ActionReconnectNV =
        "com.novora.linkengine.action.DISCOVERY_RECONNECT";

    private readonly SemaphoreSlim _connectGateNV =
        new(1, 1);

    private CancellationTokenSource?
        _lifetimeCtsNV;

    private PresenceClientRemoteNV?
        _presenceNV;

    private bool _everConnectedNV;
    private bool _connectedNV;
    private bool _stoppingNV;

    public override void OnCreate()
    {
        base.OnCreate();

        _lifetimeCtsNV =
            new CancellationTokenSource();

        NotificationDiscoveryNV
            .EnsureChannelNV(
                this);

        StartForeground(
            NotificationDiscoveryNV.NotificationIdNV,
            NotificationDiscoveryNV.CreateNV(
                this,
                "Esperando NOVORA PC"));
    }

    public override StartCommandResult OnStartCommand(
        Intent? intent,
        StartCommandFlags flags,
        int startId)
    {
        string actionNV =
            intent?.Action
            ??
            ActionStartNV;

        bool recoveryNV =
            string.Equals(
                actionNV,
                ActionReconnectNV,
                StringComparison.Ordinal) &&
            _everConnectedNV &&
            !_connectedNV;

        _ =
            ConnectFromSignalNVAsync(
                recoveryNV);

        return StartCommandResult.Sticky;
    }

    private async Task ConnectFromSignalNVAsync(
        bool recoveryNV)
    {
        CancellationTokenSource? lifetimeNV =
            _lifetimeCtsNV;

        if (lifetimeNV is null ||
            lifetimeNV.IsCancellationRequested ||
            _stoppingNV)
        {
            return;
        }

        // Conserva la señal de una credencial nueva aunque haya una conexión en curso.
        await _connectGateNV.WaitAsync(lifetimeNV.Token).ConfigureAwait(false);

        try
        {
            if (_presenceNV?.IsConnectedNV ==
                true)
            {
                return;
            }

            if (recoveryNV)
            {
                NotificationDiscoveryNV.ShowNV(
                    this,
                    "Recuperando conexión…");
            }

            PresenceClientRemoteNV? oldPresenceNV =
                _presenceNV;

            _presenceNV =
                null;

            if (oldPresenceNV is not null)
            {
                await oldPresenceNV
                    .DisposeAsync();
            }

            var presenceNV =
                new PresenceClientRemoteNV();

            bool connectedNV =
                await presenceNV
                    .ConnectAsync(
                        lifetimeNV.Token)
                    .ConfigureAwait(false);

            if (!connectedNV)
            {
                await presenceNV
                    .DisposeAsync();

                _connectedNV =
                    false;

                NotificationDiscoveryNV.ShowNV(
                    this,
                    recoveryNV
                        ? "No se pudo recuperar la conexión"
                        : "Desconectado de tu PC");

                return;
            }

            _presenceNV =
                presenceNV;

            _connectedNV =
                true;

            bool recoveredNV =
                recoveryNV;

            _everConnectedNV =
                true;

            NotificationDiscoveryNV.ShowNV(
                this,
                recoveredNV
                    ? "Conexión recuperada"
                    : "Conectado a tu PC");

            _ =
                MonitorPresenceNVAsync(
                    presenceNV,
                    lifetimeNV.Token);
        }
        catch (global::System.OperationCanceledException)
        {
        }
        catch
        {
            _connectedNV =
                false;

            NotificationDiscoveryNV.ShowNV(
                this,
                recoveryNV
                    ? "No se pudo recuperar la conexión"
                    : "Desconectado de tu PC");
        }
        finally
        {
            _connectGateNV.Release();
        }
    }

    private async Task MonitorPresenceNVAsync(
        PresenceClientRemoteNV presenceNV,
        CancellationToken cancellationToken)
    {
        try
        {
            await presenceNV
                .WaitForDisconnectAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
        }

        if (_stoppingNV ||
            cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await _connectGateNV
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (!ReferenceEquals(
                    _presenceNV,
                    presenceNV))
            {
                return;
            }

            _presenceNV =
                null;

            _connectedNV =
                false;

            await presenceNV
                .DisposeAsync();

            NotificationDiscoveryNV.ShowNV(
                this,
                "Desconectado de tu PC");
        }
        finally
        {
            _connectGateNV.Release();
        }
    }

    public override void OnDestroy()
    {
        _stoppingNV =
            true;

        _lifetimeCtsNV?.Cancel();

        PresenceClientRemoteNV? presenceNV =
            _presenceNV;

        _presenceNV =
            null;

        if (presenceNV is not null)
        {
            try
            {
                presenceNV
                    .DisposeAsync()
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
            }
        }

        _lifetimeCtsNV?.Dispose();
        _lifetimeCtsNV =
            null;

        base.OnDestroy();
    }

    public override global::Android.OS.IBinder?
        OnBind(
            Intent? intent)
    {
        return null;
    }
}
