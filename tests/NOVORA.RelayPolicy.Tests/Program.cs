using NOVORA.Control;
using NOVORA.LinkEngine.Network;
using System.Text.Json;

var passed = 0;
void Check(string name, Action body) { body(); Console.WriteLine("PASS " + name); passed++; }
void Require(bool value, string message) { if (!value) throw new Exception(message); }
void Reject(Action body) { try { body(); } catch (ArgumentException) { return; } throw new Exception("Invalid config accepted"); }

Check("Readiness requires explicit versioned shaping", () => {
    Require(LERelayPolicyProtocol.TryReadReady("LE_READY {\"v\":1,\"shaping\":true,\"fairQueues\":true,\"queueTelemetry\":true}", out var ready) && ready!.Shaping, "New relay not recognized");
    Require(!LERelayPolicyProtocol.TryReadReady("NOVORA LinkEngine Relay DATA tcp:27184", out _), "Old listener presented as QoS ready");
    Require(!LERelayPolicyProtocol.TryReadReady("LE_READY {\"v\":2,\"shaping\":true}", out _), "Unsupported version accepted");
});
Check("Duplicate fields and oversized lines rejected", () => {
    Require(!LERelayPolicyProtocol.TryReadReady("LE_READY {\"v\":1,\"v\":2,\"shaping\":true,\"fairQueues\":true,\"queueTelemetry\":true}", out _), "Duplicate property accepted");
    Require(!LERelayPolicyProtocol.TryReadReady("LE_READY " + new string(' ', 4096), out _), "Oversized IPC line accepted");
});
var policies = new[] {
    new NLNetworkPolicy(7, 42, "test-link", NLNetworkDomain.Internet, NLNetworkPolicyMode.Manual, 1000, 2000),
    new NLNetworkPolicy(7, 42, "test-link", NLNetworkDomain.DeviceLink, NLNetworkPolicyMode.Manual, 3000, 4000)
};
var grants = new[] {
    new NLNetworkGrant(7,42,"test-link",NLNetworkDomain.Internet,NLNetworkDirection.FromAndroid,NLNetworkTrafficClass.Bulk,800,1500,123),
    new NLNetworkGrant(7,42,"test-link",NLNetworkDomain.DeviceLink,NLNetworkDirection.FromAndroid,NLNetworkTrafficClass.Video,1000,1500,123)
};
Check("Internet and device ceilings remain independent on the wire", () => {
    var line = LERelayPolicyProtocol.BuildConfiguration(policies, grants);
    using var doc = JsonDocument.Parse(line["LE_CONFIG ".Length..]); var root = doc.RootElement;
    Require(root.GetProperty("wanUploadBps").GetInt64()==1000 && root.GetProperty("deviceFromAndroidBps").GetInt64()==3000, "Domains collapsed");
    Require(root.GetProperty("grants")[0].GetProperty("domain").GetString()=="Internet", "Grant lost domain");
    Require(!line.Contains("validUntilTicks",StringComparison.OrdinalIgnoreCase), "Remote monotonic clock serialized");
});
Check("Config validates generation, duplicate scope and grant totals", () => {
    Reject(()=>LERelayPolicyProtocol.BuildConfiguration(new[]{policies[0],policies[1] with {Generation=43}}, grants));
    Reject(()=>LERelayPolicyProtocol.BuildConfiguration(new[]{policies[0],policies[0]}, grants));
    Reject(()=>LERelayPolicyProtocol.BuildConfiguration(policies,new[]{grants[0] with {BytesPerSecond=1001}}));
    Reject(()=>LERelayPolicyProtocol.BuildConfiguration(policies,new[]{grants[0],grants[0]}));
    Reject(()=>LERelayPolicyProtocol.BuildConfiguration(new[]{policies[0],policies[1] with {Mode=NLNetworkPolicyMode.Disabled}},grants));
    Reject(()=>LERelayPolicyProtocol.BuildConfiguration(policies,new[]{grants[0] with {Revision=6}}));
});
Check("Policy ACK must match all accepted ceilings and grants", () => {
    var config = LERelayPolicyProtocol.BuildConfiguration(policies,grants);
    var json=config["LE_CONFIG ".Length..];
    var ack="LE_CONFIG_ACK " + json[..^1] + ",\"accepted\":true,\"reason\":null}";
    Require(LERelayPolicyProtocol.TryReadAcknowledgement(ack,out var reply) && LERelayPolicyProtocol.AcknowledgementMatchesConfig(reply!,config), "Exact ACK rejected");
    var changed=ack.Replace("\"deviceFromAndroidBps\":3000","\"deviceFromAndroidBps\":3001");
    Require(LERelayPolicyProtocol.TryReadAcknowledgement(changed,out var wrong) && !LERelayPolicyProtocol.AcknowledgementMatchesConfig(wrong!,config), "Mismatched ceiling confirmed");
    var rejected=ack.Replace("\"accepted\":true","\"accepted\":false");
    Require(LERelayPolicyProtocol.TryReadAcknowledgement(rejected,out var no) && !LERelayPolicyProtocol.AcknowledgementMatchesConfig(no!,config), "Negative ACK confirmed");
});
Check("QoS telemetry labels pipeline timing without invented RTT", () => {
    var line="LE_QOS {\"v\":1,\"revision\":7,\"generation\":42,\"linkKey\":\"test-link\",\"sequence\":1,\"windowSeconds\":1,\"mode\":\"Manual\",\"samples\":[{\"domain\":\"Internet\",\"direction\":\"ToAndroid\",\"source\":\"Pipeline\",\"usefulBytes\":100,\"protocolBytes\":120,\"queueBytes\":400,\"queueAgeP95Ms\":20,\"writeWaitP95Ms\":5,\"dropped\":0,\"valid\":true}]}";
    Require(LERelayPolicyProtocol.TryReadQos(line, 100, out var snapshot), "Valid metrics rejected");
    Require(snapshot!.Samples[0].RttMs is null && snapshot.Samples[0].Source==NLNetworkSampleSource.Pipeline, "Queue time mislabeled as RTT");
    Require(!LERelayPolicyProtocol.TryReadQos(line.Replace("\"queueBytes\":400","\"queueBytes\":-1"),100,out _), "Negative queue accepted");
    Require(!LERelayPolicyProtocol.TryReadQos(line.Replace("\"windowSeconds\":1","\"windowSeconds\":0"),100,out _), "Zero window accepted");
});
var session = new LERelayPolicySession();
session.HandleLine("LE_READY {\"v\":1,\"shaping\":true,\"fairQueues\":true,\"queueTelemetry\":true}");
var configuration = LERelayPolicyProtocol.BuildConfiguration(policies,grants);
var applied = await session.ApplyAsync(configuration, (line,ct)=> {
    var json=line["LE_CONFIG ".Length..];
    session.HandleLine("LE_CONFIG_ACK " + json[..^1] + ",\"accepted\":true,\"reason\":null}");
    return Task.CompletedTask;
}, CancellationToken.None);
Require(applied,"Accepted ACK did not complete pending apply"); passed++;
var waiting = session.ApplyAsync(configuration, (line,ct)=>Task.CompletedTask, CancellationToken.None);
session.Reset();
Require(!await waiting,"Reset confirmed an old request"); passed++;
Require(!session.IsReady,"Reset kept stale capabilities");
session.HandleLine("LE_READY {\"v\":1,\"shaping\":true,\"fairQueues\":true,\"queueTelemetry\":true}");
var next=LERelayPolicyProtocol.BuildConfiguration(policies.Select(p=>p with {Revision=8,Generation=43}),grants.Select(g=>g with {Revision=8,Generation=43}));
var lateAckResult=await session.ApplyAsync(next,(line,ct)=> {
    var stale=configuration["LE_CONFIG ".Length..];
    session.HandleLine("LE_CONFIG_ACK "+stale[..^1]+",\"accepted\":true,\"reason\":null}");
    var current=line["LE_CONFIG ".Length..];
    session.HandleLine("LE_CONFIG_ACK "+current[..^1]+",\"accepted\":true,\"reason\":null}");
    return Task.CompletedTask;
},CancellationToken.None);
Require(lateAckResult,"Late old ACK completed a newer request"); passed++;
Console.WriteLine($"Relay policy protocol: {passed} checks passed.");
