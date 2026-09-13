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

    private readonly AdbService _adb;
    private readonly CollectorMetricsLE _metrics;

    private readonly RelayNetworkLE _relay =
        new();

    private readonly DataTransportLE _data;

    private readonly ConcurrentDictionary<string, SessionNetworkLE> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _relayRecoveryGateLE =
        new(1, 1);

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

        _relay.ExitedLE +=
            Relay_ExitedLE;
    }

    public event EventHandler<SessionNetworkLE>? SessionChangedLE;

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

            SessionNetworkLE onlineSession =
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

            _sessions[serial] = onlineSession;
            PublishSessionChangedLE(onlineSession);

            var metrics =
                _metrics.GetOrCreate(
                    serial);

            metrics.SetInternetActive(
                true);

            metrics.SetState(
                StatesCoreLE.Online);

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

            SessionNetworkLE failedSession =
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

            _sessions[serial] = failedSession;
            PublishSessionChangedLE(failedSession);

            return ResultCoreLE.Fail(
                $"ManagerNetworkLE no pudo iniciar: {ex.Message}");
        }
    }

    private void Relay_ExitedLE(
        object? sender,
        EventArgs e)
    {
        foreach ((string serial, SessionNetworkLE session) in
                 _sessions.ToArray())
        {
            SessionNetworkLE degraded =
                session with
                {
                    State = StateNetworkLE.Degraded,
                    RelayRunning = false,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Message = "LinkEngine Relay terminó inesperadamente.",
                    LastError = "Relay process exited."
                };

            _sessions[serial] = degraded;
            PublishSessionChangedLE(degraded);

            _metrics
                .GetOrCreate(serial)
                .SetInternetActive(false);
        }

        // Evento real: intentamos una recuperación única del Data Plane.
        // No existe loop de mantenimiento ni reintento periódico.
        _ = RecoverRelayAfterExitLEAsync();
    }

    private async Task RecoverRelayAfterExitLEAsync()
    {
        try
        {
            await _relayRecoveryGateLE.WaitAsync().ConfigureAwait(false);
            try
            {
                string[] serials = _sessions.Keys.ToArray();
                if (serials.Length == 0 || _disposed)
                    return;

                await _relay
                    .EnsureRunningAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                foreach (string serial in serials)
                {
                    if (!_sessions.TryGetValue(serial, out SessionNetworkLE? current))
                        continue;

                    try
                    {
                        await _data
                            .EnsureAsync(serial, CancellationToken.None)
                            .ConfigureAwait(false);

                        SessionNetworkLE recovered =
                            current with
                            {
                                State = StateNetworkLE.Online,
                                RelayRunning = true,
                                DataReverseConfigured = true,
                                UpdatedAtUtc = DateTimeOffset.UtcNow,
                                Message = "LINKENGINE DATA PLANE RECOVERED BY EVENT.",
                                LastError = null
                            };

                        _sessions[serial] = recovered;
                        PublishSessionChangedLE(recovered);

                        var metrics = _metrics.GetOrCreate(serial);
                        metrics.SetInternetActive(true);
                        metrics.SetState(StatesCoreLE.Online);
                    }
                    catch (Exception ex)
                    {
                        SessionNetworkLE failed =
                            current with
                            {
                                State = StateNetworkLE.Degraded,
                                RelayRunning = _relay.IsRunningLE,
                                DataReverseConfigured = false,
                                UpdatedAtUtc = DateTimeOffset.UtcNow,
                                Message = "Data Plane degradado después de recuperación por evento.",
                                LastError = ex.Message
                            };

                        _sessions[serial] = failed;
                        PublishSessionChangedLE(failed);
                        _metrics.GetOrCreate(serial).SetInternetActive(false);
                    }
                }
            }
            finally
            {
                _relayRecoveryGateLE.Release();
            }
        }
        catch
        {
            // El estado degradado ya quedó publicado; no creamos un poller de rescate.
        }
    }

    private void PublishSessionChangedLE(
        SessionNetworkLE session)
    {
        try
        {
            SessionChangedLE?.Invoke(this, session);
        }
        catch
        {
            // Observers/UI nunca deben romper NetworkLE.
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

        _relay.ExitedLE -=
            Relay_ExitedLE;

        await _relay
            .DisposeAsync()
            .ConfigureAwait(false);

        _sessions.Clear();

        _initialized =
            false;

        _disposed =
            true;

        _relayRecoveryGateLE.Dispose();
    }
}