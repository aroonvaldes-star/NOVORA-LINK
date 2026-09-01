using System.Windows;

namespace NOVORA;

public partial class MainWindow
{
    private string? _lastMirroringSerial;

    private enum MirroringAction
    {
        Invalid,
        Start,
        Stop
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // MainWindow.xaml ya enlaza el handler histórico. Lo sustituimos aquí
        // por el flujo corregido para que STOP se evalúe antes que los
        // requisitos exclusivos de PLAY.
        MainActionButton.Click -= MainActionButton_Click;
        MainActionButton.Click += MainActionButtonSafe_Click;

        // UpdateRuntimeButtons() pertenece al MainWindow histórico y puede
        // volver a escribir PLAY cuando el polling marca temporalmente el
        // dispositivo como desconectado. Esta sincronización mantiene el
        // texto coherente con la sesión scrcpy real sin añadir otro timer.
        MainActionButton.LayoutUpdated +=
            (_, _) => RefreshMirroringButtonContent();
    }

    private async void MainActionButtonSafe_Click(
        object sender,
        RoutedEventArgs e)
    {
        var device = _viewModel.Device;

        var currentSerial =
            string.IsNullOrWhiteSpace(device?.Serial)
                ? null
                : device.Serial.Trim();

        var runningSerial = GetRunningMirroringSerial(currentSerial);

        var action = ResolveMirroringAction(
            scrcpyRunning: !string.IsNullOrWhiteSpace(runningSerial),
            deviceConnected: device?.Connected == true,
            hasSerial: !string.IsNullOrWhiteSpace(currentSerial),
            monitorSelected: _viewModel.SelectedMonitor is not null);

        try
        {
            MainActionButton.IsEnabled = false;

            if (action == MirroringAction.Stop)
            {
                await _scrcpy.StopAsync(runningSerial!);

                if (string.Equals(
                        _lastMirroringSerial,
                        runningSerial,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _lastMirroringSerial = null;
                }

                _viewModel.ConnectionStatus =
                    "Screen mirroring detenido.";

                return;
            }

            if (action == MirroringAction.Invalid ||
                device is null ||
                _viewModel.SelectedMonitor is null)
            {
                MessageBox.Show(
                    "Selecciona un dispositivo conectado y un monitor.",
                    "NOVORA",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            UpdateOutputProfile();

            if (_viewModel.OutputProfile is null)
            {
                throw new InvalidOperationException(
                    "No se pudo calcular el perfil de salida.");
            }

            _scrcpy.StartOptimized(
                device,
                _viewModel.SelectedMonitor,
                _viewModel.OutputProfile,
                _viewModel.AudioEnabled);

            _lastMirroringSerial = currentSerial;

            _viewModel.ConnectionStatus =
                "Screen mirroring activo.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "NOVORA — Screen Mirroring",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            MainActionButton.IsEnabled = true;

            UpdateRuntimeButtons();
            RefreshMirroringButtonContent();
        }
    }

    private string? GetRunningMirroringSerial(string? currentSerial)
    {
        if (!string.IsNullOrWhiteSpace(currentSerial) &&
            _scrcpy.IsRunning(currentSerial))
        {
            return currentSerial;
        }

        if (!string.IsNullOrWhiteSpace(_lastMirroringSerial) &&
            _scrcpy.IsRunning(_lastMirroringSerial))
        {
            return _lastMirroringSerial;
        }

        return null;
    }

    private void RefreshMirroringButtonContent()
    {
        if (_closing)
        {
            return;
        }

        var currentSerial =
            string.IsNullOrWhiteSpace(_viewModel.Device?.Serial)
                ? null
                : _viewModel.Device.Serial.Trim();

        var running =
            !string.IsNullOrWhiteSpace(
                GetRunningMirroringSerial(currentSerial));

        var expected =
            running
                ? "⏹️ STOP"
                : "▶️ PLAY";

        if (!Equals(MainActionButton.Content, expected))
        {
            MainActionButton.Content = expected;
        }
    }

    private static MirroringAction ResolveMirroringAction(
        bool scrcpyRunning,
        bool deviceConnected,
        bool hasSerial,
        bool monitorSelected)
    {
        // STOP tiene prioridad. Una sesión ya iniciada debe poder cerrarse
        // aunque el polling haya marcado temporalmente el dispositivo como
        // desconectado o aunque el monitor deje de estar seleccionado.
        if (scrcpyRunning)
        {
            return MirroringAction.Stop;
        }

        // Estas condiciones sólo son necesarias para iniciar una sesión.
        if (!deviceConnected ||
            !hasSerial ||
            !monitorSelected)
        {
            return MirroringAction.Invalid;
        }

        return MirroringAction.Start;
    }
}
