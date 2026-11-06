using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;
using NOVORA.Linux;
using NOVORA.Linux.Control;
using NOVORA.Linux.VisionEngine;

if (args is ["--ve-runtime-native"]) { VisionRuntimeChecks.RunNative(); return; }
if (args is ["--outputs-native"]) { VisionInputChecks.RunOutputsNative(); return; }
if (args is ["--ve-ui"]) { VisionUiChecks.Run(); return; }
if (args is ["--input-native"]) { VisionInputChecks.RunNative(); return; }
await VisionInputChecks.RunAsync();
AndroidKeyboardChecks.Run();
VisionSettingsChecks.Run();
await VisionAccelerationChecks.RunAsync();
await VisionFilesChecks.RunAsync();
await VisionIntegrationChecks.RunAsync();
await VisionRecordingChecks.RunAsync();
string directory = Path.Combine(Path.GetTempPath(), "novora-linux-protocol-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);

try
{
    if (args is ["--renderer-only"])
    {
        TestSdlRenderer();
        return;
    }
    if (args is ["--renderer-live"])
    {
        TestSdlRenderer(useDummyDriver: false);
        return;
    }
    if (args is ["--le-lan-only"])
    {
        await TestLanLinkGatewayAsync(Path.Combine(directory, "le-lan-gateway"));
        await TestLanLinkControlRoutingAsync(Path.Combine(directory, "le-lan-control"));
        Console.WriteLine("LinkEngine LAN checks passed.");
        return;
    }

    await TestFrameCompatibilityAsync();
    TestAdbParsing();
    TestLinkProtocol();
    TestThemeColors();
    await TestVisionProtocolAsync();
    await TestVisionAudioProtocolAsync();
    await TestAppControlTransportAsync();
    TestVisionLanOfferValidation();
    await TestVisionLanChannelAsync(Path.Combine(directory, "vision-lan"));
    await TestVisionLanControlIntegrationAsync(Path.Combine(directory, "vision-lan-control"));
    await TestVisionDecoderMonitoringAsync();
    TestVisionDecoderReconfiguration();
    await TestFfmpegDecoderAsync();
    TestSdlRenderer();
    TestSdlAudioOutput();
    await TestPairingAndResumeAsync(Path.Combine(directory, "trust"));
    await TestActiveRevocationAsync(Path.Combine(directory, "revoke-trust"));
    await TestUsbTunnelAsync(Path.Combine(directory, "usb-trust"));
    await TestLanLinkGatewayAsync(Path.Combine(directory, "le-lan-gateway"));
    await TestLanLinkControlRoutingAsync(Path.Combine(directory, "le-lan-control"));
    if (args.Length == 2 && args[0] == "--device")
        await TestPhysicalUsbAsync(Path.Combine(directory, "physical-usb-trust"), args[1]);
    Console.WriteLine("Linux protocol and trust-store checks passed.");
}
finally
{
    Directory.Delete(directory, recursive: true);
}

static async Task TestUsbTunnelAsync(string directory)
{
    using var store = new LinuxTrustStore(directory);
    var server = new LinuxControlServer(IPAddress.Loopback, store,
        port: 0, transport: "USB", allowLoopbackForTests: true);
    server.Start();
    ControlInvitation invitation = server.UsbInvitation;
    try
    {
        string wrongSecret = invitation.Secret[0] == '0'
            ? new string('1', 64)
            : new string('0', 64);
        using (TestConnection rejected = await ConnectToEndpointAsync(invitation.Host, invitation.Port, invitation.Fingerprint))
        {
            await ControlProtocol.WriteAsync(rejected.Stream,
                new ControlRequest(1, 1, "pair", Code: wrongSecret), CancellationToken.None);
            ControlReply denied = await ControlProtocol.ReadAsync<ControlReply>(rejected.Stream, CancellationToken.None);
            Assert(!denied.Success, "Invalid USB invitation secret was accepted.");
        }

        using (TestConnection paired = await ConnectToEndpointAsync(invitation.Host, invitation.Port, invitation.Fingerprint))
        {
            await ControlProtocol.WriteAsync(paired.Stream,
                new ControlRequest(1, 1, "pair", Code: invitation.Secret), CancellationToken.None);
            ControlReply connected = await ControlProtocol.ReadAsync<ControlReply>(paired.Stream, CancellationToken.None);
            Assert(connected.Success && connected.Snapshot is not null, "USB tunnel pairing did not return a snapshot.");
            await ControlProtocol.WriteAsync(paired.Stream,
                new ControlRequest(1, 2, "trust.enroll", Value: "USB test phone"), CancellationToken.None);
            ControlReply enrollment = await ControlProtocol.ReadAsync<ControlReply>(paired.Stream, CancellationToken.None);
            Assert(enrollment.Success && enrollment.TrustedPc?.Transport == "USB",
                "USB trust enrollment did not preserve its transport.");
        }

        await WaitUntilAsync(() => !server.IsAuthorized);
        using (TestConnection replayed = await ConnectToEndpointAsync(invitation.Host, invitation.Port, invitation.Fingerprint))
        {
            await ControlProtocol.WriteAsync(replayed.Stream,
                new ControlRequest(1, 1, "pair", Code: invitation.Secret), CancellationToken.None);
            ControlReply denied = await ControlProtocol.ReadAsync<ControlReply>(replayed.Stream, CancellationToken.None);
            Assert(!denied.Success, "A consumed USB invitation was accepted a second time.");
        }
    }
    finally
    {
        await server.DisposeAsync();
    }
}

static async Task TestPhysicalUsbAsync(string directory, string serial)
{
    using var store = new LinuxTrustStore(directory);
    var server = new LinuxControlServer(IPAddress.Loopback, store, port: 27214, transport: "USB");
    server.StatusChanged += (_, message) => Console.WriteLine(message);
    server.Start();
    try
    {
        var adb = new AdbBridge();
        await using AdbBridge.UsbReverseLease reverse =
            await adb.PrepareUsbControlAsync(serial, server.UsbInvitation, CancellationToken.None);
        await WaitUntilAsync(() => server.IsAuthorized, TimeSpan.FromSeconds(20));
        Console.WriteLine("Physical Android USB control link confirmed.");
    }
    finally
    {
        await server.DisposeAsync();
    }
}

static async Task TestFrameCompatibilityAsync()
{
    var request = new ControlRequest(1, 42, "pair.lan", Code: "012345");
    byte[] json = JsonSerializer.SerializeToUtf8Bytes(request);
    using (JsonDocument document = JsonDocument.Parse(json))
    {
        Assert(document.RootElement.GetProperty("Version").GetInt32() == 1, "Version field mismatch.");
        Assert(document.RootElement.GetProperty("Action").GetString() == "pair.lan", "Action field mismatch.");
        Assert(document.RootElement.GetProperty("Code").GetString() == "012345", "Code field mismatch.");
    }

    using var frame = new MemoryStream();
    await ControlProtocol.WriteAsync(frame, request, CancellationToken.None);
    frame.Position = 0;
    ControlRequest decoded = await ControlProtocol.ReadAsync<ControlRequest>(frame, CancellationToken.None);
    Assert(decoded == request, "Length-prefixed JSON round-trip failed.");
}

static void TestAdbParsing()
{
    AdbDevice device = AdbBridge.ParseDevice(
        "R5CY3118MEW device usb:3-1 product:a56xnsxx model:SM_A566E device:a56x transport_id:1")
        ?? throw new InvalidOperationException("ADB USB device line was not parsed.");
    Assert(device.Serial == "R5CY3118MEW" && device.State == "device" &&
        device.Model == "SM A566E" && device.IsUsb,
        "ADB did not recognize the connected USB phone and model.");
    Assert(AdbBridge.ParseDevice("offline-serial unauthorized") is { State: "unauthorized", IsUsb: false },
        "ADB unauthorized device status was parsed incorrectly.");
    Assert(AdbBridge.TryFindMapping("UsbFfs tcp:27214 tcp:27214\n",
        out bool matching, out bool conflicting) && matching && !conflicting,
        "Existing exact ADB reverse mapping was not recognized.");
    Assert(!AdbBridge.TryFindMapping("UsbFfs tcp:27214 tcp:27218\n",
        out matching, out conflicting) && !matching && conflicting,
        "Conflicting ADB reverse mapping was not recognized.");
    Assert(AdbBridge.TryFindMapping("UsbFfs tcp:27183 tcp:27183\n", 27183,
        out matching, out conflicting) && matching && !conflicting,
        "LinkEngine CONTROL reverse mapping was not recognized.");
    Assert(!AdbBridge.TryFindMapping("UsbFfs tcp:27184 tcp:28000\n", 27184,
        out matching, out conflicting) && !matching && conflicting,
        "Conflicting LinkEngine DATA reverse mapping was not recognized.");
    Assert(AdbBridge.TryFindMapping("UsbFfs tcp:27215 tcp:43821\n", 27215, 43821,
        out matching, out conflicting) && matching && !conflicting,
        "ADB reverse mapping to an ephemeral Linux listener was not recognized.");
    Assert(!AdbBridge.TryFindMapping("UsbFfs tcp:27215 tcp:43822\n", 27215, 43821,
        out matching, out conflicting) && !matching && conflicting,
        "An ADB reverse mapping to a different ephemeral listener was not rejected.");
}

static void TestLinkProtocol()
{
    const string token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    Assert(LinuxLinkEngine.TryValidateHello($"NOVORA-LINK|2|HELLO|android-client|{token}",
        token, out string clientId) && clientId == "android-client",
        "LinkEngine USB HELLO was not authenticated.");
    Assert(!LinuxLinkEngine.TryValidateHello($"NOVORA-LINK|2|HELLO|android-client|{new string('0', 64)}",
        token, out _),
        "LinkEngine accepted a HELLO with the wrong session token.");

    long sequence = 0;
    Assert(LinuxLinkEngine.TryCreateHeartbeatAck("NOVORA-LINK|2|HEARTBEAT|1|1700000000000",
        ref sequence, out string ack) && sequence == 1,
        "Valid LinkEngine heartbeat was rejected.");
    string[] fields = ack.Split('|');
    Assert(fields.Length == 6 && fields[2] == "HEARTBEAT_ACK" &&
        fields[3] == "1" && fields[4] == "1700000000000" &&
        long.TryParse(fields[5], out long serverTime) && serverTime > 0,
        "LinkEngine heartbeat ACK did not match the Android protocol.");
    Assert(!LinuxLinkEngine.TryCreateHeartbeatAck("NOVORA-LINK|2|HEARTBEAT|1|1700000000000",
        ref sequence, out _),
        "LinkEngine accepted a replayed heartbeat.");
}

static void TestThemeColors()
{
    Assert(LinuxTheme.ResolveColor("#0b1220", LinuxTheme.Dark) == "#0b1220",
        "Dark theme changed the NOVORA dark canvas color.");
    Assert(LinuxTheme.ResolveColor("#0b1220", LinuxTheme.Light) == "#F4F6FB",
        "Light theme did not map the NOVORA canvas color.");
    Assert(LinuxTheme.ResolveColor("#ffffff", LinuxTheme.Light) == "#ffffff",
        "Theme mapping changed the neutral logo color.");
}

static async Task TestVisionProtocolAsync()
{
    using var stream = new MemoryStream();
    WriteUInt32(stream, 0x68323634);
    WriteSession(stream, 1920, 1080, 0);
    WriteMediaHeader(stream, 1UL << 62, 3);
    stream.Write([1, 2, 3]);
    WriteMediaHeader(stream, 2_500_000, 2);
    stream.Write([4, 5]);
    stream.Position = 0;

    var reader = new LinuxVisionProtocolReader(stream);
    (VideoCodec codec, VideoSession session) = await reader.OpenAsync(CancellationToken.None);
    Assert(codec == VideoCodec.H264 && session.Width == 1920 && session.Height == 1080,
        "VisionEngine did not parse the codec and initial session header.");
    VideoPacket configuration = await reader.ReadPacketAsync(session, _ => { }, CancellationToken.None)
        ?? throw new InvalidOperationException("VisionEngine configuration packet was missing.");
    Assert(configuration.IsConfiguration && configuration.PresentationTimeUs is null &&
        configuration.Data.SequenceEqual(new byte[] { 1, 2, 3 }),
        "VisionEngine configuration packet flags or payload were parsed incorrectly.");
    VideoPacket frame = await reader.ReadPacketAsync(session, _ => { }, CancellationToken.None)
        ?? throw new InvalidOperationException("VisionEngine video packet was missing.");
    Assert(!frame.IsConfiguration && frame.PresentationTimeUs == 2_500_000 &&
        frame.Data.SequenceEqual(new byte[] { 4, 5 }),
        "VisionEngine frame timestamp or payload was parsed incorrectly.");
}

static async Task TestVisionAudioProtocolAsync()
{
    using var stream = new MemoryStream();
    WriteUInt32(stream, (uint)AudioCodec.Raw);
    WriteMediaHeader(stream, 20_000, 8);
    byte[] pcm = [1, 0, 2, 0, 3, 0, 4, 0];
    stream.Write(pcm);
    stream.Position = 0;

    var reader = new LinuxAudioProtocolReader(stream);
    Assert(await reader.OpenAsync(CancellationToken.None) == AudioCodec.Raw,
        "VisionEngine did not parse the raw audio codec.");
    AudioPacket packet = await reader.ReadPacketAsync(CancellationToken.None);
    using var decoder = new LinuxFfmpegAudioDecoder(AudioCodec.Raw);
    Assert(decoder.Decode(packet).Single().SequenceEqual(pcm),
        "VisionEngine changed raw PCM audio samples.");
}

static async Task TestAppControlTransportAsync()
{
    await using var transport = new LinuxAppControlTransport();
    AppControlStreamOffer offer = transport.Prepare();
    Task<Stream> accepting = transport.AcceptAsync(
        offer, TimeSpan.FromSeconds(5), CancellationToken.None);

    using (var rejected = new TcpClient())
    {
        await rejected.ConnectAsync(IPAddress.Loopback, offer.Port);
        await rejected.GetStream().WriteAsync(new byte[32]);
    }

    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, offer.Port);
    Stream clientStream = client.GetStream();
    await clientStream.WriteAsync(Convert.FromHexString(offer.Token));
    await clientStream.WriteAsync(new byte[] { 9, 8, 7 });

    await using Stream accepted = await accepting;
    byte[] payload = new byte[3];
    await accepted.ReadExactlyAsync(payload);
    Assert(payload.SequenceEqual(new byte[] { 9, 8, 7 }),
        "AppControl accepted the authenticated VE socket but lost its payload.");
}

