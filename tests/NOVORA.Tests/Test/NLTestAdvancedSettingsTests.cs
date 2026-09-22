using System.Text.Json;
using NOVORA.Service;
using NOVORA.ViewModel;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestAdvancedSettingsTests
{
    [Fact]
    public void Saved_advanced_preferences_survive_json_round_trip_and_restore()
    {
        var initial = new NLViewModelMain
        {
            PrivacyShieldEnabled = true,
            IntegrationClipboardEnabled = false,
            IntegrationFileTransferEnabled = false,
            IntegrationDragDropEnabled = false,
            IntegrationApplicationsEnabled = false,
            IntegrationNotificationsEnabled = false,
            IntegrationDynamicResizeEnabled = false,
            ExInEnabled = false,
            NvidiaProfile = "Disabled"
        };
        var saved = new NLServiceNovoraSettings();
        NLServiceSettingsAdvanced.CaptureNV(initial, saved);
        var loaded = JsonSerializer.Deserialize<NLServiceNovoraSettings>(JsonSerializer.Serialize(saved))!;
        var restored = new NLViewModelMain();
        NLServiceSettingsAdvanced.ApplyNV(loaded, restored);
        Assert.True(restored.PrivacyShieldEnabled);
        Assert.False(restored.IntegrationClipboardEnabled);
        Assert.False(restored.IntegrationFileTransferEnabled);
        Assert.False(restored.IntegrationDragDropEnabled);
        Assert.False(restored.IntegrationApplicationsEnabled);
        Assert.False(restored.IntegrationNotificationsEnabled);
        Assert.False(restored.IntegrationDynamicResizeEnabled);
        Assert.False(restored.ExInEnabled);
        Assert.Equal("Disabled", restored.NvidiaProfile);
    }

    [Fact]
    public void Cancel_snapshot_restores_values_after_live_changes()
    {
        var view = new NLViewModelMain();
        var snapshot = new NLServiceNovoraSettings();
        NLServiceSettingsAdvanced.CaptureNV(view, snapshot);
        view.PrivacyShieldEnabled = true;
        view.NvidiaProfile = "Disabled";
        NLServiceSettingsAdvanced.ApplyNV(snapshot, view);
        Assert.False(view.PrivacyShieldEnabled);
        Assert.Equal("Automatic", view.NvidiaProfile);
    }

    [Fact]
    public void Older_settings_files_get_defaults_without_credentials()
    {
        var settings = JsonSerializer.Deserialize<NLServiceNovoraSettings>("{\"TargetFps\":30,\"RemoteAndroidEnabled\":true}")!;
        Assert.Equal(30, settings.TargetFps);
        Assert.DoesNotContain("RemoteAndroidEnabled", JsonSerializer.Serialize(settings));
        Assert.False(settings.PrivacyShieldEnabled);
        Assert.DoesNotContain("token", JsonSerializer.Serialize(settings), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Legacy_gamepad_setting_migrates_to_exin()
    {
        var settings = JsonSerializer.Deserialize<NLServiceNovoraSettings>("{\"GamepadEnabled\":false}")!;
        Assert.False(settings.ExInEnabled);
    }
}
