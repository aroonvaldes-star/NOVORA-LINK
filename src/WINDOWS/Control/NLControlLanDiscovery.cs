using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NOVORA.Control;

public sealed record NLControlLanPeer(string Name, string Host, int Port, string Fingerprint = "")
{
    public void Validate(bool allowLoopback = false)
    {
        if (Name is not { Length: > 0 and <= 128 } || Port is < 1 or > 65535 ||
            Fingerprint is not { Length: 64 } || !Fingerprint.All(Uri.IsHexDigit) ||
            !IPAddress.TryParse(Host, out IPAddress? address))
            throw new InvalidDataException("Equipo LAN no válido.");
        NLControlLanInvitation.ValidateAddress(address, allowLoopback);
    }
}
public sealed record NLControlLanPairing(string Name, NLControlLanInvitation Invitation);

/// <summary>Discovery broadcasts public hints only. Pairing credentials are never sent over UDP.</summary>
public sealed class NLControlLanDiscovery : IAsyncDisposable
{
    public const int Port = 27216;
    private sealed record NLControlDiscoveryPacket(string Protocol, string Nonce,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] NLControlLanPeer? Peer = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] NLControlLanPairing? Pairing = null);
    private const string Protocol = "NOVORA-CONTROL-DISCOVER/2";
    private readonly NLControlLanPeer _peer;
    private readonly IPEndPoint _bind;
    private readonly bool _allowLoopback;
    private readonly bool _persistent;
    private readonly NLControlLanPeerGuard _peers = new(maxConcurrentPerPeer: 1, maxFailures: 0,
        maxPeers: 1024, packetBurst: 20, packetRefillPerSecond: 10);
    internal IPEndPoint LocalEndpoint => (IPEndPoint)_udp!.Client.LocalEndPoint!;
    private readonly CancellationTokenSource _stop = new();
    private int _disposed;
    private UdpClient? _udp;
    private Task _run = Task.CompletedTask;
    public NLControlLanDiscovery(NLControlLanPeer peer, bool persistent = false)
        : this(peer, new IPEndPoint(IPAddress.Any, Port), false, persistent) { }
    internal NLControlLanDiscovery(NLControlLanPeer peer, IPEndPoint bind, bool allowLoopback, bool persistent = false)
    { _peer = peer; _bind = bind; _allowLoopback = allowLoopback; _persistent = persistent; }
    public void Start()
    {
        if (_udp is not null || Volatile.Read(ref _disposed) != 0) throw new InvalidOperationException("La búsqueda ya fue iniciada o cerrada.");
        _peer.Validate(_allowLoopback);
        if (!_persistent) _stop.CancelAfter(TimeSpan.FromMinutes(2));
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
                    if (!_peers.TryAcquirePacket(received.RemoteEndPoint.Address)) continue;
                    var NLControlDiscoveryPacket = JsonSerializer.Deserialize<NLControlDiscoveryPacket>(received.Buffer);
                    if (NLControlDiscoveryPacket?.Protocol != Protocol || NLControlDiscoveryPacket.Peer is not null ||
                        NLControlDiscoveryPacket.Pairing is not null || NLControlDiscoveryPacket.Nonce is not { Length: 32 } ||
                        NLControlDiscoveryPacket.Code is not null || !NLControlDiscoveryPacket.Nonce.All(Uri.IsHexDigit)) continue;
                    byte[] reply = JsonSerializer.SerializeToUtf8Bytes(new NLControlDiscoveryPacket(
                        Protocol, NLControlDiscoveryPacket.Nonce, Peer: _peer));
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
                    if (NLControlDiscoveryPacket?.Protocol != Protocol || NLControlDiscoveryPacket.Nonce != nonce ||
                        NLControlDiscoveryPacket.Code is not null || NLControlDiscoveryPacket.Pairing is not null ||
                        NLControlDiscoveryPacket.Peer is not { } peer ||
                        !IPAddress.TryParse(peer.Host, out var address) || !address.Equals(response.RemoteEndPoint.Address)) continue;
                    peer.Validate(allowLoopback);
                    peers[peer.Host] = peer;
                    // Keep a short window for other PCs, then return the first
                    // discovery result without waiting for the full timeout.
                    if (peers.Count == 1)
                        timeout.CancelAfter(TimeSpan.FromMilliseconds(500));
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
