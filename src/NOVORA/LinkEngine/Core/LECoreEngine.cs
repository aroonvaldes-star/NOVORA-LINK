using System;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Device;
using NOVORA.LinkEngine.Metrics;
using NOVORA.LinkEngine.Network;
using NOVORA.LinkEngine.Recovery;
using NOVORA.LinkEngine.Transport;
using NOVORA.Service;

namespace NOVORA.LinkEngine.Core;

public sealed class LECoreEngine : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleLock =
        new(1, 1);

    private bool _initialized;
    private bool _disposed;

    public LEDeviceManager Device { get; }
    public LETransportManager Transport { get; }
    public LENetworkManager Network { get; }
    public LERecoveryManager Recovery { get; }
    public LEMetricsCollector Metrics { get; }

    public bool IsInitialized => _initialized;

    public LECoreEngine(
        LEDeviceManager device,
        LETransportManager transport,
        LENetworkManager network,
        LERecoveryManager recovery,
        LEMetricsCollector metrics)
    {
        Device =
            device ??
            throw new ArgumentNullException(nameof(device));

        Transport =
            transport ??
            throw new ArgumentNullException(nameof(transport));

        Network =
            network ??
            throw new ArgumentNullException(nameof(network));

        Recovery =
            recovery ??
            throw new ArgumentNullException(nameof(recovery));

        Metrics =
            metrics ??
            throw new ArgumentNullException(nameof(metrics));
    }

    public static LECoreEngine CreateDefault(
        NLServiceADB adbService)
    {
        ArgumentNullException.ThrowIfNull(adbService);

        var metrics =
            new LEMetricsCollector();

        var device =
            new LEDeviceManager(
                adbService,
                metrics);

        var transport =
            new LETransportManager(metrics);

        /*
         * LE-003B:
         * LEDeviceManager y LETransportManager comparten exactamente la misma
         * instancia de NLServiceADB.
         *
         * No se crea un segundo servidor ADB ni una segunda ruta
         * de ejecuciÃ³n hacia adb.exe.
         */
        transport.ConfigureAdbLE(
            adbService);

        /*
         * LE-006:
         * LENetworkManager comparte el mismo NLServiceADB para controlar
         * exclusivamente el DATA reverse tcp:27184.
         *
         * LETransportManager continúa siendo propietario de CONTROL.
         */
        var network =
            new LENetworkManager(
                adbService,
                metrics);

        var recovery =
            new LERecoveryManager(
                device,
                transport,
                network,
                metrics);

        return new LECoreEngine(
            device,
            transport,
            network,
            recovery,
            metrics);
    }

    public async Task<LECoreResult> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _lifecycleLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        bool deviceInitialized = false;
        bool transportInitialized = false;
        bool networkInitialized = false;
        bool recoveryInitialized = false;

        try
        {
            if (_initialized)
            {
                return LECoreResult.Ok(
                    "LECoreEngine ya estaba inicializado.");
            }

            deviceInitialized = true;
            await Device
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);

            transportInitialized = true;
            await Transport
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);

            networkInitialized = true;
            await Network
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);

            recoveryInitialized = true;
            await Recovery
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);

            _initialized = true;

            return LECoreResult.Ok(
                "LECoreEngine inicializado con ADB compartido.");
        }
        catch (OperationCanceledException)
        {
            await CleanupInitializationAsync(
                    recoveryInitialized,
                    networkInitialized,
                    transportInitialized,
                    deviceInitialized)
                .ConfigureAwait(false);

            throw;
        }
        catch (Exception ex)
        {
            await CleanupInitializationAsync(
                    recoveryInitialized,
                    networkInitialized,
                    transportInitialized,
                    deviceInitialized)
                .ConfigureAwait(false);

            return LECoreResult.Fail(
                "No fue posible inicializar LECoreEngine.",
                ex);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<LECoreResult> ConnectAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_initialized)
        {
            return LECoreResult.Fail(
                "LECoreEngine no estÃ¡ inicializado.");
        }

        if (string.IsNullOrWhiteSpace(serial))
        {
            return LECoreResult.Fail(
                "El serial interno del dispositivo es obligatorio.");
        }

        serial = serial.Trim();

        Metrics
            .GetOrCreate(serial)
            .SetState(LECoreStates.Connecting);

        var deviceResult =
            await Device
                .ConnectAsync(
                    serial,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!deviceResult.Success)
        {
            Metrics
                .GetOrCreate(serial)
                .SetState(LECoreStates.Failed);

            return deviceResult;
        }

        /*
         * Desde LE-003B este OpenAsync ya puede configurar
         * adb reverse porque LETransportManager recibiÃ³ el NLServiceADB
         * compartido durante CreateDefault().
         */
        LECoreResult transportResult;

        try
        {
            transportResult =
                await Transport
                    .OpenAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await CleanupConnectionAsync(serial)
                .ConfigureAwait(false);

            throw;
        }
        catch (Exception ex)
        {
            await CleanupConnectionAsync(serial)
                .ConfigureAwait(false);

            Metrics
                .GetOrCreate(serial)
                .SetState(LECoreStates.Failed);

            return LECoreResult.Fail(
                "No fue posible abrir el transporte de LINKENGINE.",
                ex);
        }

        if (!transportResult.Success)
        {
            await CleanupConnectionAsync(serial)
                .ConfigureAwait(false);

            Metrics
                .GetOrCreate(serial)
                .SetState(LECoreStates.Failed);

            return transportResult;
        }

        try
        {
            await Recovery
                .RegisterAsync(
                    serial,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await CleanupConnectionAsync(serial)
                .ConfigureAwait(false);

            throw;
        }
        catch (Exception ex)
        {
            await CleanupConnectionAsync(serial)
                .ConfigureAwait(false);

            Metrics
                .GetOrCreate(serial)
                .SetState(LECoreStates.Failed);

            return LECoreResult.Fail(
                "No fue posible registrar la recuperación de LINKENGINE.",
                ex);
        }

        Metrics
            .GetOrCreate(serial)
            .SetState(LECoreStates.Connected);

        var session =
            Device.GetSession(serial);

        string connection =
            session?.ConnectionType.ToString()
            ?? LEDeviceConnection.Unknown.ToString();

        var transportSession =
            Transport.GetSessionLE(serial);

        string transportDescription =
            transportSession is null
                ? "Transport desconocido"
                : transportSession.ReverseConfigured
                    ? $"ADB reverse tcp:{transportSession.DevicePort}"
                    : $"Puerto tcp:{transportSession.DevicePort} preparado";

        return LECoreResult.Ok(
            $"LECoreEngine conectado por {connection}. {transportDescription}.");
    }

    public Task<LECoreResult> RefreshDeviceAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        return Device.RefreshAsync(
            serial,
            cancellationToken);
    }

    public async Task<LECoreResult> StartInternetAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_initialized)
        {
            return LECoreResult.Fail(
                "LECoreEngine no estÃ¡ inicializado.");
        }

        if (!Device.IsConnected(serial))
        {
            return LECoreResult.Fail(
                "LEDeviceManager no reporta el dispositivo como online.");
        }

        if (!Transport.IsOpen(serial))
        {
            return LECoreResult.Fail(
                "LETransportManager no estÃ¡ abierto.");
        }

        /*
         * LE-006 Data Plane real:
         *
         * LENetworkManager administra DATA por tcp:27184 y NovoraRelay
         * procesa TCP/UDP hacia Internet. CONTROL permanece separado
         * dentro de LETransportManager.
         */
        return await Network
            .StartAsync(
                serial,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LECoreResult> StopInternetAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var result =
            await Network
                .StopAsync(
                    serial,
                    cancellationToken)
                .ConfigureAwait(false);

        if (Device.IsConnected(serial))
        {
            Metrics
                .GetOrCreate(serial)
                .SetState(LECoreStates.Connected);
        }

        return result;
    }

    public async Task DisconnectAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(serial))
            return;

        serial = serial.Trim();

        await Recovery
            .UnregisterAsync(
                serial,
                cancellationToken)
            .ConfigureAwait(false);

        await Network
            .StopAsync(
                serial,
                cancellationToken)
            .ConfigureAwait(false);

        /*
         * LETransportManager elimina el adb reverse y libera
         * el puerto reservado de esta sesiÃ³n.
         */
        await Transport
            .CloseAsync(
                serial,
                cancellationToken)
            .ConfigureAwait(false);

        await Device
            .DisconnectAsync(
                serial,
                cancellationToken)
            .ConfigureAwait(false);

        Metrics.Remove(serial);
    }

    public LECoreStatus GetStatus(
        string serial)
    {
        ThrowIfDisposed();

        var snapshot =
            Metrics.GetSnapshot(serial);

        return new LECoreStatus(
            Serial: serial,
            State: snapshot.State,
            DeviceConnected:
                Device.IsConnected(serial),
            TransportOpen:
                Transport.IsOpen(serial),
            InternetActive:
                Network.IsActive(serial),
            Healthy:
                Recovery.IsHealthy(serial),
            UpdatedAtUtc:
                snapshot.LastActivityUtc);
    }

    public LEDeviceSession? GetDeviceSession(
        string serial)
    {
        ThrowIfDisposed();

        return Device.GetSession(serial);
    }

    public LETransportSession? GetTransportSessionLE(
        string serial)
    {
        ThrowIfDisposed();

        return Transport.GetSessionLE(serial);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        /*
         * Esperamos el lock antes de marcar disposed porque los
         * DisposeAsync internos deben poder completar su limpieza.
         */
        await _lifecycleLock
            .WaitAsync()
            .ConfigureAwait(false);

        try
        {
            if (_disposed)
                return;

            await Recovery
                .DisposeAsync()
                .ConfigureAwait(false);

            /*
             * Transport se cierra antes que Device para que todavÃ­a
             * exista ADB cuando se eliminen los adb reverse.
             */
            await Network
                .DisposeAsync()
                .ConfigureAwait(false);

            await Transport
                .DisposeAsync()
                .ConfigureAwait(false);

            await Device
                .DisposeAsync()
                .ConfigureAwait(false);

            Metrics.Clear();

            _initialized = false;
            _disposed = true;
        }
        finally
        {
            _lifecycleLock.Release();
            _lifecycleLock.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    private async Task CleanupConnectionAsync(
        string serial)
    {
        try
        {
            await Recovery
                .UnregisterAsync(
                    serial,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // La limpieza debe continuar aunque una capa ya esté degradada.
        }

        try
        {
            await Network
                .StopAsync(
                    serial,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Transport
                .CloseAsync(
                    serial,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Device
                .DisconnectAsync(
                    serial,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task CleanupInitializationAsync(
        bool recoveryInitialized,
        bool networkInitialized,
        bool transportInitialized,
        bool deviceInitialized)
    {
        _initialized = false;

        if (recoveryInitialized)
        {
            try
            {
                await Recovery
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (networkInitialized)
        {
            try
            {
                await Network
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (transportInitialized)
        {
            try
            {
                await Transport
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (deviceInitialized)
        {
            try
            {
                await Device
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
}
