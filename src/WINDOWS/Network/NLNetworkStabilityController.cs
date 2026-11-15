using System;
using System.Collections.Generic;
using System.Linq;
using NOVORA.Control;

namespace NOVORA.Network;

/// <summary>Pure session decisions; never performs transport I/O or changes a user profile.</summary>
public sealed class NLNetworkStabilityController
{
    private readonly TimeProvider _time;
    private readonly NLNetworkBudget _budget;
    private readonly object _gate = new();
    private readonly Dictionary<(string, NLNetworkDomain), Route> _routes = new();
    private readonly int _maxRoutes;
    private sealed class Route(long generation)
    {
        public long Generation = generation;
        public readonly Dictionary<NLNetworkDirection, State> Directions = new();
    }
    private sealed class State
    {
        public long? Evaluated, LatestEvidence, LastBulkCut, ReducedAt, HealthySince, LastRecovery, LastVideoCut;
        public int BadWindows;
        public decimal BulkFactor = 1m;
        public double VideoFactor = 1;
        public bool Congested;
        public IReadOnlyList<NLNetworkGrant> LastGrants = Array.Empty<NLNetworkGrant>();
        public readonly Dictionary<NLNetworkSampleSource, List<NLNetworkSample>> Probes = new();
    }

    public NLNetworkStabilityController(TimeProvider? timeProvider = null, int maxRoutes = 128)
    {
        _time = timeProvider ?? TimeProvider.System; _budget = new NLNetworkBudget(_time, maxRoutes);
        if (maxRoutes < 1 || maxRoutes > 4096) throw new ArgumentOutOfRangeException(nameof(maxRoutes)); _maxRoutes = maxRoutes;
    }

