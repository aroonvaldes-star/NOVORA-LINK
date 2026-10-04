using System.Net;
using System.Security.Authentication;
using NOVORA.Control;
using NOVORA.LinkClient;
using NOVORA.VisionEngine.Transport;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestVeLanClient
{
    [Fact]
    public async Task ClientPinsAdvertisedCertificateAndAuthenticatesAllChannels()
    {
        using var scope = new TrustScope("Success");
        await using var session = new VETransportLanSession(
            IPAddress.Loopback, scope.Store, allowLoopback: true);
        NLControlVeLanOffer offer = session.PrepareVE(8_000_000, 1920, 60, true, audioEnabled: true);
        Task<VETransportLanAcceptedStreams> accepting = session.AcceptAsync();

        var client = new NovoraVeLanClient(allowLoopback: true);
        await using NovoraVeLanStreams connected = await client.ConnectAsync(offer, default);
        await using VETransportLanAcceptedStreams accepted =
            await accepting.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(connected.Audio);
        Assert.NotNull(accepted.Audio);
        await connected.Video.WriteAsync(new byte[] { 7, 8, 9 });
        byte[] received = new byte[3];
        await accepted.Video.ReadExactlyAsync(received);
        Assert.Equal(new byte[] { 7, 8, 9 }, received);
    }

    [Fact]
    public async Task ClientRejectsChangedCertificate()
    {
        using var scope = new TrustScope("ChangedCertificate");
        await using var session = new VETransportLanSession(
            IPAddress.Loopback, scope.Store, allowLoopback: true);
        NLControlVeLanOffer actual = session.PrepareVE(8_000_000, 1920, 60, true, audioEnabled: false);
        NLControlVeLanOffer forged = actual with { Fingerprint = new string('0', 64) };
        Task<VETransportLanAcceptedStreams> accepting = session.AcceptAsync();

        var client = new NovoraVeLanClient(allowLoopback: true);
        await Assert.ThrowsAnyAsync<AuthenticationException>(() => client.ConnectAsync(forged, default));

        await session.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accepting);
    }

    [Fact]
    public async Task ClientRejectsExpiredOfferBeforeOpeningSocket()
    {
        var offer = new NLControlVeLanOffer(
            NLControlProtocol.Version, Guid.NewGuid().ToString("N"), IPAddress.Loopback.ToString(),
            new string('A', 64), DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeSeconds(),
            new NLControlVeLanEndpoint(65001, new string('B', 64), "VIDEO"),
            new NLControlVeLanEndpoint(65002, new string('C', 64), "CONTROL"),
            null, 8_000_000, 1920, 60, true);
        var client = new NovoraVeLanClient(allowLoopback: true);

        await Assert.ThrowsAsync<InvalidDataException>(() => client.ConnectAsync(offer, default));
    }

    [Fact]
    public async Task ClientClosesPreviouslyOpenedStreamsWhenLaterChannelFails()
    {
        using var videoScope = new TrustScope("Video");
        using var controlScope = new TrustScope("Control");
        await using var videoChannel = new VETransportLanChannel(
            IPAddress.Loopback, videoScope.Store, NLControlVeLanChannel.Video, allowLoopback: true);
        await using var controlChannel = new VETransportLanChannel(
            IPAddress.Loopback, controlScope.Store, NLControlVeLanChannel.Control, allowLoopback: true);
        string sessionId = Guid.NewGuid().ToString("N");
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddSeconds(20);
        NLControlVeLanEndpoint videoEndpoint = videoChannel.PrepareVE(sessionId, expiresAt);
        NLControlVeLanEndpoint controlEndpoint = controlChannel.PrepareVE(sessionId, expiresAt);
        var offer = new NLControlVeLanOffer(
            NLControlProtocol.Version, sessionId, IPAddress.Loopback.ToString(), videoScope.Store.Fingerprint,
            expiresAt.ToUnixTimeSeconds(), videoEndpoint, controlEndpoint, null,
            8_000_000, 1920, 60, true);
        Task<Stream> acceptingVideo = videoChannel.AcceptAsync(sessionId, videoEndpoint, expiresAt);
        Task<Stream> acceptingControl = controlChannel.AcceptAsync(sessionId, controlEndpoint, expiresAt);

        var client = new NovoraVeLanClient(allowLoopback: true);
        await Assert.ThrowsAnyAsync<AuthenticationException>(() => client.ConnectAsync(offer, default));
        await using Stream serverVideo = await acceptingVideo.WaitAsync(TimeSpan.FromSeconds(5));
        await AssertPeerClosedAsync(serverVideo);

        await controlChannel.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acceptingControl);
    }

    private static async Task AssertPeerClosedAsync(Stream stream)
    {
        try
        {
            Assert.Equal(0, await stream.ReadAsync(new byte[1]).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5)));
        }
        catch (IOException)
        {
            // Windows can report an aborted socket instead of a clean TLS close-notify.
        }
    }

    private sealed class TrustScope : IDisposable
    {
        private readonly string _directory;

        internal TrustScope(string name)
        {
            _directory = Path.Combine(Path.GetTempPath(),
                $"NOVORA-VeLanClientTest-{name}-{Guid.NewGuid():N}");
            Store = new NLControlTrustStore(_directory);
        }

        internal NLControlTrustStore Store { get; }

        public void Dispose()
        {
            Store.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
