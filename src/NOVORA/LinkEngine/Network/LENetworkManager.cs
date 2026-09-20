using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Metrics;
using NOVORA.LinkEngine.Transport;
using NOVORA.Service;

namespace NOVORA.LinkEngine.Network;

public sealed class LENetworkManager :
    IAsyncDisposable
{

    private readonly NLServiceADB _adb;
    private readonly LEMetricsCollector _metrics;

    private readonly LENetworkRelay _relay =
        new();

    private readonly LETransportData _data;

    private readonly ConcurrentDictionary<string, LENetworkSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _relayRecoveryGateLE =
        new(1, 1);

    private bool _initialized;
    private bool _disposed;

    public LENetworkManager(
        NLServiceADB adb,
        LEMetricsCollector metrics)
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
            new LETransportData(
                _adb);

        _relay.ExitedLE +=
            Relay_ExitedLE;
    }

    public event EventHandler<LENetworkSession>? SessionChangedLE;

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

    public async Task<LECoreResult> StartAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        if (!_initialized)
        {
            return LECoreResult.Fail(
                "LENetworkManager no está inicializado.");
        }

        if (string.IsNullOrWhiteSpace(serial))
        {
            return LECoreResult.Fail(
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

            LECoreResult dataResult =
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

            LENetworkSession onlineSession =
                new LENetworkSession(
                    Serial:
                        serial,

                    State:
                        LENetworkState.Online,

                    RelayRunning:
                        _relay.IsRunningLE,

                    DataReverseConfigured:
                        true,

                    DataPort:
                        LETransportData.DevicePortLE,

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
                LECoreStates.Online);

            return LECoreResult.Ok(
                $"LENetworkManager DATA ONLINE · relay TCP/UDP + adb reverse tcp:{LETransportData.DevicePortLE}.");
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

            LENetworkSession failedSession =
                LENetworkSession.CreateLE(
                    serial,
                    LETransportData.DevicePortLE) with
                {
                    State =
                        LENetworkState.Failed,

                    UpdatedAtUtc =
                        DateTimeOffset.UtcNow,

                    Message =
                        "LENetworkManager no pudo iniciar.",

                    LastError =
                        ex.Message
                };

            _sessions[serial] = failedSession;
            PublishSessionChangedLE(failedSession);

            return LECoreResult.Fail(
                $"LENetworkManager no pudo iniciar: {ex.Message}");
        }
    }

    private void Relay_ExitedLE(
        object? sender,
        EventArgs e)
    {
        foreach ((string serial, LENetworkSession session) in
                 _sessions.ToArray())
        {
            LENetworkSession degraded =
                session with
                {
                    State = LENetworkState.Degraded,
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
                    if (!_sessions.TryGetValue(serial, out LENetworkSession? current))
                        continue;

                    try
                    {
                        await _data
                            .EnsureAsync(serial, CancellationToken.None)
                            .ConfigureAwait(false);

                        LENetworkSession recovered =
                            current with
                            {
                                State = LENetworkState.Online,
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
                        metrics.SetState(LECoreStates.Online);
                    }
                    catch (Exception ex)
                    {
                        LENetworkSession failed =
                            current with
                            {
                                State = LENetworkState.Degraded,
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
        LENetworkSession session)
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

    public async Task<LECoreResult> StopAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return LECoreResult.Ok(
                "LENetworkManager ya fue liberado.");
        }

        if (string.IsNullOrWhiteSpace(serial))
        {
            return LECoreResult.Ok(
                "No había LENetworkManager que detener.");
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

        return LECoreResult.Ok(
            "LENetworkManager DATA detenido.");
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
                out LENetworkSession? session) &&
            session.State ==
                LENetworkState.Online &&
            session.RelayRunning &&
            session.DataReverseConfigured;
    }

    public LENetworkSession? GetSessionLE(
        string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return null;
        }

        _sessions.TryGetValue(
            serial.Trim(),
            out LENetworkSession? session);

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