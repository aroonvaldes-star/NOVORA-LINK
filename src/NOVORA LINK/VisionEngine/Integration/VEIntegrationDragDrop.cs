using NOVORA.VisionEngine.Exchange;
using NOVORA.VisionEngine.Privacy;

namespace NOVORA.VisionEngine.Integration;

public sealed class VEIntegrationDragDrop
{
    private readonly VEExchangeFile _filesVE;
    private readonly VEPrivacyManager _privacyVE;
    private readonly VEIntegrationManager _integrationVE;

    public VEIntegrationDragDrop(
        VEExchangeFile files,
        VEPrivacyManager privacy,
        VEIntegrationManager integration)
    {
        _filesVE =
            files ??
            throw new ArgumentNullException(
                nameof(files));

        _privacyVE =
            privacy ??
            throw new ArgumentNullException(
                nameof(privacy));

        _integrationVE =
            integration ??
            throw new ArgumentNullException(
                nameof(integration));
    }

    public Task<VEExchangeResult> DropToAndroidAsync(
        string serial,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        if (!_privacyVE.CanIntegrateVE ||
            !_integrationVE.StatusVE.Capabilities.FileTransfer ||
            !_integrationVE.StatusVE.Capabilities.DragDrop)
        {
            return Task.FromResult(
                VEExchangeResult.FailVE(
                    "Privacy Shield bloquea Drag & Drop."));
        }

        return _filesVE.PushToDownloadsAsync(
            serial,
            localPath,
            scanMedia: true,
            cancellationToken: cancellationToken);
    }
}