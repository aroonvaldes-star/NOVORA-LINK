using NOVORA.Service;
using NOVORA.VisionEngine.Control;
using NOVORA.VisionEngine.Exchange;
using NOVORA.VisionEngine.Integration;
using NOVORA.VisionEngine.Privacy;

namespace NOVORA.Integration;

/// <summary>
/// Owns NOVORA integrations whose lifetime is independent from VisionEngine.
/// The current control-channel adapter can be supplied by VE or ExIn without
/// making file transfer, clipboard policy, or capability state part of VE.
/// </summary>
public sealed class NLIntegrationRuntime : IAsyncDisposable
{
    private bool _disposed;

    public NLIntegrationRuntime(
        NLServiceADB adb,
        VEControlManager control,
        VEPrivacyManager privacy)
    {
        ArgumentNullException.ThrowIfNull(adb);
        ArgumentNullException.ThrowIfNull(control);
        Privacy = privacy ?? throw new ArgumentNullException(nameof(privacy));

        Capabilities = new VEIntegrationManager();
        Clipboard = new VEExchangeClipboard(
            control,
            () => Privacy.CanUseClipboardVE && Capabilities.StatusVE.Capabilities.Clipboard);
        Files = new VEExchangeFile(
            adb,
            control,
            () => Privacy.CanExchangeFilesVE && Capabilities.StatusVE.Capabilities.FileTransfer);
        Images = new VEExchangeImage(Files);
        Media = new VEExchangeMedia(Files);

        ClipboardIntegration = new VEIntegrationClipboard(Clipboard, Privacy, Capabilities);
        DragDrop = new VEIntegrationDragDrop(Files, Privacy, Capabilities);
        Share = new VEIntegrationShare(Files, Privacy, Capabilities);
        Applications = new VEIntegrationApp(control, Privacy, Capabilities);
        Notifications = new VEIntegrationNotification(control, Privacy, Capabilities);
        VirtualDisplay = new VEIntegrationVirtualDisplay(control, Privacy, Capabilities);
        Camera = new VEIntegrationCamera();
        Microphone = new VEIntegrationMicrophone();
        Window = new VEIntegrationWindow();
        Transfer = new VEExchangeTransfer(
            () => Privacy.CanExchangeFilesVE &&
                  Capabilities.StatusVE.Capabilities.FileTransfer &&
                  Capabilities.StatusVE.Capabilities.DragDrop);
    }

    public VEPrivacyManager Privacy { get; }
    public VEIntegrationManager Capabilities { get; }
    public VEExchangeClipboard Clipboard { get; }
    public VEExchangeFile Files { get; }
    public VEExchangeImage Images { get; }
    public VEExchangeMedia Media { get; }
    public VEIntegrationClipboard ClipboardIntegration { get; }
    public VEIntegrationDragDrop DragDrop { get; }
    public VEIntegrationShare Share { get; }
    public VEIntegrationApp Applications { get; }
    public VEIntegrationNotification Notifications { get; }
    public VEIntegrationVirtualDisplay VirtualDisplay { get; }
    public VEIntegrationCamera Camera { get; }
    public VEIntegrationMicrophone Microphone { get; }
    public VEIntegrationWindow Window { get; }
    internal VEExchangeTransfer Transfer { get; }

    public bool IsVisionEngineRequiredForFiles => false;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await Transfer.DisposeAsync().ConfigureAwait(false);
        Clipboard.Dispose();
    }
}
