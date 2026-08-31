using NOVORA.Models;

namespace NOVORA.Services;

/// <summary>
/// Snapshot central del estado dinámico de NOVORA.
///
/// NCP obtiene los datos mediante servicios NOVORA y nunca
/// ejecuta ADB, scrcpy o Gnirehtet directamente. De esta forma varios widgets
/// pueden consumir el mismo resultado sin repetir consultas al dispositivo.
/// </summary>
public sealed class NovoraCenterPollingSnapshot : EventArgs
{
    public DeviceInfo? Device { get; init; }

    public NetworkStatus? Network { get; init; }

    public DeviceMetrics? Metrics { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public string? Error { get; init; }

    public bool HasDevice =>
        Device is not null &&
        Device.Connected &&
        !string.IsNullOrWhiteSpace(Device.Serial);

    public bool IsHealthy =>
        HasDevice &&
        string.IsNullOrWhiteSpace(Error);
}

/// <summary>
/// Centro único de polling para el estado dinámico de NOVORA.
///
/// Responsabilidades:
/// - Mantener un solo ciclo periódico de actualización.
/// - Resolver el dispositivo activo mediante un proveedor externo.
/// - Reutilizar DeviceStateService para red y métricas.
/// - Evitar ciclos superpuestos.
/// - Publicar un snapshot compartido para UI/widgets.
/// - Permitir una actualización inmediata bajo demanda.
///
/// No contiene referencias a WPF para que siga siendo un servicio reutilizable.
/// </summary>
public sealed class NCP : IAsyncDisposable, IDisposable
{
    private readonly DeviceStateService _deviceState;
    private readonly Func<DeviceInfo?> _deviceProvider;
    private readonly TimeSpan _interval;

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateGate = new();

    private CancellationTokenSource? _runCts;
    private Task? _runTask;

    private NovoraCenterPollingSnapshot _current = EmptySnapshot();

    private string? _lastSerial;
    private bool _disposed;

