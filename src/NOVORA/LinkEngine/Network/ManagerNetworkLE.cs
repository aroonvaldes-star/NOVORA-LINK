using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Metrics;
using NOVORA.LinkEngine.Transport;
using NOVORA.Services;

namespace NOVORA.LinkEngine.Network;

public sealed class ManagerNetworkLE :
    IAsyncDisposable
{
    private static readonly TimeSpan MaintenanceIntervalLE =
        TimeSpan.FromSeconds(5);

    private readonly AdbService _adb;
    private readonly CollectorMetricsLE _metrics;

    private readonly RelayNetworkLE _relay =
        new();

    private readonly DataTransportLE _data;

    private readonly ConcurrentDictionary<string, SessionNetworkLE> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _monitorCts =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Task> _monitorTasks =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _initialized;
    private bool _disposed;

    public ManagerNetworkLE(
        AdbService adb,
        CollectorMetricsLE metrics)
    {
        _adb =
            adb ??
            throw new ArgumentNullException(
                nameof(adb));

        _metrics =
            metrics ??
            throw new ArgumentNullException(
                nameof(metrics));

        _data =
            new DataTransportLE(
                _adb);
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        if (_initialized)
        {
            return;
        }

        await _relay
            .StartAsync(
                cancellationToken)
            .ConfigureAwait(false);

        _initialized =
            true;
    }

    public async Task<ResultCoreLE> StartAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        if (!_initialized)
        {
            return ResultCoreLE.Fail(
                "ManagerNetworkLE no está inicializado.");
        }

        if (string.IsNullOrWhiteSpace(serial))
        {
            return ResultCoreLE.Fail(
                "Serial inválido.");
        }

        serial =
            serial.Trim();

        try
        {
            await _relay
                .EnsureRunningAsync(
                    cancellationToken)
                .ConfigureAwait(false);

            ResultCoreLE dataResult =
                await _data
                    .StartAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!dataResult.Success)
            {
                return dataResult;
            }

            DateTimeOffset now =
                DateTimeOffset.UtcNow;

            _sessions[serial] =
                new SessionNetworkLE(
                    Serial:
                        serial,

                    State:
                        StateNetworkLE.Online,

                    RelayRunning:
                        _relay.IsRunningLE,

                    DataReverseConfigured:
                        true,

                    DataPort:
                        DataTransportLE.DevicePortLE,

                    StartedAtUtc:
                        now,

                    UpdatedAtUtc:
                        now,

                    Message:
                        "LINKENGINE DATA PLANE ONLINE.",

                    LastError:
                        null);

            var metrics =
                _metrics.GetOrCreate(
                    serial);

            metrics.SetInternetActive(
                true);

            metrics.SetState(
                StatesCoreLE.Online);

            StartMonitorLE(
                serial);

            return ResultCoreLE.Ok(
                $"ManagerNetworkLE DATA ONLINE · relay TCP/UDP + adb reverse tcp:{DataTransportLE.DevicePortLE}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _metrics
                .GetOrCreate(serial)
                .SetInternetActive(false);

            _sessions[serial] =
                SessionNetworkLE.CreateLE(
                    serial,
                    DataTransportLE.DevicePortLE) with
                {
                    State =
                        StateNetworkLE.Failed,

                    UpdatedAtUtc =
                        DateTimeOffset.UtcNow,

                    Message =
                        "ManagerNetworkLE no pudo iniciar.",

                    LastError =
                        ex.Message
                };

            return ResultCoreLE.Fail(
                $"ManagerNetworkLE no pudo iniciar: {ex.Message}");
        }
    }

    private void StartMonitorLE(
        string serial)
    {
        if (_monitorTasks.TryGetValue(
                serial,
                out Task? current) &&
            !current.IsCompleted)
        {
            return;
        }

        var cts =
            new CancellationTokenSource();

        _monitorCts[serial] =
            cts;

        _monitorTasks[serial] =
            Task.Run(
                () =>
                    MaintainLEAsync(
                        serial,
                        cts.Token),
                CancellationToken.None);
    }

    private async Task MaintainLEAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _relay
                    .EnsureRunningAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

                await _data
                    .EnsureAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

                _metrics
                    .GetOrCreate(serial)
                    .SetInternetActive(true);

                if (_sessions.TryGetValue(
                        serial,
                        out SessionNetworkLE? session))
                {
                    _sessions[serial] =
                        session with
                        {
                            State =
                                StateNetworkLE.Online,

                            RelayRunning =
                                true,

                            DataReverseConfigured =
                                true,

                            UpdatedAtUtc =
                                DateTimeOffset.UtcNow,

                            Message =
                                "LINKENGINE DATA PLANE ONLINE.",

                            LastError =
                                null
                        };
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _metrics
                    .GetOrCreate(serial)
                    .SetInternetActive(false);

                if (_sessions.TryGetValue(
                        serial,
                        out SessionNetworkLE? session))
                {
                    _sessions[serial] =
                        session with
                        {
                            State =
                                StateNetworkLE.Degraded,

                            RelayRunning =
                                _relay.IsRunningLE,

                            DataReverseConfigured =
                                false,

                            UpdatedAtUtc =
                                DateTimeOffset.UtcNow,

                            Message =
                                "DATA degradado. Reintentando.",

                            LastError =
                                ex.Message
                        };
                }
            }

            try
            {
                await Task.Delay(
                        MaintenanceIntervalLE,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<ResultCoreLE> StopAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return ResultCoreLE.Ok(
                "ManagerNetworkLE ya fue liberado.");
        }

        if (string.IsNullOrWhiteSpace(serial))
        {
            return ResultCoreLE.Ok(
                "No había ManagerNetworkLE que detener.");
        }

        serial =
            serial.Trim();

        if (_monitorCts.TryRemove(
                serial,
                out CancellationTokenSource? cts))
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
            }
        }

        if (_monitorTasks.TryRemove(
                serial,
                out Task? task))
        {
            try
            {
                await task
                    .WaitAsync(
                        TimeSpan.FromSeconds(2),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        cts?.Dispose();

        await _data
            .StopAsync(
                serial,
                cancellationToken)
            .ConfigureAwait(false);

        _sessions.TryRemove(
            serial,
            out _);

        _metrics
            .GetOrCreate(serial)
            .SetInternetActive(false);

        if (_sessions.IsEmpty)
        {
            await _relay
                .StopAsync(
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        return ResultCoreLE.Ok(
            "ManagerNetworkLE DATA detenido.");
    }

    public bool IsActive(
        string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return false;
        }

        return
            _sessions.TryGetValue(
                serial.Trim(),
                out SessionNetworkLE? session) &&
            session.State ==
                StateNetworkLE.Online &&
            session.RelayRunning &&
            session.DataReverseConfigured;
    }

    public SessionNetworkLE? GetSessionLE(
        string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return null;
        }

        _sessions.TryGetValue(
            serial.Trim(),
            out SessionNetworkLE? session);

        return session;
    }

    private void ThrowIfDisposedLE()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        string[] serials =
            _sessions.Keys.ToArray();

        foreach (string serial in serials)
        {
            try
            {
                await StopAsync(
                        serial,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        await _data
            .DisposeAsync()
            .ConfigureAwait(false);

        await _relay
            .DisposeAsync()
            .ConfigureAwait(false);

        _sessions.Clear();

        _initialized =
            false;

        _disposed =
            true;
    }
}