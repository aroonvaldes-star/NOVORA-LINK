using NOVORA.Control;
using NOVORA.LinkClient;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestVeLanProtocol
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact]
    public void VeLanOfferAcceptsPrivateIpv4BeforeExpiry()
    {
        NLControlVeLanOffer offer = Offer("192.168.1.20", Now.AddSeconds(30));

        offer.Validate(Now);

        Assert.Equal(1, offer.Version);
        Assert.Equal("VIDEO", offer.Video.Channel);
        Assert.Equal("CONTROL", offer.Control.Channel);
        Assert.Equal("AUDIO", offer.Audio!.Channel);
    }

    [Fact]
    public void VeLanOfferRejectsPublicLoopbackExpiredAndDuplicateTokens()
    {
        Assert.Throws<InvalidDataException>(() => Offer("8.8.8.8", Now.AddSeconds(30)).Validate(Now));
        Assert.Throws<InvalidDataException>(() => Offer("127.0.0.1", Now.AddSeconds(30)).Validate(Now));
        Assert.Throws<InvalidDataException>(() => Offer("192.168.1.20", Now).Validate(Now));
        Assert.Throws<InvalidDataException>(() => Offer("192.168.1.20", Now.AddSeconds(31)).Validate(Now));

        NLControlVeLanOffer duplicate = Offer("192.168.1.20", Now.AddSeconds(30)) with
        {
            Audio = Endpoint(41203, 'B', "AUDIO")
        };
        Assert.Throws<InvalidDataException>(() => duplicate.Validate(Now));
    }

    [Fact]
    public void VeLanHelloRequiresMatchingChannel()
    {
        NLControlVeLanEndpoint video = Endpoint(41201, 'A', "VIDEO");
        var valid = new NLControlVeLanHello(1, "session-123", "VIDEO", video.Token);
        valid.Validate("session-123", video);

        Assert.Throws<InvalidDataException>(() =>
            (valid with { Channel = "CONTROL" }).Validate("session-123", video));
        Assert.Throws<InvalidDataException>(() =>
            (valid with { SessionId = "other-session" }).Validate("session-123", video));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("other", 0)]
    [InlineData("USB", 0)]
    [InlineData("LAN", 1)]
    public void VeTransportSelectionDefaultsToUsbAndAcceptsLan(
        string? storedValue,
        int expected)
    {
        Assert.Equal((NovoraVeTransportSelection)expected, NovoraVeTransportPolicy.Resolve(storedValue));
    }

    [Fact]
    public void StartVideoLanUsesVideoStartPreconditions()
    {
        var engines = new NLControlEngines(true, false, false, false, false,
            "Stopped", "", "Phone");
        var state = new NLControlSnapshot(7, "PC", "1", "8M", "Balanced", "", "", false,
            [], [], [], engines);

        Assert.Null(NLControlCommands.Validate(
            new NLControlRequest(1, 1, "startVideoLan", Revision: 7), state));
        Assert.NotNull(NLControlCommands.Validate(
            new NLControlRequest(1, 2, "startVideoLan", Value: "extra", Revision: 7), state));
    }

    private static NLControlVeLanOffer Offer(string host, DateTimeOffset expires) => new(
        1,
        "session-123",
        host,
        new string('F', 64),
        expires.ToUnixTimeSeconds(),
        Endpoint(41201, 'A', "VIDEO"),
        Endpoint(41202, 'B', "CONTROL"),
        Endpoint(41203, 'C', "AUDIO"),
        8_000_000,
        1920,
        60,
        true);

    private static NLControlVeLanEndpoint Endpoint(int port, char token, string channel) =>
        new(port, new string(token, 64), channel);
}
