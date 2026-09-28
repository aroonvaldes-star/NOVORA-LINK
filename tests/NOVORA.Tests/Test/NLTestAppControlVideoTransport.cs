using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NOVORA.Control;
using NOVORA.Service;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Transport;
using NOVORA.VisionEngine.Control;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestAppControlVideoTransport
{
    [Fact]
    public async Task VisionEngine_runs_from_an_AppControl_stream_without_scrcpy_transport()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var sender = new TcpClient();
        Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
        await sender.ConnectAsync(IPAddress.Loopback, port);
        using TcpClient receiver = await accepting;
        await using MemoryStream control = new();
        await using MemoryStream audio = CreateRawAudioStream();
        listener.Stop();

        var writer = new VEProtocolWriter(sender.GetStream());
        await writer.WriteVideoSessionAsync(
            VEProtocolCodec.H264,
            new VEProtocolSession(720, 1280, false));

        await using var engine = new VECoreEngine(new NLServiceNovoraPaths());
        Assert.True((await engine.InitializeAsync()).Success);

        var started = await engine.StartAppControlAsync(
            "R5CY3118MEW",
            receiver.GetStream(),
            control,
            audio);

        Assert.True(started.Success, started.Message);
        Assert.True(engine.IsRunningVE);
        Assert.False(engine.StatusVE.ServerRunning);
        Assert.True(engine.StatusVE.TransportConnected);
        Assert.True((await engine.StopAsync()).Success);
    }

    [Fact]
    public async Task VisionEngine_stops_when_the_AppControl_stream_closes()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var sender = new TcpClient();
        Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
        await sender.ConnectAsync(IPAddress.Loopback, port);
        using TcpClient receiver = await accepting;
        await using MemoryStream control = new();
        await using MemoryStream audio = CreateRawAudioStream();
        listener.Stop();

        var writer = new VEProtocolWriter(sender.GetStream());
        await writer.WriteVideoSessionAsync(VEProtocolCodec.H264, new VEProtocolSession(720, 1280, false));
        await using var engine = new VECoreEngine(new NLServiceNovoraPaths());
        Assert.True((await engine.InitializeAsync()).Success);
        Assert.True((await engine.StartAppControlAsync("R5CY3118MEW", receiver.GetStream(), control, audio)).Success);

        sender.Dispose();
        await WaitUntilAsync(() => !engine.IsRunningVE, TimeSpan.FromSeconds(5));

        Assert.False(engine.IsRunningVE);
        Assert.Equal(VECoreStates.Stopped, engine.StatusVE.State);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!condition()) await Task.Delay(20, cancellation.Token);
    }

    [Fact]
    public async Task Control_reply_round_trips_private_value_separately_from_message()
    {
        await using MemoryStream stream = new();
        var expected = new NLControlReply(
            NLControlProtocol.Version,
            7,
            true,
            "Video listo.",
            Value: "token-privado");

        await NLControlProtocol.WriteAsync(stream, expected, CancellationToken.None);
        stream.Position = 0;
        NLControlReply actual = await NLControlProtocol.ReadAsync<NLControlReply>(stream, CancellationToken.None);

        Assert.Equal("Video listo.", actual.Message);
        Assert.Equal("token-privado", actual.Value);
    }

    [Fact]
    public void Start_AppControl_video_requires_the_same_ready_state_as_legacy_video()
    {
        var engines = new NLControlEngines(
            VideoCanStart: true, VideoCanStop: false,
            LinkCanStart: false, LinkCanStop: false, LinkRunning: false,
            LinkState: "Stopped", LinkMessage: "", DeviceName: "Samsung");
        var state = new NLControlSnapshot(
            4, "PC", "1.4", "4M", "Gaming", "disabled", "disabled", false,
            [], [], [], engines);

        string? error = NLControlCommands.Validate(
            new NLControlRequest(1, 9, "startAppVideo", Revision: 4),
            state);

        Assert.Null(error);
    }

    [Fact]
    public void Video_authorization_request_round_trips_and_defaults_off_for_older_snapshots()
    {
        var requested = new NLControlEngines(
            VideoCanStart: true, VideoCanStop: false,
            LinkCanStart: false, LinkCanStop: false, LinkRunning: false,
            LinkState: "Stopped", LinkMessage: "", DeviceName: "Samsung",
            VideoAuthorizationRequested: true);

        var restored = JsonSerializer.Deserialize<NLControlEngines>(JsonSerializer.Serialize(requested));
        var legacy = JsonSerializer.Deserialize<NLControlEngines>(
            "{\"VideoCanStart\":true,\"VideoCanStop\":false,\"LinkCanStart\":false," +
            "\"LinkCanStop\":false,\"LinkRunning\":false,\"LinkState\":\"Stopped\"," +
            "\"LinkMessage\":\"\",\"DeviceName\":\"Samsung\"}");

        Assert.True(restored!.VideoAuthorizationRequested);
        Assert.False(legacy!.VideoAuthorizationRequested);
    }

    [Fact]
    public void AppControl_offer_carries_only_bounded_encoder_settings()
    {
        var offer = new NLControlVideoSourceOffer(27215, new string('A', 64), 4_000_000, 1920, 60,
            27216, new string('B', 64), 27217, new string('C', 64));

        var restored = JsonSerializer.Deserialize<NLControlVideoSourceOffer>(
            JsonSerializer.Serialize(offer));

        Assert.Equal(offer, restored);
        Assert.Equal(4_000_000, restored!.Bitrate);
        Assert.Equal(1920, restored.MaxSize);
        Assert.Equal(60, restored.Fps);
        Assert.Equal(27216, restored.ControlPort);
        Assert.Equal(new string('B', 64), restored.ControlToken);
        Assert.Equal(27217, restored.AudioPort);
        Assert.Equal(new string('C', 64), restored.AudioToken);
    }

    [Theory]
    [InlineData("usb tcp:27215 tcp:27215", true)]
    [InlineData("usb tcp:27215 tcp:30000", false)]
    [InlineData("usb tcp:27214 tcp:27214", false)]
    public void AppControl_video_mapping_requires_exact_USB_route(string output, bool expected)
    {
        Assert.Equal(expected, NLUIWindowMain.HasAppControlVideoMapping(output));
    }

    [Theory]
    [InlineData("usb tcp:27216 tcp:27216", true)]
    [InlineData("usb tcp:27216 tcp:30000", false)]
    [InlineData("usb tcp:27215 tcp:27215", false)]
    public void AppControl_control_mapping_requires_exact_USB_route(string output, bool expected)
    {
        Assert.Equal(expected, NLUIWindowMain.HasAppControlControlMapping(output));
    }

    [Theory]
    [InlineData("usb tcp:27217 tcp:27217", true)]
    [InlineData("usb tcp:27217 tcp:30000", false)]
    [InlineData("usb tcp:27216 tcp:27216", false)]
    public void AppControl_audio_mapping_requires_exact_USB_route(string output, bool expected)
    {
        Assert.Equal(expected, NLUIWindowMain.HasAppControlAudioMapping(output));
    }

    [Fact]
    public async Task AppControl_transport_accepts_the_matching_ephemeral_token()
    {
        await using var transport = new VETransportAppControl(IPAddress.Loopback, 0);
        VETransportAppControlOffer offer = transport.PrepareVE();
        Task<Stream> accepting = transport.AcceptAsync(offer, TimeSpan.FromSeconds(2));

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, offer.Port);
        await client.GetStream().WriteAsync(Convert.FromHexString(offer.Token));

        Stream accepted = await accepting;

        Assert.True(accepted.CanRead);
        Assert.True(transport.IsConnectedVE);
    }

    [Fact]
    public async Task AppControl_transport_rejects_a_wrong_token()
    {
        await using var transport = new VETransportAppControl(IPAddress.Loopback, 0);
        VETransportAppControlOffer offer = transport.PrepareVE();
        Task<Stream> accepting = transport.AcceptAsync(offer, TimeSpan.FromSeconds(2));

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, offer.Port);
        await client.GetStream().WriteAsync(new byte[32]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => accepting);
        Assert.False(transport.IsConnectedVE);
    }

    [Fact]
    public async Task AppControl_control_uses_the_native_framed_protocol()
    {
        await using var stream = new MemoryStream();
        await using var control = new VEControlManager();
        await control.StartNativeAsync(stream);

        await control.SendAsync(VEControlMessage.TouchVE(
            VEControlActionMotion.Up, 7, 120, 240, 720, 1280, 1f, 0, 1));

        stream.Position = 0;
        NLControlInputCommand command = await NLControlProtocol.ReadAsync<NLControlInputCommand>(
            stream, CancellationToken.None);
        Assert.Equal((int)VEControlType.InjectTouchEvent, command.Type);
        Assert.Equal(120, command.X);
        Assert.Equal(240, command.Y);
        Assert.Equal((ushort)720, command.ScreenWidth);
        Assert.Equal((ushort)1280, command.ScreenHeight);
    }

    private static MemoryStream CreateRawAudioStream()
    {
        var stream = new MemoryStream();
        stream.Write([(byte)0x00, (byte)'r', (byte)'a', (byte)'w']);
        stream.Position = 0;
        return stream;
    }
}
