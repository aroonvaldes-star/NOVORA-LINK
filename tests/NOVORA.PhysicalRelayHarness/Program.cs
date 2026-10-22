using System.Diagnostics;
using System.Net;
using System.Text.Json;
using NOVORA.Control;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: harness <PC-private-IP> <adb-path> <adb-serial>");
    return 2;
}

IPAddress host = IPAddress.Parse(args[0]);
string adbPath = Path.GetFullPath(args[1]);
string serial = args[2];
string profile = Path.Combine(Path.GetTempPath(), "novora-physical-relay-" + Guid.NewGuid().ToString("N"));
using var store = new NLControlTrustStore(profile);
NLControlLanDataGateway? gateway = null;
NLControlTrustServer? server = null;

NLControlSnapshot Snapshot()
{
    bool prepared = gateway is not null;
    var engines = new NLControlEngines(false, false, !prepared, prepared,
        gateway?.IsConnected == true, prepared ? "Starting" : "Stopped",
        prepared ? "Canal DATA LAN preparado." : "Listo para Internet LAN.", "Android autorizado");
    return new NLControlSnapshot(1, Environment.MachineName, "1.4.91", "", "", "", "", false,
        [], [], [], engines);
}

async Task<NLControlReply> Handle(NLControlRequest request)
{
    if (request.Action == "startLink")
    {
        if (gateway is not null)
            return new(1, request.Id, false, "El canal DATA ya está preparado.", Snapshot());
        gateway = new NLControlLanDataGateway(host, store);
        gateway.StateChanged += (_, _) => server?.Publish(Snapshot());
        NLControlLinkOffer offer = gateway.Start();
        return new(1, request.Id, true, "DATA LAN listo.", Snapshot(),
            Value: JsonSerializer.Serialize(offer));
    }
    if (request.Action == "stopLink")
    {
        if (gateway is not null) await gateway.DisposeAsync();
        gateway = null;
        return new(1, request.Id, true, "DATA LAN detenido.", Snapshot());
    }
    return new(1, request.Id, true, "Estado actualizado.", Snapshot());
}

try
{
    server = new NLControlTrustServer(host, Handle, store);
    server.StatusChanged += (_, status) => Console.WriteLine("CONTROL: " + status);
    server.Start();
    await using var discovery = new NLControlLanDiscovery(
        new NLControlLanPeer(Environment.MachineName, host.ToString(), server.Port, store.Fingerprint));
    discovery.Start();
    var handoff = new NLControlLanCodeHandoff(Environment.MachineName, host.ToString(),
        server.Port, store.Fingerprint, server.PairingCode, server.Invitation.ExpiresUnix);
    handoff.Validate();
    string payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(handoff));
    var info = new ProcessStartInfo(adbPath)
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true
    };
    foreach (string argument in new[] { "-s", serial, "shell", "am", "start", "--user", "0", "-n",
                 "com.novora.appcontrol/.LanCodeActivity", "--es", "novora.lan.code", payload })
        info.ArgumentList.Add(argument);
    using (Process adb = Process.Start(info) ?? throw new IOException("ADB did not start"))
    {
        await adb.WaitForExitAsync();
        if (adb.ExitCode != 0) throw new IOException("ADB LAN code handoff failed");
    }
    Console.WriteLine("READY: code delivered to AppControl; approve fingerprint and pair on the phone.");
    await Task.Delay(TimeSpan.FromMinutes(5));
}
finally
{
    if (gateway is not null) await gateway.DisposeAsync();
    if (server is not null) await server.DisposeAsync();
}
return 0;
