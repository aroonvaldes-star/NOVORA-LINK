using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Metrics;
using NOVORA.Service;

namespace NOVORA.LinkEngine.Device;

public sealed class LEDeviceManager : IAsyncDisposable
{
    private readonly NLServiceADB _adb;
    private readonly LEMetricsCollector _metrics;

    private readonly ConcurrentDictionary<string, LEDeviceSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _initialized;
    private bool _disposed;

    public LEDeviceManager(
        NLServiceADB adb,
        LEMetricsCollector metrics)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _adb.StartServerAsync(cancellationToken)
            .ConfigureAwait(false);

        _initialized = true;
    }

    public async Task<LECoreResult> ConnectAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_initialized)
            return LECoreResult.Fail("LEDeviceManager no está inicializado.");

        if (string.IsNullOrWhiteSpace(serial))
            return LECoreResult.Fail("Serial ADB inválido.");

        serial = serial.Trim();

        _metrics
            .GetOrCreate(serial)
            .SetState(LECoreStates.Connecting);

        var now = DateTimeOffset.UtcNow;

        try
        {
            string stateText;

            try
            {
                stateText = await _adb.GetStateAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
                when (ContainsUnauthorized(ex.Message))
            {
                SaveSession(
                    serial,
                    LEDeviceState.Unauthorized,
                    DetectConnectionType(serial),
                    adbOnline: false,
                    connectedAtUtc: now,
                    lastSeenUtc: now,
                    lastError: ex.Message);

                _metrics
                    .GetOrCreate(serial)
                    .SetState(LECoreStates.Failed);

                return LECoreResult.Fail(
                    $"ADB no está autorizado para {serial}. Autoriza esta PC en el teléfono.",
                    ex);
            }
            catch (InvalidOperationException ex)
                when (ContainsOffline(ex.Message))
            {
                SaveSession(
                    serial,
                    LEDeviceState.Offline,
                    DetectConnectionType(serial),
                    adbOnline: false,
                    connectedAtUtc: now,
                    lastSeenUtc: now,
                    lastError: ex.Message);

                _metrics
                    .GetOrCreate(serial)
                    .SetState(LECoreStates.Degraded);

                return LECoreResult.Fail(
                    $"El dispositivo {serial} aparece offline en ADB.",
                    ex);
            }

            var normalizedState =
                (stateText ?? string.Empty)
                .Trim();

            if (!string.Equals(
                    normalizedState,
                    "device",
                    StringComparison.OrdinalIgnoreCase))
            {
                var deviceState =
                    ParseAdbState(normalizedState);

                SaveSession(
                    serial,
                    deviceState,
                    DetectConnectionType(serial),
                    adbOnline: false,
                    connectedAtUtc: now,
                    lastSeenUtc: now,
                    lastError: $"ADB state: {normalizedState}");

                _metrics
                    .GetOrCreate(serial)
                    .SetState(
                        deviceState == LEDeviceState.Offline
                            ? LECoreStates.Degraded
                            : LECoreStates.Failed);

                return LECoreResult.Fail(
                    $"ADB no reportó el dispositivo como online. Estado: {normalizedState}");
            }

            bool online =
                await _adb.IsDeviceOnlineAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!online)
            {
                SaveSession(
                    serial,
                    LEDeviceState.Offline,
                    DetectConnectionType(serial),
                    adbOnline: false,
                    connectedAtUtc: now,
                    lastSeenUtc: DateTimeOffset.UtcNow,
                    lastError: "IsDeviceOnlineAsync devolvió false.");

                _metrics
                    .GetOrCreate(serial)
                    .SetState(LECoreStates.Degraded);

                return LECoreResult.Fail(
                    $"El dispositivo {serial} dejó de estar online durante la validación.");
            }

            var connectionType =
                DetectConnectionType(serial);

            SaveSession(
                serial,
                LEDeviceState.Online,
                connectionType,
                adbOnline: true,
                connectedAtUtc: now,
                lastSeenUtc: DateTimeOffset.UtcNow,
                lastError: null);

            _metrics
                .GetOrCreate(serial)
                .SetState(LECoreStates.Connected);

            return LECoreResult.Ok(
                $"LEDeviceManager conectado: {serial} | {connectionType} | ADB Online.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SaveSession(
                serial,
                LEDeviceState.Error,
                DetectConnectionType(serial),
                adbOnline: false,
                connectedAtUtc: now,
                lastSeenUtc: DateTimeOffset.UtcNow,
                lastError: ex.Message);

            _metrics
                .GetOrCreate(serial)
                .SetState(LECoreStates.Failed);

            return LECoreResult.Fail(
                $"LEDeviceManager no pudo validar {serial}.",
                ex);
        }
    }

    public async Task<LECoreResult> RefreshAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(serial))
            return LECoreResult.Fail("Serial ADB inválido.");

        serial = serial.Trim();

        if (!_sessions.TryGetValue(serial, out var current))
            return await ConnectAsync(serial, cancellationToken)
                .ConfigureAwait(false);

        try
        {
            bool online =
                await _adb.IsDeviceOnlineAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

            var state =
                online
                    ? LEDeviceState.Online
                    : LEDeviceState.Offline;

            var updated =
                current with
                {
                    State = state,
                    ConnectionType = DetectConnectionType(serial),
                    AdbOnline = online,
                    LastSeenUtc = DateTimeOffset.UtcNow,
                    LastError = online
                        ? null
                        : "ADB dejó de reportar el dispositivo como online."
                };

            _sessions[serial] = updated;

            _metrics
                .GetOrCreate(serial)
                .SetState(
                    online
                        ? LECoreStates.Connected
                        : LECoreStates.Degraded);

            return online
                ? LECoreResult.Ok(
                    $"LEDeviceManager saludable: {serial} | {updated.ConnectionType}.")
                : LECoreResult.Fail(
                    $"LEDeviceManager detectó {serial} offline.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var updated =
                current with
                {
                    State = LEDeviceState.Error,
                    AdbOnline = false,
                    LastSeenUtc = DateTimeOffset.UtcNow,
                    LastError = ex.Message
                };

            _sessions[serial] = updated;

            _metrics
                .GetOrCreate(serial)
                .SetState(LECoreStates.Failed);

            return LECoreResult.Fail(
                $"No fue posible refrescar LEDeviceManager para {serial}.",
                ex);
        }
    }

    public Task DisconnectAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(serial))
        {
            serial = serial.Trim();

            _sessions.TryRemove(
                serial,
                out _);

            _metrics
                .GetOrCreate(serial)
                .SetState(LECoreStates.Disconnected);
        }

        return Task.CompletedTask;
    }

    public bool IsConnected(string serial)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(serial))
            return false;

        return _sessions.TryGetValue(
                   serial.Trim(),
                   out var session) &&
               session.State == LEDeviceState.Online &&
               session.AdbOnline;
    }

    public LEDeviceSession? GetSession(string serial)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(serial))
            return null;

        return _sessions.TryGetValue(
            serial.Trim(),
            out var session)
            ? session
            : null;
    }

    public LEDeviceConnection GetConnectionType(string serial)
    {
        ThrowIfDisposed();

        var session = GetSession(serial);

        return session?.ConnectionType
            ?? LEDeviceConnection.Unknown;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _sessions.Clear();
        _initialized = false;

        return ValueTask.CompletedTask;
    }

    private void SaveSession(
        string serial,
        LEDeviceState state,
        LEDeviceConnection connectionType,
        bool adbOnline,
        DateTimeOffset connectedAtUtc,
        DateTimeOffset lastSeenUtc,
        string? lastError)
    {
        _sessions[serial] =
            new LEDeviceSession(
                serial,
                state,
                connectionType,
                adbOnline,
                connectedAtUtc,
                lastSeenUtc,
                lastError);
    }

    private static LEDeviceState ParseAdbState(string state)
    {
        if (string.Equals(
                state,
                "device",
                StringComparison.OrdinalIgnoreCase))
        {
            return LEDeviceState.Online;
        }

        if (string.Equals(
                state,
                "offline",
                StringComparison.OrdinalIgnoreCase))
        {
            return LEDeviceState.Offline;
        }

        if (string.Equals(
                state,
                "unauthorized",
                StringComparison.OrdinalIgnoreCase))
        {
            return LEDeviceState.Unauthorized;
        }

        return LEDeviceState.Unknown;
    }

    private static LEDeviceConnection DetectConnectionType(string serial)
    {
        /*
         * ADB por red usa normalmente un endpoint host:port.
         * USB usa un serial físico.
         *
         * No exponemos el serial/IP a la UI; solamente devolvemos
         * la categoría USB o Wi-Fi.
         */
        if (string.IsNullOrWhiteSpace(serial))
            return LEDeviceConnection.Unknown;

        return LooksLikeNetworkEndpoint(serial)
            ? LEDeviceConnection.Wifi
            : LEDeviceConnection.Usb;
    }

    private static bool LooksLikeNetworkEndpoint(string serial)
    {
        int separatorIndex =
            serial.LastIndexOf(':');

        if (separatorIndex <= 0 ||
            separatorIndex >= serial.Length - 1)
        {
            return false;
        }

        string portText =
            serial[(separatorIndex + 1)..];

        if (!int.TryParse(
                portText,
                out int port) ||
            port is < 1 or > 65535)
        {
            return false;
        }

        string host =
            serial[..separatorIndex];

        if (string.IsNullOrWhiteSpace(host))
            return false;

        return host.Contains('.') ||
               host.Contains(':') ||
               string.Equals(
                   host,
                   "localhost",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsUnauthorized(string text)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               text.Contains(
                   "unauthorized",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsOffline(string text)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               text.Contains(
                   "offline",
                   StringComparison.OrdinalIgnoreCase);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