static void TestVisionLanOfferValidation()
{
    DateTimeOffset now = DateTimeOffset.UtcNow;
    var offer = new LinuxVisionLanOffer(
        ControlProtocol.Version,
        Guid.NewGuid().ToString("N"),
        "10.1.2.3",
        new string('A', 64),
        now.AddSeconds(90).ToUnixTimeSeconds(),
        new LinuxVisionLanEndpoint(41001, new string('1', 64), "VIDEO"),
        new LinuxVisionLanEndpoint(41002, new string('2', 64), "CONTROL"),
        new LinuxVisionLanEndpoint(41003, new string('3', 64), "AUDIO"),
        8_000_000,
        1920,
        60,
        true);
    offer.Validate(now);

    bool expiredRejected = false;
    try { (offer with { ExpiresUnix = now.AddSeconds(-1).ToUnixTimeSeconds() }).Validate(now); }
    catch (InvalidDataException) { expiredRejected = true; }
    Assert(expiredRejected, "Expired VisionEngine LAN offers must be rejected.");

    bool duplicateEndpointsRejected = false;
    try
    {
        (offer with { Control = offer.Video }).Validate(now);
    }
    catch (InvalidDataException) { duplicateEndpointsRejected = true; }
    Assert(duplicateEndpointsRejected, "VisionEngine LAN offers must use independent channels.");
}

