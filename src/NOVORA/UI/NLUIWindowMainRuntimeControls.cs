using System;

namespace NOVORA;

/// <summary>
/// Botones y estado observable del runtime de la ventana principal.
/// Se separa para mantener el comportamiento visual fuera del flujo principal.
/// </summary>
public partial class NLUIWindowMain
{
    private void UpdateRuntimeButtons()
    {
        bool hasDevice =
            _viewModel.Device.Connected;

        bool visionBusy =
            _visionCommandApplyingVE ||
            _visionRecoveryRunningVE;

        MainActionButton.IsEnabled =
            hasDevice &&
            !_closing &&
            !visionBusy;

        MainActionButton.Content =
            visionBusy
                ? IsVisionEngineRunningVE()
                    ? "DETENIENDO…"
                    : "INICIANDO…"
                : IsVisionEngineRunningVE()
                ? "DETENER"
                : "INICIAR";

        RefreshDevicesButton.IsEnabled =
            !_closing;

        UpdateLinkEngineButtonLE();

        WifiAdbButton.IsEnabled =
            hasDevice &&
            !_closing &&
            !visionBusy &&
            !IsVisionEngineRunningVE();
    }
}
