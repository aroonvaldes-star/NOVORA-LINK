using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using NOVORA.Control;
using NOVORA.VisionEngine.Transport;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestVeLanChannel
{
    [Fact]
    public async Task LanChannelAuthenticatesPinnedTlsAndReturnsStream()
    {
        using var scope = new TrustScope();
        await using var channel = new VETransportLanChannel(
            IPAddress.Loopback, scope.Store, NLControlVeLanChannel.Video, allowLoopback: true);
        string sessionId = Guid.NewGuid().ToString("N");
        DateTimeOffset expires = DateTimeOffset.UtcNow.AddSeconds(20);
        NLControlVeLanEndpoint endpoint = channel.PrepareVE(sessionId, expires);
        Task<Stream> accept = channel.AcceptAsync(sessionId, endpoint, expires);

        using SslStream client = await ConnectAsync(endpoint, scope.Store.Fingerprint,
            new NLControlVeLanHello(1, sessionId, "VIDEO", endpoint.Token));
        await using Stream server = await accept.WaitAsync(TimeSpan.FromSeconds(5));

        byte[] sent = [1, 2, 3, 4];
        await client.WriteAsync(sent);
        byte[] received = new byte[sent.Length];
        await server.ReadExactlyAsync(received);
        Assert.Equal(sent, received);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("channel")]
    [InlineData("session")]
    public async Task LanChannelRejectsWrongTokenChannelAndSession(string fault)
    {
        using var scope = new TrustScope();
        await using var channel = new VETransportLanChannel(
            IPAddress.Loopback, scope.Store, NLControlVeLanChannel.Video, allowLoopback: true);
        string sessionId = Guid.NewGuid().ToString("N");
        DateTimeOffset expires = DateTimeOffset.UtcNow.AddSeconds(20);
        NLControlVeLanEndpoint endpoint = channel.PrepareVE(sessionId, expires);
        Task<Stream> accept = channel.AcceptAsync(sessionId, endpoint, expires);
        var hello = new NLControlVeLanHello(1,
            fault == "session" ? Guid.NewGuid().ToString("N") : sessionId,
            fault == "channel" ? "CONTROL" : "VIDEO",
            fault == "token" ? new string('0', 64) : endpoint.Token);

        using (SslStream rejected = await ConnectAsync(endpoint, scope.Store.Fingerprint, hello))
            Assert.Equal(0, await rejected.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(accept.IsCompleted);

        using SslStream valid = await ConnectAsync(endpoint, scope.Store.Fingerprint,
            new NLControlVeLanHello(1, sessionId, "VIDEO", endpoint.Token));
        await using Stream accepted = await accept.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(accepted.CanRead);
    }

    [Fact]
    public async Task LanChannelConsumesTokenOnce()
    {
        using var scope = new TrustScope();
        await using var channel = new VETransportLanChannel(
            IPAddress.Loopback, scope.Store, NLControlVeLanChannel.Control, allowLoopback: true);
        string sessionId = Guid.NewGuid().ToString("N");
        DateTimeOffset expires = DateTimeOffset.UtcNow.AddSeconds(20);
        NLControlVeLanEndpoint endpoint = channel.PrepareVE(sessionId, expires);
        Task<Stream> accept = channel.AcceptAsync(sessionId, endpoint, expires);
        using SslStream first = await ConnectAsync(endpoint, scope.Store.Fingerprint,
            new NLControlVeLanHello(1, sessionId, "CONTROL", endpoint.Token));
        await using Stream accepted = await accept.WaitAsync(TimeSpan.FromSeconds(5));

        using var replay = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() =>
            replay.ConnectAsync(IPAddress.Loopback, endpoint.Port).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task LanChannelRejectsAfterExpiry()
    {
        using var scope = new TrustScope();
        await using var channel = new VETransportLanChannel(
            IPAddress.Loopback, scope.Store, NLControlVeLanChannel.Audio, allowLoopback: true);
        string sessionId = Guid.NewGuid().ToString("N");
        DateTimeOffset expired = DateTimeOffset.UtcNow.AddSeconds(-1);
        NLControlVeLanEndpoint endpoint = channel.PrepareVE(sessionId, DateTimeOffset.UtcNow.AddSeconds(20));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            channel.AcceptAsync(sessionId, endpoint, expired));
    }

    [Fact]
    public async Task LanChannelDisposeCancelsAccept()
    {
        using var scope = new TrustScope();
        var channel = new VETransportLanChannel(
            IPAddress.Loopback, scope.Store, NLControlVeLanChannel.Video, allowLoopback: true);
        string sessionId = Guid.NewGuid().ToString("N");
        DateTimeOffset expires = DateTimeOffset.UtcNow.AddSeconds(20);
        NLControlVeLanEndpoint endpoint = channel.PrepareVE(sessionId, expires);
        Task<Stream> accept = channel.AcceptAsync(sessionId, endpoint, expires);

        await channel.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accept);
    }

    private static async Task<SslStream> ConnectAsync(
        NLControlVeLanEndpoint endpoint,
        string fingerprint,
        NLControlVeLanHello hello)
    {
        var socket = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        await socket.ConnectAsync(IPAddress.Loopback, endpoint.Port);
        var tls = new SslStream(socket.GetStream(), false, (_, certificate, _, _) =>
            certificate is not null && Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())) == fingerprint);
        try
        {
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "NOVORA",
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            });
            await NLControlProtocol.WriteAsync(tls, hello, default);
            return tls;
        }
        catch
        {
            tls.Dispose();
            socket.Dispose();
            throw;
        }
    }

    private sealed class TrustScope : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(),
            "NOVORA-VeLanChannelTest-" + Guid.NewGuid().ToString("N"));

        internal TrustScope() => Store = new NLControlTrustStore(_directory);

        internal NLControlTrustStore Store { get; }

        public void Dispose()
        {
            Store.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