    public NLNetworkDecision Evaluate(NLNetworkPolicy policy, NLNetworkBaseline? baseline,
        IReadOnlyList<NLNetworkSample> samples, IReadOnlyDictionary<NLNetworkTrafficClass, long> demands, long nowTicks,
        bool allowUnknownTrafficReduction = false)
    {
        NLNetworkBudget.Validate(policy); ArgumentNullException.ThrowIfNull(samples); ArgumentNullException.ThrowIfNull(demands);
        if (samples.Count > 4096) throw new ArgumentOutOfRangeException(nameof(samples), "Una ventana admite hasta 4096 muestras.");
        lock (_gate)
        {
            var key = (policy.LinkKey, policy.Domain);
            if (!_routes.TryGetValue(key, out var route))
            {
                if (_routes.Count >= _maxRoutes) throw new InvalidOperationException("Se alcanzó la cota de rutas del controlador.");
                route = new Route(policy.Generation); _routes.Add(key, route);
            }
            else if (policy.Generation < route.Generation)
                return new(policy with { Mode = NLNetworkPolicyMode.Observe }, Array.Empty<NLNetworkGrant>(), false, 1, "Generación anterior.");
            else if (policy.Generation > route.Generation) { route = new Route(policy.Generation); _routes[key] = route; }
            _budget.Update(policy, nowTicks);
            var grants = new List<NLNetworkGrant>(10); bool congested = false, observed = false; double videoFactor = 1;
            string reason = "Observación: sin capacidad configurada o telemetría suficiente.";
            foreach (var direction in Enum.GetValues<NLNetworkDirection>())
            {
                long? cap = direction == NLNetworkDirection.FromAndroid ? policy.FromAndroidBytesPerSecond : policy.ToAndroidBytesPerSecond;
                if (cap is null) continue;
                if (!route.Directions.TryGetValue(direction, out var state)) { state = new State(); route.Directions.Add(direction, state); }
                var nominal = _budget.Allocate(policy with { Mode = policy.Mode == NLNetworkPolicyMode.Adaptive ? NLNetworkPolicyMode.Manual : policy.Mode }, direction, demands, nowTicks);
                if (policy.Mode is NLNetworkPolicyMode.Disabled or NLNetworkPolicyMode.Observe)
                { state.LastGrants = nominal; continue; }
                if (policy.Mode == NLNetworkPolicyMode.Manual)
                { grants.AddRange(nominal); state.LastGrants = nominal; reason = "Límites manuales."; continue; }
                var current = samples.Where(s => Valid(s, policy, direction, nowTicks)).ToArray();
                bool ownPipeline = current.Any(s => s.Source == NLNetworkSampleSource.Pipeline && s.QueueAgeP95Ms.HasValue && s.WriteWaitP95Ms.HasValue);
                foreach (var source in state.Probes.Values) source.RemoveAll(s => NLNetworkTime.Seconds(_time, s.ObservedAtTicks, nowTicks) > 10);
                foreach (var sample in current.Where(s => s.RttMs.HasValue && s.Source != NLNetworkSampleSource.Pipeline))
                {
                    if (!state.Probes.TryGetValue(sample.Source, out var probes)) { probes = new List<NLNetworkSample>(); state.Probes.Add(sample.Source, probes); }
                    if (!probes.Any(p => p.ObservedAtTicks == sample.ObservedAtTicks)) probes.Add(sample);
                    probes.Sort((a,b) => a.ObservedAtTicks.CompareTo(b.ObservedAtTicks));
                    if (probes.Count > 256) probes.RemoveRange(0, probes.Count - 256);
                }
                bool baselineValid = baseline is not null && baseline.Generation == policy.Generation && baseline.LinkKey == policy.LinkKey &&
                    baseline.Domain == policy.Domain && baseline.QuietCalibrationConfirmed && baseline.ObservedAtTicks <= nowTicks &&
                    double.IsFinite(baseline.RttP95Ms) && baseline.RttP95Ms >= 0 && baseline.Source != NLNetworkSampleSource.Pipeline;
                List<NLNetworkSample>? rtt = null;
                bool enoughRtt = baselineValid && state.Probes.TryGetValue(baseline!.Source, out rtt) && rtt.Count >= 10 && current.Any(s => s.Source == baseline.Source && s.RttMs.HasValue);
                bool evidence = ownPipeline || enoughRtt;
                if (current.Length == 0 || !evidence)
                {
                    state.HealthySince = null; state.BadWindows = 0;
                    var frozen = state.LastGrants.Count == 0 ? Scale(nominal, state.BulkFactor, allowUnknownTrafficReduction && policy.Domain == NLNetworkDomain.Internet) : FreezeWithin(nominal, state.LastGrants);
                    state.LastGrants = frozen; grants.AddRange(frozen); videoFactor = Math.Min(videoFactor, state.VideoFactor);
                    reason = current.Length == 0 ? "Observación: telemetría caducada; límites conservados." : "Observación: falta baseline de la fuente o probes suficientes.";
                    continue;
                }
                observed = true;
                long latestEvidence = current.Max(s => s.ObservedAtTicks);
                if ((state.LatestEvidence is null || latestEvidence > state.LatestEvidence.Value) &&
                    (state.Evaluated is null || NLNetworkTime.Seconds(_time, state.Evaluated.Value, nowTicks) >= 1))
                {
                    state.Evaluated = nowTicks; state.LatestEvidence = latestEvidence;
                    bool queueBad = current.Any(s => s.Source == NLNetworkSampleSource.Pipeline && s.QueueAgeP95Ms > 40 && s.WriteWaitP95Ms >= 10);
                    bool rttBad = enoughRtt && P95(rtt!) > baseline!.RttP95Ms + 20;
                    bool bad = queueBad || rttBad;
                    if (bad)
                    {
                        state.HealthySince = null; state.BadWindows++; state.Congested = state.BadWindows >= 2;
                        if (state.Congested && (state.LastBulkCut is null || NLNetworkTime.Seconds(_time,state.LastBulkCut.Value,nowTicks) >= 2))
                        {
                            state.BulkFactor = Math.Max(0.01m, state.BulkFactor * .8m); state.LastBulkCut = nowTicks; state.ReducedAt ??= nowTicks;
                        }
                        if (policy.Domain == NLNetworkDomain.DeviceLink && queueBad && demands.TryGetValue(NLNetworkTrafficClass.Video,out var video) && video > 0 &&
                            state.ReducedAt is not null && NLNetworkTime.Seconds(_time,state.ReducedAt.Value,nowTicks) >= 3 &&
                            (state.LastVideoCut is null || NLNetworkTime.Seconds(_time,state.LastVideoCut.Value,nowTicks) >= 2))
                        { state.VideoFactor = Math.Max(.1,state.VideoFactor * .85); state.LastVideoCut = nowTicks; }
                    }
                    else
                    {
                        state.BadWindows = 0; state.Congested = false; state.ReducedAt = null; state.HealthySince ??= nowTicks;
                        if (NLNetworkTime.Seconds(_time,state.HealthySince.Value,nowTicks) >= 10 &&
                            (state.LastRecovery is null || NLNetworkTime.Seconds(_time,state.LastRecovery.Value,nowTicks) >= 5))
                        { state.BulkFactor = Math.Min(1,state.BulkFactor * 1.05m); state.VideoFactor = Math.Min(1,state.VideoFactor * 1.05); state.LastRecovery = nowTicks; }
                    }
                }
                state.LastGrants = Scale(nominal,state.BulkFactor, allowUnknownTrafficReduction && policy.Domain == NLNetworkDomain.Internet); grants.AddRange(state.LastGrants);
                congested |= state.Congested; videoFactor = Math.Min(videoFactor,state.VideoFactor);
                reason = state.Congested ? "Congestión corroborada; Bulk reducido antes del video." : "Ventana válida; recuperación con histéresis.";
            }
            var applied = policy.Mode == NLNetworkPolicyMode.Adaptive && !observed ? policy with { Mode = NLNetworkPolicyMode.Observe } : policy;
            return new(applied,grants.ToArray(),congested,policy.Domain == NLNetworkDomain.Internet ? 1 : videoFactor,reason);
        }
    }

