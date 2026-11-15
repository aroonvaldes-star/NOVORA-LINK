using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.Control;

namespace NOVORA.Network;

/// <summary>One explicitly selected physical route. Internet and device sessions never share a budget implicitly.</summary>
public sealed class NLExperimentalNetworkSession : IDisposable
{
    private readonly object _sync = new();
    private readonly TimeProvider _clock;
    private readonly NLNetworkPolicy _configured;
    private readonly NLNetworkStabilityController[] _controllers;
    private readonly NLNetworkPacer _pacer;
    private NLNetworkSample[] _samples = [];
    private NLNetworkDecision? _last;
    private readonly int _videoBitrate;
    private readonly bool _audio;
    private long _revision;
    private bool _disposed;
    private NLNetworkBaseline? _baseline;

    public NLExperimentalNetworkSession(NLNetworkPolicy configured, int videoBitrate = 0, bool audio = false, TimeProvider? clock = null)
    {
        NLNetworkBudget.Validate(configured);
        _configured=configured; _revision=configured.Revision; _videoBitrate=videoBitrate; _audio=audio;
        _clock=clock??TimeProvider.System;
        _controllers=[new(_clock),new(_clock)]; _pacer=new(_clock);
    }
    public string LinkKey => _configured.LinkKey;
    public long Generation => _configured.Generation;
    public NLNetworkDomain Domain => _configured.Domain;
    public NLNetworkPolicyMode Mode => _configured.Mode;
    public void AdvanceRevision() {lock(_sync) _revision++;}
    public bool IsLimited(NLNetworkDirection direction) => _configured.Mode is NLNetworkPolicyMode.Manual or NLNetworkPolicyMode.Adaptive &&
        (direction==NLNetworkDirection.FromAndroid ? _configured.FromAndroidBytesPerSecond : _configured.ToAndroidBytesPerSecond) is not null;
    public NLNetworkPolicy DisabledPolicy => _configured with { Revision=_revision+1, Mode=NLNetworkPolicyMode.Disabled };

    public void Observe(IReadOnlyList<NLNetworkSample> samples)
    {
        lock(_sync)
        {
            if(_disposed) return;
            _samples=samples.Where(s=>s.Generation==Generation && s.LinkKey==LinkKey && s.Domain==Domain).TakeLast(64).ToArray();
        }
    }
    public void SetBaseline(NLNetworkBaseline baseline)
    {
        lock(_sync)
        {
            if(baseline.Generation!=Generation || baseline.LinkKey!=LinkKey || baseline.Domain!=Domain || !baseline.QuietCalibrationConfirmed)
                throw new ArgumentException("El baseline no pertenece a esta sesión confirmada.");
            _baseline=baseline;
        }
    }

