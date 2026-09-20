using System.Net;
using NOVORA.Control;

namespace NOVORA.Tests;

internal sealed class NLTestControlTrustHarness : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NOVORA-TrustTest-" + Guid.NewGuid().ToString("N"));
    public NLControlTrustStore Store { get; }
    public NLControlTrustServer Server { get; }

    public NLTestControlTrustHarness(Func<NLControlRequest, Task<NLControlReply>> handler)
    {
        Store = new NLControlTrustStore(_directory);
        Server = new NLControlTrustServer(IPAddress.Loopback, handler, Store, NLControlProtocol.Port, transport: "USB");
        Server.Start();
    }

    public Task<NLControlReply> ConnectAsync(NLControlClient client) =>
        client.ConnectLanAsync(Server.Invitation, true);

    public Task ConnectAsync(NLControlSession session) =>
        session.ConnectUsbAsync(Server.Invitation);

    public async ValueTask DisposeAsync()
    {
        await Server.DisposeAsync();
        Store.Dispose();
        string resolved = Path.GetFullPath(_directory);
        string root = Path.GetFullPath(Path.GetTempPath());
        if (resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(resolved).StartsWith("NOVORA-TrustTest-", StringComparison.Ordinal) &&
            Directory.Exists(resolved))
            Directory.Delete(resolved, true);
    }
}
