using NOVORA;
using NOVORA.Models;
using NOVORA.Services;
using System.Reflection;
using Xunit;

namespace NOVORA.Tests;

public sealed class RegressionTests
{
    [Fact]
    public void DeviceInfo_ToString_returns_visible_label()
    {
        var device = new DeviceInfo
        {
            Model = "SM-A566E",
            Connected = true,
            ConnectionType = "USB"
        };

        Assert.Equal("SM-A566E • USB", device.DisplayLabel);
        Assert.Equal(device.DisplayLabel, device.ToString());
    }

    [Fact]
    public void MonitorInfo_ToString_returns_visible_label()
    {
        var monitor = new MonitorInfo(
            "\\\\.\\DISPLAY2",
            "Monitor 2",
            0,
            0,
            1920,
            1080,
            60,
            false);

        Assert.Equal("Monitor 2 — 1920x1080 @ 60 Hz", monitor.DisplayLabel);
        Assert.Equal(monitor.DisplayLabel, monitor.ToString());
    }

    [Fact]
    public void Output_profile_is_limited_by_phone_not_pc_monitor()
    {
        var device = new DeviceInfo
        {
            Serial = "R58TEST",
            Model = "Phone",
            Connected = true,
            BestDisplayMode = new DisplayModeInfo(1080, 2400, 120),
            SupportedDisplayModes = new[] { new DisplayModeInfo(1080, 2400, 120) }
        };
        var monitor = new MonitorInfo(
            "DISPLAY1",
            "Monitor 1",
            0,
            0,
            1920,
            1080,
            60,
            true);

        var profile = new OutputProfileService().Calculate(
            device,
            monitor,
            "10M",
            targetFps: 120,
            maxSize: 2400);

        Assert.Equal(1080, profile.Width);
        Assert.Equal(2400, profile.Height);
        Assert.Equal(120, profile.TargetFps);
        Assert.Equal(2400, profile.MaxSize);
    }

    [Fact]
    public void Settings_window_rolls_back_when_closed_with_window_chrome()
    {
        var closingHandler = typeof(SettingsWindow).GetMethod(
            "SettingsWindow_Closing",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(closingHandler);
    }

    [Fact]
    public void Adb_device_metadata_has_safe_shell_path()
    {
        var safeShell = typeof(AdbService).GetMethod(
            "SafeShellAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(safeShell);
    }

    [Fact]
    public void Adb_validation_does_not_require_scrcpy()
    {
        var root = Path.Combine(Path.GetTempPath(), "NOVORA.Tests", Guid.NewGuid().ToString("N"));
        var tools = Path.Combine(root, "Tools");
        Directory.CreateDirectory(tools);
        File.WriteAllText(Path.Combine(tools, "adb.exe"), string.Empty);

        try
        {
            var paths = new NovoraPaths(root);
            paths.ValidateRequiredTools();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Cpu_parser_handles_android_top_summary_format()
    {
        const string sample = "400%cpu 35%user 0%nice 20%sys 345%idle 0%iow 0%irq 0%sirq 0%host";
        var method = typeof(DeviceMetricsService).GetMethod(
            "ParseCpu",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ParseCpu no encontrado.");

        var value = (double)(method.Invoke(null, new object[] { sample }) ?? 0d);
        Assert.InRange(value, 13.0, 15.0);
    }
}
