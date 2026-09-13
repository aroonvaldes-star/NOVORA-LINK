using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NOVORA.LinkEngine.Android.Discovery;

public sealed record DiscoveredPcNV(string InstanceIdNV, string NameNV, IPAddress AddressNV);

/* Búsqueda iniciada por el usuario: una ráfaga y callbacks de socket, sin polling persistente. */
public static class LanDiscoveryClientNV
{
    private const int PortNV = 27187;
    private const string QueryNV = "NOVORA-DISCOVER/1";

    public static async Task<IReadOnlyList<DiscoveredPcNV>> FindAsync(CancellationToken cancellationToken = default)
    {
        using var clientNV = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        clientNV.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        byte[] queryNV = Encoding.UTF8.GetBytes(QueryNV);
        await clientNV.SendAsync(queryNV, new IPEndPoint(IPAddress.Broadcast, PortNV), cancellationToken).ConfigureAwait(false);
        using var windowNV = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        windowNV.CancelAfter(TimeSpan.FromSeconds(2));
        var foundNV = new Dictionary<string, DiscoveredPcNV>(StringComparer.Ordinal);
        try
        {
            while (!windowNV.IsCancellationRequested)
            {
                UdpReceiveResult replyNV = await clientNV.ReceiveAsync(windowNV.Token).ConfigureAwait(false);
                using JsonDocument jsonNV = JsonDocument.Parse(replyNV.Buffer);
                JsonElement rootNV = jsonNV.RootElement;
                if (!rootNV.TryGetProperty("protocol", out JsonElement protocolNV) || protocolNV.GetInt32() != 1 ||
                    !rootNV.TryGetProperty("instanceId", out JsonElement idNV) || !rootNV.TryGetProperty("name", out JsonElement nameNV)) continue;
                string? id = idNV.GetString(); string? name = nameNV.GetString();
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) foundNV[id] = new DiscoveredPcNV(id, name, replyNV.RemoteEndPoint.Address);
            }
        }
        catch (OperationCanceledException) when (windowNV.IsCancellationRequested) { }
        return foundNV.Values.OrderBy(value => value.NameNV, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
