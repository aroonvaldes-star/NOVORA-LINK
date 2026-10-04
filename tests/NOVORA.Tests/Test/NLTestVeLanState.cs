using NOVORA.Control;
using NOVORA.LinkClient;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestVeLanState
{
    [Theory]
    [InlineData("Disconnected", false, "Sin conexión")]
    [InlineData("Ready", false, "Listo")]
    [InlineData("AwaitingPermission", false, "Esperando permiso")]
    [InlineData("Preparing", false, "Preparando")]
    [InlineData("Connecting", false, "Conectando")]
    [InlineData("Streaming", false, "Transmitiendo")]
    [InlineData("Degraded", true, "Transmitiendo sin audio")]
    [InlineData("Error", false, "Error")]
    [InlineData("Stopping", false, "Deteniendo")]
    public void LanPhasesMapToVisibleSpanishStates(string phase, bool degraded, string expected)
    {
        Assert.Equal(expected, NovoraViewState.DescribeVePhase(phase, degraded));
    }

    [Fact]
    public void WifiLossStopsLanWithoutSelectingUsb()
    {
        NovoraVeTransportSelection selection = NovoraVeTransportPolicy.Resolve("LAN");
        NovoraViewState view = NovoraViewState.From(new(
            4, NLControlSessionPhase.Lost, "LAN", "Wi-Fi desconectado", null));

        Assert.Equal(NovoraVeTransportSelection.Lan, selection);
        Assert.False(view.VideoButton.Enabled);
        Assert.Equal("Conexión perdida", view.Connection);
    }

    [Fact]
    public void ControlLossCancelsOfferAndCapture()
    {
        Assert.False(NLUIWindowMain.IsVeLanControlCurrent(
            expectedGeneration: 7, currentGeneration: 8, authorized: false,
            offerHost: "192.168.1.20", currentHost: null));
    }

    [Fact]
    public void AddressChangeRejectsStaleOffer()
    {
        Assert.False(NLUIWindowMain.IsVeLanControlCurrent(
            expectedGeneration: 7, currentGeneration: 7, authorized: true,
            offerHost: "192.168.1.20", currentHost: "192.168.1.21"));
    }

    [Fact]
    public void AudioFailurePublishesDegradedWhileVideoRemainsRunning()
    {
        var engines = new NLControlEngines(
            VideoCanStart: false, VideoCanStop: true,
            LinkCanStart: false, LinkCanStop: false, LinkRunning: false,
            LinkState: "Stopped", LinkMessage: "", DeviceName: "Android LAN",
            VideoState: "Activo", VideoMessage: "Video estable",
            VideoTransport: "LAN", VideoPhase: "Degraded", VideoDegraded: true);
        var snapshot = new NLControlSnapshot(
            9, "NOVORA", "1", "8 Mbps", "Equilibrado", "Altavoces", "Sin audio",
            true, [], [], [], engines);

        NovoraViewState view = NovoraViewState.From(new(
            9, NLControlSessionPhase.Connected, "LAN", "Conectado", snapshot));

        Assert.Equal("Detener VisionEngine", view.VideoButton.Label);
        Assert.Contains("Transmitiendo sin audio", view.VideoStatus, StringComparison.Ordinal);
        Assert.Contains("LAN", view.VideoDetails, StringComparison.Ordinal);
    }
}
