using NOVORA.Services;
using System;

namespace NOVORA;

/// <summary>
/// Presentación funcional de las métricas del MainWindow 1.4.
/// Mantiene la lógica visual fuera del motor y fuera del servicio ADB.
/// </summary>
public partial class MainWindow
{
    private void SetPerformanceReading14()
    {
        CpuValueText14.Text = "...";
        RamValueText14.Text = "...";
        BatteryPercentText14.Text = "...";
        TemperatureValueText14.Text = "...";
    }

    private void ResetPerformanceSurface14(string message)
    {
        CpuProgress14.Value = 0;
        RamProgress14.Value = 0;
        BatteryProgress14.Value = 0;
        TemperatureProgress14.Value = 0;

        CpuValueText14.Text = "--";
        RamValueText14.Text = "--";
        BatteryPercentText14.Text = "--";
        TemperatureValueText14.Text = "--";

        _performanceStatus.Text = message;
    }

    private void ApplyPerformanceSurface14(DeviceMetrics metrics)
    {
        double cpu =
            Math.Clamp(
                metrics.CpuPercent,
                0d,
                100d);

        double ramPercent =
            metrics.TotalMemoryKb > 0
                ? Math.Clamp(
                    metrics.UsedMemoryKb /
                    (double)metrics.TotalMemoryKb *
                    100d,
                    0d,
                    100d)
                : 0d;

        double usedMemoryGb =
            metrics.UsedMemoryKb /
            1024d /
            1024d;

        double totalMemoryGb =
            metrics.TotalMemoryKb /
            1024d /
            1024d;

        int battery =
            Math.Clamp(
                metrics.BatteryPercent,
                0,
                100);

        double temperature =
            Math.Max(
                0d,
                metrics.BatteryTemperatureC);

        // 50 °C equivale al límite visual superior de la barra.
        // El valor textual conserva la lectura real.
        double temperatureScale =
            Math.Clamp(
                temperature / 50d * 100d,
                0d,
                100d);

        CpuProgress14.Value = cpu;
        RamProgress14.Value = ramPercent;
        BatteryProgress14.Value = battery;
        TemperatureProgress14.Value = temperatureScale;

        CpuValueText14.Text =
            metrics.CpuPercent > 0
                ? $"{cpu:0.#}%"
                : "--";

        RamValueText14.Text =
            metrics.TotalMemoryKb > 0
                ? $"{usedMemoryGb:0.00}/{totalMemoryGb:0.00} GB"
                : "--";

        BatteryPercentText14.Text =
            metrics.BatteryPercent > 0
                ? $"{battery}%"
                : "--";

        TemperatureValueText14.Text =
            metrics.BatteryTemperatureC > 0
                ? $"{temperature:0.#} °C"
                : "--";
    }
}
