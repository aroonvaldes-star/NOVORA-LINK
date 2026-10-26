using NOVORA.Control;
using NOVORA.Network;

var tests = new (string Name, Func<Task> Run)[]
{
    ("GrantSumWithinOneMegabyte", Sync(GrantSumWithinOneMegabyte)),
    ("PriorityAndNoClassStarvation", Sync(PriorityAndNoClassStarvation)),
    ("SeparateRoutesDomainsDirections", Sync(SeparateRoutesDomainsDirections)),
    ("UnknownCapacityObserveAndDisabled", Sync(UnknownCapacityObserveAndDisabled)),
    ("RejectInvalidLimitsAndOldRevision", Sync(RejectInvalidLimitsAndOldRevision)),
    ("TwoWindowsReduceBulkPreserveEssentials", Sync(TwoWindowsReduceBulkPreserveEssentials)),
    ("CooldownAndSlowRecovery", Sync(CooldownAndSlowRecovery)),
    ("StaleNanAndOldGenerationFreeze", Sync(StaleNanAndOldGenerationFreeze)),
    ("NoCapacityOrBaselineStaysObserve", Sync(NoCapacityOrBaselineStaysObserve)),
    ("SeparateRttSourcesAndMinimumProbes", Sync(SeparateRttSourcesAndMinimumProbes)),
    ("WanCongestionDoesNotReduceVideo", Sync(WanCongestionDoesNotReduceVideo)),
    ("LocalPersistentQueueReducesVideoAfterBulk", Sync(LocalPersistentQueueReducesVideoAfterBulk)),
    ("GenerationResetAndDirectionIsolation", Sync(GenerationResetAndDirectionIsolation)),
    ("DecodeDropsAloneDoNotMeanCongestion", Sync(DecodeDropsAloneDoNotMeanCongestion)),
    ("PacerExactRateAndNoIdleBurst", PacerExactRateAndNoIdleBurst),
    ("PacerSharedClassAndIndependentDomains", PacerSharedClassAndIndependentDomains),
    ("PacerCancellationExpiryAndReplacement", PacerCancellationExpiryAndReplacement),
    ("Pacing35SecondsBeforeResponseDeadline", Pacing35SecondsBeforeResponseDeadline),
    ("PacerBoundsPendingWaiters", PacerBoundsPendingWaiters),
    ("PacerRevisionChangesRateWithoutRefillingBurst", PacerRevisionChangesRateWithoutRefillingBurst),
    ("RepeatedTelemetryCannotCountAsAnotherWindow", Sync(RepeatedTelemetryCannotCountAsAnotherWindow)),
    ("ExactStaleBoundaryAndUnconfirmedCalibration", Sync(ExactStaleBoundaryAndUnconfirmedCalibration)),
    ("MemoryBoundsRejectNewRoutes", Sync(MemoryBoundsRejectNewRoutes)),
    ("UnknownInternetTrafficCanBeExplicitlyReduced", Sync(UnknownInternetTrafficCanBeExplicitlyReduced)),
};
int failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {test.Name}: {ex}"); }
}
Console.WriteLine($"RESULT {tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static Func<Task> Sync(Action action) => () => { action(); return Task.CompletedTask; };
static void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
static void True(bool value, string message = "Expected true") { if (!value) throw new Exception(message); }
static async Task Throws<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
static NLNetworkPolicy Policy(NLNetworkDomain domain = NLNetworkDomain.DeviceLink, long generation = 1, string link = "lan-a", NLNetworkPolicyMode mode = NLNetworkPolicyMode.Adaptive, long? cap = 1_000_000) => new(1, generation, link, domain, mode, cap, null);
static Dictionary<NLNetworkTrafficClass,long> Demands() => new() { [NLNetworkTrafficClass.Interactive] = 100_000, [NLNetworkTrafficClass.Audio] = 100_000, [NLNetworkTrafficClass.Video] = 300_000, [NLNetworkTrafficClass.Bulk] = 900_000 };
static long Rate(IReadOnlyList<NLNetworkGrant> grants, NLNetworkTrafficClass cls, NLNetworkDirection direction = NLNetworkDirection.FromAndroid) => grants.Where(x => x.TrafficClass == cls && x.Direction == direction).Sum(x => x.BytesPerSecond);
static NLNetworkSample Queue(long at, double age = 60, double wait = 15, long generation = 1, NLNetworkDomain domain = NLNetworkDomain.DeviceLink, NLNetworkDirection direction = NLNetworkDirection.FromAndroid) => new(generation,"lan-a",domain,direction,NLNetworkSampleSource.Pipeline,at,1,null,wait,age,65536,400000,2000,0,true);
static NLNetworkSample Probe(long at, NLNetworkSampleSource source, double rtt, NLNetworkDomain domain = NLNetworkDomain.DeviceLink) => new(1,"lan-a",domain,NLNetworkDirection.FromAndroid,source,at,1,rtt,null,null,0,0,0,0,true);
static NLNetworkBaseline Baseline(NLNetworkSampleSource source = NLNetworkSampleSource.LanControl, NLNetworkDomain domain = NLNetworkDomain.DeviceLink) => new(1,"lan-a",domain,source,10,0,true);

static void GrantSumWithinOneMegabyte()
{
    var budget = new NLNetworkBudget(new ManualClock()); budget.Update(Policy(mode:NLNetworkPolicyMode.Manual),0);
    var grants = budget.Allocate(NLNetworkDirection.FromAndroid, Demands(),0);
    Eq(1_000_000L, grants.Sum(g => g.BytesPerSecond)); Eq(100_000L,Rate(grants,NLNetworkTrafficClass.Interactive)); Eq(100_000L,Rate(grants,NLNetworkTrafficClass.Audio)); Eq(300_000L,Rate(grants,NLNetworkTrafficClass.Video)); Eq(500_000L,Rate(grants,NLNetworkTrafficClass.Bulk));
    True(grants.All(g => g.Domain == NLNetworkDomain.DeviceLink && g.BurstBytes <= 65536 && g.ValidUntilTicks > 0));
}
static void PriorityAndNoClassStarvation()
{
    var budget = new NLNetworkBudget(new ManualClock()); var policy = Policy(mode:NLNetworkPolicyMode.Manual);
    var demand = new Dictionary<NLNetworkTrafficClass,long> { [NLNetworkTrafficClass.Interactive]=1_000_000,[NLNetworkTrafficClass.Video]=1_000_000,[NLNetworkTrafficClass.BestEffort]=1_000_000,[NLNetworkTrafficClass.Bulk]=1_000_000 };
    var grants=budget.Allocate(policy,NLNetworkDirection.FromAndroid,demand,0);
    Eq(400_000L,Rate(grants,NLNetworkTrafficClass.Interactive)); Eq(200_000L,Rate(grants,NLNetworkTrafficClass.Video)); Eq(200_000L,Rate(grants,NLNetworkTrafficClass.BestEffort)); Eq(200_000L,Rate(grants,NLNetworkTrafficClass.Bulk));
}
static void SeparateRoutesDomainsDirections()
{
    var budget=new NLNetworkBudget(new ManualClock()); var a=Policy(mode:NLNetworkPolicyMode.Manual); var b=a with { LinkKey="usb-b", Domain=NLNetworkDomain.Internet, FromAndroidBytesPerSecond=200_000,ToAndroidBytesPerSecond=400_000 };
    budget.Update(a,0); budget.Update(b,0);
    Eq(1_000_000L,budget.Allocate(a,NLNetworkDirection.FromAndroid,Demands(),0).Sum(x=>x.BytesPerSecond));
    Eq(200_000L,budget.Allocate(b,NLNetworkDirection.FromAndroid,Demands(),0).Sum(x=>x.BytesPerSecond));
    Eq(400_000L,budget.Allocate(b,NLNetworkDirection.ToAndroid,Demands(),0).Sum(x=>x.BytesPerSecond));
    Eq(1_000_000L,budget.Allocate(a with { Domain=NLNetworkDomain.Internet },NLNetworkDirection.FromAndroid,Demands(),0).Sum(x=>x.BytesPerSecond));
}
static void UnknownCapacityObserveAndDisabled()
{
    var b=new NLNetworkBudget(new ManualClock()); Eq(0,b.Allocate(Policy(cap:null),NLNetworkDirection.FromAndroid,Demands(),0).Count);
    Eq(0,b.Allocate(Policy(mode:NLNetworkPolicyMode.Observe),NLNetworkDirection.FromAndroid,Demands(),0).Count);
    Eq(0,b.Allocate(Policy(mode:NLNetworkPolicyMode.Disabled),NLNetworkDirection.FromAndroid,Demands(),0).Count);
}
static void RejectInvalidLimitsAndOldRevision()
{
    var b=new NLNetworkBudget(new ManualClock()); Reject<ArgumentOutOfRangeException>(()=>b.Update(Policy(cap:0),0)); Reject<ArgumentOutOfRangeException>(()=>b.Update(Policy(cap:1_250_000_001),0));
    var p=Policy(mode:NLNetworkPolicyMode.Manual) with { Revision=2 }; b.Update(p,0);
    Reject<InvalidOperationException>(()=>b.Update(p with { Revision=1 },0)); Reject<InvalidOperationException>(()=>b.Update(p with { FromAndroidBytesPerSecond=200_000 },0));
    Eq(1_000_000L,b.Allocate(NLNetworkDirection.FromAndroid,Demands(),0).Sum(x=>x.BytesPerSecond));
}
static void TwoWindowsReduceBulkPreserveEssentials()
{
    var c=new NLNetworkStabilityController(new ManualClock()); var p=Policy();
    var first=c.Evaluate(p,null,[Queue(0)],Demands(),0); Eq(500_000L,Rate(first.Grants,NLNetworkTrafficClass.Bulk));
    var duplicate=c.Evaluate(p,null,[Queue(0)],Demands(),0); Eq(500_000L,Rate(duplicate.Grants,NLNetworkTrafficClass.Bulk));
    var second=c.Evaluate(p,null,[Queue(1000)],Demands(),1000); Eq(400_000L,Rate(second.Grants,NLNetworkTrafficClass.Bulk)); Eq(100_000L,Rate(second.Grants,NLNetworkTrafficClass.Audio)); Eq(100_000L,Rate(second.Grants,NLNetworkTrafficClass.Interactive)); Eq(0L,Rate(second.Grants,NLNetworkTrafficClass.BestEffort)); Eq(900_000L,second.Grants.Sum(g=>g.BytesPerSecond));
}
static void CooldownAndSlowRecovery()
{
    var c=new NLNetworkStabilityController(new ManualClock()); var p=Policy();
    c.Evaluate(p,null,[Queue(0)],Demands(),0); c.Evaluate(p,null,[Queue(1000)],Demands(),1000);
    Eq(400_000L,Rate(c.Evaluate(p,null,[Queue(2000)],Demands(),2000).Grants,NLNetworkTrafficClass.Bulk));
    Eq(320_000L,Rate(c.Evaluate(p,null,[Queue(3000)],Demands(),3000).Grants,NLNetworkTrafficClass.Bulk));
    for(long t=4000;t<14000;t+=1000) Eq(320_000L,Rate(c.Evaluate(p,null,[Queue(t,0,0)],Demands(),t).Grants,NLNetworkTrafficClass.Bulk));
    Eq(336_000L,Rate(c.Evaluate(p,null,[Queue(14000,0,0)],Demands(),14000).Grants,NLNetworkTrafficClass.Bulk));
    Eq(336_000L,Rate(c.Evaluate(p,null,[Queue(15000,0,0)],Demands(),15000).Grants,NLNetworkTrafficClass.Bulk));
    Eq(352_800L,Rate(c.Evaluate(p,null,[Queue(19000,0,0)],Demands(),19000).Grants,NLNetworkTrafficClass.Bulk));
}
static void StaleNanAndOldGenerationFreeze()
{
    var c=new NLNetworkStabilityController(new ManualClock()); var p=Policy(); c.Evaluate(p,null,[Queue(0)],Demands(),0); c.Evaluate(p,null,[Queue(1000)],Demands(),1000);
    foreach(var s in new[] { Queue(0),Queue(5000,double.NaN),Queue(5000,generation:0),Queue(5000) with { WindowSeconds=0 },Queue(5000) with { RttMs=-1 } })
    { var d=c.Evaluate(p,null,[s],Demands(),5000); Eq(400_000L,Rate(d.Grants,NLNetworkTrafficClass.Bulk)); Eq(NLNetworkPolicyMode.Observe,d.AppliedPolicy.Mode); Eq(1_000_000L,d.AppliedPolicy.FromAndroidBytesPerSecond!.Value); }
}
static void NoCapacityOrBaselineStaysObserve()
{
    var c=new NLNetworkStabilityController(new ManualClock()); var unknown=c.Evaluate(Policy(cap:null),Baseline(),[Queue(0)],Demands(),0); Eq(NLNetworkPolicyMode.Observe,unknown.AppliedPolicy.Mode); Eq(0,unknown.Grants.Count);
    var d=c.Evaluate(Policy() with { Revision=2 },null,[Probe(1000,NLNetworkSampleSource.LanControl,100)],Demands(),1000); Eq(NLNetworkPolicyMode.Observe,d.AppliedPolicy.Mode); Eq(500_000L,Rate(d.Grants,NLNetworkTrafficClass.Bulk));
    var manual=c.Evaluate(Policy(mode:NLNetworkPolicyMode.Manual) with { Revision=3 },null,[],Demands(),2000); Eq(NLNetworkPolicyMode.Manual,manual.AppliedPolicy.Mode); Eq(1_000_000L,manual.Grants.Sum(x=>x.BytesPerSecond));
}
static void SeparateRttSourcesAndMinimumProbes()
{
    var c=new NLNetworkStabilityController(new ManualClock()); var p=Policy();
    var foreign=Enumerable.Range(0,10).Select(i=>Probe(i*100,NLNetworkSampleSource.InternetPcHttp,100)).ToArray();
    var ignored=c.Evaluate(p,Baseline(),foreign,Demands(),1000); Eq(NLNetworkPolicyMode.Observe,ignored.AppliedPolicy.Mode); Eq(500_000L,Rate(ignored.Grants,NLNetworkTrafficClass.Bulk));
    var probes=Enumerable.Range(0,9).Select(i=>Probe(2000+i*100,NLNetworkSampleSource.LanControl,100)).ToArray();
    Eq(NLNetworkPolicyMode.Observe,c.Evaluate(p,Baseline(),probes,Demands(),3000).AppliedPolicy.Mode);
    c.Evaluate(p,Baseline(),[Probe(4000,NLNetworkSampleSource.LanControl,100)],Demands(),4000);
    var reduced=c.Evaluate(p,Baseline(),[Probe(5000,NLNetworkSampleSource.LanControl,100)],Demands(),5000); Eq(400_000L,Rate(reduced.Grants,NLNetworkTrafficClass.Bulk)); Eq(1d,reduced.VideoRateFactor);
}
static void WanCongestionDoesNotReduceVideo()
{
    var c=new NLNetworkStabilityController(new ManualClock()); var p=Policy(domain:NLNetworkDomain.Internet);
    NLNetworkDecision? d=null; for(long t=0;t<=7000;t+=1000)d=c.Evaluate(p,null,[Queue(t,domain:NLNetworkDomain.Internet)],Demands(),t);
    True(d!.Congested); Eq(1d,d.VideoRateFactor); True(Rate(d.Grants,NLNetworkTrafficClass.Bulk)<400_000);
}
static void LocalPersistentQueueReducesVideoAfterBulk()
{
    var c=new NLNetworkStabilityController(new ManualClock()); var p=Policy();
    for(long t=0;t<4000;t+=1000) Eq(1d,c.Evaluate(p,null,[Queue(t)],Demands(),t).VideoRateFactor);
    Eq(.85d,c.Evaluate(p,null,[Queue(4000)],Demands(),4000).VideoRateFactor);
    var d=c.Evaluate(p,null,[Queue(5000)],Demands(),5000); Eq(.85d,d.VideoRateFactor); Eq(100_000L,Rate(d.Grants,NLNetworkTrafficClass.Audio));
}
static void GenerationResetAndDirectionIsolation()
{
    var c=new NLNetworkStabilityController(new ManualClock()); var p=Policy() with { ToAndroidBytesPerSecond=1_000_000 };
    c.Evaluate(p,null,[Queue(0)],Demands(),0); var d=c.Evaluate(p,null,[Queue(1000)],Demands(),1000); Eq(400_000L,Rate(d.Grants,NLNetworkTrafficClass.Bulk)); Eq(500_000L,Rate(d.Grants,NLNetworkTrafficClass.Bulk,NLNetworkDirection.ToAndroid));
    var reset=c.Evaluate(p with { Generation=2 },Baseline(),[Queue(2000,generation:1)],Demands(),2000); Eq(500_000L,Rate(reset.Grants,NLNetworkTrafficClass.Bulk)); Eq(1d,reset.VideoRateFactor);
    var old=c.Evaluate(p,null,[Queue(3000)],Demands(),3000); Eq(0,old.Grants.Count); Eq(NLNetworkPolicyMode.Observe,old.AppliedPolicy.Mode);
}
static void DecodeDropsAloneDoNotMeanCongestion()
{
    var c=new NLNetworkStabilityController(new ManualClock()); for(long t=0;t<=3000;t+=1000) { var d=c.Evaluate(Policy(),null,[Queue(t,0,0) with { Dropped=500 }],Demands(),t); True(!d.Congested); Eq(500_000L,Rate(d.Grants,NLNetworkTrafficClass.Bulk)); }
}
static NLNetworkGrant Grant(NLNetworkDomain domain=NLNetworkDomain.DeviceLink,long revision=1,long generation=1,long expiry=100000,long rate=1000,int burst=1000) => new(revision,generation,"lan-a",domain,NLNetworkDirection.FromAndroid,NLNetworkTrafficClass.Bulk,rate,burst,expiry);
static async Task Pump() { for(int i=0;i<20;i++) await Task.Yield(); }
static async Task PacerExactRateAndNoIdleBurst()
{
    var clock=new ManualClock(); using var pacer=new NLNetworkPacer(clock); var g=Grant(); await pacer.WaitAsync(g,1000,CancellationToken.None);
    var wait=pacer.WaitAsync(g,1000,CancellationToken.None).AsTask(); await Pump(); True(!wait.IsCompleted); clock.Advance(999); await Pump(); True(!wait.IsCompleted); clock.Advance(1); await wait.WaitAsync(TimeSpan.FromSeconds(3));
    clock.Advance(10000); await pacer.WaitAsync(g,1000,CancellationToken.None); var second=pacer.WaitAsync(g,1000,CancellationToken.None).AsTask(); await Pump(); True(!second.IsCompleted); clock.Advance(1000); await second.WaitAsync(TimeSpan.FromSeconds(3));
}
static async Task PacerSharedClassAndIndependentDomains()
{
    var clock=new ManualClock(); using var pacer=new NLNetworkPacer(clock); var g=Grant(); await pacer.WaitAsync(g,1000,CancellationToken.None);
    var same=pacer.WaitAsync(g,1000,CancellationToken.None).AsTask(); await Pump(); True(!same.IsCompleted); await pacer.WaitAsync(Grant(domain:NLNetworkDomain.Internet),1000,CancellationToken.None);
    clock.Advance(1000); await same.WaitAsync(TimeSpan.FromSeconds(3));
}
static async Task PacerCancellationExpiryAndReplacement()
{
    var clock=new ManualClock(); using var pacer=new NLNetworkPacer(clock); var g=Grant(expiry:500); await pacer.WaitAsync(g,1000,CancellationToken.None);
    using var cancel=new CancellationTokenSource(); var cancelled=pacer.WaitAsync(g,1000,cancel.Token).AsTask(); await Pump(); cancel.Cancel(); await Throws<OperationCanceledException>(()=>cancelled);
    var expired=pacer.WaitAsync(g,1000,CancellationToken.None).AsTask(); await Pump(); clock.Advance(500); await Pump(); True(expired.IsCompleted,"Expired grant must complete from virtual time, before wall clock guard"); await Throws<TimeoutException>(()=>expired);
    var fresh=Grant(revision:2,generation:2); await pacer.WaitAsync(fresh,1000,CancellationToken.None); await Throws<OperationCanceledException>(()=>pacer.WaitAsync(g,1,CancellationToken.None).AsTask());
    var waiting=pacer.WaitAsync(fresh,1000,CancellationToken.None).AsTask(); await Pump(); await pacer.WaitAsync(Grant(revision:3,generation:3),1,CancellationToken.None); await Throws<OperationCanceledException>(()=>waiting);
}
static async Task Pacing35SecondsBeforeResponseDeadline()
{
    var clock=new ManualClock(); using var pacer=new NLNetworkPacer(clock); var g=Grant(rate:1000,burst:1000); var wait=pacer.WaitAsync(g,36000,CancellationToken.None).AsTask();
    await Pump(); clock.Advance(35000); await wait.WaitAsync(TimeSpan.FromSeconds(3)); Eq(35000L,clock.GetTimestamp());
    // The caller creates its response deadline after voluntary pacing, at t=35 s.
    long responseDeadline=clock.GetTimestamp()+30000; Eq(65000L,responseDeadline);
}
static async Task PacerBoundsPendingWaiters()
{
    var clock=new ManualClock(); using var pacer=new NLNetworkPacer(clock,maxPendingWaiters:2); var g=Grant(); await pacer.WaitAsync(g,1000,CancellationToken.None); using var ct=new CancellationTokenSource();
    var a=pacer.WaitAsync(g,1000,ct.Token).AsTask(); var b=pacer.WaitAsync(g,1000,ct.Token).AsTask(); await Pump(); await Throws<InvalidOperationException>(()=>pacer.WaitAsync(g,1000,ct.Token).AsTask()); ct.Cancel(); await Throws<OperationCanceledException>(()=>a); await Throws<OperationCanceledException>(()=>b);
    await Throws<ArgumentOutOfRangeException>(()=>pacer.WaitAsync(g,1048577,CancellationToken.None).AsTask());
}
static async Task PacerRevisionChangesRateWithoutRefillingBurst()
{
    var clock=new ManualClock(); using var pacer=new NLNetworkPacer(clock); await pacer.WaitAsync(Grant(),1000,CancellationToken.None);
    var slower=pacer.WaitAsync(Grant(revision:2,rate:500),500,CancellationToken.None).AsTask(); await Pump(); True(!slower.IsCompleted); clock.Advance(999); await Pump(); True(!slower.IsCompleted); clock.Advance(1); await slower.WaitAsync(TimeSpan.FromSeconds(3));
}
static void RepeatedTelemetryCannotCountAsAnotherWindow()
{
    var c=new NLNetworkStabilityController(new ManualClock()); var p=Policy(); c.Evaluate(p,null,[Queue(0)],Demands(),0);
    Eq(500_000L,Rate(c.Evaluate(p,null,[Queue(0)],Demands(),1000).Grants,NLNetworkTrafficClass.Bulk));
    Eq(400_000L,Rate(c.Evaluate(p,null,[Queue(2000)],Demands(),2000).Grants,NLNetworkTrafficClass.Bulk));
}
static void ExactStaleBoundaryAndUnconfirmedCalibration()
{
    var c=new NLNetworkStabilityController(new ManualClock()); var p=Policy();
    Eq(NLNetworkPolicyMode.Observe,c.Evaluate(p,null,[Queue(0)],Demands(),3001).AppliedPolicy.Mode);
    Eq(NLNetworkPolicyMode.Adaptive,c.Evaluate(p,null,[Queue(1)],Demands(),3001).AppliedPolicy.Mode);
    var other=new NLNetworkStabilityController(new ManualClock()); var probes=Enumerable.Range(0,10).Select(i=>Probe(i*100,NLNetworkSampleSource.LanControl,100)).ToArray();
    var d=other.Evaluate(p,Baseline() with { QuietCalibrationConfirmed=false },probes,Demands(),1000); Eq(NLNetworkPolicyMode.Observe,d.AppliedPolicy.Mode); Eq(500_000L,Rate(d.Grants,NLNetworkTrafficClass.Bulk));
    var future=other.Evaluate(p,null,[Queue(2001)],Demands(),2000); Eq(NLNetworkPolicyMode.Observe,future.AppliedPolicy.Mode);
}
static void MemoryBoundsRejectNewRoutes()
{
    var b=new NLNetworkBudget(new ManualClock(),maxRoutes:1); b.Update(Policy(),0); Reject<InvalidOperationException>(()=>b.Update(Policy(link:"other"),0));
    var c=new NLNetworkStabilityController(new ManualClock(),maxRoutes:1); c.Evaluate(Policy(),null,[Queue(0)],Demands(),0); Reject<InvalidOperationException>(()=>c.Evaluate(Policy(link:"other"),null,[],Demands(),0));
}
static void UnknownInternetTrafficCanBeExplicitlyReduced()
{
    var c=new NLNetworkStabilityController(new ManualClock());
    var p=Policy(domain:NLNetworkDomain.Internet);
    var demands=new Dictionary<NLNetworkTrafficClass,long> { [NLNetworkTrafficClass.BestEffort]=1_000_000 };
    c.Evaluate(p,null,[Queue(0,domain:NLNetworkDomain.Internet)],demands,0,allowUnknownTrafficReduction:true);
    var d=c.Evaluate(p,null,[Queue(1000,domain:NLNetworkDomain.Internet)],demands,1000,allowUnknownTrafficReduction:true);
    Eq(800_000L,Rate(d.Grants,NLNetworkTrafficClass.BestEffort)); Eq(1d,d.VideoRateFactor);
    var conservative=new NLNetworkStabilityController(new ManualClock());
    conservative.Evaluate(p,null,[Queue(0,domain:NLNetworkDomain.Internet)],demands,0);
    Eq(1_000_000L,Rate(conservative.Evaluate(p,null,[Queue(1000,domain:NLNetworkDomain.Internet)],demands,1000).Grants,NLNetworkTrafficClass.BestEffort));
}
