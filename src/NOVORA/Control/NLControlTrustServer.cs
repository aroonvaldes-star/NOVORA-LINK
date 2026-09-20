using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
namespace NOVORA.Control;
/// <summary>Persistent pinned TLS listener. New trust is granted only by a fresh, single-use QR session.</summary>
public sealed class NLControlTrustServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly IPAddress _address;
    private readonly Func<NLControlRequest, Task<NLControlReply>> _handler;
    private readonly NLControlTrustStore _store;
    private readonly CancellationTokenSource _stop = new();
    private System.Threading.Timer? _expiry;
    private readonly string _secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly bool _allowPairing;
    private readonly Func<bool>? _allowRemember;
    private readonly string Transport;
    private Channel<NLControlReply>? _outgoing;
    private TcpClient? _client;
    private Task _run = Task.CompletedTask;
    private bool _started;
    private int _consumed, _disposed;
    private volatile bool _authorized;
    public NLControlLanInvitation Invitation { get; private set; } = null!;
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public bool IsAuthorized => _authorized;
    public bool IsClosed { get; private set; }
    public bool IsInvitationOpen => _started && !IsClosed && _allowPairing && Volatile.Read(ref _consumed) == 0 && DateTimeOffset.UtcNow.ToUnixTimeSeconds() < Invitation.ExpiresUnix;
    public event EventHandler<string>? StatusChanged;
    public NLControlTrustServer(IPAddress address, Func<NLControlRequest, Task<NLControlReply>> handler, NLControlTrustStore store, int port = 27215, bool allowPairing = true, Func<bool>? allowRemember = null, string transport = "LAN")
    {
        NLControlLanInvitation.ValidateAddress(address, true);
        _address = address; _handler = handler; _store = store; _allowPairing = allowPairing;
        _allowRemember = allowRemember;
        if (transport is not ("USB" or "LAN") || transport == "USB" && (!IPAddress.IsLoopback(address) || port != NLControlProtocol.Port)) throw new ArgumentException("Transporte o destino USB inválido.");
        Transport = transport;
        _listener = new TcpListener(address, port);
    }
    public void Start()
    {
        if (_started || Volatile.Read(ref _disposed) != 0) throw new InvalidOperationException("Servidor ya iniciado o cerrado.");
        _listener.Start(2); _started = true;
        Invitation = new(1, _address.ToString(), Port, _store.Fingerprint, _secret, DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds());
        if (_allowPairing) _expiry = new System.Threading.Timer(_ => { if (!_authorized && Interlocked.Exchange(ref _consumed, 1) == 0) StatusChanged?.Invoke(this, "Invitación vencida; confianza previa disponible."); }, null, TimeSpan.FromMinutes(2), Timeout.InfiniteTimeSpan);
        _run = RunAsync();
    }
    public void CancelInvitation() { Interlocked.Exchange(ref _consumed, 1); _expiry?.Change(Timeout.Infinite, Timeout.Infinite); }
    public void Publish(NLControlSnapshot snapshot)
    {
        if (_authorized && _outgoing is { } channel && !channel.Writer.TryWrite(new(1, 0, true, "Estado actualizado.", snapshot))) _client?.Dispose();
    }
    private async Task RunAsync()
    {
        int failures = 0;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _client = client; client.NoDelay = true;
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                try
                {
                    client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);
                    client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10);
                    client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
                }
                catch (Exception ex) when (ex is SocketException or PlatformNotSupportedException) { }

                bool authenticated = false;
                try { authenticated = await ServeAsync(client); }
                catch (Exception ex) when (ex is IOException or AuthenticationException or InvalidDataException or OperationCanceledException or SocketException or ObjectDisposedException or System.Text.Json.JsonException or ChannelClosedException) { }
                finally { _authorized = false; _client = null; _outgoing = null; }
                if (_stop.IsCancellationRequested) break;
                if (!authenticated)
                {
                    failures = Math.Min(failures + 1, 5);
                    if (failures >= 5) CancelInvitation();
                    // Bounded failure backoff only; no timer polls when idle or authenticated.
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(failures, 5)), _stop.Token);
                }
                else failures = 0;
                StatusChanged?.Invoke(this, $"{Transport} disponible para dispositivos de confianza.");
            }
        }
        catch (InvalidOperationException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        finally { IsClosed = true; _authorized = false; _listener.Stop(); StatusChanged?.Invoke(this, $"Control {Transport} detenido."); }
    }
    private async Task<bool> ServeAsync(TcpClient client)
    {
        using var stream = new SslStream(client.GetStream(), false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        { ServerCertificate = _store.Certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, timeout.Token);
        var hello = await NLControlProtocol.ReadAsync<NLControlRequest>(stream, timeout.Token);
        bool paired = hello.Action == "pair" && IsInvitationOpen && hello.Code is { Length:64 } &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(hello.Code)), SHA256.HashData(Encoding.UTF8.GetBytes(_secret)));
        bool resumed = hello.Action == "resume" && _store.Authenticate(hello.Value, hello.Code);
        if (hello.Version != 1 || hello.Id <= 0 || string.IsNullOrEmpty(hello.Action) || !(paired || resumed))
        { await NLControlProtocol.WriteAsync(stream, new NLControlReply(1, hello.Id, false, "Autorización rechazada. Prepara un enlace nuevo en PC."), timeout.Token); return false; }
        if (paired) CancelInvitation();
        string? deviceId = resumed ? hello.Value : null;
        bool enrollmentUsed = false;
        var channel = Channel.CreateBounded<NLControlReply>(32);
        _outgoing = channel; _authorized = true;
        StatusChanged?.Invoke(this, resumed ? $"Android de confianza conectado por {Transport}." : $"Android autorizado por {Transport}.");
        Task writer = WriteAsync(client, stream, channel);
        try
        {
            await channel.Writer.WriteAsync(await _handler(hello with { Action = "pair", Code = null, Value = null }), _stop.Token);
            long lastId = hello.Id;
            while (!_stop.IsCancellationRequested)
            {
                var request = await NLControlProtocol.ReadAsync<NLControlRequest>(stream, _stop.Token);
                if (request.Version != 1 || request.Id <= lastId || request.Code is not null || string.IsNullOrEmpty(request.Action) || request.Action.Length > 64) throw new InvalidDataException("Secuencia inválida.");
                lastId = request.Id;
                if (deviceId is not null && !_store.Contains(deviceId)) throw new AuthenticationException("Confianza revocada.");
                NLControlReply reply;
                if (request.Action == "trust.enroll")
                {
                    if (!paired || enrollmentUsed || _allowRemember?.Invoke() == false) reply = new(1, request.Id, false, "Recordar no está habilitado o requiere un enlace nuevo.");
                    else
                    {
                        try
                        {
                            var grant = _store.Enroll(request.Value ?? "");
                            deviceId = grant.Id; enrollmentUsed = true;
                            reply = new(1, request.Id, true, "PC guardada como confiable.", TrustedPc: new(Environment.MachineName, _address.ToString(), Port, _store.Fingerprint, grant.Id, grant.Token, Transport));
                        }
                        catch (InvalidDataException ex) { reply = new(1, request.Id, false, ex.Message); }
                    }
                }
                else if (request.Action is "pair" or "resume" || request.Action.StartsWith("trust.", StringComparison.Ordinal)) reply = new(1, request.Id, false, "Acción de confianza no permitida.");
                else reply = await _handler(request);
                await channel.Writer.WriteAsync(reply, _stop.Token);
            }
        }
        catch (Exception ex) when (ex is IOException or AuthenticationException or InvalidDataException or OperationCanceledException or SocketException or ObjectDisposedException or System.Text.Json.JsonException or ChannelClosedException) { }
        finally
        {
            _authorized = false; channel.Writer.TryComplete(); client.Dispose();
            try { await writer; } catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException or ChannelClosedException) { }
        }
        return true;
    }
    private async Task WriteAsync(TcpClient client, Stream stream, Channel<NLControlReply> channel)
    {
        try
        {
            await foreach (var reply in channel.Reader.ReadAllAsync(_stop.Token))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await NLControlProtocol.WriteAsync(stream, reply, timeout.Token);
            }
        }
        finally { channel.Writer.TryComplete(); client.Dispose(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_expiry is not null) await _expiry.DisposeAsync();
        _stop.Cancel(); _listener.Stop(); _client?.Dispose(); _outgoing?.Writer.TryComplete();
        await _run; _stop.Dispose();
    }
}
