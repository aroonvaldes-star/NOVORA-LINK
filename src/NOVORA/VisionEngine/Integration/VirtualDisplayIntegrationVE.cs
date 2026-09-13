using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Privacy;

namespace NOVORA.VisionEngine.Integration;

/// <summary>
/// Integración de resize del display que ya expone el protocolo actual.
///
/// Crear un display virtual NUEVO todavía no forma parte del contrato
/// actual de OptionsServerVE.
/// </summary>
public sealed class VirtualDisplayIntegrationVE
{
    private readonly ManagerControlVE _controlVE;
    private readonly ManagerPrivacyVE _privacyVE;
    private readonly ManagerIntegrationVE _integrationVE;

    public VirtualDisplayIntegrationVE(
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

    public bool CanResizeDisplayVE =>
        true;

    public bool CanCreateNewVirtualDisplayVE =>
        false;

    public Task ResizeDisplayAsync(
        ushort width,
        ushort height,
        CancellationToken cancellationToken = default)
    {
        if (width == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width));
        }

        if (height == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height));
        }

        if (!_privacyVE.CanIntegrateVE ||
            !_integrationVE.StatusVE.Capabilities.DynamicResize)
        {
            throw new InvalidOperationException(
                "Privacy Shield bloquea cambios remotos del display.");
        }

        return _controlVE.SendAsync(
            MessageControlVE.ResizeDisplayVE(
                width,
                height),
            cancellationToken);
    }
}