using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NOVORA.Discovery;

/*
 * Descubrimiento local solamente. No abre RemoteNV ni entrega credenciales:
 * emparejar/autenticar sigue siendo obligatorio antes de aceptar comandos.
 */
public sealed class LanDiscoveryResponderNV : IAsyncDisposable
{
    public const int PortNV = 27187;
    private const string QueryNV = "NOVORA-DISCOVER/1";
    private readonly UdpClient _socketNV = new(AddressFamily.InterNetwork);
    private readonly CancellationTokenSource _stopNV = new();
    private readonly string _instanceIdNV = CreateInstanceIdNV();
    private Task? _receiveTaskNV;

    public void StartNV()
    {
        if (_receiveTaskNV is not null) return;
        _socketNV.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socketNV.Client.Bind(new IPEndPoint(IPAddress.Any, PortNV));
        _receiveTaskNV = ReceiveLoopNVAsync(_stopNV.Token);
    }

    private async Task ReceiveLoopNVAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult received = await _socketNV.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (!string.Equals(Encoding.UTF8.GetString(received.Buffer), QueryNV, StringComparison.Ordinal)) continue;
                var response = new { protocol = 1, instanceId = _instanceIdNV, name = Environment.MachineName, pairingRequired = true };
                byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response));
                await _socketNV.SendAsync(payload, received.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _stopNV.Cancel();
        _socketNV.Dispose();
        if (_receiveTaskNV is not null) try { await _receiveTaskNV.ConfigureAwait(false); } catch { }
        _stopNV.Dispose();
    }

    private static string CreateInstanceIdNV()
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{Environment.MachineName}|{Environment.UserName}"));
        return Convert.ToHexString(bytes.AsSpan(0, 12));
    }
}
