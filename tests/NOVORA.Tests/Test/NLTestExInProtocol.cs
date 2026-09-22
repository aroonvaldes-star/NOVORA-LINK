using NOVORA.Control;
using NOVORA;
using Xunit;

namespace NOVORA.Tests.Test;

public sealed class NLTestExInProtocol
{
    [Theory]
    [InlineData("USB-DEVICE", "USB-CONTROL", "LAN-VISION", "USB-DEVICE")]
    [InlineData(null, "USB-CONTROL", "LAN-VISION", "USB-CONTROL")]
    [InlineData(null, null, "LAN-VISION", "LAN-VISION")]
    [InlineData("  ", " USB-CONTROL ", null, "USB-CONTROL")]
    public void ExIn_target_prefers_live_device_then_authorized_control_then_vision(
        string? connectedDevice,
        string? authorizedControl,
        string? activeVision,
        string expected)
        => Assert.Equal(
            expected,
            NLUIWindowMain.ResolveExInSerialVE(connectedDevice, authorizedControl, activeVision));

    [Theory]
    [InlineData("Game")]
    [InlineData("Ui")]
    public void Mode_accepts_known_values(string mode)
        => Assert.Null(NLControlCommands.Validate(RequestVE("exin.mode", mode), SnapshotVE()));

    [Fact]
    public void Mode_rejects_invalid_stale_and_busy_requests()
    {
        Assert.Equal("Modo de control no válido.", NLControlCommands.Validate(RequestVE("exin.mode", "Auto"), SnapshotVE()));
        Assert.Contains("cambiaron", NLControlCommands.Validate(RequestVE("exin.mode", "Game") with { Revision = 6 }, SnapshotVE()));
        Assert.Contains("cambiando", NLControlCommands.Validate(RequestVE("exin.mode", "Game"), SnapshotVE(transitioning: true)));
    }

    [Fact]
    public void Reactivate_requires_detected_controller_and_no_value()
    {
        Assert.Null(NLControlCommands.Validate(RequestVE("exin.reactivate"), SnapshotVE()));
        Assert.Contains("control físico", NLControlCommands.Validate(RequestVE("exin.reactivate"), SnapshotVE(detected: false)));
        Assert.Contains("no acepta", NLControlCommands.Validate(RequestVE("exin.reactivate", "x"), SnapshotVE()));
    }

    [Fact]
    public void Calibration_requires_explicit_capability()
    {
        Assert.Contains("no está disponible", NLControlCommands.Validate(RequestVE("exin.calibration.start"), SnapshotVE()));
        NLControlSnapshot allowed = SnapshotVE() with { ExIn = SnapshotVE().ExIn! with { CanCalibrate = true } };
        Assert.Null(NLControlCommands.Validate(RequestVE("exin.calibration.start"), allowed));
    }

    [Fact]
    public async Task Legacy_exin_json_keeps_new_fields_optional()
    {
        const string json = "{\"Detected\":true,\"DeviceName\":\"Pad\",\"VidPid\":\"045E : 028E\",\"LeftX\":0,\"LeftY\":0,\"RightX\":0,\"RightY\":0,\"LeftTrigger\":0,\"RightTrigger\":0,\"Buttons\":[],\"Calibrating\":false,\"Calibrated\":false,\"Deadzone\":0.05,\"Message\":\"ok\"}";
        using MemoryStream stream = new();
        await NLControlProtocol.WriteAsync(stream, System.Text.Json.JsonSerializer.Deserialize<NLControlExIn>(json)!, default);
        stream.Position = 0;
        NLControlExIn value = await NLControlProtocol.ReadAsync<NLControlExIn>(stream, default);
        Assert.Equal("Game", value.Mode);
        Assert.Equal("Unknown", value.Family);
        Assert.Equal(-1, value.BatteryPercent);
        Assert.Equal("", value.BatteryAlert);
        Assert.Equal(0, value.BatteryAlertSequence);
    }

    private static NLControlRequest RequestVE(string action, string? value = null)
        => new(NLControlProtocol.Version, 1, action, value, 7);

    private static NLControlSnapshot SnapshotVE(bool detected = true, bool transitioning = false)
        => new(7, "PC", "1.4", "8M", "Gaming", "Disabled", "Disabled", false, [], [], [],
            ExIn: new(detected, "Pad", "045E : 028E", 0, 0, 0, 0, 0, 0, [], false, false, 0.05, "ok",
                "Xbox", "Game", transitioning, !transitioning, !transitioning));
}
