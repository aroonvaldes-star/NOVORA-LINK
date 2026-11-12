using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.Control;

public sealed record NLFileLeaseRequestV2(Guid TransferId, long Generation, NLFileTransferDirectionV2 Direction);
public sealed record NLFileLeaseGrantV2(bool Granted, Guid TransferId, long Generation, NLFileTransferDirectionV2 Direction);

/// <summary>PC-authoritative arbitration for local sends and authenticated Android acquire/cancel polls.</summary>
public sealed class NLControlFileLeaseCoordinatorV2 : IDisposable
{
    private sealed class Remote(Task<NLFileLease> grant, long polledAt)
    {
        public readonly Task<NLFileLease> Grant = grant;
        public long PolledAt = polledAt;
    }
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Remote> _remote = new();
    private readonly NLControlFileTransferLease _leases;
    private readonly TimeProvider _clock;
    private readonly ITimer _prune;
    private bool _closed;
    public long Generation { get; }
    public NLFileLease? Active => _leases.Active;

    public NLControlFileLeaseCoordinatorV2(long generation, TimeProvider? clock = null)
    {
        Generation = generation; _clock = clock ?? TimeProvider.System;
        _leases = new(generation, _clock);
        _prune = _clock.CreateTimer(_ => Prune(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public Task<NLFileLease> AcquireLocalAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate) { ThrowIfClosed(); PruneLocked(); return _leases.AcquireAsync(id, Generation, NLFileTransferDirectionV2.PcToAndroid, ct); }
    }

    /// <summary>Idempotent polling retains FIFO position. Only AndroidToPc requests are accepted remotely.</summary>
    public NLFileLeaseGrantV2 PollRemote(NLFileLeaseRequestV2 request)
    {
        lock (_gate)
        {
            ThrowIfClosed(); PruneLocked();
            if (request.TransferId == Guid.Empty || request.Generation != Generation || request.Direction != NLFileTransferDirectionV2.AndroidToPc)
                throw new InvalidDataException("FILES acquire request is stale or has an invalid direction.");
            if (!_remote.TryGetValue(request.TransferId, out var pending))
            {
                if (_remote.Count >= 128) throw new IOException("FILES pending remote request bound reached.");
                pending = new(_leases.AcquireAsync(request.TransferId, Generation, request.Direction), _clock.GetTimestamp());
                _remote.Add(request.TransferId, pending);
            }
            pending.PolledAt = _clock.GetTimestamp();
            if (!pending.Grant.IsCompleted) return new(false, request.TransferId, Generation, request.Direction);
            NLFileLease lease = pending.Grant.GetAwaiter().GetResult();
            if (!_leases.IsCurrent(lease)) throw new IOException("FILES lease has expired or already terminated.");
            return new(true, lease.TransferId, lease.Generation, lease.Direction);
        }
    }

    public bool CancelRemote(NLFileLeaseRequestV2 request)
    {
        lock (_gate)
        {
            if (_closed || request.Generation != Generation || request.Direction != NLFileTransferDirectionV2.AndroidToPc) return false;
            bool cancelled = _leases.CancelBeforeBegin(request.TransferId, request.Generation);
            if (cancelled) _remote.Remove(request.TransferId);
            return cancelled;
        }
    }

    public bool ConfirmIncomingBegin(Guid id, long generation)
    {
        lock (_gate)
        {
            if (_closed || generation != Generation || _leases.Active is not { } lease ||
                lease.TransferId != id || lease.Direction != NLFileTransferDirectionV2.AndroidToPc) return false;
            _leases.ConfirmBegin(lease); return true;
        }
    }

    public void ConfirmLocalBegin(Guid id)
    {
        lock (_gate)
        {
            ThrowIfClosed();
            if (_leases.Active is not { } lease || lease.TransferId != id || lease.Direction != NLFileTransferDirectionV2.PcToAndroid)
                throw new IOException("FILES local lease is no longer current.");
            _leases.ConfirmBegin(lease);
        }
    }

    public bool NotifyTerminal(Guid id, NLFileFrameKindV2 receivedOrSentKind)
    {
        lock (_gate)
        {
            if (_closed || _leases.Active is not { } lease || lease.TransferId != id) return false;
            bool released = _leases.NotifyTerminal(lease, receivedOrSentKind);
            if (released) _remote.Remove(id);
            return released;
        }
    }

    public bool CancelLocalBeforeBegin(Guid id) => _leases.CancelBeforeBegin(id, Generation);
    public void CloseGeneration(long generation, Exception? reason = null)
    {
        lock (_gate)
        {
            if (_closed || generation != Generation) return;
            _closed = true; _prune.Dispose(); _leases.CloseGeneration(generation, reason); _remote.Clear();
        }
    }
    private void Prune() { lock (_gate) { if (!_closed) PruneLocked(); } }
    private void PruneLocked()
    {
        foreach (var entry in new List<KeyValuePair<Guid, Remote>>(_remote))
        {
            if (_clock.GetElapsedTime(entry.Value.PolledAt) <= TimeSpan.FromSeconds(30)) continue;
            // Never preempt an active transfer. Only abandoned queued/before-Begin requests expire.
            if (_leases.CancelBeforeBegin(entry.Key, Generation)) _remote.Remove(entry.Key);
        }
    }
    private void ThrowIfClosed() { if (_closed) throw new IOException("FILES coordinator generation closed."); }
    public void Dispose() => CloseGeneration(Generation);
}
