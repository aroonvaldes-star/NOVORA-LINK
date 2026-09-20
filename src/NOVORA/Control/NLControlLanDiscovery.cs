using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace NOVORA.Control;

public sealed record NLControlLanPeer(string Name, string Host, int Port);

/// <summary>Discovery is only a hint; TLS pin and secret must arrive through the PC invitation.</summary>
public sealed class NLControlLanDiscovery : IAsyncDisposable
{
    public const int Port = 27216;
    private sealed record NLControlDiscoveryPacket(string Protocol, string Nonce, NLControlLanPeer? Peer = null);
    private const string Protocol = "NOVORA-CONTROL-DISCOVER/1";
    private readonly NLControlLanPeer _peer;
    private readonly IPEndPoint _bind;
    private readonly bool _allowLoopback;
    internal IPEndPoint LocalEndpoint => (IPEndPoint)_udp!.Client.LocalEndPoint!;
    private readonly CancellationTokenSource _stop = new();
    private int _disposed;
    private UdpClient? _udp;
    private Task _run = Task.CompletedTask;
    public NLControlLanDiscovery(NLControlLanPeer peer) : this(peer, new IPEndPoint(IPAddress.Any, Port), false) { }
    internal NLControlLanDiscovery(NLControlLanPeer peer, IPEndPoint bind, bool allowLoopback)
    { _peer = peer; _bind = bind; _allowLoopback = allowLoopback; }
    public void Start()
    {
        if (_udp is not null || Volatile.Read(ref _disposed) != 0) throw new InvalidOperationException("La búsqueda ya fue iniciada o cerrada.");
        NLControlLanInvitation.ValidateAddress(IPAddress.Parse(_peer.Host), _allowLoopback);
        if (_peer.Name is not { Length: > 0 and <= 128 } || _peer.Port is < 1 or > 65535)
            throw new ArgumentException("Equipo LAN no válido.");
        _stop.CancelAfter(TimeSpan.FromMinutes(2));
        _udp = new UdpClient(_bind);
        _run = RespondAsync();
    }
    private async Task RespondAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var received = await _udp!.ReceiveAsync(_stop.Token);
                if (received.Buffer.Length > 1024) continue;
                try
                {
                    NLControlLanInvitation.ValidateAddress(received.RemoteEndPoint.Address, _allowLoopback);
                    var NLControlDiscoveryPacket = JsonSerializer.Deserialize<NLControlDiscoveryPacket>(received.Buffer);
                    if (NLControlDiscoveryPacket?.Protocol != Protocol || NLControlDiscoveryPacket.Peer is not null || NLControlDiscoveryPacket.Nonce is not { Length: 32 } || !NLControlDiscoveryPacket.Nonce.All(Uri.IsHexDigit)) continue;
                    byte[] reply = JsonSerializer.SerializeToUtf8Bytes(new NLControlDiscoveryPacket(Protocol, NLControlDiscoveryPacket.Nonce, _peer));
                    await _udp.SendAsync(reply, received.RemoteEndPoint, _stop.Token);
                }
                catch (Exception ex) when (ex is System.IO.InvalidDataException or JsonException) { }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        finally { _udp?.Dispose(); }
    }
    public static Task<IReadOnlyList<NLControlLanPeer>> SearchAsync(CancellationToken cancellationToken) =>
        SearchCoreAsync(cancellationToken, new IPEndPoint(IPAddress.Broadcast, Port), false);

    internal static async Task<IReadOnlyList<NLControlLanPeer>> SearchCoreAsync(
        CancellationToken cancellationToken, IPEndPoint destination, bool allowLoopback)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        byte[] query = JsonSerializer.SerializeToUtf8Bytes(new NLControlDiscoveryPacket(Protocol, nonce));
        var peers = new Dictionary<string, NLControlLanPeer>();
        await udp.SendAsync(query, destination, timeout.Token);
        try
        {
            while (peers.Count < 32)
            {
                var response = await udp.ReceiveAsync(timeout.Token);
                if (response.Buffer.Length > 1024) continue;
                try
                {
                    var NLControlDiscoveryPacket = JsonSerializer.Deserialize<NLControlDiscoveryPacket>(response.Buffer);
                    if (NLControlDiscoveryPacket?.Protocol != Protocol || NLControlDiscoveryPacket.Nonce != nonce || NLControlDiscoveryPacket.Peer is not { } peer ||
                        peer.Name is not { Length: > 0 and <= 128 } || peer.Port is < 1 or > 65535 ||
                        !IPAddress.TryParse(peer.Host, out var address) || !address.Equals(response.RemoteEndPoint.Address)) continue;
                    NLControlLanInvitation.ValidateAddress(address, allowLoopback);
                    peers[peer.Host] = peer;
                }
                catch (Exception ex) when (ex is System.IO.InvalidDataException or JsonException) { }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        return peers.Values.ToArray();
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _udp?.Dispose();
        await _run;
        _stop.Dispose();
    }
}
