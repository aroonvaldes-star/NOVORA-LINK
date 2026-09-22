using NOVORA.Service;

namespace NOVORA.ExInEngine;

public sealed class ExInCoreEngine : IAsyncDisposable
{
    private readonly ExInOutputRouter _output = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private bool _initialized;
    private bool _enabled = true;
    private bool _disposed;
    private CancellationTokenSource? _recovery;
    private Task _recoveryTask = Task.CompletedTask;
    private int _recoveryAttempts;

    public ExInCoreEngine(NLServiceNovoraPaths paths)
    {
        Manager = new ExInManager(_output, paths);
        Manager.StatusChangedVE += Manager_StatusChanged;
    }
    public ExInManager Manager { get; }
    public ExInStatus Status => Manager.StatusVE;
    public ExInLiveSnapshot LiveSnapshot => Manager.LiveSnapshotVE;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            if (_enabled) await Manager.StartAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally { _lifecycle.Release(); }
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _enabled = enabled;
            if (!enabled) CancelRecovery();
            if (!_initialized) return;
            if (enabled) await Manager.StartAsync(cancellationToken).ConfigureAwait(false);
            else await Manager.StopAsync().ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
    }

    public async Task AttachOutputAsync(IExInOutput output, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized && output.IsReady)
                await Manager.BindOutputAsync(() => _output.Bind(output), cancellationToken).ConfigureAwait(false);
            else
                _output.Bind(output);
        }
        finally { _lifecycle.Release(); }
    }

    public async Task DetachOutputAsync(IExInOutput output, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_output.IsBoundTo(output)) return;
            if (_initialized)
                await Manager.UnbindOutputAsync(() => _output.Unbind(output), cancellationToken).ConfigureAwait(false);
            else
                _output.Unbind(output);
        }
        finally { _lifecycle.Release(); }
    }

    public void SetPrivacyProtected(bool value) => Manager.SetPrivacyProtectedVE(value);

    public async Task<ExInModeResult> ReactivateAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_enabled)
                return new(false, Manager.ModeVE, 0, "ExInEngine está desactivado.", "Disabled");
            if (!_initialized)
            {
                await Manager.StartAsync(cancellationToken).ConfigureAwait(false);
                _initialized = true;
            }
            else if (Manager.StatusVE.State == ExInStates.Failed)
            {
                await Manager.StopAsync().ConfigureAwait(false);
                await Manager.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await Manager.ResyncConnectedDevicesVEAsync(cancellationToken).ConfigureAwait(false);
            }
            return new(true, Manager.ModeVE, 0, "ExInEngine reactivado.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(false, Manager.ModeVE, 0, "No fue posible reactivar ExInEngine.", ex.Message);
        }
        finally { _lifecycle.Release(); }
    }

    private void Manager_StatusChanged(object? sender, ExInStatus status)
    {
        if (_disposed || !_initialized || !_enabled || !_output.IsReady ||
            status.State != ExInStates.Failed || _recovery is not null) return;
        _recovery = new CancellationTokenSource();
        _recoveryTask = RecoverAsync(_recovery);
    }

    private async Task RecoverAsync(CancellationTokenSource recovery)
    {
        await Task.Yield();
        try
        {
            int[] delays = [1, 3];
            for (int attempt = 0; attempt < delays.Length; attempt++)
            {
                recovery.Token.ThrowIfCancellationRequested();
                _recoveryAttempts = attempt + 1;
                await Task.Delay(TimeSpan.FromSeconds(delays[attempt]), recovery.Token).ConfigureAwait(false);
                await _lifecycle.WaitAsync(recovery.Token).ConfigureAwait(false);
                try
                {
                    if (_disposed || !_enabled) return;
                    await Manager.StopAsync().ConfigureAwait(false);
                    await Manager.StartAsync(recovery.Token).ConfigureAwait(false);
                    _recoveryAttempts = 0;
                    return;
                }
                catch when (!recovery.IsCancellationRequested && attempt + 1 < delays.Length) { }
                finally { _lifecycle.Release(); }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_recovery, recovery)) _recovery = null;
            recovery.Dispose();
        }
    }

    private void CancelRecovery()
    {
        var recovery = _recovery;
        _recovery = null;
        if (recovery is null) return;
        try { recovery.Cancel(); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        CancelRecovery();
        try { await _recoveryTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        Manager.StatusChangedVE -= Manager_StatusChanged;
        try { await Manager.DisposeAsync().ConfigureAwait(false); }
        finally { _lifecycle.Dispose(); }
    }
}
