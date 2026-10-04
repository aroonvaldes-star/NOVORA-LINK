using System.Net;
using System.Text.Json;
using NOVORA.Control;
using NOVORA.LinkClient;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestVeCaptureCoordinator
{
    [Fact]
    public async Task UsbSelectionSendsStartVideoWithoutProjection()
    {
        var sent = new List<string>();
        var coordinator = Coordinator(sent, _ => Reply(true, "USB iniciado."));

        NovoraVeStartDecision decision = await coordinator.BeginAsync(
            NovoraVeTransportSelection.Usb, ConnectedState(videoRunning: false));

        Assert.True(decision.Success);
        Assert.False(decision.RequestProjection);
        Assert.Equal(new[] { "startVideo" }, sent);
    }

    [Fact]
    public async Task LanSelectionSendsStartVideoLanThenRequestsProjection()
    {
        NLControlVeLanOffer offer = ValidOffer();
        var sent = new List<string>();
        var coordinator = Coordinator(sent, _ => Reply(true, "LAN preparado.", JsonSerializer.Serialize(offer)));

        NovoraVeStartDecision decision = await coordinator.BeginAsync(
            NovoraVeTransportSelection.Lan, ConnectedState(videoRunning: false));

        Assert.True(decision.Success);
        Assert.True(decision.RequestProjection);
        Assert.Equal(offer, decision.Offer);
        Assert.Equal(new[] { "startVideoLan" }, sent);
    }

    [Fact]
    public async Task RunningSessionRequiresStopBeforeSelectionChange()
    {
        var sent = new List<string>();
        var coordinator = Coordinator(sent, _ => Reply(true, "No debe enviarse."));

        NovoraVeStartDecision decision = await coordinator.BeginAsync(
            NovoraVeTransportSelection.Lan, ConnectedState(videoRunning: true));

        Assert.False(decision.Success);
        Assert.True(decision.StopRequired);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task ProjectionDenialStopsRemotePreparation()
    {
        NLControlVeLanOffer offer = ValidOffer();
        var sent = new List<string>();
        var coordinator = Coordinator(sent, action => action == "startVideoLan"
            ? Reply(true, "LAN preparado.", JsonSerializer.Serialize(offer))
            : Reply(true, "Detenido."));
        _ = await coordinator.BeginAsync(
            NovoraVeTransportSelection.Lan, ConnectedState(videoRunning: false));

        await coordinator.ProjectionDeniedAsync();
        await coordinator.ProjectionDeniedAsync();

        Assert.Equal(new[] { "startVideoLan", "stopVideo" }, sent);
    }

    [Fact]
    public async Task NoFallbackOccursAfterLanFailure()
    {
        var sent = new List<string>();
        var coordinator = Coordinator(sent, _ => Reply(false, "LAN no disponible."));

        NovoraVeStartDecision decision = await coordinator.BeginAsync(
            NovoraVeTransportSelection.Lan, ConnectedState(videoRunning: false));

        Assert.False(decision.Success);
        Assert.False(decision.RequestProjection);
        Assert.Equal(new[] { "startVideoLan" }, sent);
    }

    private static NovoraVeCaptureCoordinator Coordinator(
        List<string> sent,
        Func<string, NLControlReply> reply) => new(async (action, _) =>
        {
            sent.Add(action);
            await Task.Yield();
            return reply(action);
        }, allowLoopback: true);

    private static NLControlReply Reply(bool success, string message, string? value = null) =>
        new(NLControlProtocol.Version, 1, success, message, Value: value);

    private static NLControlSessionState ConnectedState(bool videoRunning)
    {
        var engines = new NLControlEngines(
            VideoCanStart: !videoRunning, VideoCanStop: videoRunning,
            LinkCanStart: true, LinkCanStop: false, LinkRunning: false,
            LinkState: "Stopped", LinkMessage: "", DeviceName: "Android");
        var snapshot = new NLControlSnapshot(
            1, "NOVORA", "1", "8 Mbps", "Equilibrado", "Desactivado", "Desactivado",
            videoRunning, [], [], [], engines);
        return new NLControlSessionState(1, NLControlSessionPhase.Connected, "LAN", "Conectado", snapshot);
    }

    private static NLControlVeLanOffer ValidOffer()
    {
        long expiry = DateTimeOffset.UtcNow.AddSeconds(20).ToUnixTimeSeconds();
        return new NLControlVeLanOffer(
            NLControlProtocol.Version, Guid.NewGuid().ToString("N"), IPAddress.Loopback.ToString(),
            new string('A', 64), expiry,
            new NLControlVeLanEndpoint(61001, new string('B', 64), "VIDEO"),
            new NLControlVeLanEndpoint(61002, new string('C', 64), "CONTROL"),
            new NLControlVeLanEndpoint(61003, new string('D', 64), "AUDIO"),
            8_000_000, 1920, 60, true);
    }
}
