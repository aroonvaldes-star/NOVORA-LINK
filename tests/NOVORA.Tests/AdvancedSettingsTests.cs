using System.Text.Json;
using NOVORA.Services;
using NOVORA.ViewModels;
using Xunit;

namespace NOVORA.Tests;

public sealed class AdvancedSettingsTests
{
    [Fact]
    public void Saved_advanced_preferences_survive_json_round_trip_and_restore()
    {
        var initial = new MainViewModel
        {
            PrivacyShieldEnabled = true,
            IntegrationClipboardEnabled = false,
            IntegrationFileTransferEnabled = false,
            IntegrationDragDropEnabled = false,
            IntegrationApplicationsEnabled = false,
            IntegrationNotificationsEnabled = false,
            IntegrationDynamicResizeEnabled = false,
            GamepadEnabled = false,
            RemoteAndroidEnabled = false,
            NvidiaProfile = "Disabled"
        };
        var saved = new NovoraSettings();
        SettingsAdvancedNV.CaptureNV(initial, saved);
        var loaded = JsonSerializer.Deserialize<NovoraSettings>(JsonSerializer.Serialize(saved))!;
        var restored = new MainViewModel();
        SettingsAdvancedNV.ApplyNV(loaded, restored);
        Assert.True(restored.PrivacyShieldEnabled);
        Assert.False(restored.IntegrationClipboardEnabled);
        Assert.False(restored.IntegrationFileTransferEnabled);
        Assert.False(restored.IntegrationDragDropEnabled);
        Assert.False(restored.IntegrationApplicationsEnabled);
        Assert.False(restored.IntegrationNotificationsEnabled);
        Assert.False(restored.IntegrationDynamicResizeEnabled);
        Assert.False(restored.GamepadEnabled);
        Assert.False(restored.RemoteAndroidEnabled);
        Assert.Equal("Disabled", restored.NvidiaProfile);
    }

    [Fact]
    public void Cancel_snapshot_restores_values_after_live_changes()
    {
        var view = new MainViewModel();
        var snapshot = new NovoraSettings();
        SettingsAdvancedNV.CaptureNV(view, snapshot);
        view.PrivacyShieldEnabled = true;
        view.RemoteAndroidEnabled = false;
        view.NvidiaProfile = "Disabled";
        SettingsAdvancedNV.ApplyNV(snapshot, view);
        Assert.False(view.PrivacyShieldEnabled);
        Assert.True(view.RemoteAndroidEnabled);
        Assert.Equal("Automatic", view.NvidiaProfile);
    }

    [Fact]
    public void Older_settings_files_get_defaults_without_credentials()
    {
        var settings = JsonSerializer.Deserialize<NovoraSettings>("{\"TargetFps\":30}")!;
        Assert.Equal(30, settings.TargetFps);
        Assert.True(settings.RemoteAndroidEnabled);
        Assert.False(settings.PrivacyShieldEnabled);
        Assert.DoesNotContain("token", JsonSerializer.Serialize(settings), StringComparison.OrdinalIgnoreCase);
    }
}
