using System;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Device;
using NOVORA.LinkEngine.Metrics;
using NOVORA.LinkEngine.Network;
using NOVORA.LinkEngine.Recovery;
using NOVORA.LinkEngine.Transport;
using NOVORA.Services;

namespace NOVORA.LinkEngine.Core;

public sealed class EngineCoreLE : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleLock =
        new(1, 1);

    private bool _initialized;
    private bool _disposed;

    public ManagerDeviceLE Device { get; }
    public ManagerTransportLE Transport { get; }
    public ManagerNetworkLE Network { get; }
    public ManagerRecoveryLE Recovery { get; }
    public CollectorMetricsLE Metrics { get; }

    public NOVORA.LinkEngine.Traffic.TrafficEngineLE Traffic { get; } =
        new();
    public bool IsInitialized => _initialized;

    public EngineCoreLE(
        ManagerDeviceLE device,
        ManagerTransportLE transport,
        ManagerNetworkLE network,
        ManagerRecoveryLE recovery,
        CollectorMetricsLE metrics)
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

    public static EngineCoreLE CreateDefault(
        AdbService adbService)
    {
        ArgumentNullException.ThrowIfNull(adbService);

        var metrics =
            new CollectorMetricsLE();

        var device =
            new ManagerDeviceLE(
                adbService,
                metrics);

        var transport =
            new ManagerTransportLE(metrics);

        /*
         * LE-003B:
         * ManagerDeviceLE y ManagerTransportLE comparten exactamente la misma
         * instancia de AdbService.
         *
         * No se crea un segundo servidor ADB ni una segunda ruta
         * de ejecuciÃ³n hacia adb.exe.
         */
        transport.ConfigureAdbLE(
            adbService);

        /*
         * LE-006:
         * ManagerNetworkLE comparte el mismo AdbService para controlar
         * exclusivamente el DATA reverse tcp:27184.
         *
         * ManagerTransportLE continúa siendo propietario de CONTROL.
         */
        var network =
            new ManagerNetworkLE(
                adbService,
                metrics);

        var recovery =
            new ManagerRecoveryLE(
                device,
                transport,
                network,
                metrics);

        return new EngineCoreLE(
            device,
            transport,
            network,
            recovery,
            metrics);
    }

    public async Task<ResultCoreLE> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _lifecycleLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (_initialized)
            {
                return ResultCoreLE.Ok(
                    "EngineCoreLE ya estaba inicializado.");
            }

            await Device
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);

            await Transport
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);

            await Network
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);

            await Recovery
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);

            _initialized = true;

            return ResultCoreLE.Ok(
                "EngineCoreLE inicializado con ADB compartido.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ResultCoreLE.Fail(
                "No fue posible inicializar EngineCoreLE.",
                ex);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<ResultCoreLE> ConnectAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_initialized)
        {
            return ResultCoreLE.Fail(
                "EngineCoreLE no estÃ¡ inicializado.");
        }

        if (string.IsNullOrWhiteSpace(serial))
        {
            return ResultCoreLE.Fail(
                "El serial interno del dispositivo es obligatorio.");
        }

        serial = serial.Trim();

        Metrics
            .GetOrCreate(serial)
            .SetState(StatesCoreLE.Connecting);

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
                .SetState(StatesCoreLE.Failed);

            return deviceResult;
        }

        /*
         * Desde LE-003B este OpenAsync ya puede configurar
         * adb reverse porque ManagerTransportLE recibiÃ³ el AdbService
         * compartido durante CreateDefault().
         */
        var transportResult =
            await Transport
                .OpenAsync(
                    serial,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!transportResult.Success)
        {
            await Device
                .DisconnectAsync(
                    serial,
                    cancellationToken)
                .ConfigureAwait(false);

            Metrics
                .GetOrCreate(serial)
                .SetState(StatesCoreLE.Failed);

            return transportResult;
        }

        await Recovery
            .RegisterAsync(
                serial,
                cancellationToken)
            .ConfigureAwait(false);

        Metrics
            .GetOrCreate(serial)
            .SetState(StatesCoreLE.Connected);

        var session =
            Device.GetSession(serial);

        string connection =
            session?.ConnectionType.ToString()
            ?? ConnectionDeviceLE.Unknown.ToString();

        var transportSession =
            Transport.GetSessionLE(serial);

        string transportDescription =
            transportSession is null
                ? "Transport desconocido"
                : transportSession.ReverseConfigured
                    ? $"ADB reverse tcp:{transportSession.DevicePort}"
                    : $"Puerto tcp:{transportSession.DevicePort} preparado";

        return ResultCoreLE.Ok(
            $"EngineCoreLE conectado por {connection}. {transportDescription}.");
    }

    public Task<ResultCoreLE> RefreshDeviceAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        return Device.RefreshAsync(
            serial,
            cancellationToken);
    }

    public async Task<ResultCoreLE> StartInternetAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_initialized)
        {
            return ResultCoreLE.Fail(
                "EngineCoreLE no estÃ¡ inicializado.");
        }

        if (!Device.IsConnected(serial))
        {
            return ResultCoreLE.Fail(
                "ManagerDeviceLE no reporta el dispositivo como online.");
        }

        if (!Transport.IsOpen(serial))
        {
            return ResultCoreLE.Fail(
                "ManagerTransportLE no estÃ¡ abierto.");
        }

        /*
         * LE-006 Data Plane real:
         *
         * ManagerNetworkLE administra DATA por tcp:27184 y RelayCore
         * procesa TCP/UDP hacia Internet. CONTROL permanece separado
         * dentro de ManagerTransportLE.
         */
        return await Network
            .StartAsync(
                serial,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ResultCoreLE> StopInternetAsync(
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
                .SetState(StatesCoreLE.Connected);
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
         * ManagerTransportLE elimina el adb reverse y libera
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

    public StatusCoreLE GetStatus(
        string serial)
    {
        ThrowIfDisposed();

        var snapshot =
            Metrics.GetSnapshot(serial);

        return new StatusCoreLE(
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

    public SessionDeviceLE? GetDeviceSession(
        string serial)
    {
        ThrowIfDisposed();

        return Device.GetSession(serial);
    }

    public SessionTransportLE? GetTransportSessionLE(
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
}
