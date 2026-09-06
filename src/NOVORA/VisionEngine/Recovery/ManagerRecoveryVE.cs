namespace NOVORA.VisionEngine.Recovery;

public sealed class ManagerRecoveryVE : IAsyncDisposable
{
    private readonly PolicyRecoveryVE _policyVE;
    private readonly Func<ScopeRecoveryVE, int, CancellationToken, Task<ResultRecoveryVE>> _recoverVE;
    private readonly SemaphoreSlim _gateVE = new(1, 1);
    private CancellationTokenSource? _runCtsVE;
    private Task? _runTaskVE;
    private int _attemptsVE;
    private int _consecutiveFailuresVE;
    private StatesRecoveryVE _stateVE = StatesRecoveryVE.Stopped;
    private bool _disposedVE;

    public ManagerRecoveryVE(
        PolicyRecoveryVE policy,
        Func<ScopeRecoveryVE, int, CancellationToken, Task<ResultRecoveryVE>> recover)
    {
        _policyVE = policy ?? throw new ArgumentNullException(nameof(policy));
        _policyVE.ValidateVE();
        _recoverVE = recover ?? throw new ArgumentNullException(nameof(recover));
    }

    public event EventHandler<StatesRecoveryVE>? StateChangedVE;
    public event EventHandler<ResultRecoveryVE>? RecoveryCompletedVE;

    public StatesRecoveryVE StateVE => _stateVE;
    public int AttemptsVE => Volatile.Read(ref _attemptsVE);

    public Task StartAsync(
        Func<HealthRecoveryVE> healthProvider,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposedVE, this);
        ArgumentNullException.ThrowIfNull(healthProvider);
        cancellationToken.ThrowIfCancellationRequested();

        if (_runTaskVE is not null)
            throw new InvalidOperationException("RecoveryVE ya está activo.");

        _runCtsVE = new CancellationTokenSource();
        PublishStateVE(StatesRecoveryVE.Monitoring);
        _runTaskVE = RunAsync(healthProvider, _runCtsVE.Token);
        return Task.CompletedTask;
    }

    public async Task<ResultRecoveryVE?> EvaluateOnceAsync(
        HealthRecoveryVE health,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposedVE, this);
        ArgumentNullException.ThrowIfNull(health);

        if (health.IsHealthy)
        {
            Interlocked.Exchange(ref _consecutiveFailuresVE, 0);
            PublishStateVE(StatesRecoveryVE.Monitoring);
            return null;
        }

        PublishStateVE(StatesRecoveryVE.Degraded);
        int failures = Interlocked.Increment(ref _consecutiveFailuresVE);
        if (failures < _policyVE.ConsecutiveFailuresBeforeRecovery)
            return null;

        Interlocked.Exchange(ref _consecutiveFailuresVE, 0);
        return await RecoverAsync(health.SuggestedScope, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts = _runCtsVE;
        Task? task = _runTaskVE;
        _runCtsVE = null;
        _runTaskVE = null;

        if (cts is not null)
        {
            cts.Cancel();
            try { if (task is not null) await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            cts.Dispose();
        }

        PublishStateVE(StatesRecoveryVE.Stopped);
    }

    private async Task RunAsync(Func<HealthRecoveryVE> healthProvider, CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(_policyVE.SampleInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await EvaluateOnceAsync(healthProvider(), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            PublishStateVE(StatesRecoveryVE.Failed);
        }
    }

    private async Task<ResultRecoveryVE> RecoverAsync(ScopeRecoveryVE scope, CancellationToken cancellationToken)
    {
        await _gateVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int attempt = Interlocked.Increment(ref _attemptsVE);
            if (attempt > _policyVE.MaxRecoveryAttempts)
            {
                ResultRecoveryVE exhausted = ResultRecoveryVE.FailVE(
                    scope, attempt, TimeSpan.Zero, "RecoveryVE agotó el máximo de intentos.");
                PublishStateVE(StatesRecoveryVE.Failed);
                RecoveryCompletedVE?.Invoke(this, exhausted);
                return exhausted;
            }

            PublishStateVE(StatesRecoveryVE.Recovering);
            ResultRecoveryVE result = await _recoverVE(scope, attempt, cancellationToken).ConfigureAwait(false);
            RecoveryCompletedVE?.Invoke(this, result);

            if (!result.Success)
            {
                PublishStateVE(StatesRecoveryVE.Degraded);
                return result;
            }

            PublishStateVE(StatesRecoveryVE.Cooldown);
            if (_policyVE.RecoveryCooldown > TimeSpan.Zero)
                await Task.Delay(_policyVE.RecoveryCooldown, cancellationToken).ConfigureAwait(false);

            PublishStateVE(StatesRecoveryVE.Monitoring);
            return result;
        }
        finally
        {
            _gateVE.Release();
        }
    }

    private void PublishStateVE(StatesRecoveryVE state)
    {
        _stateVE = state;
        StateChangedVE?.Invoke(this, state);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposedVE) return;
        await StopAsync().ConfigureAwait(false);
        _disposedVE = true;
        _gateVE.Dispose();
    }
}
