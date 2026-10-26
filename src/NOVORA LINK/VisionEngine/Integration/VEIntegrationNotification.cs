using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Privacy;

namespace NOVORA.VisionEngine.Integration;

public sealed class VEIntegrationNotification
{
    private readonly VEControlManager _controlVE;
    private readonly VEPrivacyManager _privacyVE;
    private readonly VEIntegrationManager _integrationVE;

    public VEIntegrationNotification(
        VEControlManager control,
        VEPrivacyManager privacy,
        VEIntegrationManager integration)
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

    public Task ExpandAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureAllowedVE();

        return _controlVE.SendAsync(
            VEControlMessage.SimpleVE(
                VEControlType.ExpandNotificationPanel),
            cancellationToken);
    }

    public Task CollapseAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureAllowedVE();

        return _controlVE.SendAsync(
            VEControlMessage.SimpleVE(
                VEControlType.CollapsePanels),
            cancellationToken);
    }

    private void EnsureAllowedVE()
    {
        if (!_privacyVE.CanIntegrateVE ||
            !_integrationVE.StatusVE.Capabilities.Notifications)
        {
            throw new InvalidOperationException(
                "Privacy Shield bloquea paneles remotos.");
        }
    }
}