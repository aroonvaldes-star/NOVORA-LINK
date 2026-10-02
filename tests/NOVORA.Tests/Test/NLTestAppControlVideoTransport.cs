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
    [Theory]
    [InlineData(0, 500, false)]
    [InlineData(500, 500, true)]
    [InlineData(1499, 500, true)]
    [InlineData(1999, 500, false)]
    public void Native_pointer_rejects_letterbox_and_maps_only_the_visible_frame(
        int clientX, int clientY, bool expected)
    {
        bool mapped = NLControlPointerGeometry.TryMap(
            clientX, clientY, 2000, 1000, 1000, 1000, 0,
            clampToViewport: false, verticalEdgeActivationPixels: 0, out _);

        Assert.Equal(expected, mapped);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(999, 0, 1999, 0)]
    [InlineData(0, 499, 0, 999)]
    [InlineData(999, 499, 1999, 999)]
    public void Native_pointer_rotation_maps_landscape_view_to_portrait_source(
        int clientX, int clientY, int expectedX, int expectedY)
    {
        Assert.True(NLControlPointerGeometry.TryMap(
            clientX, clientY, 1000, 500, 1000, 2000, 90,
            clampToViewport: false, verticalEdgeActivationPixels: 0,
            out NLControlPointerPosition position));
        Assert.Equal((expectedX, expectedY), (position.X, position.Y));
        Assert.Equal((2000, 1000), (position.ScreenWidth, position.ScreenHeight));
    }

    [Theory]
    [InlineData(1080, 2400, 1080, 2400, true)]
    [InlineData(1080, 2400, 2400, 1080, false)]
    [InlineData(2400, 1080, 2400, 1080, true)]
    public void Native_pointer_rejects_commands_from_the_previous_orientation(
        ushort commandWidth, ushort commandHeight,
        int currentWidth, int currentHeight, bool expected)
    {
        Assert.Equal(expected, NLControlPointerGeometry.MatchesCurrentOrientation(
            commandWidth, commandHeight, currentWidth, currentHeight));
    }

    [Theory]
    [InlineData(0, 0, 2399, 0)]
    [InlineData(1079, 2399, 0, 1079)]
    public void Native_pointer_reprojects_previous_orientation_to_current_screen(
        float x, float y, float expectedX, float expectedY)
    {
        (float mappedX, float mappedY) = NLControlPointerGeometry.MapToCurrentOrientation(
            x, y, 1080, 2400, 2400, 1080);
        Assert.Equal(expectedX, mappedX);
        Assert.Equal(expectedY, mappedY);
    }

    [Theory]
    [InlineData(1080, 2400, 1920, 864, 1920)]
    [InlineData(2400, 1080, 1920, 1920, 864)]
    [InlineData(1080, 2400, 4320, 1080, 2400)]
    public void Native_video_size_follows_the_current_Android_orientation(
        int sourceWidth,
        int sourceHeight,
        int maxSize,
        int expectedWidth,
        int expectedHeight)
    {
        Assert.Equal(
            new NLControlVideoSize(expectedWidth, expectedHeight),
            NLControlVideoSize.Fit(sourceWidth, sourceHeight, maxSize));
    }

    [Theory]
    [InlineData("hola", 4, 4, "!", "hola!")]
    [InlineData("hola", 1, 3, "X", "hXa")]
    [InlineData("hola", 4, 4, "\b", "hol")]
    [InlineData("hola", 1, 3, "\b", "ha")]
    [InlineData("hola", 1, 1, "\u007f", "hla")]
    [InlineData("hola", -1, -1, "!", "hola!")]
    [InlineData("Españ", 5, 5, "ó", "Españó")]
    public void Native_keyboard_edits_the_focused_Android_text(
        string current, int selectionStart, int selectionEnd, string input, string expected)
    {
        Assert.Equal(expected,
            NLControlAndroidInput.EditText(current, selectionStart, selectionEnd, input));
    }

    [Theory]
    [InlineData("Escribe aquí", "Escribe aquí", true, "")]
    [InlineData("Escribe aquí", "Escribe aquí", false, "")]
    [InlineData("Hola", "Escribe aquí", false, "Hola")]
    [InlineData(null, "Escribe aquí", false, "")]
    public void Native_keyboard_does_not_treat_Android_hint_as_user_text(
        string? current, string? hint, bool isShowingHint, string expected)
    {
        Assert.Equal(expected,
            NLControlAndroidInput.GetEditableText(current, hint, isShowingHint));
    }

    [Fact]
    public void Native_keyboard_types_into_an_empty_field_that_exposes_its_hint_as_text()
    {
        string current = NLControlAndroidInput.GetEditableText(
            "Escribe aquí", "Escribe aquí", isShowingHint: true);

        Assert.Equal("ñ", NLControlAndroidInput.EditText(current, 0, 0, "ñ"));
    }

    [Fact]
    public async Task Native_control_response_round_trips_clipboard_without_polling()
    {
        await using var stream = new MemoryStream();
        var expected = new NLControlInputResponse(
            NLControlInputResponse.ClipboardType, Text: "texto Android");

        await NLControlProtocol.WriteAsync(stream, expected, CancellationToken.None);
        stream.Position = 0;

        Assert.Equal(expected,
            await NLControlProtocol.ReadAsync<NLControlInputResponse>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Native_control_channel_receives_Android_clipboard_response()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var android = new TcpClient();
        Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
        await android.ConnectAsync(IPAddress.Loopback, port);
        using TcpClient pc = await accepting;
        listener.Stop();
        await using var control = new VEControlManager();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        control.ClipboardChangedVE += (_, text) => received.TrySetResult(text);
        await control.StartNativeAsync(pc.GetStream());

        Task androidSide = Task.Run(async () =>
        {
            NLControlInputCommand request = await NLControlProtocol.ReadAsync<NLControlInputCommand>(
                android.GetStream(), CancellationToken.None);
            Assert.Equal((int)VEControlType.GetClipboard, request.Type);
            await NLControlProtocol.WriteAsync(android.GetStream(),
                new NLControlInputResponse(NLControlInputResponse.ClipboardType, Text: "desde Android"),
                CancellationToken.None);
        });

        await control.SendAsync(VEControlMessage.GetClipboardVE(VEControlCopy.Copy));

        Assert.Equal("desde Android", await received.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        await androidSide;
    }

    [Fact]
    public void Successful_AppControl_start_attaches_and_focuses_VisionEngine_input()
    {
        var effects = new List<string>();

        NLUIWindowMain.CompleteAppControlVideoStartVE(
            VECoreResult.Ok(),
            () => effects.Add("attach"),
            () => effects.Add("focus"),
            () => effects.Add("publish"));

        Assert.Equal(["attach", "focus", "publish"], effects);
    }

    [Fact]
    public async Task Native_input_loop_continues_after_Android_cancels_one_gesture()
    {
        await using var stream = new MemoryStream();
        await NLControlProtocol.WriteAsync(stream, new NLControlInputCommand(2, PointerId: 7),
            CancellationToken.None);
        await NLControlProtocol.WriteAsync(stream, new NLControlInputCommand(2, PointerId: 8),
            CancellationToken.None);
        stream.Position = 0;
        var executed = new List<ulong>();
        var recovered = new List<string>();

        await Assert.ThrowsAsync<EndOfStreamException>(() => NLControlInputLoop.RunAsync(
            stream,
            (command, _) =>
            {
                executed.Add(command.PointerId);
                return command.PointerId == 7
                    ? Task.FromException(new InvalidOperationException("Android canceló el gesto."))
                    : Task.CompletedTask;
            },
            (_, exception, _) =>
            {
                recovered.Add(exception.Message);
                return Task.CompletedTask;
            },
            TimeSpan.FromSeconds(1),
            CancellationToken.None));

        Assert.Equal([7UL, 8UL], executed);
        Assert.Equal(["Android canceló el gesto."], recovered);
    }

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
    public async Task VisionEngine_runs_AppControl_video_when_audio_is_disabled()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var sender = new TcpClient();
        Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
        await sender.ConnectAsync(IPAddress.Loopback, port);
        using TcpClient receiver = await accepting;
        await using MemoryStream control = new();
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
            audioStream: null,
            audioEnabled: false);

        Assert.True(started.Success, started.Message);
        Assert.True(engine.IsRunningVE);
        Assert.Equal(
            NOVORA.VisionEngine.Audio.VEAudioStates.Stopped,
            engine.RuntimeVE.AudioVE.StatusVE.State);
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
            27216, new string('B', 64), 27217, new string('C', 64),
            AudioEnabled: false, MuteDeviceAudio: true);

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
        Assert.False(restored.AudioEnabled);
        Assert.True(restored.MuteDeviceAudio);

        var legacy = JsonSerializer.Deserialize<NLControlVideoSourceOffer>(
            "{\"Port\":27215,\"Token\":\"" + new string('A', 64) +
            "\",\"Bitrate\":4000000,\"MaxSize\":1920,\"Fps\":60}");
        Assert.True(legacy!.AudioEnabled);
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
