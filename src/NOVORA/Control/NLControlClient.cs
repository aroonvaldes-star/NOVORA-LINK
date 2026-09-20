using System.Collections.Concurrent;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Net;
using System.Net.Sockets;

namespace NOVORA.Control;

public sealed class NLControlClient : IAsyncDisposable
{
    private readonly TcpClient _socket = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<NLControlReply>> _pending = new();
    private Stream? _stream;
    private long _nextId;
    private Task _reader = Task.CompletedTask;
    public event EventHandler<NLControlSnapshot>? StateChanged;
    public event EventHandler<string>? Disconnected;

    public async Task<NLControlReply> ConnectLanAsync(NLControlLanInvitation invitation, bool allowLoopback = false)
    {
        invitation.Validate(allowLoopback);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await _socket.ConnectAsync(IPAddress.Parse(invitation.Host), invitation.Port, deadline.Token);
            _socket.NoDelay = true;
            ConfigureLiveness();
            var tls = new SslStream(_socket.GetStream(), false, (_, certificate, _, _) =>
                certificate is not null && CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(certificate.GetRawCertData()), Convert.FromHexString(invitation.Fingerprint)));
            _stream = tls;
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            { TargetHost = "NOVORA", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, deadline.Token);
            // Pin validation completed before transmitting the secret.
            _reader = ReadLoopAsync();
            return await SendAsync("pair", code: invitation.Secret);
        }
        catch { _stop.Cancel(); _socket.Dispose(); _stream?.Dispose(); throw; }
    }

    public async Task<NLControlReply> ConnectTrustedAsync(NLControlTrustedPc pc, bool allowLoopback = false)
    {
        pc.Validate(allowLoopback);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await _socket.ConnectAsync(IPAddress.Parse(pc.Host), pc.Port, deadline.Token);
            _socket.NoDelay = true;
            ConfigureLiveness();
            var tls = new SslStream(_socket.GetStream(), false, (_, certificate, _, _) =>
                certificate is not null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.GetRawCertData()), Convert.FromHexString(pc.Fingerprint)));
            _stream = tls;
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            { TargetHost = "NOVORA", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, deadline.Token);
            _reader = ReadLoopAsync();
            var reply = await SendAsync("resume", value: pc.DeviceId, code: pc.Token);
            if (!reply.Success) throw new AuthenticationException("La PC rechazó la confianza guardada. Vincula de nuevo con QR.");
            return reply;
        }
        catch { _stop.Cancel(); _socket.Dispose(); _stream?.Dispose(); throw; }
    }

    private void ConfigureLiveness()
    {
        // Kernel probes detect an otherwise idle broken link; no application polling or command replay.
        _socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        try
        {
            _socket.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);
            _socket.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10);
            _socket.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
        }
        catch (Exception ex) when (ex is SocketException or PlatformNotSupportedException)
        { /* Fall back to OS keepalive defaults; a command still has its own deadline. */ }
    }

    public async Task<NLControlReply> SendAsync(string action, string? value = null, long revision = -1, string? code = null)
    {
        await _send.WaitAsync(_stop.Token);
        try
        {
            long id = Interlocked.Increment(ref _nextId);
            var source = new TaskCompletionSource<NLControlReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = source;
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var writeStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, deadline.Token);
                await NLControlProtocol.WriteAsync(_stream ?? throw new InvalidOperationException("Sin conexión."),
                    new NLControlRequest(NLControlProtocol.Version, id, action, value, revision, code), writeStop.Token);
                // The reader completes pending replies or errors. EOF must not race a reply already
                // received through a linked cancellation token and erase its confirmed result.
                return await source.Task.WaitAsync(deadline.Token);
            }
            catch
            {
                // A peer may reply and close while the write continuation is still finishing.
                if (source.Task.IsCompletedSuccessfully) return source.Task.Result;
                // A timed-out mutation has an unknown outcome. Close and require a fresh state; never retry it automatically.
                _stop.Cancel();
                _socket.Dispose();
                throw;
            }
            finally { _pending.TryRemove(id, out _); }
        }
        finally { _send.Release(); }
    }

    private async Task ReadLoopAsync()
    {
        string message = "Desconectado. Prepara un nuevo enlace en NOVORA PC.";
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var reply = await NLControlProtocol.ReadAsync<NLControlReply>(_stream ?? throw new InvalidOperationException("Sin conexión."), _stop.Token);
                if (reply.Version != NLControlProtocol.Version) throw new InvalidDataException("PC incompatible.");
                if (reply.Snapshot is not null) StateChanged?.Invoke(this, reply.Snapshot);
                if (reply.Id > 0 && _pending.TryGetValue(reply.Id, out var source)) source.TrySetResult(reply);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or SocketException or OperationCanceledException or ObjectDisposedException or System.Text.Json.JsonException)
        { if (!_stop.IsCancellationRequested) message = "Se perdió el enlace. Revisa NOVORA PC."; }
        finally
        {
            _stop.Cancel();
            _socket.Dispose();
            foreach (var pending in _pending.Values) pending.TrySetException(new IOException(message));
            Disconnected?.Invoke(this, message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _socket.Dispose();
        await _reader;
        _stream?.Dispose();
        // Synchronization primitives stay valid for in-flight request finally blocks.
    }
}