    private bool Valid(NLNetworkSample s, NLNetworkPolicy p, NLNetworkDirection direction, long now) =>
        s.Valid && s.Generation == p.Generation && s.LinkKey == p.LinkKey && s.Domain == p.Domain && s.Direction == direction && Enum.IsDefined(s.Source) &&
        s.ObservedAtTicks <= now && NLNetworkTime.Seconds(_time,s.ObservedAtTicks,now) <= 3 && double.IsFinite(s.WindowSeconds) && s.WindowSeconds > 0 &&
        Good(s.RttMs) && Good(s.WriteWaitP95Ms) && Good(s.QueueAgeP95Ms) && s.QueueBytes >= 0 && s.UsefulBytes >= 0 && s.ProtocolBytes >= 0 && s.Dropped >= 0;
    private static bool Good(double? value) => !value.HasValue || (double.IsFinite(value.Value) && value.Value >= 0);
    private static double P95(List<NLNetworkSample> probes)
    { var sorted = probes.Select(s => s.RttMs!.Value).OrderBy(x=>x).ToArray(); return sorted[(int)Math.Ceiling(sorted.Length * .95) - 1]; }
    private static IReadOnlyList<NLNetworkGrant> Scale(IReadOnlyList<NLNetworkGrant> grants, decimal factor, bool includeBestEffort = false) =>
        grants.Select(g => g.TrafficClass == NLNetworkTrafficClass.Bulk || includeBestEffort && g.TrafficClass == NLNetworkTrafficClass.BestEffort ? g with { BytesPerSecond = Math.Max(1,(long)decimal.Floor(g.BytesPerSecond * factor)), BurstBytes = (int)Math.Clamp((long)decimal.Floor(g.BytesPerSecond * factor) / 200,1500,65536) } : g).ToArray();
    private static IReadOnlyList<NLNetworkGrant> FreezeWithin(IReadOnlyList<NLNetworkGrant> nominal, IReadOnlyList<NLNetworkGrant> last) =>
        nominal.Select(g => { var old = last.FirstOrDefault(x=>x.TrafficClass == g.TrafficClass); return old is null ? g with { BytesPerSecond=1 } : g with { BytesPerSecond=Math.Min(g.BytesPerSecond,old.BytesPerSecond),BurstBytes=Math.Min(g.BurstBytes,old.BurstBytes) }; }).ToArray();
}
