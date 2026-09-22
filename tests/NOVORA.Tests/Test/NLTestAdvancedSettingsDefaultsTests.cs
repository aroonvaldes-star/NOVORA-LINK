using NOVORA.Service;
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
        Assert.Equal("Automatic", settings.NvidiaProfile);
    }
}
