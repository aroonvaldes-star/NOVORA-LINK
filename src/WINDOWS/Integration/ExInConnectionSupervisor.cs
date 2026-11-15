namespace NOVORA.Integration;

/// <summary>Retries an enabled connection and observes readiness until explicitly stopped.</summary>
internal sealed class ExInConnectionSupervisor(
    Func<CancellationToken, Task> ensureConnected,
    Func<bool> shouldRun,
    TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private Task _task = Task.CompletedTask;
    private bool _restartRequested;

    public void Start()
    {
        lock (_gate)
        {
            if (!shouldRun()) return;
            if (!_task.IsCompleted)
            {
                _restartRequested = true;
                return;
            }
            _cancellation?.Dispose();
            _cancellation = new();
            CancellationToken token = _cancellation.Token;
            _restartRequested = false;
            _task = Task.Run(() => RunAsync(token), CancellationToken.None);
        }
    }

    public async Task StopAsync()
    {
        Task task;
        lock (_gate)
        {
            _restartRequested = false;
            _cancellation?.Cancel();
            task = _task;
        }
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        lock (_gate)
        {
            if (ReferenceEquals(task, _task))
            {
                _cancellation?.Dispose();
                _cancellation = null;
            }
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            int failures = 0;
            while (!token.IsCancellationRequested && shouldRun())
            {
                try
                {
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
                    attempt.CancelAfter(TimeSpan.FromSeconds(20));
                    await ensureConnected(attempt.Token).ConfigureAwait(false);
                    failures = 0;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failures = Math.Min(4, failures + 1);
                    System.Diagnostics.Debug.WriteLine("ExIn automatic connection retry: " + ex.Message);
                }
                if (!shouldRun()) break;
                int delaySeconds = failures == 0 ? 1 : 1 << (failures - 1);
                try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), timeProvider, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (!token.IsCancellationRequested && _restartRequested && shouldRun())
                {
                    _restartRequested = false;
                    _task = Task.Run(() => RunAsync(token), CancellationToken.None);
                }
            }
        }
    }
}
