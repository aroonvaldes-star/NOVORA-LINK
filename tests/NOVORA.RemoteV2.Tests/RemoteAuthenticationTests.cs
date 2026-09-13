using System.Net;
using System.Net.Sockets;
using NOVORA.Remote;
using AndroidProtocol = NOVORA.LinkEngine.Android.Remote.ProtocolRemoteNV;
using AndroidSession = NOVORA.LinkEngine.Android.Remote.SessionRemoteNV;
using Xunit;

namespace NOVORA.RemoteV2.Tests;

public sealed class RemoteAuthenticationTests
{
    [Theory]
    [InlineData("NOVORA-REMOTE|2|HELLO")]
    [InlineData("NOVORA-REMOTE|1|HELLO")]
    [InlineData("NOVORA-REMOTE|2|HELLO|WRONG")]
    public async Task Server_rejects_unauthorized_clients_before_commands(string hello)
    {
        int commands = 0;
        await using var server = new ServerRemoteNV((_, _, _) =>
        {
            Interlocked.Increment(ref commands);
            return Task.FromResult(ResultRemoteNV.OkNV("ok"));
        }, 0);
        await server.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.BoundPortNV, timeout.Token);
        using var stream = client.GetStream();
        await ProtocolRemoteNV.WriteFrameNVAsync(stream, hello, timeout.Token);
        // EOF o cierre de conexión: nunca WELCOME ni ejecución de comandos.
        try
        {
            byte[] data = new byte[1];
            Assert.Equal(0, await stream.ReadAsync(data, timeout.Token));
        }
        catch (IOException) { }
        Assert.Equal(0, commands);
        Assert.False(server.HasClientNV);
    }

    [Fact]
    public async Task Android_hello_authorizes_command_and_restart_revokes_old_token()
    {
        int commands = 0;
        await using var server = new ServerRemoteNV((_, _, _) =>
        {
            Interlocked.Increment(ref commands);
            return Task.FromResult(ResultRemoteNV.OkNV("accepted"));
        }, 0);
        await server.StartAsync();
        string oldToken = server.SessionTokenNV;
        Assert.Equal(64, oldToken.Length);
        Assert.True(oldToken.All(Uri.IsHexDigit));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.BoundPortNV, timeout.Token);
        using var stream = client.GetStream();
        await AndroidProtocol.WriteFrameNVAsync(stream, AndroidProtocol.CreateHelloNV(oldToken), timeout.Token);
        Assert.True(AndroidProtocol.IsWelcomeNV(await AndroidProtocol.ReadFrameNVAsync(stream, timeout.Token)));
        Assert.True(server.HasClientNV);
        await ProtocolRemoteNV.WriteFrameNVAsync(stream,
            ProtocolRemoteNV.CreateCommandNV("test", CommandRemoteNV.Status), timeout.Token);
        string response = await ProtocolRemoteNV.ReadFrameNVAsync(stream, timeout.Token);
        Assert.True(AndroidProtocol.TryParseResultNV(response, "test", out var result, out var error), error);
        Assert.True(result.Success);
        Assert.Equal("accepted", result.Message);
        Assert.Equal(1, commands);
        await server.StopAsync();
        Assert.Empty(server.SessionTokenNV);
        await server.StartAsync();
        Assert.NotEqual(oldToken, server.SessionTokenNV);
        Assert.False(ProtocolRemoteNV.IsHelloNV(AndroidProtocol.CreateHelloNV(oldToken), server.SessionTokenNV));
        using var staleClient = new TcpClient();
        await staleClient.ConnectAsync(IPAddress.Loopback, server.BoundPortNV, timeout.Token);
        using var staleStream = staleClient.GetStream();
        await AndroidProtocol.WriteFrameNVAsync(staleStream, AndroidProtocol.CreateHelloNV(oldToken), timeout.Token);
        try { Assert.Equal(0, await staleStream.ReadAsync(new byte[1], timeout.Token)); }
        catch (IOException) { }
        Assert.Equal(1, commands);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("000000000000000000000000000000000000000000000000000000000000000Z")]
    public void Android_rejects_invalid_credentials(string? token)
    {
        Assert.False(AndroidSession.SetTokenNV(token));
        Assert.Throws<InvalidOperationException>(() => AndroidProtocol.CreateHelloNV(token!));
    }
}
