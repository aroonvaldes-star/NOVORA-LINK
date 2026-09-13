using NOVORA.Remote;
using NOVORA.Services;
using Xunit;

namespace NOVORA.Tests;

public sealed class RemoteSecurityTests
{
    [Fact]
    public void Remote_hello_requires_current_v2_session_token()
    {
        const string token = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        string hello = $"NOVORA-REMOTE|2|HELLO|{token}";

        Assert.True(ProtocolRemoteNV.IsHelloNV(hello, token));
        Assert.False(ProtocolRemoteNV.IsHelloNV(hello, "OTHER"));
        Assert.False(ProtocolRemoteNV.IsHelloNV("NOVORA-REMOTE|1|HELLO", token));
        Assert.False(ProtocolRemoteNV.IsHelloNV("NOVORA-REMOTE|2|HELLO", token));
    }

    [Fact]
    public void Advanced_settings_default_to_safe_operational_values()
    {
        var settings = new NovoraSettings();

        Assert.False(settings.PrivacyShieldEnabled);
        Assert.True(settings.IntegrationClipboardEnabled);
        Assert.True(settings.IntegrationFileTransferEnabled);
        Assert.True(settings.IntegrationDragDropEnabled);
        Assert.True(settings.IntegrationApplicationsEnabled);
        Assert.True(settings.IntegrationNotificationsEnabled);
        Assert.True(settings.IntegrationDynamicResizeEnabled);
        Assert.True(settings.GamepadEnabled);
        Assert.True(settings.RemoteAndroidEnabled);
        Assert.Equal("Automatic", settings.NvidiaProfile);
    }
}
