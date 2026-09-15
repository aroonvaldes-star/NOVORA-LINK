using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace NOVORA.Control;

/// <summary>One explicitly authorized USB session. Code expires in two minutes, five attempts maximum.</summary>
public sealed class NLControlServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<NLControlRequest, Task<NLControlReply>> _handle;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationTokenSource _invitation = new(TimeSpan.FromMinutes(2));
    private readonly Channel<NLControlReply> _outgoing = Channel.CreateBounded<NLControlReply>(32);
    private readonly byte[] _codeHash;
    private Task _run = Task.CompletedTask;
    private TcpClient? _client;
    private volatile bool _authorized;
    private bool _consumed;
    private volatile bool _closed;
    public bool IsAuthorized => _authorized;
    public bool IsClosed => _closed;
    public event EventHandler<string>? StatusChanged;
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public NLControlServer(string code, Func<NLControlRequest, Task<NLControlReply>> handle, int port = NLControlProtocol.Port)
    {
        _codeHash = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        _handle = handle;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public static string NewCode() => RandomNumberGenerator.GetInt32(10000000, 100000000).ToString();
    public void Start() { _listener.Start(2); _run = RunAsync(); }
    public void Publish(NLControlSnapshot snapshot)
    {
        if (!_authorized) return;
        if (!_outgoing.Writer.TryWrite(new(NLControlProtocol.Version, 0, true, "Estado actualizado.", snapshot)))
            _client?.Dispose(); // A slow consumer must reconnect rather than receive silently dropped confirmations.
    }

    private async Task RunAsync()
    {
        using var pairing = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, _invitation.Token);
        try
        {
            for (int attempt = 0; attempt < 5 && !_consumed; attempt++)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(pairing.Token);
                _client = client;
                client.NoDelay = true;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(pairing.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    var hello = await NLControlProtocol.ReadAsync<NLControlRequest>(client.GetStream(), timeout.Token);
                    bool valid = hello.Version == NLControlProtocol.Version && hello.Action == "pair" &&
                        hello.Id > 0 && hello.Code is { Length: 8 } &&
                        CryptographicOperations.FixedTimeEquals(_codeHash,
                            SHA256.HashData(Encoding.UTF8.GetBytes(hello.Code)));
                    if (!valid)
                    {
                        await NLControlProtocol.WriteAsync(client.GetStream(),
                            new NLControlReply(NLControlProtocol.Version, hello.Id, false, "Código o protocolo no válido."), timeout.Token);
                        continue;
                    }
                    _authorized = true;
                    _consumed = true;
                    _listener.Stop();
                    StatusChanged?.Invoke(this, "Android autorizado por USB.");
                    Task writer = WriteLoopAsync(client);
                    try
                    {
                        await _outgoing.Writer.WriteAsync(await _handle(hello), _stop.Token);
                        long lastId = hello.Id;
                        while (!_stop.IsCancellationRequested)
                        {
                            var request = await NLControlProtocol.ReadAsync<NLControlRequest>(client.GetStream(), _stop.Token);
                            if (request.Version != NLControlProtocol.Version || request.Id <= lastId || request.Code is not null)
                                throw new InvalidDataException("Secuencia o versión no válida.");
                            lastId = request.Id;
                            var reply = await _handle(request);
                            await _outgoing.Writer.WriteAsync(reply, _stop.Token);
                        }
                    }
                    finally
                    {
                        _authorized = false;
                        _outgoing.Writer.TryComplete();
                        client.Dispose();
                        try { await writer; } catch (Exception) when (_stop.IsCancellationRequested) { }
                    }
                }
                catch (Exception ex) when (!_authorized &&
                    (ex is ChannelClosedException or InvalidDataException or IOException or OperationCanceledException or System.Text.Json.JsonException or SocketException))
                { if (pairing.IsCancellationRequested) break; }
            }
        }
        catch (Exception ex) when (ex is ChannelClosedException or InvalidDataException or IOException or SocketException or OperationCanceledException or ObjectDisposedException or System.Text.Json.JsonException) { }
        finally
        {
            _authorized = false;
            _closed = true;
            _listener.Stop();
            _client = null;
            StatusChanged?.Invoke(this, "Sesión cerrada. Prepara un nuevo enlace USB para reconectar.");
        }
    }

    private async Task WriteLoopAsync(TcpClient client)
    {
        try
        {
            await foreach (var reply in _outgoing.Reader.ReadAllAsync(_stop.Token))
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                await NLControlProtocol.WriteAsync(client.GetStream(), reply, deadline.Token);
            }
        }
        finally { _outgoing.Writer.TryComplete(); client.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        _client?.Dispose();
        _outgoing.Writer.TryComplete();
        try { await _run; } catch (Exception ex) when (ex is ChannelClosedException or InvalidDataException or IOException or OperationCanceledException or ObjectDisposedException or SocketException) { }
        _invitation.Dispose();
        _stop.Dispose();
    }
}
