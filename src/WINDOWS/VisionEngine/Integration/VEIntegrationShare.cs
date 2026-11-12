using NOVORA.VisionEngine.Exchange;
using NOVORA.VisionEngine.Privacy;

namespace NOVORA.VisionEngine.Integration;

public sealed class VEIntegrationShare
{
    private readonly VEExchangeFile _filesVE;
    private readonly VEPrivacyManager _privacyVE;
    private readonly VEIntegrationManager _integrationVE;

    public VEIntegrationShare(
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

    public Task<VEExchangeResult> SendToAndroidAsync(
        string serial,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        if (!_privacyVE.CanIntegrateVE ||
            !_integrationVE.StatusVE.Capabilities.FileTransfer)
        {
            return Task.FromResult(
                VEExchangeResult.FailVE(
                    "Privacy Shield bloquea Share."));
        }

        return _filesVE.PushToDownloadsAsync(
            serial,
            localPath,
            scanMedia: true,
            cancellationToken: cancellationToken);
    }

    public Task<VEExchangeResult> ReceiveFromAndroidAsync(
        string serial,
        string remotePath,
        string localDestination,
        CancellationToken cancellationToken = default)
    {
        if (!_privacyVE.CanIntegrateVE ||
            !_integrationVE.StatusVE.Capabilities.FileTransfer)
        {
            return Task.FromResult(
                VEExchangeResult.FailVE(
                    "Privacy Shield bloquea Share."));
        }

        return _filesVE.PullFromAndroidAsync(
            serial,
            remotePath,
            localDestination,
            cancellationToken);
    }
}