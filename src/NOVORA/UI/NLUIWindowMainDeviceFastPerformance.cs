using NOVORA.Model;
using NOVORA.Service;
using System.Windows;

using WpfButton = System.Windows.Controls.Button;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private bool _deviceFastPerformanceRunning;

    private async void DeviceFastPerformanceButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_deviceFastPerformanceRunning)
        {
            return;
        }

        WpfButton? button = sender as WpfButton;

        try
        {
            _deviceFastPerformanceRunning = true;

            if (button is not null)
            {
                button.IsEnabled = false;
                button.Content = "ANALIZANDO...";
            }

            SetDeviceFastPerformanceStatus(
                "Analizando dispositivo...",
                "MutedBrush");

            var device = _viewModel.Device;

            if (device is null)
            {
                SetDeviceFastPerformanceStatus(
                    "No hay dispositivo seleccionado.",
                    "OrangeBrush");

                return;
            }

            if (!device.Connected)
            {
                SetDeviceFastPerformanceStatus(
                    "El dispositivo no esta conectado.",
                    "OrangeBrush");

                return;
            }

            if (string.IsNullOrWhiteSpace(device.Serial))
            {
                SetDeviceFastPerformanceStatus(
                    "ADB no tiene un dispositivo valido.",
                    "OrangeBrush");

                return;
            }

            if (button is not null)
            {
                button.Content = "OPTIMIZANDO...";
            }

            SetDeviceFastPerformanceStatus(
                "Ejecutando optimizacion segura...",
                "MutedBrush");

            var service =
                new NLServiceDeviceFastPerformance(
                    _adb,
                    _metricsService);

            NLServiceDeviceFastPerformanceResult result =
                await service.OptimizeAsync(device);

            string memoryText =
                FormatDeviceFastPerformanceSize(
                    result.MemoryDeltaKb);

            string storageText =
                FormatDeviceFastPerformanceSize(
                    result.StorageDeltaKb);

            string status;
            string brushKey;

            if (result.MeasurableImprovement)
            {
                status =
                    $"OPTIMIZADO - RAM +{memoryText} - " +
                    $"Espacio +{storageText} - " +
                    $"{result.ActionsCompleted}/" +
                    $"{result.ActionsAttempted} acciones";

                brushKey = "SuccessTextBrush";
            }
            else
            {
                status =
                    $"DISPOSITIVO ESTABLE - " +
                    $"{result.ActionsCompleted}/" +
                    $"{result.ActionsAttempted} acciones";

                brushKey = "SuccessTextBrush";
            }

            SetDeviceFastPerformanceStatus(
                status,
                brushKey);

            await RefreshPerformanceOnceAsync();
        }
        catch (OperationCanceledException)
        {
            SetDeviceFastPerformanceStatus(
                "Optimizacion cancelada.",
                "OrangeBrush");
        }
        catch (Exception ex)
        {
            SetDeviceFastPerformanceStatus(
                $"Optimizacion no disponible: {ex.Message}",
                "DangerForegroundBrush");
        }
        finally
        {
            _deviceFastPerformanceRunning = false;

            if (button is not null)
            {
                button.Content = "OPTIMIZAR AHORA";
                button.IsEnabled = true;
            }
        }
    }

    private void SetDeviceFastPerformanceStatus(
        string text,
        string brushKey)
    {
        DeviceFastPerformanceStatusText.Text = text;
        DeviceFastPerformanceStatusText.SetResourceReference(
            System.Windows.Controls.TextBlock.ForegroundProperty,
            brushKey);
    }

    private static string FormatDeviceFastPerformanceSize(
        long kilobytes)
    {
        if (kilobytes <= 0)
        {
            return "0 MB";
        }

        double megabytes = kilobytes / 1024.0;

        if (megabytes >= 1024.0)
        {
            double gigabytes = megabytes / 1024.0;
            return $"{gigabytes:N2} GB";
        }

        return $"{megabytes:N1} MB";
    }
}
