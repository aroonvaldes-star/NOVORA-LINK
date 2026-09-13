using System.Text.Json;
using NOVORA.Remote;
using Xunit;

namespace NOVORA.RemoteV2.Tests;

public sealed class RemoteSettingsV2Tests
{
    [Fact]
    public void Settings_update_json_round_trips_all_settingswindow_fields()
    {
        var update = new SettingsUpdateRemoteNV
        {
            Theme = "Light",
            VideoPresentationMode = "Fullscreen",
            SelectedMonitorDeviceName = "\\\\.\\DISPLAY1",
            Bitrate = "20M",
            TargetFps = 120,
            MaxSize = 2340,
            SelectedAudioOutput = "Predeterminado"
        };

        string json = JsonSerializer.Serialize(update);
        SettingsUpdateRemoteNV? parsed = JsonSerializer.Deserialize<SettingsUpdateRemoteNV>(json);

        Assert.NotNull(parsed);
        Assert.Equal(update.Theme, parsed!.Theme);
        Assert.Equal(update.VideoPresentationMode, parsed.VideoPresentationMode);
        Assert.Equal(update.SelectedMonitorDeviceName, parsed.SelectedMonitorDeviceName);
        Assert.Equal(update.Bitrate, parsed.Bitrate);
        Assert.Equal(update.TargetFps, parsed.TargetFps);
        Assert.Equal(update.MaxSize, parsed.MaxSize);
        Assert.Equal(update.SelectedAudioOutput, parsed.SelectedAudioOutput);
    }

    [Fact]
    public void Snapshot_can_expose_current_settingswindow_options_without_personal_data()
    {
        var snapshot = new SettingsSnapshotRemoteNV
        {
            Theme = "Dark",
            ThemeOptions =
            [
                new("Dark", "Dark"),
                new("Light", "Light")
            ],
            DeviceNativeResolution = "1080x2340",
            DeviceMaxFps = "120 FPS",
            DeviceMaxRefreshRate = "120 Hz"
        };

        string json = JsonSerializer.Serialize(snapshot);

        Assert.Contains("1080x2340", json);
        Assert.False(json.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains("email", StringComparison.OrdinalIgnoreCase));
    }
}
