using NOVORA.VisionEngine.Events;
using System.Threading.Channels;

namespace NOVORA.VisionEngine.Recovery;

/// <summary>
/// Recovery event-driven. No usa PeriodicTimer ni sondeos periódicos.
/// Los Task.Delay restantes representan deadlines, confirmación o cooldown.
/// </summary>
public sealed class VERecoveryManager : IAsyncDisposable
{
    private readonly VERecoveryPolicy _policyVE;
    private readonly Func<VERecoveryScope, int, CancellationToken, Task<VERecoveryResult>> _recoverVE;
    private readonly SemaphoreSlim _gateVE = new(1, 1);
    private readonly object _lifecycleGateVE = new();

    private CancellationTokenSource? _runCtsVE;
    private CancellationTokenSource? _videoDeadlineCtsVE;
    private CancellationTokenSource? _audioDeadlineCtsVE;
    private CancellationTokenSource? _failureConfirmationCtsVE;
    private Channel<byte>? _signalChannelVE;
    private VEEventsEventCore? _eventsVE;
    private Func<VERecoveryHealth>? _healthProviderVE;
    private Task? _runTaskVE;
    private int _attemptsVE;
    private int _consecutiveFailuresVE;
    private VERecoveryStates _stateVE = VERecoveryStates.Stopped;
    private bool _disposedVE;

    public VERecoveryManager(
        VERecoveryPolicy policy,
        Func<VERecoveryScope, int, CancellationToken, Task<VERecoveryResult>> recover)
    {
        _policyVE = policy ?? throw new ArgumentNullException(nameof(policy));
        _policyVE.ValidateVE();
        _recoverVE = recover ?? throw new ArgumentNullException(nameof(recover));
    }

    public event EventHandler<VERecoveryStates>? StateChangedVE;
    public event EventHandler<VERecoveryResult>? RecoveryCompletedVE;

    public VERecoveryStates StateVE => _stateVE;
    public int AttemptsVE => Volatile.Read(ref _attemptsVE);