static async Task TestVisionLanChannelAsync(string directory)
{
    using var store = new LinuxTrustStore(directory);
    var channel = new LinuxVisionLanChannel(
        IPAddress.Loopback, store.Certificate, LinuxVisionLanChannelKind.Video, allowLoopback: true);
    DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddSeconds(30);
    string sessionId = Guid.NewGuid().ToString("N");
    LinuxVisionLanEndpoint endpoint = channel.Prepare(sessionId, expiresAt);
    Task<Stream> accepting = channel.AcceptAsync(sessionId, endpoint, expiresAt, CancellationToken.None);
    string fingerprint = Convert.ToHexString(SHA256.HashData(store.Certificate.RawData));

    using (var invalid = new TcpClient())
    {
        await invalid.ConnectAsync(IPAddress.Loopback, endpoint.Port);
        using var tls = new SslStream(invalid.GetStream(), leaveInnerStreamOpen: false);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "NOVORA",
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                certificate is not null &&
                Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())) == fingerprint
        });
        await ControlProtocol.WriteAsync(tls,
            new LinuxVisionLanHello(ControlProtocol.Version, sessionId, endpoint.Channel, new string('0', 64)),
            CancellationToken.None);
    }

    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, endpoint.Port);
    using var validTls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
    await validTls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
    {
        TargetHost = "NOVORA",
        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        RemoteCertificateValidationCallback = (_, certificate, _, _) =>
            certificate is not null &&
            Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())) == fingerprint
    });
    await ControlProtocol.WriteAsync(validTls,
        new LinuxVisionLanHello(ControlProtocol.Version, sessionId, endpoint.Channel, endpoint.Token),
        CancellationToken.None);
    await using Stream accepted = await accepting.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(accepted.CanRead, "VisionEngine LAN did not retain the authenticated TLS channel.");

    var cancelledChannel = new LinuxVisionLanChannel(
        IPAddress.Loopback, store.Certificate, LinuxVisionLanChannelKind.Control, allowLoopback: true);
    DateTimeOffset cancelExpiry = DateTimeOffset.UtcNow.AddSeconds(20);
    string cancelSessionId = Guid.NewGuid().ToString("N");
    LinuxVisionLanEndpoint cancelEndpoint = cancelledChannel.Prepare(cancelSessionId, cancelExpiry);
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
    bool cancellationObserved = false;
    try
    {
        await cancelledChannel.AcceptAsync(
            cancelSessionId, cancelEndpoint, cancelExpiry, cancellation.Token);
    }
    catch (OperationCanceledException) { cancellationObserved = true; }
    finally { await cancelledChannel.DisposeAsync(); }
    Assert(cancellationObserved, "VisionEngine LAN channel cancellation must release pending accepts.");
    await channel.DisposeAsync();
}

