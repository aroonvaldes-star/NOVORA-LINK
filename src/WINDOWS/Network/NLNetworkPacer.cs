using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.Control;

namespace NOVORA.Network;

/// <summary>Shared class token buckets. The caller waits before starting a protocol response deadline.</summary>
public sealed class NLNetworkPacer : IDisposable
{
    public const int MaximumRequestBytes = 1024 * 1024;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<(string, NLNetworkDomain, NLNetworkDirection), Scope> _scopes = new();
    private readonly int _maxScopes, _maxPending;
    private int _pending;
    private bool _disposed;
    private sealed class Scope(long generation, long revision)
    {
        public long Generation = generation, Revision = revision;
        public readonly CancellationTokenSource Cancel = new();
        public readonly Dictionary<NLNetworkTrafficClass, Bucket> Buckets = new();
    }
    private sealed class Bucket(long rate, int burst, long now, long revision)
    {
        public long Rate = rate, Updated = now, Revision = revision;
        public int Burst = burst;
        public double Balance = burst;
    }

    public NLNetworkPacer(TimeProvider? timeProvider = null, int maxScopes = 128, int maxPendingWaiters = 256)
    {
        _time = timeProvider ?? TimeProvider.System;
        if (maxScopes < 1 || maxScopes > 4096) throw new ArgumentOutOfRangeException(nameof(maxScopes));
        if (maxPendingWaiters < 1 || maxPendingWaiters > 4096) throw new ArgumentOutOfRangeException(nameof(maxPendingWaiters));
        _maxScopes = maxScopes; _maxPending = maxPendingWaiters;
    }

    public async ValueTask WaitAsync(NLNetworkGrant grant, long bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant); cancellationToken.ThrowIfCancellationRequested();
        if (bytes < 0 || bytes > MaximumRequestBytes) throw new ArgumentOutOfRangeException(nameof(bytes));
        if (string.IsNullOrWhiteSpace(grant.LinkKey) || grant.LinkKey.Length > 256 || grant.Generation < 0 || grant.Revision < 0 ||
            !Enum.IsDefined(grant.Domain) || !Enum.IsDefined(grant.Direction) || !Enum.IsDefined(grant.TrafficClass) ||
            grant.BytesPerSecond <= 0 || grant.BytesPerSecond > NLNetworkBudget.MaximumBytesPerSecond || grant.BurstBytes < 1 || grant.BurstBytes > 65536)
            throw new ArgumentException("Asignación inválida.", nameof(grant));
        Scope scope; Bucket bucket; double delay; long now; CancellationToken scopeToken;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var key = (grant.LinkKey, grant.Domain, grant.Direction);
            if (_scopes.TryGetValue(key, out scope!))
            {
                if (grant.Generation < scope.Generation || (grant.Generation == scope.Generation && grant.Revision < scope.Revision))
                    throw new OperationCanceledException("Asignación reemplazada.", cancellationToken);
                if (grant.Generation > scope.Generation || grant.Revision > scope.Revision)
                {
                    // A new revision cancels old reservations and carries available credit without adding an idle burst.
                    var replacement = new Scope(grant.Generation, grant.Revision);
                    if (grant.Generation == scope.Generation)
                        foreach (var pair in scope.Buckets)
                        {
                            Refill(pair.Value, _time.GetTimestamp());
                            replacement.Buckets[pair.Key] = new Bucket(pair.Value.Rate, pair.Value.Burst, _time.GetTimestamp(), pair.Value.Revision) { Balance = Math.Max(0, pair.Value.Balance) };
                        }
                    scope.Cancel.Cancel(); scope.Cancel.Dispose(); _scopes[key] = replacement; scope = replacement;
                }
            }
            else
            {
                if (_scopes.Count >= _maxScopes) throw new InvalidOperationException("Se alcanzó la cota de rutas del pacer.");
                scope = new Scope(grant.Generation, grant.Revision); _scopes.Add(key, scope);
            }
            now = _time.GetTimestamp();
            if (grant.ValidUntilTicks <= now) throw new TimeoutException("La asignación ha caducado.");
            if (_pending >= _maxPending) throw new InvalidOperationException("Se alcanzó la cota de esperas del pacer.");
            if (!scope.Buckets.TryGetValue(grant.TrafficClass, out bucket!))
            { bucket = new Bucket(grant.BytesPerSecond, grant.BurstBytes, now, grant.Revision); scope.Buckets.Add(grant.TrafficClass, bucket); }
            else
            {
                Refill(bucket, now);
                if (bucket.Revision < grant.Revision)
                {
                    bucket.Rate = grant.BytesPerSecond; bucket.Burst = grant.BurstBytes;
                    bucket.Balance = Math.Min(bucket.Balance, grant.BurstBytes); bucket.Revision = grant.Revision;
                }
                else if (bucket.Rate != grant.BytesPerSecond || bucket.Burst != grant.BurstBytes)
                    throw new InvalidOperationException("La tasa requiere una revisión nueva.");
            }
            bucket.Balance -= bytes;
            delay = Math.Max(0, -bucket.Balance / bucket.Rate); _pending++; scopeToken = scope.Cancel.Token;
        }
        bool completed = false;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, scopeToken);
            double lifetime = NLNetworkTime.Seconds(_time, now, grant.ValidUntilTicks);
            if (delay > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(delay, lifetime)), _time, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                if (_time.GetTimestamp() >= grant.ValidUntilTicks) throw new TimeoutException("La asignación caducó durante el pacing.");
            }
            linked.Token.ThrowIfCancellationRequested(); completed = true;
        }
        finally
        {
            lock (_gate)
            {
                _pending--;
                if (!completed) { Refill(bucket, _time.GetTimestamp()); bucket.Balance = Math.Min(bucket.Burst, bucket.Balance + bytes); }
            }
        }
    }

    private void Refill(Bucket bucket, long now)
    {
        bucket.Balance = Math.Min(bucket.Burst, bucket.Balance + Math.Max(0, NLNetworkTime.Seconds(_time, bucket.Updated, now)) * bucket.Rate);
        bucket.Updated = now;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            foreach (var scope in _scopes.Values) { scope.Cancel.Cancel(); scope.Cancel.Dispose(); }
            _scopes.Clear();
        }
    }
}
