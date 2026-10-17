using NOVORA.Control;
using NOVORA.VisionEngine.Exchange;

namespace NOVORA;

/// <summary>
/// Enlace del Exchange de VisionEngine con la fuente activa (USB AppControl o LAN nativa).
/// El adaptador vive mientras dura su generación de inicio y se libera al detenerla.
/// </summary>
public partial class NLUIWindowMain
{
    private readonly VEExchangeRouter _veExchangeRouterVE = new();
    private VEExchangeSessionBinder? _veExchangeBinderVE;
    private long _veExchangeGenerationVE;

    private void EnsureExchangeBinderVE()
        => _veExchangeBinderVE ??= new VEExchangeSessionBinder(_veExchangeRouterVE);

    private IVEExchangeTransport? ActiveExchangeTransportVE()
        => _veExchangeRouterVE.Current?.Transport;

    private VEExchangeCapabilities ExchangeCapabilitiesVE()
        => new(
            Clipboard: _viewModel.IntegrationClipboardEnabled,
            SendFiles: _viewModel.IntegrationFileTransferEnabled,
            ReceiveFiles: _viewModel.IntegrationFileTransferEnabled,
            ReceiveFolder: _viewModel.IntegrationFileTransferEnabled,
            DragDrop: _viewModel.IntegrationDragDropEnabled,
            InstallApk: _viewModel.IntegrationFileTransferEnabled);

    private bool ExchangeFilesAllowedVE()
        => _integrationRuntime?.Privacy.CanExchangeFilesVE ?? false;

    private async Task BindUsbExchangeVEAsync(long generation, string serial)
    {
        EnsureExchangeBinderVE();
        _veExchangeGenerationVE = generation;
        await _veExchangeBinderVE!.BindUsbAsync(
            generation,
            new VEExchangeUsbBackend(_adb),
            serial,
            ExchangeCapabilitiesVE(),
            NLControlFileStorage.PcRoot,
            ExchangeFilesAllowedVE);
    }

    private async Task BindLanExchangeVEAsync(long generation, IVEExchangeLanFiles files)
    {
        EnsureExchangeBinderVE();
        _veExchangeGenerationVE = generation;
        await _veExchangeBinderVE!.BindNativeLanAsync(
            generation,
            files,
            ExchangeCapabilitiesVE(),
            ExchangeFilesAllowedVE);
    }

    /// <summary>
    /// Libera la sesión activa solo si pertenece a la ruta indicada; una fuente USB no libera una LAN.
    /// </summary>
    private async Task ReleaseExchangeVEAsync(VEExchangeRoute route)
    {
        VEExchangeSession? current = _veExchangeRouterVE.Current;
        if (_veExchangeBinderVE is null || current is null || current.Transport.Route != route) return;
        await _veExchangeBinderVE.ReleaseAsync(current.Generation);
    }
}