static async Task TestVisionLanControlIntegrationAsync(string directory)
{
    using var store = new LinuxTrustStore(directory);
    (string deviceId, string token) = store.Enroll("VE LAN test phone");
    using var sessionGate = new SemaphoreSlim(1, 1);
    var vision = new LinuxVisionLanEngine(
        IPAddress.Loopback, store, sessionGate, allowLoopbackForTests: true);
    var server = new LinuxControlServer(
        IPAddress.Loopback,
        store,
        port: 0,
        allowLoopbackForTests: true,
        visionLanEngine: vision);
    server.Start();

    try
    {
        using TestConnection client = await ConnectAsync(server.Invitation);
        await ControlProtocol.WriteAsync(client.Stream,
            new ControlRequest(1, 1, "resume", Value: deviceId, Code: token),
            CancellationToken.None);
        ControlReply connected = await ControlProtocol.ReadAsync<ControlReply>(
            client.Stream, CancellationToken.None);
        Assert(connected.Success && connected.Snapshot?.Engines?.VideoCanStart == true,
            "Authorized LAN control did not expose VE LAN start.");

        await ControlProtocol.WriteAsync(client.Stream,
            new ControlRequest(1, 2, "startVideoLan", Revision: connected.Snapshot!.Revision),
            CancellationToken.None);
        ControlReply started = await ControlProtocol.ReadAsync<ControlReply>(
            client.Stream, CancellationToken.None);
        Assert(started.Success && started.Value is not null && server.HasVisionSession,
            "LAN control did not return a prepared VisionEngine offer.");
        LinuxVisionLanOffer offer = JsonSerializer.Deserialize<LinuxVisionLanOffer>(started.Value!)
            ?? throw new InvalidOperationException("VisionEngine LAN offer could not be deserialized.");
        offer.Validate(DateTimeOffset.UtcNow, allowLoopback: true);
        Assert(offer.Audio is not null && offer.Files is not null && offer.FilesWireVersion == 2 && offer.CaptureGeneration > 0,
            "LAN offer did not preserve optional audio and disabled file-channel semantics.");
        Assert(!sessionGate.Wait(0),
            "The shared VisionEngine gate did not exclude a concurrent USB transport.");

        await ControlProtocol.WriteAsync(client.Stream,
            new ControlRequest(1, 3, "startVideoLan"), CancellationToken.None);
        ControlReply duplicate = await ControlProtocol.ReadAsync<ControlReply>(
            client.Stream, CancellationToken.None);
        Assert(!duplicate.Success, "A second VisionEngine LAN session was accepted concurrently.");

        await ControlProtocol.WriteAsync(client.Stream,
            new ControlRequest(1, 4, "stopVideo"), CancellationToken.None);
        ControlReply stopped = await ControlProtocol.ReadAsync<ControlReply>(
            client.Stream, CancellationToken.None);
        Assert(stopped.Success && !server.HasVisionSession && sessionGate.Wait(0),
            "Stopping VE LAN did not release its session and shared transport gate.");
        sessionGate.Release();
    }
    finally
    {
        await server.DisposeAsync();
    }
}

