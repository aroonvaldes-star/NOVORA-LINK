using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.Control;

public enum NLFileTransferDirectionV2 { PcToAndroid, AndroidToPc }
public sealed record NLFileLease(Guid TransferId, long Generation, NLFileTransferDirectionV2 Direction);

/// <summary>PC-side FIFO arbitration. Call ConfirmBegin before starting a granted transfer;
/// only a received terminal frame or channel closure releases an active transfer.</summary>
public sealed class NLControlFileTransferLease : IDisposable
{
    private sealed class Request(NLFileLease lease)
    {
        public readonly NLFileLease Lease = lease;
        public readonly TaskCompletionSource<NLFileLease> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Cancellation;
        public bool Begun;
    }
    private readonly object _gate = new();
    private readonly LinkedList<Request> _queue = new();
    private readonly Dictionary<Guid, Request> _requests = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _beginTimeout;
    private Request? _active;
    private ITimer? _expiry;
    private long _generation;
    private bool _closed;

    public NLControlFileTransferLease(long generation, TimeProvider? clock = null, TimeSpan? beginTimeout = null)
    {
        if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
        _generation = generation; _clock = clock ?? TimeProvider.System;
        _beginTimeout = beginTimeout ?? TimeSpan.FromSeconds(30);
        if (_beginTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(beginTimeout));
    }

    public NLFileLease? Active { get { lock (_gate) return _active?.Lease; } }

    public Task<NLFileLease> AcquireAsync(Guid id, long generation, NLFileTransferDirectionV2 direction, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_closed || generation != _generation) return Task.FromException<NLFileLease>(new IOException("FILES lease generation is closed or stale."));
            if (id == Guid.Empty || !Enum.IsDefined(direction)) return Task.FromException<NLFileLease>(new InvalidDataException("Invalid FILES lease request."));
            if (ct.IsCancellationRequested) return Task.FromCanceled<NLFileLease>(ct);
            if (_requests.TryGetValue(id, out var existing))
            {
                if (existing.Lease.Direction != direction) return Task.FromException<NLFileLease>(new InvalidDataException("Conflicting FILES lease request."));
                return existing.Ready.Task;
            }
            if (_requests.Count >= 4096 || _queue.Count >= 256)
                return Task.FromException<NLFileLease>(new IOException("FILES lease request bound reached; open a new channel."));
            var request = new Request(new(id, generation, direction));
            _requests.Add(id, request); _queue.AddLast(request);
            if (ct.CanBeCanceled) request.Cancellation = ct.Register(() => CancelPending(request, ct));
            GrantNext();
            return request.Ready.Task;
        }
    }

    public bool IsCurrent(NLFileLease lease) { lock (_gate) return !_closed && _active?.Lease == lease; }

    public void ConfirmBegin(NLFileLease lease)
    {
        lock (_gate)
        {
            if (_closed || _active?.Lease != lease) throw new IOException("FILES lease is no longer current.");
            _active.Begun = true; _expiry?.Dispose(); _expiry = null;
        }
    }

    public bool NotifyTerminal(NLFileLease lease, NLFileFrameKindV2 receivedKind)
    {
        lock (_gate)
        {
            if (receivedKind is not (NLFileFrameKindV2.Complete or NLFileFrameKindV2.Cancelled or NLFileFrameKindV2.Error)) return false;
            if (_closed || _active?.Lease != lease) return false;
            _expiry?.Dispose(); _expiry = null;
            _active = null;
            GrantNext();
            return true;
        }
    }

    public bool CancelBeforeBegin(Guid transferId, long generation)
    {
        lock (_gate)
        {
            if (_closed || generation != _generation || !_requests.TryGetValue(transferId, out var request)) return false;
            if (_active == request)
            {
                if (request.Begun) return false;
                _expiry?.Dispose(); _expiry = null; _active = null; GrantNext(); return true;
            }
            if (!_queue.Remove(request)) return false;
            request.Cancellation.Unregister(); request.Ready.TrySetException(new IOException("FILES pending lease cancelled."));
            return true;
        }
    }

    /// <summary>All requests of this channel generation fail; opening a new channel requires a new coordinator.</summary>
    public void CloseGeneration(long generation, Exception? reason = null)
    {
        lock (_gate)
        {
            if (generation != _generation || _closed) return;
            _closed = true; _expiry?.Dispose(); _expiry = null;
            foreach (var request in _queue)
            {
                request.Cancellation.Unregister();
                request.Ready.TrySetException(reason ?? new IOException("FILES channel generation closed."));
            }
            _queue.Clear(); _active = null; _requests.Clear();
        }
    }

    private void GrantNext()
    {
        if (_active is not null || _closed || _queue.First is null) return;
        var request = _queue.First.Value; _queue.RemoveFirst();
        request.Cancellation.Unregister(); _active = request;
        _expiry = _clock.CreateTimer(_ => ExpireBeforeBegin(request), null, _beginTimeout, Timeout.InfiniteTimeSpan);
        request.Ready.TrySetResult(request.Lease);
    }

    private void ExpireBeforeBegin(Request request)
    {
        lock (_gate)
        {
            if (_active != request || request.Begun || _closed) return;
            _active = null; _expiry?.Dispose(); _expiry = null;
            GrantNext();
        }
    }

    private void CancelPending(Request request, CancellationToken ct)
    {
        lock (_gate)
        {
            // Cancellation of a granted/active request is handled by the FILES Cancel/Cancelled exchange.
            if (request == _active || !_queue.Remove(request)) return;
            _requests.Remove(request.Lease.TransferId); request.Ready.TrySetCanceled(ct);
        }
    }

    public void Dispose() => CloseGeneration(_generation);
}
