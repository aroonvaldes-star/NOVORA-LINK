using System.Security.Cryptography;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using NOVORA.Control;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestAndroidFiles : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "novora-files-test-" + Guid.NewGuid().ToString("N"));
    private static string Message(NLControlFileMessage value) => JsonSerializer.Serialize(value);
    private static NLControlSnapshot State() => new(1, "Test PC", "1.4", "4M", "Balanced", "default", "default", false, [], [], []);

    [Fact]
    public async Task MaximumHexChunkFitsNestedControlFrameAndRoundtrips()
    {
        var data = Enumerable.Repeat((byte)255, NLControlFileReceiver.ChunkLength).ToArray();
        var value = Message(new(Guid.NewGuid().ToString("N"), Data: Convert.ToHexString(data)));
        var request = new NLControlRequest(1, 2, "file.chunk", value);
        using var stream = new MemoryStream();
        await NLControlProtocol.WriteAsync(stream, request, default);
        Assert.True(stream.Length <= NLControlProtocol.MaxFrame + 4);
        stream.Position = 0;
        var decoded = await NLControlProtocol.ReadAsync<NLControlRequest>(stream, default);
        var chunk = JsonSerializer.Deserialize<NLControlFileMessage>(decoded.Value!)!;
        Assert.Equal(data, Convert.FromHexString(chunk.Data!));
    }

    [Fact]
    public async Task AuthenticatedSocketTransferPublishesExactFile()
    {
        using var receiver = new NLControlFileReceiver(_root);
        await using var server = new NLControlServer("12345678", async request =>
            new NLControlReply(1, request.Id, true, request.Action == "pair" ? "paired" : await receiver.HandleAsync(request.Action, request.Value), State()), 0);
        server.Start();
        await using var client = new NLControlClient();
        Assert.True((await client.ConnectAsync("12345678", server.Port).WaitAsync(TimeSpan.FromSeconds(5))).Success);
        // Bytes that previously triggered worst-case base64 escaping must survive actual framing.
        byte[] payload = Enumerable.Range(0, 90000).Select(i => new byte[] { 0xfb, 0xef, 0xbe }[i % 3]).ToArray();
        await NLControlFileSender.SendAsync(new MemoryStream(payload), "video.mp4", payload.Length,
            (action, value) => client.SendAsync(action, value, 1)).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(_root, "Videos", "video.mp4")));
    }

    [Fact]
    public async Task UnpairedSocketCannotBeginTransfer()
    {
        bool called = false;
        await using var server = new NLControlServer("12345678", request =>
        { called = true; return Task.FromResult(new NLControlReply(1, request.Id, true, "unexpected")); }, 0);
        server.Start();
        using var socket = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.ConnectAsync(IPAddress.Loopback, server.Port, timeout.Token);
        await NLControlProtocol.WriteAsync(socket.GetStream(), new NLControlRequest(1, 1, "file.begin", Message(new(Guid.NewGuid().ToString("N"), "file.txt", 0))), timeout.Token);
        Assert.False((await NLControlProtocol.ReadAsync<NLControlReply>(socket.GetStream(), timeout.Token)).Success);
        Assert.False(called); Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task RoundtripVerifiesHashAndNeverOverwrites()
    {
        using var receiver = new NLControlFileReceiver(_root);
        byte[] data = RandomNumberGenerator.GetBytes(70001);
        async Task<NLControlReply> Send(string action, string value) => new(1, 1, true, await receiver.HandleAsync(action, value));
        await NLControlFileSender.SendAsync(new MemoryStream(data), "foto.png", data.Length, Send);
        await NLControlFileSender.SendAsync(new MemoryStream(data), "foto.png", data.Length, Send);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(_root, "Imagenes", "foto.png")));
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(_root, "Imagenes", "foto (1).png")));
        Assert.Empty(Directory.GetFiles(_root, "*.partial"));
    }
    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("C:\\escape.txt")]
    [InlineData("CON.txt")]
    [InlineData("name.txt:secret")]
    public async Task RejectsUntrustedPathsBeforeCreatingFile(string name)
    {
        using var receiver = new NLControlFileReceiver(_root);
        await Assert.ThrowsAsync<InvalidDataException>(() => receiver.HandleAsync("file.begin", Message(new(Guid.NewGuid().ToString("N"), name, 0))));
        Assert.False(Directory.Exists(_root));
    }
    [Fact]
    public async Task HashMismatchDeletesPartialAndPublishesNothing()
    {
        using var receiver = new NLControlFileReceiver(_root);
        string id = Guid.NewGuid().ToString("N");
        await receiver.HandleAsync("file.begin", Message(new(id, "test.txt", 3)));
        await receiver.HandleAsync("file.chunk", Message(new(id, Data: "616263")));
        await Assert.ThrowsAsync<InvalidDataException>(() => receiver.HandleAsync("file.end", Message(new(id, Sha256: new string('0', 64)))));
        Assert.Empty(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task CancelAndDisposeRemoveIncompleteFiles()
    {
        var receiver = new NLControlFileReceiver(_root);
        string id = Guid.NewGuid().ToString("N");
        await receiver.HandleAsync("file.begin", Message(new(id, "test.txt", 3)));
        await receiver.HandleAsync("file.cancel", Message(new(id)));
        Assert.Empty(Directory.GetFiles(_root));
        await receiver.HandleAsync("file.begin", Message(new(id, "test.txt", 3)));
        receiver.Dispose();
        Assert.Empty(Directory.GetFiles(_root));
    }
    [Fact]
    public async Task RejectsOutOfOrderBlockAndOversizeFile()
    {
        using var receiver = new NLControlFileReceiver(_root);
        string id = Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<InvalidDataException>(() => receiver.HandleAsync("file.begin", Message(new(id, "test.txt", NLControlFileReceiver.MaxLength + 1))));
        await receiver.HandleAsync("file.begin", Message(new(id, "test.txt", 3)));
        await Assert.ThrowsAsync<InvalidDataException>(() => receiver.HandleAsync("file.chunk", Message(new(id, Data: "616263", Offset: 1))));
        Assert.Empty(Directory.GetFiles(_root));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
