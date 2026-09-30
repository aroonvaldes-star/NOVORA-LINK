using NOVORA.Integration;
using NOVORA.Service;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Integration;
using NOVORA.VisionEngine.Privacy;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestIntegrationRuntimeIndependence
{
    [Fact]
    public async Task FilesAndCapabilitiesExistWithoutStartingVisionEngine()
    {
        var paths = new NLServiceNovoraPaths();
        var adb = new NLServiceADB(paths);
        await using var control = new VEControlManager();
        var privacy = new VEPrivacyManager();
        await using var integrations = new NLIntegrationRuntime(adb, control, privacy);

        integrations.Capabilities.SetCapabilitiesVE(
            VEIntegrationCapabilities.CreateDefaultVE());

        Assert.False(control.IsReadyVE);
        Assert.False(integrations.IsVisionEngineRequiredForFiles);
        Assert.True(integrations.Capabilities.StatusVE.Capabilities.FileTransfer);
        Assert.True(integrations.Capabilities.StatusVE.Capabilities.DragDrop);
        Assert.NotNull(integrations.Files);
        Assert.NotNull(integrations.Share);
        Assert.Null(typeof(NOVORA.VisionEngine.Core.VECoreRuntime).GetProperty("FilesVE"));
        Assert.Null(typeof(NOVORA.VisionEngine.Core.VECoreRuntime).GetProperty("IntegrationVE"));
    }

    [Fact]
    public async Task PrivacyAndCapabilityChangesGateIndependentIntegrations()
    {
        var paths = new NLServiceNovoraPaths();
        var adb = new NLServiceADB(paths);
        await using var control = new VEControlManager();
        var privacy = new VEPrivacyManager();
        await using var integrations = new NLIntegrationRuntime(adb, control, privacy);

        integrations.Capabilities.SetCapabilitiesVE(
            VEIntegrationCapabilities.CreateDefaultVE() with { FileTransfer = false });

        var disabled = await integrations.Share.SendToAndroidAsync("device", "missing.file");
        Assert.False(disabled.Success);

        integrations.Capabilities.SetCapabilitiesVE(VEIntegrationCapabilities.CreateDefaultVE());
        privacy.SetManualShieldVE(true);

        var protectedResult = await integrations.Share.SendToAndroidAsync("device", "missing.file");
        Assert.False(protectedResult.Success);
    }
}
