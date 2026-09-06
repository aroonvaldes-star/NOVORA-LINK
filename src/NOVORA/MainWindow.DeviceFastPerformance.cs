using NOVORA.Models;
using NOVORA.Services;
using System.Windows;

using WpfButton = System.Windows.Controls.Button;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace NOVORA;

public partial class MainWindow
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
                WpfBrushes.LightGray);

            var device = _viewModel.Device;

            if (device is null)
            {
                SetDeviceFastPerformanceStatus(
                    "No hay dispositivo seleccionado.",
                    WpfBrushes.Orange);

                return;
            }

            if (!device.Connected)
            {
                SetDeviceFastPerformanceStatus(
                    "El dispositivo no esta conectado.",
                    WpfBrushes.Orange);

                return;
            }

            if (string.IsNullOrWhiteSpace(device.Serial))
            {
                SetDeviceFastPerformanceStatus(
                    "ADB no tiene un dispositivo valido.",
                    WpfBrushes.Orange);

                return;
            }

            if (button is not null)
            {
                button.Content = "OPTIMIZANDO...";
            }

            SetDeviceFastPerformanceStatus(
                "Ejecutando optimizacion segura...",
                WpfBrushes.LightGray);

            var service =
                new DeviceFastPerformanceService(
                    _adb,
                    _metricsService);

            DeviceFastPerformanceResult result =
                await service.OptimizeAsync(device);

            string memoryText =
                FormatDeviceFastPerformanceSize(
                    result.MemoryDeltaKb);

            string storageText =
                FormatDeviceFastPerformanceSize(
                    result.StorageDeltaKb);

            string status;
            WpfBrush color;

            if (result.MeasurableImprovement)
            {
                status =
                    $"OPTIMIZADO - RAM +{memoryText} - " +
                    $"Espacio +{storageText} - " +
                    $"{result.ActionsCompleted}/" +
                    $"{result.ActionsAttempted} acciones";

                color = WpfBrushes.LightGreen;
            }
            else
            {
                status =
                    $"DISPOSITIVO ESTABLE - " +
                    $"{result.ActionsCompleted}/" +
                    $"{result.ActionsAttempted} acciones";

                color = WpfBrushes.LightGreen;
            }

            SetDeviceFastPerformanceStatus(
                status,
                color);

            await RefreshPerformanceOnceAsync();
        }
        catch (OperationCanceledException)
        {
            SetDeviceFastPerformanceStatus(
                "Optimizacion cancelada.",
                WpfBrushes.Orange);
        }
        catch (Exception ex)
        {
            SetDeviceFastPerformanceStatus(
                $"Optimizacion no disponible: {ex.Message}",
                WpfBrushes.OrangeRed);
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
        WpfBrush color)
    {
        DeviceFastPerformanceStatusText.Text = text;
        DeviceFastPerformanceStatusText.Foreground = color;
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