    public NLNetworkDecision Evaluate()
    {
        lock(_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            long now=_clock.GetTimestamp(); var grants=new List<NLNetworkGrant>();
            bool congested=false; double videoFactor=1; string reason="";
            foreach(var direction in Enum.GetValues<NLNetworkDirection>())
            {
                long? cap=direction==NLNetworkDirection.FromAndroid ? _configured.FromAndroidBytesPerSecond : _configured.ToAndroidBytesPerSecond;
                var directional=_configured with { ToAndroidBytesPerSecond=direction==NLNetworkDirection.ToAndroid ? cap : null,
                    FromAndroidBytesPerSecond=direction==NLNetworkDirection.FromAndroid ? cap : null };
                var demands=new Dictionary<NLNetworkTrafficClass,long>();
                if(cap.HasValue && Domain==NLNetworkDomain.Internet) demands[NLNetworkTrafficClass.BestEffort]=cap.Value;
                else if(cap.HasValue)
                {
                    demands[NLNetworkTrafficClass.Interactive]=Math.Min(64_000,cap.Value);
                    if(direction==NLNetworkDirection.FromAndroid)
                    {
                        demands[NLNetworkTrafficClass.Audio]=_audio ? 192_000 : 0;
                        demands[NLNetworkTrafficClass.Video]=Math.Max(0,_videoBitrate/8);
                    }
                    // Files use what remains after the selected live profile. Reserving a full-capacity
                    // file demand would lower video immediately even on an otherwise quiet link.
                    demands[NLNetworkTrafficClass.Bulk]=Math.Max(0,cap.Value-demands.Values.Sum());
                }
                var decision=_controllers[(int)direction].Evaluate(directional,_baseline,_samples,demands,now,
                    allowUnknownTrafficReduction:Domain==NLNetworkDomain.Internet);
                grants.AddRange(decision.Grants); congested|=decision.Congested;
                videoFactor=Math.Min(videoFactor,decision.VideoRateFactor); reason=decision.Reason;
            }
            bool changed=_last is not null && !SameRates(_last.Grants,grants);
            if(changed) _revision++;
            var policy=_configured with {Revision=_revision};
            var revised=grants.Select(g=>g with {Revision=_revision}).ToArray();
            if(Domain==NLNetworkDomain.DeviceLink && _configured.FromAndroidBytesPerSecond.HasValue && _videoBitrate>0 && policy.Mode is NLNetworkPolicyMode.Manual or NLNetworkPolicyMode.Adaptive)
            {
                var video=revised.FirstOrDefault(g=>g.Direction==NLNetworkDirection.FromAndroid && g.TrafficClass==NLNetworkTrafficClass.Video);
                videoFactor=Math.Min(videoFactor,video is null ? 0 : video.BytesPerSecond*8d/_videoBitrate);
            }
            // Explicit ceilings remain in force while automatic decisions only observe insufficient telemetry.
            _last=new(policy,revised,congested,videoFactor,reason); return _last;
        }
    }

    public async ValueTask PaceBulkAsync(NLNetworkDirection direction,int bytes,CancellationToken ct)
    {
        int remaining=bytes;
        while(remaining>0)
        {
            ct.ThrowIfCancellationRequested(); NLNetworkGrant? grant; NLNetworkPolicyMode mode;
            lock(_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed,this);
                mode=_configured.Mode;
                grant=_last?.Grants.FirstOrDefault(g=>g.Direction==direction && g.TrafficClass==NLNetworkTrafficClass.Bulk);
            }
            if(mode is NLNetworkPolicyMode.Disabled or NLNetworkPolicyMode.Observe ||
                (direction==NLNetworkDirection.FromAndroid ? _configured.FromAndroidBytesPerSecond : _configured.ToAndroidBytesPerSecond) is null) return;
            if(grant is null || grant.BytesPerSecond==0) { await Task.Delay(100,ct).ConfigureAwait(false); continue; }
            // Reserve at most half a second; each piece obtains the renewed local lease.
            int piece=(int)Math.Min(remaining,Math.Clamp(grant.BytesPerSecond/2,1,4096));
            try { await _pacer.WaitAsync(grant,piece,ct).ConfigureAwait(false); remaining-=piece; }
            catch(TimeoutException) { await Task.Delay(50,ct).ConfigureAwait(false); }
            catch(OperationCanceledException) when(!ct.IsCancellationRequested) { }
        }
    }
    public NLNetworkGrant? GetBulkGrant(NLNetworkDirection direction) { lock(_sync) return _last?.Grants.FirstOrDefault(g=>g.Direction==direction && g.TrafficClass==NLNetworkTrafficClass.Bulk); }
    private static bool SameRates(IReadOnlyList<NLNetworkGrant> a,IReadOnlyList<NLNetworkGrant> b) =>
        a.Count==b.Count && a.Zip(b).All(pair=>pair.First.Direction==pair.Second.Direction && pair.First.TrafficClass==pair.Second.TrafficClass && pair.First.BytesPerSecond==pair.Second.BytesPerSecond && pair.First.BurstBytes==pair.Second.BurstBytes);
    public void Dispose() { lock(_sync) { if(_disposed)return; _disposed=true; _pacer.Dispose(); _samples=[]; } }
}
