using NOVORA.Contracts.Input;
using NOVORA.Service;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NOVORA.ExInEngine;

public sealed class ExInCoreEngine : IAsyncDisposable
{
    private readonly ExInOutputRouter _output = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _recoveryGate = new();
    private readonly object _enableGate = new();
    private readonly ILogger _logger; // ⭐ NUEVO: Logging
    private CancellationTokenSource _enabledOperations = new();
    private long _enableRequest;
    private long _enableGeneration;
    private long _managerEnableGeneration = -1;
    private bool _initialized;
    private volatile bool _enabled = true;
    private volatile bool _disposed;
    private CancellationTokenSource? _recovery;
    private Task _recoveryTask = Task.CompletedTask;

    public ExInCoreEngine(NLServiceNovoraPaths paths, ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance; // ⭐ NUEVO: Logger inyectable
        Manager = new ExInManager(_output, paths);
        Manager.StatusChangedVE += Manager_StatusChanged;
        Manager.OutputIssueVE += Manager_OutputIssue;
    }
    
    public ExInManager Manager { get; }
    public ExInStatus Status => Manager.StatusVE;
    public ExInLiveSnapshot LiveSnapshot => Manager.LiveSnapshotVE;
    public bool IsEnabledVE => _enabled && !_disposed;
    public event EventHandler<ExInOutputIssue>? OutputIssueVE;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialized) return;
            _initialized = true;
            if (_enabled)
            {
                using var operation = EnabledOperation(cancellationToken, out long generation);
                await Manager.StartAsync(operation.Token).ConfigureAwait(false);
                _managerEnableGeneration = generation;
            }
        }
        finally { _lifecycle.Release(); }
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long request;
        CancellationTokenSource? oldOps = null; // ⭐ NUEVO: Rastrear old ops
        
        lock (_enableGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            request = ++_enableRequest;
            _enabled = enabled;
            
            if (!enabled) 
            {
                _enabledOperations.Cancel();
            }
            else if (_enabledOperations.IsCancellationRequested)
            {
                // ⭐ FIJO: Race condition - una sola thread hace esto
                oldOps = _enabledOperations;
                _enabledOperations = new CancellationTokenSource();
                _enableGeneration++;
            }
        }
        
        // ⭐ FIJO: Dispose fuera del lock
        if (oldOps is not null)
        {
            try { oldOps.Dispose(); }
            catch { }
        }
        
        if (!enabled) CancelRecovery();
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_enableGate) if (request != _enableRequest) return;
            if (!_initialized) return;
            if (enabled)
            {
                using var operation = EnabledOperation(cancellationToken, out long generation);
                if (generation != _managerEnableGeneration || Manager.StatusVE.State == ExInStates.Failed)
                    await Manager.StopAsync().ConfigureAwait(false);
                await Manager.StartAsync(operation.Token).ConfigureAwait(false);
                _managerEnableGeneration = generation;
            }
            else await Manager.StopAsync().ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
    }

    public async Task AttachOutputAsync(INLInputOutput output, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_enabled)
            {
                _output.Bind(output);
                return;
            }
            using var operation = EnabledOperation(cancellationToken, out _);
            if (_initialized && output.IsReady)
            {
                try
                {
                    await Manager.BindOutputAsync(() => _output.Bind(output), operation.Token).ConfigureAwait(false);
                }
                catch
                {
                    if (_output.IsBoundTo(output))
                    {
                        try { await Manager.UnbindOutputAsync(() => _output.Unbind(output), CancellationToken.None).ConfigureAwait(false); }
                        finally { _output.Unbind(output); }
                    }
                    throw;
                }
            }
            else
                _output.Bind(output);
        }
        finally { _lifecycle.Release(); }
    }

    public async Task DetachOutputAsync(INLInputOutput output, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) return;
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
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_enabled)
                return new(false, Manager.ModeVE, 0, "ExInEngine está desactivado.", "Disabled");
            using var operation = EnabledOperation(cancellationToken, out long generation);
            if (!_initialized)
            {
                _initialized = true;
                await Manager.StartAsync(operation.Token).ConfigureAwait(false);
                _managerEnableGeneration = generation;
            }
            else if (Manager.StatusVE.State == ExInStates.Failed)
            {
                await Manager.StopAsync().ConfigureAwait(false);
                await Manager.StartAsync(operation.Token).ConfigureAwait(false);
                _managerEnableGeneration = generation;
            }
            else
            {
                await Manager.ResyncConnectedDevicesVEAsync(operation.Token).ConfigureAwait(false);
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
        if (_disposed || !_initialized || !_enabled || status.State != ExInStates.Failed) return;
        CancellationTokenSource recovery;
        long generation;
        try { recovery = EnabledOperation(CancellationToken.None, out generation); }
        catch (ObjectDisposedException) { return; }
        lock (_recoveryGate)
        {
            if (_disposed || !_initialized || !_enabled || _recovery is not null)
            {
                recovery.Dispose();
                return;
            }
            _recovery = recovery;
            _recoveryTask = RecoverAsync(recovery, generation);
        }
    }

    private void Manager_OutputIssue(object? sender, ExInOutputIssue issue)
        => OutputIssueVE?.Invoke(this, issue);

    private async Task RecoverAsync(CancellationTokenSource recovery, long enableGeneration)
    {
        await Task.Yield();
        try
        {
            int attempt = 0;
            const int MaxRetriesVE = 10; // ⭐ NUEVO: Límite de reintentos
            
            while (attempt < MaxRetriesVE) // ⭐ FIJO: Condición de salida
            {
                recovery.Token.ThrowIfCancellationRequested();
                
                // Continue unattended while enabled, with a bounded retry frequency.
                double delaySeconds = attempt switch { 0 => 1, 1 => 3, 2 => 5, _ => 15 };
                if (attempt < 3) attempt++;
                
                _logger.LogInformation($"[ExIn Recovery] Intento #{attempt}/{MaxRetriesVE} después de {delaySeconds}s"); // ⭐ NUEVO: Logging
                
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), recovery.Token).ConfigureAwait(false);
                
                bool started = false;
                await _lifecycle.WaitAsync(recovery.Token).ConfigureAwait(false);
                try
                {
                    if (_disposed || !_enabled || Manager.StatusVE.State == ExInStates.Stopped) 
                        return; // ⭐ FIJO: Salida explícita
                        
                    await Manager.StopAsync().ConfigureAwait(false);
                    await Manager.StartAsync(recovery.Token).ConfigureAwait(false);
                    _managerEnableGeneration = enableGeneration;
                    started = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && !recovery.IsCancellationRequested) 
                { 
                    _logger.LogWarning(ex, $"[ExIn Recovery] Error en intento #{attempt}"); // ⭐ NUEVO: Logging
                }
                finally { _lifecycle.Release(); }
                
                if (started)
                {
                    // Do not reset backoff when Start returns but the next report fails.
                    await Task.Delay(TimeSpan.FromSeconds(1), recovery.Token).ConfigureAwait(false);
                    if (Manager.StatusVE.State != ExInStates.Failed) 
                        return; // ⭐ FIJO: Salida en éxito
                }
            }
            
            // ⭐ NUEVO: Log después de agotar reintentos
            _logger.LogError($"[ExIn Recovery] Abandonado después de {MaxRetriesVE} intentos fallidos");
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_recoveryGate)
            {
                if (ReferenceEquals(_recovery, recovery)) _recovery = null;
                recovery.Dispose();
            }
            // A worker may fail just as StartAsync returns while this recovery exists.
            Manager_StatusChanged(this, Manager.StatusVE);
        }
    }

    private void CancelRecovery()
    {
        lock (_recoveryGate)
        {
            var recovery = _recovery;
            _recovery = null;
            if (recovery is null) return;
            try { recovery.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    private CancellationTokenSource EnabledOperation(CancellationToken caller, out long generation)
    {
        lock (_enableGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            generation = _enableGeneration;
            return CancellationTokenSource.CreateLinkedTokenSource(caller, _enabledOperations.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_enableGate)
        {
            if (_disposed) return;
            _disposed = true;
            _enabled = false;
            _enabledOperations.Cancel();
        }
        CancelRecovery();
        try { await _recoveryTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            Manager.StatusChangedVE -= Manager_StatusChanged;
            Manager.OutputIssueVE -= Manager_OutputIssue;
            await Manager.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
            // Queued callers must acquire, recheck _disposed, and release safely.
            lock (_enableGate) _enabledOperations.Dispose();
        }
    }
}