static async Task TestFfmpegDecoderAsync()
{
    string h264Encoder = await FindH264EncoderAsync();
    var encoder = new ProcessStartInfo
    {
        FileName = "ffmpeg",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
    foreach (string argument in new[]
    {
        "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=red:s=64x32:r=10",
        "-frames:v", "3", "-c:v", h264Encoder, "-f", "h264", "pipe:1"
    })
        encoder.ArgumentList.Add(argument);

    using var process = new Process { StartInfo = encoder };
    try
    {
        if (!process.Start())
            throw new InvalidOperationException("FFmpeg encoder did not start.");
    }
    catch (System.ComponentModel.Win32Exception)
    {
        throw new InvalidOperationException("FFmpeg is required for the VisionEngine decoder smoke test.");
    }
    using var bitstream = new MemoryStream();
    Task output = process.StandardOutput.BaseStream.CopyToAsync(bitstream);
    Task<string> errors = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    await output;
    Assert(process.ExitCode == 0, $"FFmpeg could not create the H.264 smoke-test stream: {await errors}");

    var renderer = new TestVisionRenderer();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await using var decoder = new LinuxFfmpegVideoDecoder(
        VideoCodec.H264, new VideoSession(64, 32, 90, 2 * 1024 * 1024), renderer);
    await decoder.WritePacketAsync(bitstream.ToArray(), null, timeout.Token);
    Task completed = await Task.WhenAny(
        renderer.FirstFrame.Task, decoder.FrameTask, Task.Delay(TimeSpan.FromSeconds(20), timeout.Token));
    if (completed == decoder.FrameTask)
        throw new InvalidOperationException($"FFmpeg ended before producing a frame: {await decoder.ErrorTask}");
    await renderer.FirstFrame.Task.WaitAsync(timeout.Token);
    Assert(renderer.Width == 32 && renderer.Height == 64 && renderer.FrameCount > 0,
        "FFmpeg did not decode and rotate an Android-compatible H.264 frame to RGBA.");
    Console.WriteLine("VisionEngine FFmpeg decoder smoke test passed.");
}

static async Task<string> FindH264EncoderAsync()
{
    var start = new ProcessStartInfo
    {
        FileName = "ffmpeg",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
    start.ArgumentList.Add("-hide_banner");
    start.ArgumentList.Add("-encoders");

    using var process = new Process { StartInfo = start };
    try
    {
        if (!process.Start())
            throw new InvalidOperationException("FFmpeg encoder discovery did not start.");
    }
    catch (System.ComponentModel.Win32Exception)
    {
        throw new InvalidOperationException("FFmpeg is required for the VisionEngine decoder smoke test.");
    }

    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    string output = $"{await stdout}\n{await stderr}";
    Assert(process.ExitCode == 0, $"FFmpeg could not list available encoders: {output}");

    string[] available = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    foreach (string candidate in new[] { "libx264", "libopenh264" })
        if (available.Contains(candidate, StringComparer.Ordinal))
            return candidate;

    throw new InvalidOperationException(
        "FFmpeg must provide a software H.264 encoder (libx264 or libopenh264) for the VisionEngine decoder smoke test.");
}

static async Task TestVisionDecoderMonitoringAsync()
{
    using var cancellation = new CancellationTokenSource();
    var presentation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    async Task<int> PendingOperationAsync()
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
        return 0;
    }

    Task<int> operation = PendingOperationAsync();
    Task monitored = LinuxVisionDecoderMonitor.AwaitAsync(operation, presentation.Task, cancellation);
    presentation.SetException(new IOException("Renderer failed."));
    try
    {
        await monitored;
        throw new InvalidOperationException("A failed renderer was not propagated to the VE session.");
    }
    catch (IOException ex) when (ex.Message == "Renderer failed.")
    {
    }

    Assert(cancellation.IsCancellationRequested,
        "A presentation failure did not cancel the VisionEngine session.");
}

static void TestVisionDecoderReconfiguration()
{
    byte[] configuration = [1, 2, 3];
    byte[] keyFrame = [4, 5, 6];
    var changedSession = new VideoSession(128, 64, 90, 2 * 1024 * 1024);
    var packet = new VideoPacket(changedSession, keyFrame, 42, false, true);

    (byte[] Data, long? PresentationTimeUs)[] inputs =
        LinuxVisionDecoderInput.ForPacket(packet, configuration, decoderRecreated: true);
    Assert(inputs.Length == 2 && ReferenceEquals(inputs[0].Data, configuration) &&
        ReferenceEquals(inputs[1].Data, keyFrame) && inputs[1].PresentationTimeUs == 42,
        "Recreating the decoder on a geometry change did not replay configuration and forward the current keyframe.");

    var configPacket = new VideoPacket(changedSession, configuration, null, true, false);
    inputs = LinuxVisionDecoderInput.ForPacket(configPacket, configuration, decoderRecreated: true);
    Assert(inputs.Length == 1 && ReferenceEquals(inputs[0].Data, configuration),
        "A configuration packet was duplicated while recreating the decoder.");
}

static void TestSdlRenderer(bool useDummyDriver = true)
{
    string? originalDriver = Environment.GetEnvironmentVariable("SDL_VIDEODRIVER");
    if (useDummyDriver)
        Environment.SetEnvironmentVariable("SDL_VIDEODRIVER", "dummy");
    try
    {
        using var renderer = new LinuxSdlVideoRenderer();
        renderer.Present(new byte[64 * 64 * 4], 64, 64);
        renderer.Present(new byte[64 * 64 * 4], 64, 64);
        Assert(!string.IsNullOrWhiteSpace(renderer.Backend), "SDL renderer backend was not reported.");
        Assert(renderer.FramesPresented == 2, "SDL renderer did not count presented frames.");
        Assert(renderer.OutputWidth > 0 && renderer.OutputHeight > 0,
            "SDL renderer did not report its pixel output size.");
        Console.WriteLine(
            $"SDL renderer smoke test passed: {renderer.Backend}, {renderer.OutputWidth}×{renderer.OutputHeight}.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("SDL_VIDEODRIVER", originalDriver);
    }
}

static void TestSdlAudioOutput()
{
    string? originalDriver = Environment.GetEnvironmentVariable("SDL_AUDIODRIVER");
    Environment.SetEnvironmentVariable("SDL_AUDIODRIVER", "dummy");
    try
    {
        using var player = new LinuxSdlAudioPlayer();
        player.Play(new byte[48_000 * 2 * sizeof(short) / 50]);
    }
    finally
    {
        Environment.SetEnvironmentVariable("SDL_AUDIODRIVER", originalDriver);
    }
}

static void WriteUInt32(Stream stream, uint value)
{
    Span<byte> data = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(data, value);
    stream.Write(data);
}

static void WriteSession(Stream stream, int width, int height, byte maxPacketUnits)
{
    byte[] data = new byte[12];
    data[0] = 0x80;
    data[2] = maxPacketUnits;
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8, 4), (uint)height);
    stream.Write(data);
}

static void WriteMediaHeader(Stream stream, ulong ptsFlags, int length)
{
    Span<byte> data = stackalloc byte[12];
    BinaryPrimitives.WriteUInt64BigEndian(data, ptsFlags);
    BinaryPrimitives.WriteUInt32BigEndian(data[8..], (uint)length);
    stream.Write(data);
}

