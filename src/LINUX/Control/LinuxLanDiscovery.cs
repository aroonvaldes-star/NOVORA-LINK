using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace NOVORA.Linux.Control;

internal sealed class LinuxLanDiscovery : IAsyncDisposable
{
    private readonly IPAddress _listenAddress;
    private readonly string _name;
    private readonly string _fingerprint;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _rateGate = new();
    private UdpClient? _udp;
    private Task _run = Task.CompletedTask;
    private double _tokens = 40;
    private long _lastTokenTimestamp = Stopwatch.GetTimestamp();

    public LinuxLanDiscovery(IPAddress listenAddress, string name, string fingerprint)
    {
        _listenAddress = listenAddress;
        _name = name;
        _fingerprint = fingerprint;
    }

    public void Start()
    {
        if (_udp is not null)
            throw new InvalidOperationException("La búsqueda LAN ya está iniciada.");
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, ControlProtocol.DiscoveryPort));
        _run = RespondAsync();
    }

    private async Task RespondAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                UdpReceiveResult received = await _udp!.ReceiveAsync(_stop.Token);
                if (received.Buffer.Length is 0 or > 1024 ||
                    !IsPrivateIpv4(received.RemoteEndPoint.Address) ||
                    !TryConsumeToken())
                    continue;

                DiscoveryPacket? query;
                try
                {
                    query = JsonSerializer.Deserialize<DiscoveryPacket>(received.Buffer);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (query is not { Protocol: ControlProtocol.DiscoveryName } ||
                    query.Nonce is not { Length: 32 } || !query.Nonce.All(Uri.IsHexDigit) ||
                    query.Code is not null || query.Peer is not null || query.Pairing is not null)
                    continue;

                IPAddress sourceAddress = GetRouteAddress(received.RemoteEndPoint);
                if (!sourceAddress.Equals(_listenAddress))
                    continue;

                var response = new DiscoveryPacket(
                    ControlProtocol.DiscoveryName,
                    query.Nonce,
                    Peer: new LanPeer(_name, sourceAddress.ToString(), ControlProtocol.Port, _fingerprint));
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(response);
                using var sender = new UdpClient(AddressFamily.InterNetwork);
                await sender.SendAsync(payload, received.RemoteEndPoint, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
        finally
        {
            _udp?.Dispose();
        }
    }

    private bool TryConsumeToken()
    {
        lock (_rateGate)
        {
            long now = Stopwatch.GetTimestamp();
            double elapsed = (now - _lastTokenTimestamp) / (double)Stopwatch.Frequency;
            _tokens = Math.Min(40, _tokens + elapsed * 20);
            _lastTokenTimestamp = now;
            if (_tokens < 1)
                return false;
            _tokens--;
            return true;
        }
    }

    private static IPAddress GetRouteAddress(IPEndPoint destination)
    {
        using var route = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        route.Connect(destination);
        return ((IPEndPoint)route.LocalEndPoint!).Address;
    }

    internal static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
            return false;
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _udp?.Dispose();
        await _run;
        _stop.Dispose();
    }
}
