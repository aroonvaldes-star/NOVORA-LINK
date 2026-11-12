using System;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.LinkEngine.Network;

/// <summary>One acknowledged IPC transaction at a time; lifecycle reset invalidates old replies.</summary>
public sealed class LERelayPolicySession
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _requests = new(1, 1);
    private TaskCompletionSource<bool> _ready = NewCompletion();
    private sealed record Pending(long Revision,long Generation,TaskCompletionSource<LERelayPolicyAcknowledgement?> Completion);
    private Pending? _pending;
    private long _epoch;
    public bool IsReady { get { lock (_sync) return _ready.Task.IsCompletedSuccessfully && _ready.Task.Result; } }
    private static TaskCompletionSource<bool> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Reset()
    {
        lock (_sync)
        {
            _epoch++;
            _ready.TrySetResult(false);
            _ready = NewCompletion();
            _pending?.Completion.TrySetResult(null);
            _pending = null;
        }
    }

    public bool HandleLine(string line)
    {
        if (line.StartsWith("LE_READY ", StringComparison.Ordinal))
        {
            if (LERelayPolicyProtocol.TryReadReady(line, out var ready))
                lock (_sync) _ready.TrySetResult(ready!.Shaping && ready.FairQueues && ready.QueueTelemetry);
            return true;
        }
        if (line.StartsWith("LE_CONFIG_ACK ", StringComparison.Ordinal))
        {
            if (LERelayPolicyProtocol.TryReadAcknowledgement(line, out var ack))
                lock (_sync) if(_pending is { } pending && pending.Revision==ack!.Revision && pending.Generation==ack.Generation)
                    pending.Completion.TrySetResult(ack);
            return true;
        }
        return false;
    }

    public async Task<bool> ApplyAsync(string configuration, Func<string, CancellationToken, Task> write,
        CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var identity=LERelayPolicyProtocol.GetConfigurationIdentity(configuration);
        await _requests.WaitAsync(cancellationToken).ConfigureAwait(false);
        var deadline = timeout ?? TimeSpan.FromSeconds(2);
        TaskCompletionSource<LERelayPolicyAcknowledgement?>? pending = null;
        try
        {
            Task<bool> readiness; long epoch;
            lock (_sync) { readiness = _ready.Task; epoch = _epoch; }
            if (!await readiness.WaitAsync(deadline, cancellationToken).ConfigureAwait(false)) return false;
            lock (_sync)
            {
                if (_epoch != epoch) return false;
                pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending = new(identity.Revision,identity.Generation,pending);
            }
            await write(configuration, cancellationToken).ConfigureAwait(false);
            var ack = await pending.Task.WaitAsync(deadline, cancellationToken).ConfigureAwait(false);
            return ack is not null && LERelayPolicyProtocol.AcknowledgementMatchesConfig(ack, configuration);
        }
        catch (TimeoutException) { return false; }
        finally
        {
            lock (_sync) if (ReferenceEquals(_pending?.Completion, pending)) _pending = null;
            _requests.Release();
        }
    }
}
