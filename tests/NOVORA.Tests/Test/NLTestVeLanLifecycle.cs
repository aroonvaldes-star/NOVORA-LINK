using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using NOVORA.Control;
using NOVORA.Service;
using NOVORA.VisionEngine.Core;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Transport;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestVeLanLifecycle
{
    [Theory]
    [InlineData("LAN", true, true, true)]
    [InlineData("USB", true, true, false)]
    [InlineData("LAN", false, true, false)]
    [InlineData("LAN", true, false, false)]
    public void StartVideoLanRequiresAuthorizedLanControl(
        string transport, bool authorized, bool engineCanStart, bool expected)
    {
        Assert.Equal(expected,
            NLUIWindowMain.CanStartVeLanVideo(transport, authorized, engineCanStart));
    }

    [Fact]
    public async Task StartVideoLanReturnsThirtySecondPrivateOffer()
    {
        using var scope = new TrustScope();
        await using var session = new VETransportLanSession(
            IPAddress.Loopback, scope.Store, allowLoopback: true);

        NLControlVeLanOffer offer = session.PrepareVE(
            bitrate: 8_000_000, maxSize: 1920, fps: 60,
            muteDeviceAudio: true, audioEnabled: true);

        offer.Validate(DateTimeOffset.UtcNow, allowLoopback: true);
        Assert.Equal(IPAddress.Loopback.ToString(), offer.Host);
        Assert.InRange(offer.ExpiresUnix - DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 1, 30);
        Assert.Equal(scope.Store.Fingerprint, offer.Fingerprint);
    }

    [Fact]
    public async Task FirstValidatedFrameMarksNativeLanRunning()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var sender = new TcpClient();
        Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
        await sender.ConnectAsync(IPAddress.Loopback, port);
        using TcpClient receiver = await accepting;
        listener.Stop();
        await using MemoryStream control = new();
        var writer = new VEProtocolWriter(sender.GetStream());
        await writer.WriteVideoSessionAsync(VEProtocolCodec.H264,
            new VEProtocolSession(720, 1280, false));
        await using var engine = new VECoreEngine(new NLServiceNovoraPaths());
        Assert.True((await engine.InitializeAsync()).Success);

        VECoreResult started = await engine.StartExternalSourceAsync(
            "LAN:192.168.1.20", VEExternalSourceKind.NativeLan,
            receiver.GetStream(), control, audioStream: null, audioEnabled: false);

        Assert.True(started.Success, started.Message);
        Assert.True(engine.IsRunningVE);
        Assert.Equal(VEExternalSourceKind.NativeLan, engine.RuntimeVE.ExternalSourceKindVE);
        Assert.True((await engine.StopAsync()).Success);
    }

    [Fact]
    public async Task AudioFailureMarksDegradedWithoutStoppingVideo()
    {
        using var scope = new TrustScope();
        await using var session = new VETransportLanSession(
            IPAddress.Loopback, scope.Store, allowLoopback: true,
            optionalAudioGrace: TimeSpan.FromMilliseconds(150));
        NLControlVeLanOffer offer = session.PrepareVE(
            8_000_000, 1920, 60, muteDeviceAudio: true, audioEnabled: true);
        Task<VETransportLanAcceptedStreams> accepting = session.AcceptAsync();

        using SslStream video = await ConnectAsync(offer, offer.Video);
        using SslStream control = await ConnectAsync(offer, offer.Control);
        await using VETransportLanAcceptedStreams streams =
            await accepting.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(streams.Degraded);
        Assert.Null(streams.Audio);
        Assert.True(streams.Video.CanRead);
        Assert.True(streams.Control.CanRead);
    }

    [Fact]
    public async Task ControlGenerationChangeDisposesEveryChannel()
    {
        using var scope = new TrustScope();
        var session = new VETransportLanSession(
            IPAddress.Loopback, scope.Store, allowLoopback: true);
        _ = session.PrepareVE(8_000_000, 1920, 60, true, audioEnabled: false);
        Task<VETransportLanAcceptedStreams> accepting = session.AcceptAsync();

        await session.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accepting);
    }

    private static async Task<SslStream> ConnectAsync(
        NLControlVeLanOffer offer,
        NLControlVeLanEndpoint endpoint)
    {
        var socket = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        await socket.ConnectAsync(IPAddress.Parse(offer.Host), endpoint.Port);
        var tls = new SslStream(socket.GetStream(), false, (_, certificate, _, _) =>
            certificate is not null && Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())) == offer.Fingerprint);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "NOVORA",
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        });
        await NLControlProtocol.WriteAsync(tls,
            new NLControlVeLanHello(offer.Version, offer.SessionId, endpoint.Channel, endpoint.Token), default);
        return tls;
    }

    private sealed class TrustScope : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(),
            "NOVORA-VeLanLifecycleTest-" + Guid.NewGuid().ToString("N"));
        internal TrustScope() => Store = new NLControlTrustStore(_directory);
        internal NLControlTrustStore Store { get; }
        public void Dispose()
        {
            Store.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
