using NOVORA.VisionEngine.Exchange;
using NOVORA.VisionEngine.Privacy;

namespace NOVORA.VisionEngine.Integration;

public sealed class ShareIntegrationVE
{
    private readonly FileExchangeVE _filesVE;
    private readonly ManagerPrivacyVE _privacyVE;
    private readonly ManagerIntegrationVE _integrationVE;

    public ShareIntegrationVE(
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

    public Task<ResultExchangeVE> SendToAndroidAsync(
        string serial,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        if (!_privacyVE.CanIntegrateVE ||
            !_integrationVE.StatusVE.Capabilities.FileTransfer)
        {
            return Task.FromResult(
                ResultExchangeVE.FailVE(
                    "Privacy Shield bloquea Share."));
        }

        return _filesVE.PushToDownloadsAsync(
            serial,
            localPath,
            scanMedia: true,
            cancellationToken: cancellationToken);
    }

    public Task<ResultExchangeVE> ReceiveFromAndroidAsync(
        string serial,
        string remotePath,
        string localDestination,
        CancellationToken cancellationToken = default)
    {
        if (!_privacyVE.CanIntegrateVE ||
            !_integrationVE.StatusVE.Capabilities.FileTransfer)
        {
            return Task.FromResult(
                ResultExchangeVE.FailVE(
                    "Privacy Shield bloquea Share."));
        }

        return _filesVE.PullFromAndroidAsync(
            serial,
            remotePath,
            localDestination,
            cancellationToken);
    }
}