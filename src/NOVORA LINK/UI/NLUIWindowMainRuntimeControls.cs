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

        bool visionBusy = _visionCommandApplyingVE;

        MainActionButton.IsEnabled =
            (hasDevice || _androidLanControl?.IsAuthorized == true ||
                IsVisionEngineRunningVE() || _pcVideoAuthorizationRequested) &&
            !_closing &&
            !visionBusy;

        MainActionButton.Content =
            _pcVideoAuthorizationRequested
                ? "CANCELAR"
                : visionBusy
                ? IsVisionEngineRunningVE()
                    ? "DETENIENDO VIDEO…"
                    : "INICIANDO VIDEO…"
                : IsVisionEngineRunningVE()
                ? "DETENER VIDEO"
                : "VER PANTALLA";

        RefreshDevicesButton.IsEnabled =
            !_closing;

        WifiAdbButton.IsEnabled =
            !_closing &&
            !visionBusy &&
            !IsVisionEngineRunningVE();
        WifiAdbButton.Content = hasDevice && !_viewModel.Device.IsWifiConnection
            ? "PREPARAR ADB WI-FI DESDE USB"
            : "CONECTAR ADB INALÁMBRICO";
    }
}
