using System;
using System.Collections.Generic;
using System.Linq;
using NOVORA.Control;

namespace NOVORA.Network;

/// <summary>Class allocations for a local, explicitly identified route. Rates are not capacity measurements.</summary>
public sealed class NLNetworkBudget
{
    public const long MaximumBytesPerSecond = 1_250_000_000;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<(string, NLNetworkDomain), NLNetworkPolicy> _policies = new();
    private NLNetworkPolicy? _current;
    private readonly int _maxRoutes;

    public NLNetworkBudget(TimeProvider? timeProvider = null, int maxRoutes = 128)
    {
        _time = timeProvider ?? TimeProvider.System;
        if (maxRoutes < 1 || maxRoutes > 4096) throw new ArgumentOutOfRangeException(nameof(maxRoutes));
        _maxRoutes = maxRoutes;
    }

    public void Update(NLNetworkPolicy policy, long sampledAtTicks)
    {
        Validate(policy);
        lock (_gate)
        {
            var key = (policy.LinkKey, policy.Domain);
            if (_policies.TryGetValue(key, out var old))
            {
                if (policy.Generation < old.Generation || (policy.Generation == old.Generation && policy.Revision < old.Revision))
                    throw new InvalidOperationException("La política pertenece a una generación o revisión anterior.");
                if (policy.Generation == old.Generation && policy.Revision == old.Revision && policy != old)
                    throw new InvalidOperationException("Una revisión no puede cambiar de contenido.");
            }
            else if (_policies.Count >= _maxRoutes) throw new InvalidOperationException("Se alcanzó la cota de rutas de presupuesto.");
            _policies[key] = policy; _current = policy;
        }
    }

    public IReadOnlyList<NLNetworkGrant> Allocate(NLNetworkDirection direction,
        IReadOnlyDictionary<NLNetworkTrafficClass, long> demands, long nowTicks)
    {
        lock (_gate) return _current is null ? Array.Empty<NLNetworkGrant>() : Allocate(_current, direction, demands, nowTicks);
    }

    public IReadOnlyList<NLNetworkGrant> Allocate(NLNetworkPolicy policy, NLNetworkDirection direction,
        IReadOnlyDictionary<NLNetworkTrafficClass, long> demands, long nowTicks)
    {
        Validate(policy); ArgumentNullException.ThrowIfNull(demands);
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        foreach (var pair in demands)
            if (!Enum.IsDefined(pair.Key) || pair.Value < 0 || pair.Value > MaximumBytesPerSecond)
                throw new ArgumentOutOfRangeException(nameof(demands));
        lock (_gate)
        {
            if (_policies.TryGetValue((policy.LinkKey, policy.Domain), out var stored) &&
                (policy.Generation < stored.Generation || (policy.Generation == stored.Generation && policy.Revision < stored.Revision)))
                throw new InvalidOperationException("Asignación de una política anterior.");
        }
        long? limit = direction == NLNetworkDirection.FromAndroid ? policy.FromAndroidBytesPerSecond : policy.ToAndroidBytesPerSecond;
        if (policy.Mode is NLNetworkPolicyMode.Disabled or NLNetworkPolicyMode.Observe || limit is null)
            return Array.Empty<NLNetworkGrant>();
        var classes = Enum.GetValues<NLNetworkTrafficClass>();
        var demand = classes.Select(c => demands.TryGetValue(c, out long value) ? value : 0).ToArray();
        var allocated = new long[classes.Length];
        long remaining = limit.Value;
        // Explicit Audio is essential. Above 20% it may consume more; the caller reports insufficient capacity.
        allocated[(int)NLNetworkTrafficClass.Audio] = Math.Min(demand[(int)NLNetworkTrafficClass.Audio], remaining);
        remaining -= allocated[(int)NLNetworkTrafficClass.Audio];
        long preferred = Math.Max(0, limit.Value / 5 - allocated[(int)NLNetworkTrafficClass.Audio]);
        allocated[(int)NLNetworkTrafficClass.Interactive] = Math.Min(demand[(int)NLNetworkTrafficClass.Interactive], Math.Min(preferred, remaining));
        remaining -= allocated[(int)NLNetworkTrafficClass.Interactive];
        // Max-min sharing after the bounded preference; every demanded nonessential class can progress.
        while (remaining > 0)
        {
            var active = Enumerable.Range(0, classes.Length).Where(i => allocated[i] < demand[i]).ToArray();
            if (active.Length == 0) break;
            long share = Math.Max(1, remaining / active.Length);
            foreach (int i in active)
            {
                long take = Math.Min(remaining, Math.Min(share, demand[i] - allocated[i]));
                allocated[i] += take; remaining -= take;
                if (remaining == 0) break;
            }
        }
        long expiry = NLNetworkTime.AddSeconds(_time, nowTicks, 3);
        return classes.Where(c => allocated[(int)c] > 0).Select(c =>
        {
            long rate = allocated[(int)c];
            int burst = (int)Math.Clamp(rate / 200, 1500, 65536);
            return new NLNetworkGrant(policy.Revision, policy.Generation, policy.LinkKey, policy.Domain,
                direction, c, rate, burst, expiry);
        }).ToArray();
    }

    internal static void Validate(NLNetworkPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (string.IsNullOrWhiteSpace(policy.LinkKey) || policy.LinkKey.Length > 256 || policy.Generation < 0 || policy.Revision < 0 ||
            !Enum.IsDefined(policy.Domain) || !Enum.IsDefined(policy.Mode)) throw new ArgumentException("Política inválida.", nameof(policy));
        foreach (var value in new[] { policy.FromAndroidBytesPerSecond, policy.ToAndroidBytesPerSecond })
            if (value.HasValue && (value <= 0 || value > MaximumBytesPerSecond)) throw new ArgumentOutOfRangeException(nameof(policy));
    }
}

internal static class NLNetworkTime
{
    public static double Seconds(TimeProvider time, long start, long end) => time.GetElapsedTime(start, end).TotalSeconds;
    public static long AddSeconds(TimeProvider time, long ticks, double seconds)
    {
        double value = ticks + Math.Ceiling(seconds * time.TimestampFrequency);
        return value >= long.MaxValue ? long.MaxValue : (long)value;
    }
}
