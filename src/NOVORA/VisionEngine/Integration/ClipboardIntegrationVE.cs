using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Exchange;
using NOVORA.VisionEngine.Privacy;

namespace NOVORA.VisionEngine.Integration;

public sealed class ClipboardIntegrationVE
{
    private readonly ClipboardExchangeVE _clipboardVE;
    private readonly ManagerPrivacyVE _privacyVE;
    private readonly ManagerIntegrationVE _integrationVE;

    public ClipboardIntegrationVE(
        ClipboardExchangeVE clipboard,
        ManagerPrivacyVE privacy,
        ManagerIntegrationVE integration)
    {
        _clipboardVE =
            clipboard ??
            throw new ArgumentNullException(
                nameof(clipboard));

        _privacyVE =
            privacy ??
            throw new ArgumentNullException(
                nameof(privacy));

        _integrationVE =
            integration ??
            throw new ArgumentNullException(
                nameof(integration));
    }

    public Task<string> GetTextAsync(
        CopyControlVE copyKey = CopyControlVE.None,
        CancellationToken cancellationToken = default)
    {
        if (!_privacyVE.CanUseClipboardVE ||
            !_integrationVE.StatusVE.Capabilities.Clipboard)
        {
            throw new InvalidOperationException(
                "Privacy Shield bloquea el portapapeles.");
        }

        return _clipboardVE.GetTextAsync(
            copyKey,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Solicita COPY nativo Android respetando PrivacyVE.
    ///
    /// El resultado llegará posteriormente mediante ClipboardChangedVE.
    /// </summary>
    public Task CopyAsyncVE(
        CancellationToken cancellationToken = default)
    {
        if (!_privacyVE.CanUseClipboardVE ||
            !_integrationVE.StatusVE.Capabilities.Clipboard)
        {
            throw new InvalidOperationException(
                "Privacy Shield bloquea el portapapeles.");
        }

        return _clipboardVE.RequestCopyAsyncVE(
            cancellationToken);
    }
    public Task<ulong> SetTextAsync(
        string text,
        bool paste = false,
        CancellationToken cancellationToken = default)
    {
        if (!_privacyVE.CanUseClipboardVE ||
            !_integrationVE.StatusVE.Capabilities.Clipboard)
        {
            throw new InvalidOperationException(
                "Privacy Shield bloquea el portapapeles.");
        }

        return _clipboardVE.SetTextAsync(
            text,
            paste,
            cancellationToken: cancellationToken);
    }
}