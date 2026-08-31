using NOVORA.Models;

namespace NOVORA.Services;

/// <summary>
/// Estado dinámico compartido del dispositivo Android.
///
/// Centraliza y cachea las lecturas de red y métricas para evitar que NCP,
/// MainWindow u otros consumidores generen consultas ADB duplicadas.
/// </summary>
public sealed class DeviceStateService : IDisposable
{
    private readonly NetworkService _network;
    private readonly DeviceMetricsService _metrics;
    private readonly TimeSpan _cacheDuration;

    private readonly object _cacheGate = new();
    private readonly SemaphoreSlim _networkGate = new(1, 1);
    private readonly SemaphoreSlim _metricsGate = new(1, 1);

    private string? _networkSerial;
    private NetworkStatus? _networkCache;
    private DateTimeOffset _networkAt;

    private string? _metricsSerial;
    private DeviceMetrics? _metricsCache;
    private DateTimeOffset _metricsAt;

    private bool _disposed;

    public DeviceStateService(
        NetworkService network,
        DeviceMetricsService metrics,
        TimeSpan? cacheDuration = null)
    {
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

        _cacheDuration = cacheDuration ?? TimeSpan.FromSeconds(2);

        if (_cacheDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cacheDuration),
                "La duración de caché no puede ser negativa.");
        }
    }

    /// <summary>
    /// Duración durante la cual una lectura puede reutilizarse sin volver a ADB.
    /// </summary>
    public TimeSpan CacheDuration => _cacheDuration;

    /// <summary>
    /// Obtiene el estado de red del dispositivo.
    /// Reutiliza una lectura reciente y evita consultas concurrentes duplicadas.
    /// </summary>
    public async Task<NetworkStatus> GetNetworkAsync(
        DeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateDevice(device);

        if (TryGetNetworkCache(device.Serial, out var cached))
        {
            return cached;
        }

        await _networkGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            // Otro consumidor pudo haber llenado la caché mientras esperábamos.
            if (TryGetNetworkCache(device.Serial, out cached))
            {
                return cached;
            }

            var value = await _network
                .GetAsync(device, cancellationToken)
                .ConfigureAwait(false);

            lock (_cacheGate)
            {
                _networkSerial = device.Serial;
                _networkCache = value;
                _networkAt = DateTimeOffset.UtcNow;
            }

            return value;
        }
        finally
        {
            _networkGate.Release();
        }
    }

    /// <summary>
    /// Obtiene CPU, RAM, batería y temperatura del dispositivo.
    /// Reutiliza una lectura reciente y evita consultas concurrentes duplicadas.
    /// </summary>
    public async Task<DeviceMetrics> GetMetricsAsync(
        DeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateDevice(device);

        if (TryGetMetricsCache(device.Serial, out var cached))
        {
            return cached;
        }

        await _metricsGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            // Otro consumidor pudo haber llenado la caché mientras esperábamos.
            if (TryGetMetricsCache(device.Serial, out cached))
            {
                return cached;
            }

            var value = await _metrics
                .GetAsync(device, cancellationToken)
                .ConfigureAwait(false);

            lock (_cacheGate)
            {
                _metricsSerial = device.Serial;
                _metricsCache = value;
                _metricsAt = DateTimeOffset.UtcNow;
            }

            return value;
        }
        finally
        {
            _metricsGate.Release();
        }
    }

    /// <summary>
    /// Invalida las cachés dinámicas.
    ///
    /// Si serial es null, limpia todo.
    /// Si se proporciona un serial, solo limpia datos pertenecientes a ese
    /// dispositivo.
    /// </summary>
    public void Invalidate(string? serial = null)
    {
        ThrowIfDisposed();

        lock (_cacheGate)
        {
            if (serial is null ||
                string.Equals(
                    _networkSerial,
                    serial,
                    StringComparison.OrdinalIgnoreCase))
            {
                _networkSerial = null;
                _networkCache = null;
                _networkAt = default;
            }

            if (serial is null ||
                string.Equals(
                    _metricsSerial,
                    serial,
                    StringComparison.OrdinalIgnoreCase))
            {
                _metricsSerial = null;
                _metricsCache = null;
                _metricsAt = default;
            }
        }
    }

    private bool TryGetNetworkCache(
        string serial,
        out NetworkStatus value)
    {
        lock (_cacheGate)
        {
            if (_networkCache is not null &&
                string.Equals(
                    _networkSerial,
                    serial,
                    StringComparison.OrdinalIgnoreCase) &&
                IsFresh(_networkAt))
            {
                value = _networkCache;
                return true;
            }
        }

        value = null!;
        return false;
    }

    private bool TryGetMetricsCache(
        string serial,
        out DeviceMetrics value)
    {
        lock (_cacheGate)
        {
            if (_metricsCache is not null &&
                string.Equals(
                    _metricsSerial,
                    serial,
                    StringComparison.OrdinalIgnoreCase) &&
                IsFresh(_metricsAt))
            {
                value = _metricsCache;
                return true;
            }
        }

        value = null!;
        return false;
    }

    private bool IsFresh(DateTimeOffset timestamp)
    {
        return timestamp != default &&
               DateTimeOffset.UtcNow - timestamp < _cacheDuration;
    }

    private static void ValidateDevice(DeviceInfo? device)
    {
        if (device is null ||
            !device.Connected ||
            string.IsNullOrWhiteSpace(device.Serial))
        {
            throw new InvalidOperationException(
                "No hay un dispositivo Android conectado.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _networkGate.Dispose();
        _metricsGate.Dispose();

        GC.SuppressFinalize(this);
    }
}
