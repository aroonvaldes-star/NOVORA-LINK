using NOVORA.Service;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Device;
using NOVORA.VisionEngine.Server;
using NOVORA.VisionEngine.Transport;

namespace NOVORA.ExInEngine;

public sealed class ExInControlSession : IAsyncDisposable
{
    private readonly ExInCoreEngine _engine;
    private readonly VEDeviceManager _device;
    private readonly VEServerManager _server;
    private readonly VETransportManager _transport;
    private readonly VEControlManager _control = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _recoveryGateVE = new();
    private VEDeviceSession? _deviceSession;
    private VETransportTunnel? _tunnel;
    private VEServerSession? _serverSession;
    private VETransportSession? _transportSession;
    private string? _desiredSerialVE;
    private Task _recoveryTaskVE = Task.CompletedTask;
    private CancellationTokenSource? _recoveryCtsVE;
    private long _sessionGenerationVE;
    private bool _disposed;

    public ExInControlSession(ExInCoreEngine engine, NLServiceNovoraPaths paths, NLServiceADB adb)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _device = new(adb ?? throw new ArgumentNullException(nameof(adb)));
        _server = new(adb, paths ?? throw new ArgumentNullException(nameof(paths)));
        _transport = new(adb);
        _control.StatusChangedVE += Control_StatusChangedVE;
    }

    public string? Serial => _deviceSession?.Serial;
    public bool IsRunning => _control.IsReadyVE;

    public async Task StartAsync(string serial, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        string normalized = serial.Trim();
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_recoveryGateVE)
            {
                _sessionGenerationVE++;
                _desiredSerialVE = normalized;
                try { _recoveryCtsVE?.Cancel(); } catch { }
            }
            if (IsRunning && string.Equals(Serial, normalized, StringComparison.OrdinalIgnoreCase)) return;
            await StopInternalAsync(cancellationToken).ConfigureAwait(false);
            await StartInternalAsync(normalized, cancellationToken).ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_recoveryGateVE)
            {
                _sessionGenerationVE++;
                _desiredSerialVE = null;
                try { _recoveryCtsVE?.Cancel(); } catch { }
            }
            await StopInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
    }

    private async Task StopInternalAsync(CancellationToken cancellationToken)
    {
        try { await _engine.DetachOutputAsync(_control, cancellationToken).ConfigureAwait(false); } catch when (!cancellationToken.IsCancellationRequested) { }
        cancellationToken.ThrowIfCancellationRequested();
        try { await _control.StopAsync(cancellationToken).ConfigureAwait(false); } catch when (!cancellationToken.IsCancellationRequested) { }
        cancellationToken.ThrowIfCancellationRequested();
        if (_transportSession is not null) { try { await _transportSession.DisposeAsync().ConfigureAwait(false); } catch { } _transportSession = null; }
        if (_serverSession is not null) { try { await _serverSession.DisposeAsync().ConfigureAwait(false); } catch { } _serverSession = null; }
        if (_tunnel is not null) { try { await _transport.RemoveAsync(_tunnel, cancellationToken).ConfigureAwait(false); } catch when (!cancellationToken.IsCancellationRequested) { } _tunnel = null; }
        _deviceSession = null;
        _device.CloseVE();
        _server.MarkStoppedVE();
    }

    public async Task<ExInModeResult> ReactivateAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? serial;
            lock (_recoveryGateVE)
            {
                _sessionGenerationVE++;
                try { _recoveryCtsVE?.Cancel(); } catch { }
                serial = _desiredSerialVE ?? Serial;
            }
            if (string.IsNullOrWhiteSpace(serial))
                return new(false, _engine.Manager.ModeVE, 0, "No hay una sesión ExIn para reactivar.", "NoSession");
            await StopInternalAsync(cancellationToken).ConfigureAwait(false);
            await StartInternalAsync(serial, cancellationToken).ConfigureAwait(false);
            return await _engine.ReactivateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(false, _engine.Manager.ModeVE, 0, "No fue posible reactivar la sesión ExIn.", ex.Message);
        }
        finally { _lifecycle.Release(); }
    }

    private async Task StartInternalAsync(string serial, CancellationToken cancellationToken)
    {
        try
        {
            _deviceSession = await _device.OpenAsync(serial, cancellationToken).ConfigureAwait(false);
            _tunnel = await _transport.PrepareAsync(_deviceSession.Serial, cancellationToken).ConfigureAwait(false);
            VEServerOptions options = VEServerOptions.CreateControlOnlyVE();
            _serverSession = await _server.StartAsync(_deviceSession, _tunnel, options, cancellationToken).ConfigureAwait(false);
            _transportSession = await _transport.ConnectAsync(_tunnel, options, cancellationToken: cancellationToken).ConfigureAwait(false);
            await _control.StartAsync(_transportSession, cancellationToken).ConfigureAwait(false);
            await _engine.AttachOutputAsync(_control, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await StopInternalAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private void Control_StatusChangedVE(object? sender, VEControlStatus status)
    {
        if (status.State is not (VEControlStates.EndOfStream or VEControlStates.Failed)) return;
        lock (_recoveryGateVE)
        {
            if (_disposed || string.IsNullOrWhiteSpace(_desiredSerialVE) || _recoveryCtsVE is not null) return;
            string serial = _desiredSerialVE;
            long generation = _sessionGenerationVE;
            CancellationTokenSource recovery = new(TimeSpan.FromSeconds(20));
            _recoveryCtsVE = recovery;
            _recoveryTaskVE = Task.Run(
                () => RecoverControlAsync(serial, generation, recovery),
                CancellationToken.None);
        }
    }

    private async Task RecoverControlAsync(string serial, long generation, CancellationTokenSource recovery)
    {
        CancellationToken cancellationToken = recovery.Token;
        try
        {
            await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string? desired;
                long currentGeneration;
                lock (_recoveryGateVE)
                {
                    desired = _desiredSerialVE;
                    currentGeneration = _sessionGenerationVE;
                }
                if (_disposed || currentGeneration != generation ||
                    !string.Equals(desired, serial, StringComparison.OrdinalIgnoreCase)) return;
                await StopInternalAsync(cancellationToken).ConfigureAwait(false);
                await StartInternalAsync(serial, cancellationToken).ConfigureAwait(false);
                await _engine.ReactivateAsync(cancellationToken).ConfigureAwait(false);
            }
            finally { _lifecycle.Release(); }
        }
        catch (OperationCanceledException)
        {
            try
            {
                await _lifecycle.WaitAsync().ConfigureAwait(false);
                try
                {
                    bool ownsSession;
                    lock (_recoveryGateVE) ownsSession = _sessionGenerationVE == generation;
                    if (ownsSession) await StopInternalAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally { _lifecycle.Release(); }
            }
            catch { }
        }
        catch { }
        finally
        {
            lock (_recoveryGateVE)
            {
                if (ReferenceEquals(_recoveryCtsVE, recovery)) _recoveryCtsVE = null;
            }
            recovery.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        Task recoveryTask;
        lock (_recoveryGateVE)
        {
            _disposed = true;
            _sessionGenerationVE++;
            _desiredSerialVE = null;
            try { _recoveryCtsVE?.Cancel(); } catch { }
            recoveryTask = _recoveryTaskVE;
        }
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopInternalAsync(CancellationToken.None).ConfigureAwait(false);
            _control.StatusChangedVE -= Control_StatusChangedVE;
            await _control.DisposeAsync().ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
        try { await recoveryTask.ConfigureAwait(false); } catch { }
        _lifecycle.Dispose();
    }
}
