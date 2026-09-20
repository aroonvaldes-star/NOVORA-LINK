using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using NOVORA.Control;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestAndroidControl
{
    private static NLControlSnapshot State(long revision = 7) => new(revision, "Test PC", "1.4.0", "4M", "Gaming",
        "default", "default", true, [new("4M", "4 Mbps"), new("8M", "8 Mbps")],
        [new("Gaming", "Juegos"), new("Balanced", "Equilibrado")], [new("default", "Windows")]);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65537)]
    public async Task FrameLengthIsBoundedBeforeReadingPayload(int length)
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, length);
        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(() => NLControlProtocol.ReadAsync<NLControlRequest>(stream, default));
    }

    [Theory]
    [InlineData("bitrate", "999M", 7)]
    [InlineData("audio", "not-an-output", 7)]
    [InlineData("profile", "99", 7)]
    [InlineData("shell", "anything", 7)]
    [InlineData("bitrate", "8M", 6)]
    public void InvalidOrStaleChangesAreRejected(string action, string value, long revision)
        => Assert.NotNull(NLControlCommands.Validate(new(1, 2, action, value, revision), State()));

    [Fact]
    public void ValidAdvertisedValueIsAccepted()
        => Assert.Null(NLControlCommands.Validate(new(1, 2, "bitrate", "8M", 7), State()));

    [Fact]
    public async Task PlainSocketCommandsDoNotReachPcHandler()
    {
        int invoked = 0;
        await using var server = new NLTestControlTrustHarness(request =>
        {
            Interlocked.Increment(ref invoked);
            return Task.FromResult(new NLControlReply(1, request.Id, true, "ok", State()));
        });
        using var socket = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.ConnectAsync(IPAddress.Loopback, server.Server.Port, timeout.Token);
        await NLControlProtocol.WriteAsync(socket.GetStream(), new NLControlRequest(1, 1, "bitrate", "8M", 7), timeout.Token);
        await Assert.ThrowsAnyAsync<Exception>(() => NLControlProtocol.ReadAsync<NLControlReply>(socket.GetStream(), timeout.Token));
        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task WrongInvitationSecretCannotReadState()
    {
        int invoked = 0;
        await using var server = new NLTestControlTrustHarness(_ =>
        {
            Interlocked.Increment(ref invoked);
            throw new Exception("Must not execute");
        });
        var bad = server.Server.Invitation with { Secret = new string('0', 64) };
        await using var client = new NLControlClient();
        var reply = await client.ConnectLanAsync(bad, true).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(reply.Success);
        Assert.Null(reply.Snapshot);
        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task AuthorizedClientReceivesRealRepliesAndUnsolicitedState()
    {
        NLControlSnapshot state = State();
        await using var server = new NLTestControlTrustHarness(request =>
        {
            string? error = NLControlCommands.Validate(request, state);
            if (error is null && request.Action == "bitrate") state = state with { Bitrate = request.Value!, Revision = 8 };
            return Task.FromResult(new NLControlReply(1, request.Id, error is null, error ?? "ok", state));
        });
        await using var client = new NLControlClient();
        var paired = await server.ConnectAsync(client).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(paired.Success);
        Assert.Equal("4M", paired.Snapshot!.Bitrate);
        var changed = await client.SendAsync("bitrate", "8M", paired.Snapshot.Revision).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(changed.Success);
        Assert.Equal("8M", changed.Snapshot!.Bitrate);
        var stale = await client.SendAsync("bitrate", "4M", 7).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stale.Success);
        var observed = new TaskCompletionSource<NLControlSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StateChanged += (_, updated) => { if (updated.Revision == 9) observed.TrySetResult(updated); };
        server.Server.Publish(state with { Revision = 9, Bitrate = "4M" });
        Assert.Equal("4M", (await observed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Bitrate);
    }

    [Fact]
    public async Task RevocationDisconnectsClient()
    {
        var server = new NLTestControlTrustHarness(r => Task.FromResult(new NLControlReply(1, r.Id, true, "ok", State())));
        await using var client = new NLControlClient();
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += (_, _) => closed.TrySetResult(true);
        await server.ConnectAsync(client).WaitAsync(TimeSpan.FromSeconds(5));
        await server.DisposeAsync();
        Assert.True(await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task RepeatedRequestIdCannotMutateTwice()
    {
        int invoked = 0;
        await using var server = new NLTestControlTrustHarness(request =>
        {
            Interlocked.Increment(ref invoked);
            return Task.FromResult(new NLControlReply(1, request.Id, true, "ok", State()));
        });
        using var socket = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.ConnectAsync(IPAddress.Loopback, server.Server.Port, timeout.Token);
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await NLControlProtocol.WriteAsync(socket.GetStream(), new NLControlRequest(1, 1, "pair", Code: server.Server.Invitation.Secret), timeout.Token);
            await NLControlProtocol.ReadAsync<NLControlReply>(socket.GetStream(), timeout.Token);
        });
        Assert.Equal(0, invoked);
    }
}
