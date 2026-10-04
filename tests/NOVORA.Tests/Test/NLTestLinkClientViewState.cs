using NOVORA.Control;
using NOVORA.LinkClient;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestLinkClientViewState
{
    [Fact]
    public void Disconnected_state_disables_remote_actions()
    {
        var state = NovoraViewState.From(new(1, NLControlSessionPhase.Disconnected, "", "Sin conexión", null));

        Assert.Equal("Sin conexión", state.Connection);
        Assert.Equal("Iniciar VisionEngine", state.VideoButton.Label);
        Assert.False(state.VideoButton.Enabled);
        Assert.False(state.LinkButton.Enabled);
        Assert.False(state.CaptureEnabled);
        Assert.False(state.RecordButton.Enabled);
    }

    [Fact]
    public void Connected_snapshot_drives_engine_actions_and_real_status()
    {
        var snapshot = Snapshot() with
        {
            Engines = new(false, true, true, false, false, "Detenido", "Listo para iniciar", "Galaxy",
                VideoState: "Activo", VideoMessage: "Transmitiendo", ExInState: "Detected", ExInMessage: "Mando listo"),
            VideoRunning = true
        };
        var state = NovoraViewState.From(new(2, NLControlSessionPhase.Connected, "LAN", "Conectado", snapshot));

        Assert.Equal("HOST-RIG", state.PcName);
        Assert.Equal("LAN segura", state.Transport);
        Assert.Equal("stopVideo", state.VideoButton.Action);
        Assert.Equal("Detener VisionEngine", state.VideoButton.Label);
        Assert.True(state.VideoButton.Enabled);
        Assert.Equal("startLink", state.LinkButton.Action);
        Assert.Equal("Iniciar LinkEngine", state.LinkButton.Label);
        Assert.True(state.LinkButton.Enabled);
        Assert.Equal("Mando listo", state.ExInStatus);
    }

    [Fact]
    public void Media_and_controller_values_come_from_snapshot()
    {
        var snapshot = Snapshot() with
        {
            Media = new(true, true, true, "Grabando", AudioIncluded: true),
            FileSharing = true,
            ExIn = new(true, "DualSense", "054C : 0CE6", 11, -12, 23, -24, 31, 42,
                ["A", "L1"], false, true, 0.08, "Operativo", Family: "DualSense", Mode: "Game",
                ConnectionType: "USB", BatteryState: "Charging", BatteryPercent: 88)
        };
        var state = NovoraViewState.From(new(3, NLControlSessionPhase.Connected, "LAN", "Conectado", snapshot));

        Assert.Equal("Detener grabación", state.RecordButton.Label);
        Assert.Equal("stopRecording", state.RecordButton.Action);
        Assert.True(state.CaptureEnabled);
        Assert.True(state.FileSharing);
        Assert.Contains("DualSense", state.ControllerDetails, StringComparison.Ordinal);
        Assert.Equal("Stick L (L3)\nX:11 Y:-12", state.LeftStick);
        Assert.Equal("LT / L2    31", state.LeftTrigger);
    }

    private static NLControlSnapshot Snapshot() => new(
        8, "HOST-RIG", "1.4.53", "25M", "Gaming", "Speakers", "Speakers", false,
        [new("25M", "25 Mbps")], [new("Gaming", "Gaming")], [new("Speakers", "Speakers")],
        VideoSettings: new("2560x1440", "120", [new("2560x1440", "1440p QHD")], [new("120", "120 FPS")]),
        Media: new(true, true, false, "Listo"));
}
