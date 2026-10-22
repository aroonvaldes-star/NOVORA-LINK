using NOVORA.Control;
using System.Text;
using System.Text.Json;

namespace NOVORA.LinkEngine.Network;

public sealed record LERelayReady(int Version, bool Shaping, bool FairQueues, bool QueueTelemetry);
public sealed record LERelayPolicyAcknowledgement(long Revision, long Generation, bool Accepted, string? Reason, string Json);
public sealed record LERelayQosSnapshot(long Revision, long Generation, string LinkKey, long Sequence,
    NLNetworkPolicyMode Mode, IReadOnlyList<NLNetworkSample> Samples);

/// <summary>Bounded local child-process IPC. Readiness is explicit, not inferred from a listening port.</summary>
public static class LERelayPolicyProtocol
{
    public const int MaximumLineBytes = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 12 };

    public static string BuildConfiguration(IEnumerable<NLNetworkPolicy> policies, IEnumerable<NLNetworkGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(policies); ArgumentNullException.ThrowIfNull(grants);
        var items = policies.ToArray(); var allocations = grants.ToArray();
        if (items.Length is < 1 or > 2 || allocations.Length > 20) throw new ArgumentException("Configuración de red fuera de límite.");
        var first = items[0];
        if (first.Generation <= 0 || first.Revision <= 0 || string.IsNullOrWhiteSpace(first.LinkKey) || first.LinkKey.Length > 256 || first.LinkKey.Any(char.IsControl))
            throw new ArgumentException("Identidad de configuración no válida.");
        var domains = new HashSet<NLNetworkDomain>();
        foreach (var policy in items)
        {
            if (policy.Generation != first.Generation || policy.LinkKey != first.LinkKey || policy.Revision != first.Revision || policy.Mode!=first.Mode ||
                !Enum.IsDefined(policy.Domain) || !Enum.IsDefined(policy.Mode) || !domains.Add(policy.Domain) ||
                !ValidLimit(policy.FromAndroidBytesPerSecond) || !ValidLimit(policy.ToAndroidBytesPerSecond))
                throw new ArgumentException("Presupuesto de red no válido.");
        }
        var scopes = new HashSet<(NLNetworkDomain, NLNetworkDirection, NLNetworkTrafficClass)>();
        foreach (var grant in allocations)
        {
            var policy = items.SingleOrDefault(item => item.Domain == grant.Domain);
            if (policy is null || grant.Generation != first.Generation || grant.LinkKey != first.LinkKey ||
                grant.Revision != policy.Revision || grant.BytesPerSecond is < 0 or > 1_250_000_000 || grant.BurstBytes is < 1 or > 65536 ||
                !Enum.IsDefined(grant.Direction) || !Enum.IsDefined(grant.TrafficClass) ||
                !scopes.Add((grant.Domain, grant.Direction, grant.TrafficClass)))
                throw new ArgumentException("Asignación de red no válida.");
        }
        foreach (var group in allocations.GroupBy(grant => (grant.Domain, grant.Direction)))
        {
            var policy = items.Single(item => item.Domain == group.Key.Domain);
            var limit = group.Key.Direction == NLNetworkDirection.FromAndroid ? policy.FromAndroidBytesPerSecond : policy.ToAndroidBytesPerSecond;
            if (limit is null || group.Sum(grant => grant.BytesPerSecond) > limit) throw new ArgumentException("Asignaciones superan el techo de la ruta.");
        }
        var wan = items.SingleOrDefault(item => item.Domain == NLNetworkDomain.Internet);
        var device = items.SingleOrDefault(item => item.Domain == NLNetworkDomain.DeviceLink);
        var activeMode = items.Max(item => item.Mode);
        string json = JsonSerializer.Serialize(new {
            v = 1, revision = items.Max(item => item.Revision), generation = first.Generation, linkKey = first.LinkKey,
            mode = activeMode.ToString(), wanUploadBps = wan?.FromAndroidBytesPerSecond, wanDownloadBps = wan?.ToAndroidBytesPerSecond,
            deviceFromAndroidBps = device?.FromAndroidBytesPerSecond, deviceToAndroidBps = device?.ToAndroidBytesPerSecond,
            grants = allocations.Select(grant => new { domain = grant.Domain.ToString(), direction = grant.Direction.ToString(),
                trafficClass = grant.TrafficClass.ToString(), bytesPerSecond = grant.BytesPerSecond, burstBytes = grant.BurstBytes })
        }, JsonOptions);
        string line = "LE_CONFIG " + json;
        if (Encoding.UTF8.GetByteCount(line) > MaximumLineBytes) throw new ArgumentException("Configuración IPC demasiado grande.");
        return line;
    }

    public static bool TryReadReady(string line, out LERelayReady? ready)
    {
        ready = null;
        if (!TryRead(line, "LE_READY ", out var doc)) return false;
        using (doc)
        {
            var root = doc!.RootElement;
            if (!Bool(root, "shaping", out var shaping) || !Bool(root, "fairQueues", out var fair) || !Bool(root, "queueTelemetry", out var telemetry)) return false;
            ready = new(1, shaping, fair, telemetry); return true;
        }
    }
    internal static (long Revision,long Generation) GetConfigurationIdentity(string line)
    {
        if(!TryRead(line,"LE_CONFIG ",out var document))throw new ArgumentException("Configuración IPC inválida.");
        using(document)
        {
            if(!Long(document!.RootElement,"revision",out long revision) || revision<=0 || !Long(document.RootElement,"generation",out long generation) || generation<=0)
                throw new ArgumentException("Identidad IPC inválida.");
            return(revision,generation);
        }
    }

    public static bool TryReadAcknowledgement(string line, out LERelayPolicyAcknowledgement? ack)
    {
        ack = null;
        if (!TryRead(line, "LE_CONFIG_ACK ", out var doc)) return false;
        using (doc)
        {
            var root = doc!.RootElement;
            if (!Long(root,"revision",out long revision) || revision <= 0 || !Long(root,"generation",out long generation) || generation <= 0 || !Bool(root,"accepted",out var accepted)) return false;
            string? reason = null;
            if (root.TryGetProperty("reason", out var why) && why.ValueKind != JsonValueKind.Null)
            { if (why.ValueKind != JsonValueKind.String || why.GetString()!.Length > 256) return false; reason = why.GetString(); }
            ack = new(revision,generation,accepted,reason,root.GetRawText()); return true;
        }
    }

    public static bool AcknowledgementMatchesConfig(LERelayPolicyAcknowledgement ack, string config)
    {
        if (!ack.Accepted || !TryRead(config,"LE_CONFIG ",out var expected)) return false;
        using (expected)
        using (var actual = JsonDocument.Parse(ack.Json, new JsonDocumentOptions {MaxDepth=12}))
            return Equivalent(expected!.RootElement, actual.RootElement, subset:true);
    }

    public static bool TryReadQos(string line, long receivedAtTicks, out LERelayQosSnapshot? snapshot)
    {
        snapshot = null;
        if (!TryRead(line,"LE_QOS ",out var doc)) return false;
        using (doc)
        {
            var root=doc!.RootElement;
            if (!Long(root,"revision",out long revision) || revision<=0 || !Long(root,"generation",out long generation) || generation<=0 ||
                !Long(root,"sequence",out long sequence) || sequence<=0 || !Text(root,"linkKey",128,out var key) || string.IsNullOrWhiteSpace(key) ||
                !Text(root,"mode",16,out var modeName) || !Enum.TryParse<NLNetworkPolicyMode>(modeName,out var mode) || !Enum.IsDefined(mode) ||
                !Number(root,"windowSeconds",out double window) || window<=0 || window>60 ||
                !root.TryGetProperty("samples",out var samples) || samples.ValueKind!=JsonValueKind.Array || samples.GetArrayLength() is <1 or >4) return false;
            var result=new List<NLNetworkSample>(); var scopes=new HashSet<(NLNetworkDomain,NLNetworkDirection)>();
            foreach (var sample in samples.EnumerateArray())
            {
                if (!Text(sample,"domain",16,out var domainName) || !Enum.TryParse<NLNetworkDomain>(domainName,out var domain) || !Enum.IsDefined(domain) ||
                    !Text(sample,"direction",16,out var directionName) || !Enum.TryParse<NLNetworkDirection>(directionName,out var direction) || !Enum.IsDefined(direction) ||
                    !Text(sample,"source",32,out var source) || source!="Pipeline" || !scopes.Add((domain,direction)) ||
                    !Long(sample,"usefulBytes",out var useful) || useful<0 || !Long(sample,"protocolBytes",out var protocol) || protocol<0 ||
                    !Long(sample,"queueBytes",out var queue) || queue<0 || !Long(sample,"dropped",out var dropped) || dropped<0 ||
                    !OptionalNumber(sample,"queueAgeP95Ms",out var age) || !OptionalNumber(sample,"writeWaitP95Ms",out var wait) || !Bool(sample,"valid",out var valid)) return false;
                result.Add(new(generation,key!,domain,direction,NLNetworkSampleSource.Pipeline,receivedAtTicks,window,null,wait,age,queue,useful,protocol,dropped,valid));
            }
            snapshot=new(revision,generation,key!,sequence,mode,result); return true;
        }
    }

    private static bool TryRead(string line,string prefix,out JsonDocument? doc)
    {
        doc=null;
        if (line is null || !line.StartsWith(prefix,StringComparison.Ordinal) || Encoding.UTF8.GetByteCount(line)>MaximumLineBytes) return false;
        try {
            var candidate=JsonDocument.Parse(line[prefix.Length..],new JsonDocumentOptions{MaxDepth=12});
            if (candidate.RootElement.ValueKind!=JsonValueKind.Object || !UniqueProperties(candidate.RootElement) ||
                !Long(candidate.RootElement,"v",out var version) || version!=1) { candidate.Dispose(); return false; }
            doc=candidate; return true;
        } catch (JsonException) { return false; }
    }
    private static bool UniqueProperties(JsonElement node)
    {
        if(node.ValueKind==JsonValueKind.Object) { var keys=new HashSet<string>(StringComparer.Ordinal); foreach(var property in node.EnumerateObject()) if(!keys.Add(property.Name)||!UniqueProperties(property.Value))return false; }
        else if(node.ValueKind==JsonValueKind.Array) foreach(var item in node.EnumerateArray())if(!UniqueProperties(item))return false;
        return true;
    }
    private static bool Equivalent(JsonElement expected,JsonElement actual,bool subset=false)
    {
        if(expected.ValueKind!=actual.ValueKind)return false;
        if(expected.ValueKind==JsonValueKind.Object) { if(!subset && expected.EnumerateObject().Count()!=actual.EnumerateObject().Count())return false; foreach(var p in expected.EnumerateObject())if(!actual.TryGetProperty(p.Name,out var value)||!Equivalent(p.Value,value))return false; return true; }
        if(expected.ValueKind==JsonValueKind.Array) { var left=expected.EnumerateArray().ToArray();var right=actual.EnumerateArray().ToArray();return left.Length==right.Length&&left.Zip(right).All(pair=>Equivalent(pair.First,pair.Second)); }
        return expected.GetRawText()==actual.GetRawText() || expected.ValueKind==JsonValueKind.String&&expected.GetString()==actual.GetString();
    }
    private static bool Long(JsonElement root,string name,out long value) {value=0;return root.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.Number&&p.TryGetInt64(out value);}
    private static bool Bool(JsonElement root,string name,out bool value) {value=false;if(!root.TryGetProperty(name,out var p)||p.ValueKind is not(JsonValueKind.True or JsonValueKind.False))return false;value=p.GetBoolean();return true;}
    private static bool Text(JsonElement root,string name,int max,out string? value) {value=null;if(!root.TryGetProperty(name,out var p)||p.ValueKind!=JsonValueKind.String)return false;value=p.GetString();return value is not null&&value.Length<=max;}
    private static bool Number(JsonElement root,string name,out double value) {value=0;return root.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.Number&&p.TryGetDouble(out value)&&double.IsFinite(value)&&value>=0;}
    private static bool OptionalNumber(JsonElement root,string name,out double? value) {value=null;if(!root.TryGetProperty(name,out var p)||p.ValueKind==JsonValueKind.Null)return true;if(!Number(root,name,out var number))return false;value=number;return true;}
    private static bool ValidLimit(long? value)=>value is null||value is >0 and <=1_250_000_000;
}
