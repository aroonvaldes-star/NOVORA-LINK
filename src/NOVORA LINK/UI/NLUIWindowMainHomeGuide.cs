using System.Windows;

namespace NOVORA;

/// <summary>Guía de pasos de Inicio y sesión de rendimiento.</summary>
public partial class NLUIWindowMain
{
    private bool _performanceSession14;

    private void UpdateHomeGuide14()
    {
        if (HomeNextStep14 is null) return;
        bool phone = _viewModel.Device.Connected || _androidLanControl?.IsAuthorized == true;
        HomeNextStep14.Text =
            !phone ? "Paso 1 de 4 · Conecta el teléfono por USB o Wi-Fi." :
            _pcVideoAuthorizationRequested ? "Paso 2 de 4 · Autoriza la captura de pantalla en el teléfono." :
            !IsVisionEngineRunningVE() ? "Paso 3 de 4 · Abre VisionEngine con VER PANTALLA." :
            _linkEngineRuntimeLE?.IsRunningLE != true && _androidLanDataGateway is null
                ? "Paso 4 de 4 · Inicia LinkEngine solo si necesitas Internet o datos."
                : "Sesión lista · VisionEngine y LinkEngine están activos.";
    }

    private async void HomePerformanceSession_Changed14(object sender, RoutedEventArgs e)
    {
        _performanceSession14 = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
        if (_shellInitialized14)
        {
            var settings = _settingsService.Load();
            settings.PerformanceSessionEnabled = _performanceSession14;
            _settingsService.Save(settings);
        }
        if (!_performanceSession14) return;
        VisionTelemetryExpander14.IsExpanded = false;
        await DisableExperimentalNetworkForBaselineAsync();
    }
}
