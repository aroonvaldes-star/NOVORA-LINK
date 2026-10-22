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

    // Un único contador para USB y LAN: el router exige generaciones no decrecientes y el control de Android empieza en 0.
    private long _veExchangeNextGenerationVE;

    private long NextExchangeGenerationVE() => Interlocked.Increment(ref _veExchangeNextGenerationVE);

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

    private async Task BindUsbExchangeVEAsync(string serial)
    {
        EnsureExchangeBinderVE();
        long generation = NextExchangeGenerationVE();
        _veExchangeGenerationVE = generation;
        await _veExchangeBinderVE!.BindUsbAsync(
            generation,
            new VEExchangeUsbBackend(_adb),
            serial,
            ExchangeCapabilitiesVE(),
            NLControlFileStorage.PcRoot,
            ExchangeFilesAllowedVE,
            control: _visionEngineVE?.RuntimeVE.ControlVE);
    }

    private async Task BindLanExchangeVEAsync(IVEExchangeLanFiles files)
    {
        EnsureExchangeBinderVE();
        long generation = NextExchangeGenerationVE();
        _veExchangeGenerationVE = generation;
        await _veExchangeBinderVE!.BindNativeLanAsync(
            generation,
            files,
            ExchangeCapabilitiesVE(),
            ExchangeFilesAllowedVE,
            control: _visionEngineVE?.RuntimeVE.ControlVE);
        if (ActiveExchangeTransportVE() is VEExchangeLanTransport lan)
            lan.ReceivedVE += OnLanExchangeFileReceivedVE;
    }

    private void OnLanExchangeFileReceivedVE(VEExchangeResult result)
    {
        if (!result.Success) return;
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_closing) RecordReceivedFilesVE(result.ItemCount, result.Bytes, result.Destination);
        }));
    }

    /// <summary>
    /// El portapapeles viaja por el canal de control de la sesión activa; sin sesión con control no se sincroniza.
    /// </summary>
    private bool HasExchangeClipboardSessionVE()
        => _veExchangeRouterVE.Current is { IsDisposedVE: false, Control: not null };

    /// <summary>
    /// Libera la sesión activa solo si pertenece a la ruta indicada; una fuente USB no libera una LAN.
    /// </summary>
    private async Task ReleaseExchangeVEAsync(VEExchangeRoute route)
    {
        VEExchangeSession? current = _veExchangeRouterVE.Current;
        if (_veExchangeBinderVE is null || current is null || current.Transport.Route != route) return;
        if (current.Transport is VEExchangeLanTransport lan)
            lan.ReceivedVE -= OnLanExchangeFileReceivedVE;
        await _veExchangeBinderVE.ReleaseAsync(current.Generation);
    }
}