    public NCP(
        DeviceStateService deviceState,
        Func<DeviceInfo?> deviceProvider,
        TimeSpan? interval = null)
    {
        _deviceState =
            deviceState ??
            throw new ArgumentNullException(nameof(deviceState));

        _deviceProvider =
            deviceProvider ??
            throw new ArgumentNullException(nameof(deviceProvider));

        _interval =
            interval ??
            TimeSpan.FromSeconds(3);

        if (_interval < TimeSpan.FromMilliseconds(250))
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                "El intervalo de polling no puede ser menor a 250 ms.");
        }
    }

    /// <summary>
    /// Evento generado cada vez que NCP publica un nuevo snapshot.
    ///
    /// Este evento puede ejecutarse fuera del hilo principal de WPF.
    /// Los consumidores de UI deben usar Dispatcher si modifican controles.
    /// </summary>
    public event EventHandler<NovoraCenterPollingSnapshot>? SnapshotUpdated;

    /// <summary>
    /// Último estado conocido por NCP.
    /// </summary>
    public NovoraCenterPollingSnapshot Current
    {
        get
        {
            lock (_stateGate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Indica si el ciclo automático de NCP se encuentra activo.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock (_stateGate)
            {
                return _runTask is { IsCompleted: false };
            }
        }
    }

    /// <summary>
    /// Intervalo configurado entre ciclos de polling.
    /// </summary>
    public TimeSpan Interval => _interval;

    /// <summary>
    /// Inicia NCP.
    ///
    /// Si ya existe un ciclo activo, no crea otro.
    /// La primera actualización ocurre inmediatamente.
    /// </summary>
    public void Start()
    {
        ThrowIfDisposed();

        lock (_stateGate)
        {
            if (_runTask is { IsCompleted: false })
            {
                return;
            }

            _runCts?.Dispose();

            _runCts =
                new CancellationTokenSource();

            _runTask =
                RunAsync(_runCts.Token);
        }
    }

    /// <summary>
    /// Detiene el ciclo automático de polling.
    /// </summary>
    public async Task StopAsync()
    {
        Task? runTask;
        CancellationTokenSource? runCts;

        lock (_stateGate)
        {
            runTask = _runTask;
            runCts = _runCts;
        }

        if (runTask is null)
        {
            return;
        }

        try
        {
            runCts?.Cancel();

            await runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelación normal del ciclo.
        }
        finally
        {
            lock (_stateGate)
            {
                if (ReferenceEquals(
                        _runTask,
                        runTask))
                {
                    _runTask = null;

                    _runCts?.Dispose();
                    _runCts = null;
                }
            }
        }
    }

    /// <summary>
    /// Fuerza una actualización inmediata.
    ///
    /// SemaphoreSlim garantiza que nunca existan dos consultas
    /// centrales ejecutándose simultáneamente.
    /// </summary>
    public async Task<NovoraCenterPollingSnapshot> RefreshNowAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _refreshGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            DeviceInfo? device;

            try
            {
                device = _deviceProvider();
            }
            catch (Exception ex)
            {
                return Publish(
                    new NovoraCenterPollingSnapshot
                    {
                        UpdatedAtUtc =
                            DateTimeOffset.UtcNow,

                        Error =
                            $"No se pudo obtener el dispositivo activo: {ex.Message}"
                    });
            }

            if (!IsConnected(device))
            {
                if (_lastSerial is not null)
                {
                    _deviceState.Invalidate(
                        _lastSerial);

                    _lastSerial = null;
                }

                return Publish(
                    new NovoraCenterPollingSnapshot
                    {
                        Device = device,

                        UpdatedAtUtc =
                            DateTimeOffset.UtcNow
                    });
            }

            if (!string.Equals(
                    _lastSerial,
                    device!.Serial,
                    StringComparison.OrdinalIgnoreCase))
            {
                /*
                 * Cambió el dispositivo.
                 *
                 * Limpiamos cualquier dato cacheado del dispositivo anterior
                 * antes de comenzar el polling del nuevo.
                 */
                _deviceState.Invalidate();

                _lastSerial =
                    device.Serial;
            }

            NetworkStatus? network = null;
            DeviceMetrics? metrics = null;

            string? error = null;

            /*
             * Las consultas se hacen deliberadamente de forma secuencial.
             *
             * NetworkService y DeviceMetricsService terminan utilizando ADB.
             * Lanzarlas simultáneamente no aporta una ventaja importante
             * y puede incrementar el tráfico ADB de forma innecesaria.
             */

            try
            {
                network =
                    await _deviceState
                        .GetNetworkAsync(
                            device,
                            cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                error =
                    $"Red: {ex.Message}";
            }

            try
            {
                metrics =
                    await _deviceState
                        .GetMetricsAsync(
                            device,
                            cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                error =
                    string.IsNullOrWhiteSpace(error)
                        ? $"Métricas: {ex.Message}"
                        : $"{error} | Métricas: {ex.Message}";
            }

            return Publish(
                new NovoraCenterPollingSnapshot
                {
                    Device = device,

                    Network = network,

                    Metrics = metrics,

                    UpdatedAtUtc =
                        DateTimeOffset.UtcNow,

                    Error = error
                });
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Limpia el estado dinámico y las cachés utilizadas por NCP.
    ///
    /// Debe utilizarse, por ejemplo, después de:
    /// - conectar otro dispositivo;
    /// - cambiar USB/Wi-Fi;
    /// - reiniciar ADB;
    /// - perder la conexión;
    /// - cambiar manualmente el dispositivo seleccionado.
    /// </summary>
    public void Invalidate()
    {
        ThrowIfDisposed();

        _deviceState.Invalidate();

        _lastSerial = null;

        Publish(
            EmptySnapshot());
    }

    /// <summary>
    /// Ciclo interno de NCP.
    /// </summary>
    private async Task RunAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            /*
             * Primera lectura inmediata.
             * No esperamos el primer intervalo.
             */
            await RefreshNowAsync(
                    cancellationToken)
                .ConfigureAwait(false);

            using var timer =
                new PeriodicTimer(
                    _interval);

            while (
                await timer
                    .WaitForNextTickAsync(
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                await RefreshNowAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Finalización normal de NCP.
        }
    }

    /// <summary>
    /// Publica un nuevo snapshot como estado global actual.
    /// </summary>
    private NovoraCenterPollingSnapshot Publish(
        NovoraCenterPollingSnapshot snapshot)
    {
        lock (_stateGate)
        {
            _current = snapshot;
        }

        /*
         * Un consumidor defectuoso del evento no debe detener NCP.
         */
        try
        {
            SnapshotUpdated?.Invoke(
                this,
                snapshot);
        }
        catch
        {
            // NCP continúa funcionando.
        }

        return snapshot;
    }

    /// <summary>
    /// Determina si el DeviceInfo puede utilizarse para polling.
    /// </summary>
    private static bool IsConnected(
        DeviceInfo? device)
    {
        return
            device is not null &&
            device.Connected &&
            !string.IsNullOrWhiteSpace(
                device.Serial);
    }

    /// <summary>
    /// Crea el estado vacío inicial.
    /// </summary>
    private static NovoraCenterPollingSnapshot EmptySnapshot()
    {
        return new NovoraCenterPollingSnapshot
        {
            UpdatedAtUtc =
                DateTimeOffset.UtcNow
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    /// <summary>
    /// Liberación asíncrona recomendada.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync()
            .ConfigureAwait(false);

        _disposed = true;

        _refreshGate.Dispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Liberación síncrona.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopAsync()
            .GetAwaiter()
            .GetResult();

        _disposed = true;

        _refreshGate.Dispose();

        GC.SuppressFinalize(this);
    }
}