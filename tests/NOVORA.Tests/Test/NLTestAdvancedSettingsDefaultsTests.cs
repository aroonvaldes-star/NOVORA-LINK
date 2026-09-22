using NOVORA.Service;
using NOVORA.ViewModel;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestAdvancedSettingsDefaultsTests
{
    [Fact]
    public void Advanced_settings_default_to_safe_operational_values()
    {
        var settings = new NLServiceNovoraSettings();

        Assert.False(settings.PrivacyShieldEnabled);
        Assert.True(settings.IntegrationClipboardEnabled);
        Assert.True(settings.IntegrationFileTransferEnabled);
        Assert.True(settings.IntegrationDragDropEnabled);
        Assert.True(settings.IntegrationApplicationsEnabled);
        Assert.True(settings.IntegrationNotificationsEnabled);
        Assert.True(settings.IntegrationDynamicResizeEnabled);
        Assert.True(settings.ExInEnabled);
        Assert.Equal("Competitive", settings.NvidiaProfile);
        Assert.Equal("8M", settings.Bitrate);

        var view = new NLViewModelMain();
        Assert.Equal("Competitive", view.NvidiaProfile);
        Assert.Equal("8M", view.Bitrate);
        Assert.Equal(14, view.BitrateOptions.Count);
        Assert.Equal("2M", view.BitrateOptions[0].Value);
        Assert.Equal("15M", view.BitrateOptions[^1].Value);
    }
}