    public Task StartAsync(
        Func<VERecoveryHealth> healthProvider,
        VEEventsEventCore events,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposedVE, this);
        ArgumentNullException.ThrowIfNull(healthProvider);
        ArgumentNullException.ThrowIfNull(events);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lifecycleGateVE)
        {
            if (_runTaskVE is not null)
                throw new InvalidOperationException("RecoveryVE ya está activo.");

            _healthProviderVE = healthProvider;
            _eventsVE = events;
            _signalChannelVE = Channel.CreateBounded<byte>(
                new BoundedChannelOptions(1)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.DropOldest,
                    AllowSynchronousContinuations = false
                });

            _runCtsVE = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _eventsVE.DispatcherVE.EventRaisedVE += Events_EventRaisedVE;
            PublishStateVE(VERecoveryStates.Monitoring);
            _runTaskVE = RunEventLoopAsync(_signalChannelVE.Reader, _runCtsVE.Token);
        }

        TrySignalVE();
        return Task.CompletedTask;
    }

    public async Task<VERecoveryResult?> EvaluateOnceAsync(
        VERecoveryHealth health,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposedVE, this);
        ArgumentNullException.ThrowIfNull(health);

        if (health.IsHealthy)
        {
            Interlocked.Exchange(ref _consecutiveFailuresVE, 0);
            CancelFailureConfirmationVE();
            PublishStateVE(VERecoveryStates.Monitoring);
            return null;
        }

        PublishStateVE(VERecoveryStates.Degraded);
        int failures = Interlocked.Increment(ref _consecutiveFailuresVE);

        if (failures < _policyVE.ConsecutiveFailuresBeforeRecovery)
        {
            ScheduleFailureConfirmationVE();
            return null;
        }

        CancelFailureConfirmationVE();
        Interlocked.Exchange(ref _consecutiveFailuresVE, 0);
        return await RecoverAsync(health.SuggestedScope, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        VEEventsEventCore? events;
        CancellationTokenSource? runCts;
        Channel<byte>? channel;
        Task? runTask;

        lock (_lifecycleGateVE)
        {
            events = _eventsVE;
            runCts = _runCtsVE;
            channel = _signalChannelVE;
            runTask = _runTaskVE;

            _eventsVE = null;
            _runCtsVE = null;
            _signalChannelVE = null;
            _runTaskVE = null;
            _healthProviderVE = null;
        }

        if (events is not null)
            events.DispatcherVE.EventRaisedVE -= Events_EventRaisedVE;

        CancelVideoDeadlineVE();
        CancelAudioDeadlineVE();
        CancelFailureConfirmationVE();

        if (runCts is not null)
        {
            try { runCts.Cancel(); } catch { }
        }

        channel?.Writer.TryComplete();

        if (runTask is not null)
        {
            try { await runTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        runCts?.Dispose();
        Interlocked.Exchange(ref _consecutiveFailuresVE, 0);
        PublishStateVE(VERecoveryStates.Stopped);
    }

    private void Events_EventRaisedVE(object? sender, VEEventsMessageEvent message)
    {
        if (_disposedVE) return;

        switch (message.Type)
        {
            case VEEventsTypeEvent.VideoStatus:
                ScheduleVideoDeadlineVE();
                TrySignalVE();
                break;

            case VEEventsTypeEvent.AudioStatus:
                ScheduleAudioDeadlineVE();
                TrySignalVE();
                break;

            case VEEventsTypeEvent.ControlStatus:
            case VEEventsTypeEvent.TransportState:
                TrySignalVE();
                break;
        }
    }

    private async Task RunEventLoopAsync(ChannelReader<byte> reader, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out _)) { }

                Func<VERecoveryHealth>? provider;
                lock (_lifecycleGateVE) provider = _healthProviderVE;
                if (provider is null) continue;

                VERecoveryHealth health;
                try
                {
                    health = provider();
                }
                catch (Exception ex)
                {
                    PublishStateVE(VERecoveryStates.Failed);
                    RecoveryCompletedVE?.Invoke(
                        this,
                        VERecoveryResult.FailVE(
                            VERecoveryScope.Session,
                            AttemptsVE,
                            TimeSpan.Zero,
                            "RecoveryVE no pudo obtener el estado de salud.",
                            ex));
                    continue;
                }

                await EvaluateOnceAsync(health, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            PublishStateVE(VERecoveryStates.Failed);
        }
    }

    private void TrySignalVE()
    {
        Channel<byte>? channel;
        CancellationTokenSource? cts;
        lock (_lifecycleGateVE)
        {
            channel = _signalChannelVE;
            cts = _runCtsVE;
        }

        if (channel is null || cts is null || cts.IsCancellationRequested)
            return;

        channel.Writer.TryWrite(1);
    }

    private void ScheduleVideoDeadlineVE()
        => ReplaceDeadlineVE(ref _videoDeadlineCtsVE, WaitVideoDeadlineAsync);

    private void ScheduleAudioDeadlineVE()
        => ReplaceDeadlineVE(ref _audioDeadlineCtsVE, WaitAudioDeadlineAsync);

    private void ReplaceDeadlineVE(
        ref CancellationTokenSource? field,
        Func<CancellationTokenSource, Task> waiter)
    {
        CancellationTokenSource? runCts;
        lock (_lifecycleGateVE) runCts = _runCtsVE;

        if (runCts is null || runCts.IsCancellationRequested || _policyVE.NoProgressTimeout <= TimeSpan.Zero)
            return;

        CancellationTokenSource next = CancellationTokenSource.CreateLinkedTokenSource(runCts.Token);
        CancellationTokenSource? previous = Interlocked.Exchange(ref field, next);
        CancelAndDisposeVE(previous);
        _ = waiter(next);
    }

    private async Task WaitVideoDeadlineAsync(CancellationTokenSource deadline)
    {
        try
        {
            await Task.Delay(_policyVE.NoProgressTimeout, deadline.Token).ConfigureAwait(false);
            TrySignalVE();
        }
        catch (OperationCanceledException) { }
    }

    private async Task WaitAudioDeadlineAsync(CancellationTokenSource deadline)
    {
        try
        {
            await Task.Delay(_policyVE.NoProgressTimeout, deadline.Token).ConfigureAwait(false);
            TrySignalVE();
        }
        catch (OperationCanceledException) { }
    }

    private void ScheduleFailureConfirmationVE()
    {
        CancellationTokenSource? runCts;
        lock (_lifecycleGateVE) runCts = _runCtsVE;
        if (runCts is null || runCts.IsCancellationRequested) return;

        CancellationTokenSource next = CancellationTokenSource.CreateLinkedTokenSource(runCts.Token);
        CancellationTokenSource? previous = Interlocked.Exchange(ref _failureConfirmationCtsVE, next);
        CancelAndDisposeVE(previous);
        _ = WaitFailureConfirmationAsync(next);
    }

    private async Task WaitFailureConfirmationAsync(CancellationTokenSource confirmation)
    {
        try
        {
            await Task.Delay(_policyVE.SampleInterval, confirmation.Token).ConfigureAwait(false);
            TrySignalVE();
        }
        catch (OperationCanceledException) { }
    }

    private void CancelVideoDeadlineVE()
        => CancelAndDisposeVE(Interlocked.Exchange(ref _videoDeadlineCtsVE, null));

    private void CancelAudioDeadlineVE()
        => CancelAndDisposeVE(Interlocked.Exchange(ref _audioDeadlineCtsVE, null));

    private void CancelFailureConfirmationVE()
        => CancelAndDisposeVE(Interlocked.Exchange(ref _failureConfirmationCtsVE, null));

    private static void CancelAndDisposeVE(CancellationTokenSource? source)
    {
        if (source is null) return;
        try { source.Cancel(); } catch { }
        source.Dispose();
    }

    private async Task<VERecoveryResult> RecoverAsync(VERecoveryScope scope, CancellationToken cancellationToken)
    {
        await _gateVE.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int attempt = Interlocked.Increment(ref _attemptsVE);
            if (attempt > _policyVE.MaxRecoveryAttempts)
            {
                VERecoveryResult exhausted = VERecoveryResult.FailVE(
                    scope,
                    attempt,
                    TimeSpan.Zero,
                    "RecoveryVE agotó el máximo de intentos.");
                PublishStateVE(VERecoveryStates.Failed);
                RecoveryCompletedVE?.Invoke(this, exhausted);
                return exhausted;
            }

            PublishStateVE(VERecoveryStates.Recovering);
            VERecoveryResult result = await _recoverVE(scope, attempt, cancellationToken).ConfigureAwait(false);
            RecoveryCompletedVE?.Invoke(this, result);

            if (!result.Success)
            {
                PublishStateVE(VERecoveryStates.Degraded);
                ScheduleFailureConfirmationVE();
                return result;
            }

            PublishStateVE(VERecoveryStates.Cooldown);
            if (_policyVE.RecoveryCooldown > TimeSpan.Zero)
                await Task.Delay(_policyVE.RecoveryCooldown, cancellationToken).ConfigureAwait(false);

            PublishStateVE(VERecoveryStates.Monitoring);
            return result;
        }
        finally
        {
            _gateVE.Release();
        }
    }

    private void PublishStateVE(VERecoveryStates state)
    {
        if (_stateVE == state) return;
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
