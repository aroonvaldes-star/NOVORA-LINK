using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Provider;

namespace NOVORA.LinkEngine.Android.LinkEngine;

[Service(
    Name = "com.novora.linkengine.VpnNetworkLE",
    Permission = "android.permission.BIND_VPN_SERVICE",
    Exported = true,
    Enabled = true)]
[IntentFilter(
    new[]
    {
        VpnService.ServiceInterface
    })]
public sealed class VpnNetworkLE :
    VpnService
{
    public const string ActionStartLE =
        "com.novora.linkengine.action.START_VPN";

    public const string ActionStopLE =
        "com.novora.linkengine.action.STOP_VPN";

    private static readonly TimeSpan ControlTimeoutLE =
        TimeSpan.FromSeconds(45);

    private readonly object _workerGate =
        new();

    private CancellationTokenSource? _lifetimeCts;
    private Task? _workerTask;

    private ClientTransportLE? _control;
    private TunnelNetworkLE? _tunnel;
    private ParcelFileDescriptor? _vpnInterface;

    private static StatusNetworkLE _status =
        StatusNetworkLE.CreateInitialLE();

    public static event EventHandler<StatusNetworkLE>?
        StatusChangedLE;

    public static StatusNetworkLE StatusLE =>
        _status;

    public static void StartLE(
        Context context)
    {
        ArgumentNullException.ThrowIfNull(
            context);

        var intent =
            new Intent(
                context,
                typeof(VpnNetworkLE));

        intent.SetAction(
            ActionStartLE);

        if (Build.VERSION.SdkInt >=
            BuildVersionCodes.O)
        {
            context.StartForegroundService(
                intent);
        }
        else
        {
            context.StartService(
                intent);
        }
    }

    public static void StopLE(
        Context context)
    {
        ArgumentNullException.ThrowIfNull(
            context);

        var intent =
            new Intent(
                context,
                typeof(VpnNetworkLE));

        intent.SetAction(
            ActionStopLE);

        context.StartService(
            intent);
    }

    public override StartCommandResult OnStartCommand(
        Intent? intent,
        StartCommandFlags flags,
        int startId)
    {
        if (string.Equals(
                intent?.Action,
                ActionStopLE,
                StringComparison.Ordinal))
        {
            _ =
                StopWorkerLEAsync();

            return StartCommandResult.NotSticky;
        }

        StartForegroundLE();

        StartWorkerLE();

        return StartCommandResult.Sticky;
    }

    private void StartForegroundLE()
    {
        Notification notification =
            NotificationNetworkLE.CreateLE(
                this);

        /*
         * Android 14 / API 34+:
         *
         * Podemos declarar explícitamente el tipo SpecialUse.
         *
         * Android 8-13:
         *
         * Conservamos la sobrecarga clásica de StartForeground.
         *
         * El guard OperatingSystem.IsAndroidVersionAtLeast()
         * permite al analizador comprobar correctamente las
         * restricciones de plataforma.
         */
        if (OperatingSystem.IsAndroidVersionAtLeast(
                34))
        {
            StartForeground(
                NotificationNetworkLE.NotificationIdLE,
                notification,
                ForegroundService.TypeSpecialUse);
        }
        else
        {
            StartForeground(
                NotificationNetworkLE.NotificationIdLE,
                notification);
        }
    }

    private void StartWorkerLE()
    {
        lock (_workerGate)
        {
            if (_workerTask is
                {
                    IsCompleted: false
                })
            {
                return;
            }

            _lifetimeCts?.Dispose();

            _lifetimeCts =
                new CancellationTokenSource();

            _workerTask =
                Task.Run(
                    () =>
                        RunWorkerLEAsync(
                            _lifetimeCts.Token),
                    CancellationToken.None);
        }
    }

    private async Task RunWorkerLEAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            PublishLE(
                StateNetworkLE.StartingControl,
                "Iniciando CONTROL 27183...",
                controlConnected:
                    false,
                handshakeVerified:
                    false,
                vpnActive:
                    false,
                dataConnected:
                    false,
                relayClientId:
                    null);

            string clientId =
                GetClientIdLE();

            var control =
                new ClientTransportLE(
                    clientId);

            _control =
                control;

            control.StatusChangedLE +=
                Control_StatusChangedLE;

            await control
                .StartAsync(
                    cancellationToken)
                .ConfigureAwait(false);

            PublishLE(
                StateNetworkLE.WaitingControl,
                "Esperando HELLO/ACK CONTROL...",
                controlConnected:
                    false,
                handshakeVerified:
                    false,
                vpnActive:
                    false,
                dataConnected:
                    false,
                relayClientId:
                    null);

            await WaitForControlLEAsync(
                    control,
                    cancellationToken)
                .ConfigureAwait(false);

            PublishLE(
                StateNetworkLE.EstablishingVpn,
                "CONTROL VERIFIED. Estableciendo VPN IPv4...",
                controlConnected:
                    true,
                handshakeVerified:
                    true,
                vpnActive:
                    false,
                dataConnected:
                    false,
                relayClientId:
                    null);

            ParcelFileDescriptor vpnInterface =
                EstablishVpnLE();

            _vpnInterface =
                vpnInterface;

            SetUnderlyingNetworkBestEffortLE();

            PublishLE(
                StateNetworkLE.ConnectingData,
                "VPN ACTIVE. Conectando DATA 27184...",
                controlConnected:
                    true,
                handshakeVerified:
                    true,
                vpnActive:
                    true,
                dataConnected:
                    false,
                relayClientId:
                    null);

            var tunnel =
                new TunnelNetworkLE(
                    this,
                    vpnInterface);

            _tunnel =
                tunnel;

            await tunnel
                .RunAsync(
                    relayId =>
                    {
                        PublishLE(
                            StateNetworkLE.Online,
                            $"LINKENGINE INTERNET ONLINE · RELAY #{relayId}",
                            controlConnected:
                                true,
                            handshakeVerified:
                                true,
                            vpnActive:
                                true,
                            dataConnected:
                                true,
                            relayClientId:
                                relayId);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "DATA tunnel terminó inesperadamente.");
            }
        }
        catch (System.OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            PublishLE(
                StateNetworkLE.Failed,
                $"LINKENGINE FAILED: {ex.Message}",
                controlConnected:
                    _control?.StatusLE.SocketConnected ==
                        true,
                handshakeVerified:
                    _control?.StatusLE.HandshakeVerified ==
                        true,
                vpnActive:
                    _vpnInterface is not null,
                dataConnected:
                    false,
                relayClientId:
                    null);
        }
        finally
        {
            await CleanupWorkerLEAsync()
                .ConfigureAwait(false);

            if (!cancellationToken.IsCancellationRequested)
            {
                StopForeground(
                    StopForegroundFlags.Remove);

                StopSelf();
            }
        }
    }

    private async Task WaitForControlLEAsync(
        ClientTransportLE control,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline =
            DateTimeOffset.UtcNow +
            ControlTimeoutLE;

        while (DateTimeOffset.UtcNow <
               deadline)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            StatusTransportLE status =
                control.StatusLE;

            if (status.SocketConnected &&
                status.HandshakeVerified)
            {
                return;
            }

            await Task.Delay(
                    100,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            "CONTROL 27183 no alcanzó HELLO/ACK.");
    }

    private ParcelFileDescriptor EstablishVpnLE()
    {
        var builder =
            new VpnService.Builder(
                this);

        builder.SetSession(
            "NOVORA LinkEngine");

        builder.AddAddress(
            "10.0.0.2",
            32);

        builder.AddRoute(
            "0.0.0.0",
            0);

        builder.AddDnsServer(
            "1.1.1.1");

        builder.SetMtu(
            0x4000);

        builder.SetBlocking(
            true);

        /*
         * Critical:
         *
         * NOVORA itself must never enter its own VPN.
         *
         * CONTROL and DATA are therefore excluded from TUN.
         */
        /*
         * LinkEngine debe quedar fuera de su propio TUN.
         *
         * PackageName está anotado como nullable por los
         * bindings Android, así que usamos el ApplicationId
         * conocido como fallback defensivo.
         */
        string packageNameLE =
            PackageName ??
            "com.novora.linkengine";

        builder.AddDisallowedApplication(
            packageNameLE);

        return
            builder.Establish() ??
            throw new InvalidOperationException(
                "VpnService.Builder.Establish() devolvió null.");
    }

    private void SetUnderlyingNetworkBestEffortLE()
    {
        try
        {
            var manager =
                GetSystemService(
                    ConnectivityService)
                as ConnectivityManager;

            if (manager is null)
            {
                return;
            }

            /*
             * ActiveNetwork está disponible desde una API
             * anterior al mínimo soportado por LinkEngine.
             *
             * LinkEngine soporta API 26+.
             *
             * Como com.novora.linkengine está excluido de su
             * propia VPN, sus sockets CONTROL/DATA utilizan
             * la conectividad exterior disponible.
             */
            Network? activeNetwork =
                manager.ActiveNetwork;

            if (activeNetwork is null)
            {
                return;
            }

            SetUnderlyingNetworks(
                new[]
                {
                    activeNetwork
                });
        }
        catch
        {
            /*
             * Best effort.
             *
             * Android puede seleccionar automáticamente la
             * red subyacente si este ajuste no está disponible.
             *
             * El Data Plane no depende de esta optimización.
             */
        }
    }

    private void Control_StatusChangedLE(
        object? sender,
        StatusTransportLE status)
    {
        StatusNetworkLE current =
            _status;

        if (current.State ==
            StateNetworkLE.Online)
        {
            PublishLE(
                current.State,
                current.Message,
                controlConnected:
                    status.SocketConnected,
                handshakeVerified:
                    status.HandshakeVerified,
                vpnActive:
                    current.VpnActive,
                dataConnected:
                    current.DataConnected,
                relayClientId:
                    current.RelayClientId);
        }
    }

    private string GetClientIdLE()
    {
        try
        {
            string? androidId =
                Settings.Secure.GetString(
                    ContentResolver,
                    Settings.Secure.AndroidId);

            if (!string.IsNullOrWhiteSpace(
                    androidId))
            {
                return
                    $"ANDROID-{androidId.Trim()}";
            }
        }
        catch
        {
        }

        string model =
            Build.Model ??
            "ANDROID";

        return
            $"{model}-{Guid.NewGuid():N}";
    }

    private async Task StopWorkerLEAsync()
    {
        PublishLE(
            StateNetworkLE.Stopping,
            "Deteniendo LinkEngine VPN...",
            controlConnected:
                false,
            handshakeVerified:
                false,
            vpnActive:
                false,
            dataConnected:
                false,
            relayClientId:
                null);

        try
        {
            _lifetimeCts?.Cancel();
        }
        catch
        {
        }

        await CleanupWorkerLEAsync()
            .ConfigureAwait(false);

        StopForeground(
            StopForegroundFlags.Remove);

        StopSelf();

        PublishLE(
            StateNetworkLE.Stopped,
            "LinkEngine VPN detenido.",
            controlConnected:
                false,
            handshakeVerified:
                false,
            vpnActive:
                false,
            dataConnected:
                false,
            relayClientId:
                null);
    }

    private async Task CleanupWorkerLEAsync()
    {
        TunnelNetworkLE? tunnel =
            Interlocked.Exchange(
                ref _tunnel,
                null);

        if (tunnel is not null)
        {
            try
            {
                await tunnel
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        ParcelFileDescriptor? vpnInterface =
            Interlocked.Exchange(
                ref _vpnInterface,
                null);

        if (vpnInterface is not null)
        {
            try
            {
                vpnInterface.Close();
            }
            catch
            {
            }
        }

        ClientTransportLE? control =
            Interlocked.Exchange(
                ref _control,
                null);

        if (control is not null)
        {
            control.StatusChangedLE -=
                Control_StatusChangedLE;

            try
            {
                await control
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    public override void OnRevoke()
    {
        _ =
            StopWorkerLEAsync();

        base.OnRevoke();
    }

    public override void OnDestroy()
    {
        try
        {
            _lifetimeCts?.Cancel();
        }
        catch
        {
        }

        _ =
            CleanupWorkerLEAsync();

        base.OnDestroy();
    }

    private static void PublishLE(
        StateNetworkLE state,
        string message,
        bool controlConnected,
        bool handshakeVerified,
        bool vpnActive,
        bool dataConnected,
        int? relayClientId)
    {
        var status =
            new StatusNetworkLE(
                State:
                    state,

                Message:
                    message,

                ControlConnected:
                    controlConnected,

                HandshakeVerified:
                    handshakeVerified,

                VpnActive:
                    vpnActive,

                DataConnected:
                    dataConnected,

                RelayClientId:
                    relayClientId,

                UpdatedAtUtc:
                    DateTimeOffset.UtcNow);

        _status =
            status;

        try
        {
            StatusChangedLE?.Invoke(
                null,
                status);
        }
        catch
        {
        }
    }
}