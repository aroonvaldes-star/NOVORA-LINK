using NOVORA.ExInEngine;
using NOVORA.Service;

namespace NOVORA.Integration;

/// <summary>Connects ExInEngine to Android's system HID service through ADB.</summary>
public sealed class ExInControlSession : IAsyncDisposable
{
    private readonly ExInCoreEngine _engine;
    private readonly NLServiceNovoraPaths _paths;
    private readonly NLServiceADB _adb;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly ExInConnectionSupervisor _supervisor;
    private CancellationTokenSource? _startCancellation;
    private long _stopVersion;
    private volatile ExInHidOutput? _output;
    private volatile string? _serial;
    private string? _hardwareSerial;
    private volatile string? _requestedSerial;
    private volatile bool _disposed;

    public ExInControlSession(ExInCoreEngine engine, NLServiceNovoraPaths paths, NLServiceADB adb)
        : this(engine, paths, adb, TimeProvider.System) { }

    public ExInControlSession(ExInCoreEngine engine, NLServiceNovoraPaths paths, NLServiceADB adb,
        TimeProvider timeProvider)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        ArgumentNullException.ThrowIfNull(timeProvider);
        _supervisor = new(EnsureConnectedAsync,
            () => !_disposed && _requestedSerial is not null && _engine.IsEnabledVE, timeProvider);
    }

    public string? Serial => _serial;
    public bool IsRunning => _output?.IsReady == true;
    public bool HasRequestedTarget => _requestedSerial is not null;
    public string? RequestedSerial => _requestedSerial;

    public async Task StartAsync(string serial, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        string normalized = serial.Trim();
        long version = Volatile.Read(ref _stopVersion);
        await _requests.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? start = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            start = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Volatile.Write(ref _startCancellation, start);
            if (version != Volatile.Read(ref _stopVersion)) start.Cancel();
            CancellationToken attemptToken = start.Token;
            attemptToken.ThrowIfCancellationRequested();
            if (!string.Equals(_requestedSerial, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _requestedSerial = null;
                await _supervisor.StopAsync().ConfigureAwait(false);
                await _lifecycle.WaitAsync(attemptToken).ConfigureAwait(false);
                try { await StopInternalAsync().ConfigureAwait(false); _hardwareSerial = null; }
                finally { _lifecycle.Release(); }
            }
            _requestedSerial = normalized;
            try { await EnsureConnectedAsync(attemptToken).ConfigureAwait(false); }
            finally { if (version == Volatile.Read(ref _stopVersion)) _supervisor.Start(); }
        }
        finally
        {
            Interlocked.CompareExchange(ref _startCancellation, null, start);
            start?.Dispose();
            _requests.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) return;
        CancelInitialStart();
        await _requests.WaitAsync().ConfigureAwait(false);
        try
        {
            _requestedSerial = null;
            await _supervisor.StopAsync().ConfigureAwait(false);
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try { await StopInternalAsync().ConfigureAwait(false); _hardwareSerial = null; }
            finally { _lifecycle.Release(); }
        }
        finally { _requests.Release(); }
    }

    private void CancelInitialStart()
    {
        Interlocked.Increment(ref _stopVersion);
        try { Volatile.Read(ref _startCancellation)?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed || _requestedSerial is null || !_engine.IsEnabledVE || IsRunning) return;
            string serial = await ResolveCurrentSerialAsync(_requestedSerial, cancellationToken).ConfigureAwait(false);
            await StopInternalAsync().ConfigureAwait(false);
            await StartInternalAsync(serial, cancellationToken).ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
    }

    private async Task StartInternalAsync(string serial, CancellationToken cancellationToken)
    {
        await _adb.StartServerAsync(cancellationToken).ConfigureAwait(false);
        string hardwareSerial = string.Empty;
        try { hardwareSerial = await _adb.GetHardwareSerialAsync(serial, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("ExIn device identity unavailable: " + ex.Message);
        }
        if (_hardwareSerial is not null &&
            !string.Equals(hardwareSerial, _hardwareSerial, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("No se puede confirmar la identidad del teléfono de ExIn.");
        if (!string.IsNullOrWhiteSpace(hardwareSerial)) _hardwareSerial = hardwareSerial;
        var output = new ExInHidOutput(_adb, _paths, serial);
        try
        {
            output.Failed += OutputFailed;
            await _engine.AttachOutputAsync(output, cancellationToken).ConfigureAwait(false);
            ExInModeResult result = await _engine.ReactivateAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Success) throw new InvalidOperationException(result.Message);
            cancellationToken.ThrowIfCancellationRequested();
            _output = output;
            _serial = serial;
        }
        catch
        {
            output.Failed -= OutputFailed;
            try { await _engine.DetachOutputAsync(output).ConfigureAwait(false); }
            finally { await output.DisposeAsync().ConfigureAwait(false); }
            throw;
        }
    }

    private async Task StopInternalAsync()
    {
        ExInHidOutput? output = _output;
        _output = null;
        _serial = null;
        if (output is null) return;
        output.Failed -= OutputFailed;
        try { await _engine.DetachOutputAsync(output).ConfigureAwait(false); }
        finally { await output.DisposeAsync().ConfigureAwait(false); }
    }

    private void OutputFailed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _output)) _supervisor.Start();
    }

    private async Task<string> ResolveCurrentSerialAsync(string? preferredSerial, CancellationToken cancellationToken)
    {
        var devices = (await _adb.GetDevicesAsync(cancellationToken, forceRefresh: true).ConfigureAwait(false))
            .Where(device => device.Connected).ToArray();
        if (devices.Length == 0) throw new InvalidOperationException("ADB no detecta el teléfono para ExIn.");
        if (string.IsNullOrWhiteSpace(_hardwareSerial))
        {
            var current = devices.FirstOrDefault(device => string.Equals(device.Serial, _requestedSerial, StringComparison.OrdinalIgnoreCase));
            if (current is not null) return current.Serial;
            throw new InvalidOperationException("No se puede confirmar que el nuevo transporte ADB sea el mismo teléfono.");
        }
        foreach (var device in devices
            .OrderByDescending(device => string.Equals(device.Serial, preferredSerial, StringComparison.OrdinalIgnoreCase))
            .ThenBy(device => device.IsWifiConnection))
        {
            try
            {
                string hardwareSerial = await _adb.GetHardwareSerialAsync(device.Serial, cancellationToken).ConfigureAwait(false);
                if (string.Equals(hardwareSerial, _hardwareSerial, StringComparison.OrdinalIgnoreCase)) return device.Serial;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("ExIn device identity check failed: " + ex.Message);
            }
        }
        throw new InvalidOperationException("No se encontró el mismo teléfono en los transportes ADB activos.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        CancelInitialStart();
        await _requests.WaitAsync().ConfigureAwait(false);
        try
        {
            _requestedSerial = null;
            await _supervisor.StopAsync().ConfigureAwait(false);
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try { await StopInternalAsync().ConfigureAwait(false); }
            finally { _lifecycle.Release(); }
        }
        finally { _requests.Release(); }
    }
}
