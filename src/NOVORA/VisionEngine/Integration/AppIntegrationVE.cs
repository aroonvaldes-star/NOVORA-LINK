using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Privacy;

namespace NOVORA.VisionEngine.Integration;

public sealed class AppIntegrationVE
{
    private readonly ManagerControlVE _controlVE;
    private readonly ManagerPrivacyVE _privacyVE;
    private readonly ManagerIntegrationVE _integrationVE;

    public AppIntegrationVE(
        ManagerControlVE control,
        ManagerPrivacyVE privacy,
        ManagerIntegrationVE integration)
    {
        _controlVE =
            control ??
            throw new ArgumentNullException(
                nameof(control));

        _privacyVE =
            privacy ??
            throw new ArgumentNullException(
                nameof(privacy));

        _integrationVE =
            integration ??
            throw new ArgumentNullException(
                nameof(integration));
    }

    public async Task StartAppAsync(
        string packageOrAppName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            packageOrAppName);

        if (!_privacyVE.CanIntegrateVE ||
            !_integrationVE.StatusVE.Capabilities.Applications)
        {
            throw new InvalidOperationException(
                "Privacy Shield bloquea el inicio remoto de aplicaciones.");
        }

        await _controlVE.SendAsync(
                MessageControlVE.StartAppVE(
                    packageOrAppName.Trim()),
                cancellationToken)
            .ConfigureAwait(false);
    }
}