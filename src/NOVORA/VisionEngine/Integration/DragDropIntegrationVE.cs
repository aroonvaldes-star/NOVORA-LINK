using NOVORA.VisionEngine.Exchange;
using NOVORA.VisionEngine.Privacy;

namespace NOVORA.VisionEngine.Integration;

public sealed class DragDropIntegrationVE
{
    private readonly FileExchangeVE _filesVE;
    private readonly ManagerPrivacyVE _privacyVE;
    private readonly ManagerIntegrationVE _integrationVE;

    public DragDropIntegrationVE(
        FileExchangeVE files,
        ManagerPrivacyVE privacy,
        ManagerIntegrationVE integration)
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

    public Task<ResultExchangeVE> DropToAndroidAsync(
        string serial,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        if (!_privacyVE.CanIntegrateVE ||
            !_integrationVE.StatusVE.Capabilities.FileTransfer ||
            !_integrationVE.StatusVE.Capabilities.DragDrop)
        {
            return Task.FromResult(
                ResultExchangeVE.FailVE(
                    "Privacy Shield bloquea Drag & Drop."));
        }

        return _filesVE.PushToDownloadsAsync(
            serial,
            localPath,
            scanMedia: true,
            cancellationToken: cancellationToken);
    }
}