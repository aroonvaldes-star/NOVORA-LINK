using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NOVORA.Control;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestAndroidDiscovery
{
    [Fact]
    public async Task OneShotSearchFindsRealResponderAndStops()
    {
        var peer = new NLControlLanPeer("NOVORA de prueba", "127.0.0.1", 27215);
        await using var responder = new NLControlLanDiscovery(peer, new(IPAddress.Loopback, 0), true);
        responder.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var found = await NLControlLanDiscovery.SearchCoreAsync(timeout.Token, responder.LocalEndpoint, true);
        Assert.Equal(peer, Assert.Single(found));
    }

    [Fact]
    public async Task SearchRejectsWrongNonceAndMismatchedSourceAddress()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var search = NLControlLanDiscovery.SearchCoreAsync(timeout.Token, (IPEndPoint)socket.Client.LocalEndPoint!, true);
        var query = await socket.ReceiveAsync(timeout.Token);
        using var json = JsonDocument.Parse(query.Buffer);
        string nonce = json.RootElement.GetProperty("Nonce").GetString()!;
        async Task Send(string value, string host)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { Protocol = "NOVORA-CONTROL-DISCOVER/1", Nonce = value,
                Peer = new NLControlLanPeer("PC", host, 27215) });
            await socket.SendAsync(bytes, query.RemoteEndPoint, timeout.Token);
        }
        await Send(new string('Z', 32), "127.0.0.1");
        await Send(nonce, "192.168.1.10");
        Assert.Empty(await search);
    }

    [Fact]
    public async Task SearchHonorsCallerCancellation()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NLControlLanDiscovery.SearchCoreAsync(canceled.Token, new(IPAddress.Loopback, 27216), true));
    }
}