static async Task TestPairingAndResumeAsync(string directory)
{
    TrustedPc trustedPc;
    string fingerprint;
    string token;

    using (var store = new LinuxTrustStore(directory))
    {
        fingerprint = store.Fingerprint;
        var server = new LinuxControlServer(IPAddress.Loopback, store, port: 0, allowLoopbackForTests: true);
        server.Start();
        try
        {
            LanInvitation invitation = server.Invitation;
            using (var rejected = await ConnectAsync(invitation))
            {
                string wrongCode = invitation.Code == "000000" ? "000001" : "000000";
                await ControlProtocol.WriteAsync(rejected.Stream,
                    new ControlRequest(1, 1, "pair.lan", Code: wrongCode), CancellationToken.None);
                ControlReply denied = await ControlProtocol.ReadAsync<ControlReply>(rejected.Stream, CancellationToken.None);
                Assert(!denied.Success, "Invalid pairing code was accepted.");
            }

            using (var paired = await ConnectAsync(invitation))
            {
                await ControlProtocol.WriteAsync(paired.Stream,
                    new ControlRequest(1, 1, "pair.lan", Code: invitation.Code), CancellationToken.None);
                ControlReply connected = await ControlProtocol.ReadAsync<ControlReply>(paired.Stream, CancellationToken.None);
                Assert(connected.Success && connected.Snapshot is not null, "Pairing did not return a snapshot.");
                Assert(connected.Snapshot!.PcVersion.Contains("protocol v1", StringComparison.Ordinal),
                    "Linux snapshot did not identify its protocol.");

                await ControlProtocol.WriteAsync(paired.Stream,
                    new ControlRequest(1, 2, "trust.enroll", Value: "Protocol test phone"), CancellationToken.None);
                ControlReply enrollment = await ControlProtocol.ReadAsync<ControlReply>(paired.Stream, CancellationToken.None);
                Assert(enrollment.Success && enrollment.TrustedPc is not null, "Trust enrollment failed.");
                trustedPc = enrollment.TrustedPc!;
                token = trustedPc.Token;
                Assert(trustedPc.Port == invitation.Port && trustedPc.Fingerprint == fingerprint,
                    "Enrolled device received mismatched host identity.");
            }

            await WaitUntilAsync(() => !server.IsAuthorized);
            using (var repeatedPairing = await ConnectAsync(invitation))
            {
                await ControlProtocol.WriteAsync(repeatedPairing.Stream,
                    new ControlRequest(1, 1, "pair.lan", Code: invitation.Code), CancellationToken.None);
                ControlReply denied = await ControlProtocol.ReadAsync<ControlReply>(repeatedPairing.Stream, CancellationToken.None);
                Assert(!denied.Success, "A consumed pairing code was accepted a second time.");
            }

            using (var resumed = await ConnectAsync(invitation))
            {
                await ControlProtocol.WriteAsync(resumed.Stream,
                    new ControlRequest(1, 1, "resume", Value: trustedPc.DeviceId, Code: token), CancellationToken.None);
                ControlReply connected = await ControlProtocol.ReadAsync<ControlReply>(resumed.Stream, CancellationToken.None);
                Assert(connected.Success && connected.Snapshot is not null, "Trusted session did not resume.");
                Assert(connected.Snapshot!.Bitrates.Any(option => option.Value == "10M") &&
                    connected.Snapshot.VideoSettings?.Resolutions.Any(option => option.Value == "1280") == true,
                    "Linux VisionEngine did not advertise supported capture settings.");

                await ControlProtocol.WriteAsync(resumed.Stream,
                    new ControlRequest(1, 2, "bitrate", Value: "10M", Revision: connected.Snapshot.Revision), CancellationToken.None);
                ControlReply bitrate = await ControlProtocol.ReadAsync<ControlReply>(resumed.Stream, CancellationToken.None);
                Assert(bitrate.Success && bitrate.Snapshot?.Bitrate == "10M" &&
                    bitrate.Snapshot.Revision > connected.Snapshot.Revision,
                    "Linux VisionEngine did not apply and publish the selected bitrate.");

                await ControlProtocol.WriteAsync(resumed.Stream,
                    new ControlRequest(1, 3, "resolution", Value: "1280", Revision: bitrate.Snapshot!.Revision), CancellationToken.None);
                ControlReply resolution = await ControlProtocol.ReadAsync<ControlReply>(resumed.Stream, CancellationToken.None);
                Assert(resolution.Success && resolution.Snapshot?.VideoSettings?.Resolution == "1280",
                    "Linux VisionEngine did not apply the selected capture resolution.");

                await ControlProtocol.WriteAsync(resumed.Stream,
                    new ControlRequest(1, 4, "profile", Value: "Software", Revision: resolution.Snapshot!.Revision), CancellationToken.None);
                ControlReply profile = await ControlProtocol.ReadAsync<ControlReply>(resumed.Stream, CancellationToken.None);
                Assert(profile.Success && profile.Snapshot?.Profile == "Software",
                    "Linux VisionEngine did not publish the selected capture profile.");

                await ControlProtocol.WriteAsync(resumed.Stream,
                    new ControlRequest(1, 5, "fps", Value: "30", Revision: connected.Snapshot.Revision), CancellationToken.None);
                ControlReply stale = await ControlProtocol.ReadAsync<ControlReply>(resumed.Stream, CancellationToken.None);
                Assert(!stale.Success && stale.Message.Contains("ajustes cambiaron", StringComparison.OrdinalIgnoreCase),
                    "Linux VisionEngine accepted a stale capture-settings revision.");

                Assert(profile.Snapshot!.VideoSettings?.CanApplyTogether==true,"Batch VE settings not advertised.");
                string batch=JsonSerializer.Serialize(new ControlVideoChanges("Balanced","11M","1440","45","Disabled","0","Window"));
                await ControlProtocol.WriteAsync(resumed.Stream,new ControlRequest(1,51,"applyVideoSettings",batch,profile.Snapshot.Revision),CancellationToken.None);
                ControlReply applied=await ControlProtocol.ReadAsync<ControlReply>(resumed.Stream,CancellationToken.None);
                Assert(applied.Success && applied.Snapshot?.Bitrate=="11M" && applied.Snapshot.VideoSettings?.Resolution=="1440" && applied.Snapshot.AudioOutput=="Disabled","Android batch settings contract failed.");
                string invalid=JsonSerializer.Serialize(new ControlVideoChanges("Balanced","11M","9999","45","Pc","0"));
                await ControlProtocol.WriteAsync(resumed.Stream,new ControlRequest(1,52,"applyVideoSettings",invalid,applied.Snapshot!.Revision),CancellationToken.None);
                ControlReply invalidReply=await ControlProtocol.ReadAsync<ControlReply>(resumed.Stream,CancellationToken.None);
                Assert(!invalidReply.Success && invalidReply.Snapshot?.Bitrate=="11M" && invalidReply.Snapshot.AudioOutput=="Disabled","Invalid batch partially mutated settings.");
                await ControlProtocol.WriteAsync(resumed.Stream,new ControlRequest(1,53,"network.capabilities", "{\"Version\":1,\"FilesWireVersions\":[1,2]}"),CancellationToken.None);
                ControlReply capabilities=await ControlProtocol.ReadAsync<ControlReply>(resumed.Stream,CancellationToken.None);
                Assert(capabilities.Success,"Android capability negotiation failed.");

                await ControlProtocol.WriteAsync(resumed.Stream,
                    new ControlRequest(1, 54, "startLink", Revision: profile.Snapshot!.Revision), CancellationToken.None);
                ControlReply unsupported = await ControlProtocol.ReadAsync<ControlReply>(resumed.Stream, CancellationToken.None);
                Assert(!unsupported.Success && unsupported.Message.Contains("LinkEngine", StringComparison.Ordinal),
                    "Unsupported engine action was not reported explicitly.");
            }
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    using (var reopened = new LinuxTrustStore(directory))
    {
        Assert(reopened.Fingerprint == fingerprint, "Certificate identity changed after reopening the trust store.");
        Assert(reopened.Authenticate(trustedPc.DeviceId, token), "Saved trust token did not authenticate after reopening.");
        Assert(!reopened.Authenticate(trustedPc.DeviceId, new string('0', 64)), "Invalid trust token was accepted.");
        Assert(reopened.Devices.Count == 1, "Trusted-device entry did not persist.");
    }

    string trustPath = Path.Combine(directory, "control-trust.json");
    string savedTrust = await File.ReadAllTextAsync(trustPath);
    Assert(!savedTrust.Contains(token, StringComparison.Ordinal), "Plaintext trust token was persisted.");
    await File.WriteAllTextAsync(trustPath, "{}");
    try
    {
        using var corrupted = new LinuxTrustStore(directory);
        throw new InvalidOperationException("A corrupted trust store was silently regenerated.");
    }
    catch (InvalidDataException) { }

    if (OperatingSystem.IsLinux())
    {
        UnixFileMode directoryMode = File.GetUnixFileMode(directory);
        UnixFileMode fileMode = File.GetUnixFileMode(trustPath);
        Assert((directoryMode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) == 0,
            "Trust directory permissions are too broad.");
        Assert((fileMode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) == 0,
            "Trust file permissions are too broad.");
        Assert(await File.ReadAllTextAsync(trustPath) == "{}", "Corrupt trust data was overwritten.");
    }
}

static async Task<TestConnection> ConnectAsync(LanInvitation invitation)
{
    return await ConnectToEndpointAsync(invitation.Host, invitation.Port, invitation.Fingerprint);
}

static async Task TestLanLinkGatewayAsync(string directory)
{
    using var store = new LinuxTrustStore(directory);
    string relayToken = new string('a', 64);
    var relayListener = new TcpListener(IPAddress.Loopback, 0);
    relayListener.Start();
    int relayPort = ((IPEndPoint)relayListener.LocalEndpoint).Port;
    Task relaySession = Task.Run(async () =>
    {
        using TcpClient relay = await relayListener.AcceptTcpClientAsync();
        NetworkStream stream = relay.GetStream();
        string auth = await LinuxLinkProtocol.ReadFrameAsync(stream, CancellationToken.None);
        Assert(auth == LinuxLinkProtocol.CreateDataAuthentication(relayToken),
            "LAN gateway did not authenticate DATA against the relay.");
        await LinuxLinkProtocol.WriteFrameAsync(stream,
            LinuxLinkProtocol.DataAuthenticationAck, CancellationToken.None);
        await stream.WriteAsync(new byte[] { 0, 0, 0, 7 });
        byte[] packet = new byte[1];
        await stream.ReadExactlyAsync(packet);
        await stream.WriteAsync(packet);
    });

    await using var gateway = new LinuxLanLinkGateway(IPAddress.Loopback, store, relayPort,
        relayToken, allowLoopback: true);
    LanLinkOffer offer = gateway.Start();
    Assert(offer.Host == "127.0.0.1" && offer.Port is > 0 and <= 65535 && offer.Transport == "LAN",
        "LAN gateway offer is incomplete.");
    Assert(offer.Fingerprint == store.Fingerprint && LinuxLinkProtocol.IsToken(offer.Token),
        "LAN gateway offer does not carry the PC identity.");

    using (TestConnection rejected = await ConnectToEndpointAsync(offer.Host, offer.Port, offer.Fingerprint))
    {
        string wrong = offer.Token[0] == '0' ? new string('1', 64) : new string('0', 64);
        await ControlProtocol.WriteAsync(rejected.Stream, new LinkHello(1, wrong), CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { _ = await rejected.Stream.ReadAsync(new byte[1], timeout.Token); }
        catch (IOException) { }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
    }
    Assert(!gateway.IsConnected, "LAN gateway accepted an invalid DATA token.");

    using TestConnection client = await ConnectToEndpointAsync(offer.Host, offer.Port, offer.Fingerprint);
    await ControlProtocol.WriteAsync(client.Stream, new LinkHello(1, offer.Token), CancellationToken.None);
    await WaitUntilAsync(() => gateway.IsConnected, TimeSpan.FromSeconds(3));
    byte[] relayId = new byte[4];
    await client.Stream.ReadExactlyAsync(relayId);
    Assert(BinaryPrimitives.ReadUInt32BigEndian(relayId) == 7,
        "LAN gateway did not forward the relay identity.");
    await client.Stream.WriteAsync(new byte[] { 0x42 });
    byte[] echo = new byte[1];
    await client.Stream.ReadExactlyAsync(echo);
    Assert(echo[0] == 0x42, "LAN gateway did not bridge DATA in both directions.");
    await relaySession.WaitAsync(TimeSpan.FromSeconds(3));
    relayListener.Stop();
}

static async Task TestLanLinkControlRoutingAsync(string directory)
{
    using var store = new LinuxTrustStore(directory);
    var fake = new FakeLinkEngine();
    var server = new LinuxControlServer(IPAddress.Loopback, store, port: 0,
        allowLoopbackForTests: true, linkEngine: fake);
    server.Start();
    try
    {
        LanInvitation invitation = server.Invitation;
        using TestConnection paired = await ConnectAsync(invitation);
        await ControlProtocol.WriteAsync(paired.Stream,
            new ControlRequest(1, 1, "pair.lan", Code: invitation.Code), CancellationToken.None);
        ControlReply connected = await ControlProtocol.ReadAsync<ControlReply>(paired.Stream, CancellationToken.None);
        Assert(connected.Success && connected.Snapshot?.Engines?.LinkCanStart == true,
            "LAN control did not advertise LinkEngine as startable.");

        await ControlProtocol.WriteAsync(paired.Stream,
            new ControlRequest(1, 2, "startLink", Value: "USB"), CancellationToken.None);
        ControlReply wrongRoute = await ControlProtocol.ReadAsync<ControlReply>(paired.Stream, CancellationToken.None);
        Assert(!wrongRoute.Success, "LAN control accepted a USB LinkEngine offer.");

        await ControlProtocol.WriteAsync(paired.Stream,
            new ControlRequest(1, 3, "startLink"), CancellationToken.None);
        ControlReply started = await ControlProtocol.ReadAsync<ControlReply>(paired.Stream, CancellationToken.None);
        Assert(started.Success && started.Value?.Contains("\"Transport\":\"LAN\"", StringComparison.Ordinal) == true,
            "LAN control did not return the LAN LinkEngine offer.");
        Assert(started.Snapshot?.Engines?.LinkCanStop == true && started.Snapshot.Engines.LinkRunning,
            "LAN control snapshot did not publish the running LinkEngine.");

        await ControlProtocol.WriteAsync(paired.Stream,
            new ControlRequest(1, 4, "stopLink"), CancellationToken.None);
        ControlReply stopped = await ControlProtocol.ReadAsync<ControlReply>(paired.Stream, CancellationToken.None);
        Assert(stopped.Success && !fake.IsStarted, "LAN control did not stop LinkEngine.");
    }
    finally
    {
        await server.DisposeAsync();
    }
}

static async Task TestActiveRevocationAsync(string directory)
{
    using var store = new LinuxTrustStore(directory);
    (string deviceId, string token) = store.Enroll("Revocation test phone");
    (string otherDeviceId, _) = store.Enroll("Other trusted phone");
    var server = new LinuxControlServer(IPAddress.Loopback, store, port: 0, allowLoopbackForTests: true);
    server.Start();
    try
    {
        LanInvitation invitation = server.Invitation;
        using TestConnection active = await ConnectAsync(invitation);
        await ControlProtocol.WriteAsync(active.Stream,
            new ControlRequest(1, 1, "resume", Value: deviceId, Code: token), CancellationToken.None);
        ControlReply connected = await ControlProtocol.ReadAsync<ControlReply>(active.Stream, CancellationToken.None);
        Assert(connected.Success && server.IsAuthorized, "Trusted session did not become active for revocation test.");

        store.Revoke(otherDeviceId);
        await server.DisconnectRevokedDeviceAsync(otherDeviceId);
        Assert(server.IsAuthorized, "Revoking another phone disconnected the active trusted session.");

        store.Revoke(deviceId);
        await server.DisconnectRevokedDeviceAsync(deviceId);
        Assert(!server.IsAuthorized, "Revocation left the active control session authorized.");
        Assert(!store.Contains(deviceId), "Revocation left the trusted device in the store.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            _ = await ControlProtocol.ReadAsync<ControlReply>(active.Stream, timeout.Token);
            throw new InvalidOperationException("Revocation did not close the active TLS control session.");
        }
        catch (IOException) { }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new InvalidOperationException("Revocation left the active TLS session open.");
        }

        using TestConnection rejected = await ConnectAsync(invitation);
        await ControlProtocol.WriteAsync(rejected.Stream,
            new ControlRequest(1, 1, "resume", Value: deviceId, Code: token), CancellationToken.None);
        ControlReply denied = await ControlProtocol.ReadAsync<ControlReply>(rejected.Stream, CancellationToken.None);
        Assert(!denied.Success, "A revoked device resumed its trusted session.");
    }
    finally
    {
        await server.DisposeAsync();
    }
}

static async Task<TestConnection> ConnectToEndpointAsync(string host, int port, string fingerprint)
{
    var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Parse(host), port);
    byte[] expectedFingerprint = Convert.FromHexString(fingerprint);
    var stream = new SslStream(client.GetStream(), false, (_, certificate, _, _) =>
        certificate is not null && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(certificate.GetRawCertData()), expectedFingerprint));
    try
    {
        await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "NOVORA",
            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                System.Security.Authentication.SslProtocols.Tls13
        });
        return new TestConnection(client, stream);
    }
    catch
    {
        stream.Dispose();
        client.Dispose();
        throw;
    }
}

