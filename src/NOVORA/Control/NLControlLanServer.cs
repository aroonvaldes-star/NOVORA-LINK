using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace NOVORA.Control;

/// <summary>One explicitly authorized LAN session. Code expires in two minutes, five attempts maximum.</summary>
public sealed class NLControlLanServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<NLControlRequest, Task<NLControlReply>> _handle;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationTokenSource _invitation = new();
    private int _disposed;
    private bool _started;
    private readonly Channel<NLControlReply> _outgoing = Channel.CreateBounded<NLControlReply>(32);
    private readonly byte[] _codeHash;
    private readonly X509Certificate2 _certificate;
    private readonly string _secret;
    private readonly IPAddress _address;
    public NLControlLanInvitation Invitation { get; private set; } = null!;
    private Task _run = Task.CompletedTask;
    private TcpClient? _client;
    private volatile bool _authorized;
    private bool _consumed;
    public bool IsAuthorized => _authorized;
    public bool IsClosed { get; private set; }
    public event EventHandler<string>? StatusChanged;
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public NLControlLanServer(IPAddress address, Func<NLControlRequest, Task<NLControlReply>> handle, int port = 27215)
    {
        NLControlLanInvitation.ValidateAddress(address, allowLoopback: true);
        _address = address;
        _secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _codeHash = SHA256.HashData(Encoding.UTF8.GetBytes(_secret));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=NOVORA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        // Schannel cannot use CreateSelfSigned's ephemeral key. A temporary user key container
        // from PKCS#12 is removed with certificate disposal (PersistKeySet is deliberately absent).
        byte[] pfx = generated.Export(X509ContentType.Pfx);
        try { _certificate = new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.UserKeySet); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
        _handle = handle;
        _listener = new TcpListener(address, port);
    }
    public void Start()
    {
        if (_started || Volatile.Read(ref _disposed) != 0) throw new InvalidOperationException("La invitación ya fue iniciada o cerrada.");
        _started = true;
        _listener.Start(2);
        _invitation.CancelAfter(TimeSpan.FromMinutes(2));
        Invitation = new(1, _address.ToString(), Port, Convert.ToHexString(SHA256.HashData(_certificate.RawData)),
            _secret, DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds());
        _run = RunAsync();
    }
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
                using var stream = new SslStream(client.GetStream(), false);
                _client = client;
                client.NoDelay = true;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(pairing.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    { ServerCertificate = _certificate, ClientCertificateRequired = false,
                      EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, timeout.Token);
                    var hello = await NLControlProtocol.ReadAsync<NLControlRequest>(stream, timeout.Token);
                    bool valid = hello.Version == NLControlProtocol.Version && hello.Action == "pair" &&
                        hello.Id > 0 && hello.Code is { Length: 64 } &&
                        CryptographicOperations.FixedTimeEquals(_codeHash,
                            SHA256.HashData(Encoding.UTF8.GetBytes(hello.Code)));
                    if (!valid)
                    {
                        await NLControlProtocol.WriteAsync(stream,
                            new NLControlReply(NLControlProtocol.Version, hello.Id, false, "Código o protocolo no válido."), timeout.Token);
                        continue;
                    }
                    _authorized = true;
                    _consumed = true;
                    _listener.Stop();
                    StatusChanged?.Invoke(this, "Android autorizado por LAN.");
                    Task writer = WriteLoopAsync(client, stream);
                    try
                    {
                        await _outgoing.Writer.WriteAsync(await _handle(hello), _stop.Token);
                        long lastId = hello.Id;
                        while (!_stop.IsCancellationRequested)
                        {
                            var request = await NLControlProtocol.ReadAsync<NLControlRequest>(stream, _stop.Token);
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
                    (ex is AuthenticationException or ChannelClosedException or InvalidDataException or IOException or OperationCanceledException or System.Text.Json.JsonException or SocketException))
                { if (pairing.IsCancellationRequested) break; }
            }
        }
        catch (Exception ex) when (ex is AuthenticationException or ChannelClosedException or InvalidDataException or IOException or SocketException or OperationCanceledException or ObjectDisposedException or System.Text.Json.JsonException) { }
        finally
        {
            _authorized = false;
            _listener.Stop();
            _client = null;
            IsClosed = true;
            StatusChanged?.Invoke(this, "Sesión cerrada. Prepara un nuevo enlace LAN para reconectar.");
        }
    }

    private async Task WriteLoopAsync(TcpClient client, Stream stream)
    {
        try
        {
            await foreach (var reply in _outgoing.Reader.ReadAllAsync(_stop.Token))
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                await NLControlProtocol.WriteAsync(stream, reply, deadline.Token);
            }
        }
        finally { _outgoing.Writer.TryComplete(); client.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _listener.Stop();
        _client?.Dispose();
        _outgoing.Writer.TryComplete();
        try { await _run; } catch (Exception ex) when (ex is AuthenticationException or ChannelClosedException or InvalidDataException or IOException or OperationCanceledException or ObjectDisposedException or SocketException) { }
        _certificate.Dispose();
        _invitation.Dispose();
        _stop.Dispose();
    }
}
