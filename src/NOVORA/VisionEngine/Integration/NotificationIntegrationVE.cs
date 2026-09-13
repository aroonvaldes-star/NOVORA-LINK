using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Privacy;

namespace NOVORA.VisionEngine.Integration;

public sealed class NotificationIntegrationVE
{
    private readonly ManagerControlVE _controlVE;
    private readonly ManagerPrivacyVE _privacyVE;
    private readonly ManagerIntegrationVE _integrationVE;

    public NotificationIntegrationVE(
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

    public Task ExpandAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureAllowedVE();

        return _controlVE.SendAsync(
            MessageControlVE.SimpleVE(
                TypeControlVE.ExpandNotificationPanel),
            cancellationToken);
    }

    public Task CollapseAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureAllowedVE();

        return _controlVE.SendAsync(
            MessageControlVE.SimpleVE(
                TypeControlVE.CollapsePanels),
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