using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;

namespace NOVORA.Control;

/// <summary>
/// Limits concurrent handshakes and repeated failures per LAN peer, and optionally
/// rate-limits discovery packets. All state is bounded so an unauthenticated host
/// cannot grow the server indefinitely.
/// </summary>
internal sealed class NLControlLanPeerGuard
{
    private sealed class PeerState
    {
        public int Active;
        public int Failures;
        public long BlockedUntil;
        public long LastSeen;
        public double PacketTokens;
        public long PacketTimestamp;
    }

    private readonly ConcurrentDictionary<IPAddress, PeerState> _peers = new();
    private readonly int _maxConcurrentPerPeer;
    private readonly int _maxFailures;
    private readonly long _blockDurationMs;
    private readonly int _maxPeers;
    private readonly double _packetBurst;
    private readonly double _packetRefillPerSecond;

    public NLControlLanPeerGuard(
        int maxConcurrentPerPeer = 4,
        int maxFailures = 5,
        TimeSpan? blockDuration = null,
        int maxPeers = 1024,
        double packetBurst = 0,
        double packetRefillPerSecond = 0)
    {
        if (maxConcurrentPerPeer < 1) throw new ArgumentOutOfRangeException(nameof(maxConcurrentPerPeer));
        if (maxFailures < 0) throw new ArgumentOutOfRangeException(nameof(maxFailures));
        if (maxPeers < 1) throw new ArgumentOutOfRangeException(nameof(maxPeers));
        if (packetBurst < 0 || packetRefillPerSecond < 0) throw new ArgumentOutOfRangeException(nameof(packetBurst));
        _maxConcurrentPerPeer = maxConcurrentPerPeer;
        _maxFailures = maxFailures;
        _blockDurationMs = (long)(blockDuration ?? TimeSpan.FromSeconds(30)).TotalMilliseconds;
        _maxPeers = maxPeers;
        _packetBurst = packetBurst;
        _packetRefillPerSecond = packetRefillPerSecond;
    }

    /// <summary>Reserves one concurrent handshake slot for a peer.</summary>
    public bool TryEnter(IPAddress address, out NLControlLanPeerLease? lease)
    {
        lease = null;
        PeerState? state = GetState(address);
        if (state is null) return false;
        long now = Environment.TickCount64;
        lock (state)
        {
            if (state.BlockedUntil > now || state.Active >= _maxConcurrentPerPeer) return false;
            state.Active++;
            state.LastSeen = now;
        }
        lease = new(() => Exit(state));
        return true;
    }

    /// <summary>Consumes one packet token; disabled when the guard has no packet limit.</summary>
    public bool TryAcquirePacket(IPAddress address)
    {
        if (_packetBurst <= 0) return true;
        PeerState? state = GetState(address);
        if (state is null) return false;
        long now = Stopwatch.GetTimestamp();
        lock (state)
        {
            if (state.PacketTimestamp == 0)
            {
                state.PacketTokens = _packetBurst;
                state.PacketTimestamp = now;
            }
            double elapsed = (now - state.PacketTimestamp) / (double)Stopwatch.Frequency;
            state.PacketTokens = Math.Min(_packetBurst, state.PacketTokens + elapsed * _packetRefillPerSecond);
            state.PacketTimestamp = now;
            state.LastSeen = Environment.TickCount64;
            if (state.PacketTokens < 1) return false;
            state.PacketTokens -= 1;
            return true;
        }
    }

    public void ReportFailure(IPAddress address)
    {
        if (_maxFailures <= 0) return;
        PeerState? state = GetState(address);
        if (state is null) return;
        lock (state)
        {
            state.Failures++;
            if (state.Failures < _maxFailures) return;
            state.Failures = 0;
            state.BlockedUntil = Environment.TickCount64 + _blockDurationMs;
            state.LastSeen = Environment.TickCount64;
        }
    }

    public void ReportSuccess(IPAddress address)
    {
        PeerState? state = GetState(address);
        if (state is null) return;
        lock (state)
        {
            state.Failures = 0;
            state.BlockedUntil = 0;
            state.LastSeen = Environment.TickCount64;
        }
    }

    private PeerState? GetState(IPAddress address)
    {
        IPAddress key = Normalize(address);
        if (_peers.TryGetValue(key, out PeerState? existing)) return existing;
        if (_peers.Count >= _maxPeers)
        {
            PruneExpired();
            if (_peers.Count >= _maxPeers) return null;
        }
        return _peers.GetOrAdd(key, _ => new PeerState
        {
            LastSeen = Environment.TickCount64,
            PacketTokens = _packetBurst,
            PacketTimestamp = Stopwatch.GetTimestamp()
        });
    }

    private void PruneExpired()
    {
        long now = Environment.TickCount64;
        foreach (KeyValuePair<IPAddress, PeerState> pair in _peers)
        {
            PeerState state = pair.Value;
            lock (state)
            {
                if (state.Active != 0 || state.BlockedUntil > now ||
                    now - state.LastSeen < TimeSpan.FromMinutes(5).TotalMilliseconds)
                    continue;
                _peers.TryRemove(pair.Key, out _);
            }
        }
    }

    private static void Exit(PeerState state)
    {
        lock (state)
        {
            if (state.Active > 0) state.Active--;
            state.LastSeen = Environment.TickCount64;
        }
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}

internal sealed class NLControlLanPeerLease : IDisposable
{
    private readonly Action _release;
    private int _disposed;

    public NLControlLanPeerLease(Action release)
    {
        _release = release ?? throw new ArgumentNullException(nameof(release));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _release();
    }
}