static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
{
    DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(3));
    while (!condition())
    {
        if (DateTime.UtcNow >= deadline)
            throw new InvalidOperationException("Timed out waiting for the expected protocol state.");
        await Task.Delay(20);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class TestConnection(TcpClient client, SslStream stream) : IDisposable
{
    public TcpClient Client { get; } = client;
    public SslStream Stream { get; } = stream;

    public void Dispose()
    {
        Stream.Dispose();
        Client.Dispose();
    }
}

sealed class FakeLinkEngine : ILinuxLinkEngine
{
    public event EventHandler? StateChanged;
    public bool IsStarted { get; private set; }
    public bool IsConnected => IsStarted;
    public string State => IsStarted ? "Connected" : "Stopped";
    public string Message => State;

    public Task<string> StartAsync(CancellationToken cancellationToken)
    {
        IsStarted = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult("{\"Transport\":\"LAN\"}");
    }

    public Task StopAsync()
    {
        IsStarted = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}

sealed class TestVisionRenderer : ILinuxVideoRenderer
{
    public TaskCompletionSource FirstFrame { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int FrameCount;
    public int Width;
    public int Height;

    public void Present(byte[] rgba, int width, int height)
    {
        if (rgba.Length != width * height * 4)
            throw new InvalidDataException("FFmpeg RGBA frame size mismatch.");
        Width = width;
        Height = height;
        Interlocked.Increment(ref FrameCount);
        FirstFrame.TrySetResult();
    }
}
