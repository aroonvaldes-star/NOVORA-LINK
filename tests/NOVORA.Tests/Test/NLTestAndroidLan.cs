using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using NOVORA.Control;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestAndroidLan
{
    private static NLControlSnapshot State => new(7, "PC", "1.4.0", "4M", "Gaming", "default", "default", true,
        [new("4M", "4 Mbps")], [new("Gaming", "Juegos")], [new("default", "Windows")]);
    private static NLControlLanInvitation Invitation => new(1, "192.168.1.10", 27215, new string('A', 64), new string('B', 64), DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds());

    [Fact]
    public void InvitationRoundTripsWithoutLosingSecret()
    { var invitation = Invitation; Assert.Equal(invitation, NLControlLanInvitation.Parse(invitation.Encode())); }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("novora://pair?data=@@@")]
    [InlineData("novora://pair?data=e30")]
    public void MalformedQrIsRejected(string value) => Assert.Throws<InvalidDataException>(() => NLControlLanInvitation.Parse(value));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("0.0.0.0")]
    public void NonPrivateQrEndpointIsRejected(string host) => Assert.Throws<InvalidDataException>(() => (Invitation with { Host = host }).Validate());

    [Fact]
    public void ExpiredAndExcessLifetimeInvitationsRejected()
    {
        Assert.Throws<InvalidDataException>(() => (Invitation with { ExpiresUnix = 0 }).Validate());
        Assert.Throws<InvalidDataException>(() => (Invitation with { ExpiresUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() }).Validate());
    }

    [Fact]
    public async Task PinnedTlsPairingAndRequestWork()
    {
        int invoked = 0;
        using var store = new NLControlTrustStore(Path.Combine(Path.GetTempPath(), "NOVORA-LanTest-" + Guid.NewGuid().ToString("N")));
        await using var server = new NLControlTrustServer(IPAddress.Loopback, request =>
        { Interlocked.Increment(ref invoked); return Task.FromResult(new NLControlReply(1, request.Id, true, "OK", State)); }, store, 0);
        server.Start();
        await using var client = new NLControlClient();
        Assert.True((await client.ConnectLanAsync(server.Invitation, true)).Success);
        Assert.True(server.IsAuthorized);
        Assert.True((await client.SendAsync("get")).Success);
        Assert.Equal(2, invoked);
    }

    [Fact]
    public async Task WrongFingerprintFailsBeforeAnyHandlerInvocation()
    {
        int invoked = 0;
        using var store = new NLControlTrustStore(Path.Combine(Path.GetTempPath(), "NOVORA-LanTest-" + Guid.NewGuid().ToString("N")));
        await using var server = new NLControlTrustServer(IPAddress.Loopback, request =>
        { Interlocked.Increment(ref invoked); return Task.FromResult(new NLControlReply(1, request.Id, true, "OK", State)); }, store, 0);
        server.Start();
        await using var client = new NLControlClient();
        await Assert.ThrowsAnyAsync<AuthenticationException>(() => client.ConnectLanAsync(server.Invitation with { Fingerprint = new string('0', 64) }, true));
        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task WrongSecretDoesNotRevealStateOrInvokeHandler()
    {
        int invoked = 0;
        using var store = new NLControlTrustStore(Path.Combine(Path.GetTempPath(), "NOVORA-LanTest-" + Guid.NewGuid().ToString("N")));
        await using var server = new NLControlTrustServer(IPAddress.Loopback, request =>
        { Interlocked.Increment(ref invoked); return Task.FromResult(new NLControlReply(1, request.Id, true, "OK", State)); }, store, 0);
        server.Start();
        await using var client = new NLControlClient();
        var reply = await client.ConnectLanAsync(server.Invitation with { Secret = new string('0', 64) }, true);
        Assert.False(reply.Success);
        Assert.Null(reply.Snapshot);
        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task TlsWithoutPairingCannotRunCommand()
    {
        int invoked = 0;
        using var store = new NLControlTrustStore(Path.Combine(Path.GetTempPath(), "NOVORA-LanTest-" + Guid.NewGuid().ToString("N")));
        await using var server = new NLControlTrustServer(IPAddress.Loopback, request =>
        { Interlocked.Increment(ref invoked); return Task.FromResult(new NLControlReply(1, request.Id, true, "OK", State)); }, store, 0);
        server.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = new TcpClient();
        await socket.ConnectAsync(IPAddress.Loopback, server.Port, deadline.Token);
        using var tls = new SslStream(socket.GetStream(), false, (_, _, _, _) => true);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "NOVORA" }, deadline.Token);
        await NLControlProtocol.WriteAsync(tls, new NLControlRequest(1, 1, "bitrate", "4M", 7), deadline.Token);
        var reply = await NLControlProtocol.ReadAsync<NLControlReply>(tls, deadline.Token);
        Assert.False(reply.Success);
        Assert.Null(reply.Snapshot);
        Assert.Equal(0, invoked);
    }
}
