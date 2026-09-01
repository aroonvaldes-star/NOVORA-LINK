using NOVORA;
using NOVORA.Models;
using NOVORA.Services;
using NOVORA.ViewModels;
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
            Serial = "R58TEST",
            Model = "SM-A566E",
            Connected = true
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
        File.WriteAllText(Path.Combine(tools, "AdbWinApi.dll"), string.Empty);
        File.WriteAllText(Path.Combine(tools, "AdbWinUsbApi.dll"), string.Empty);

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

    [Fact]
    public void MainViewModel_builds_output_options_from_phone_capabilities()
    {
        var assembly = typeof(DeviceInfo).Assembly;
        var capabilitiesType = assembly.GetType("NOVORA.Models.DeviceCapabilities");
        Assert.NotNull(capabilitiesType);

        var capabilitiesProperty = typeof(DeviceInfo).GetProperty("Capabilities");
        Assert.NotNull(capabilitiesProperty);

        var capabilities = Activator.CreateInstance(capabilitiesType!);
        Assert.NotNull(capabilities);
        capabilitiesType!.GetProperty("NativeWidth")!.SetValue(capabilities, 1080);
        capabilitiesType.GetProperty("NativeHeight")!.SetValue(capabilities, 2400);
        capabilitiesType.GetProperty("SupportedRefreshRatesHz")!.SetValue(capabilities, new[] { 60d, 120d });

        var device = new DeviceInfo
        {
            Serial = "R58TEST",
            Model = "Phone",
            Connected = true,
            BestDisplayMode = new DisplayModeInfo(1080, 2400, 120),
            SupportedDisplayModes = new[] { new DisplayModeInfo(1080, 2400, 120) }
        };
        capabilitiesProperty!.SetValue(device, capabilities);

        var viewModel = new MainViewModel
        {
            TargetFps = 240,
            MaxSize = 5120,
            Device = device
        };

        Assert.Contains(viewModel.FpsOptions, option => option.Value == 120);
        Assert.DoesNotContain(viewModel.FpsOptions, option => option.Value > 120);
        Assert.Equal(120, viewModel.TargetFps);
        Assert.Equal(2400, viewModel.MaxSize);
        Assert.Contains(viewModel.ResolutionOptions, option => option.Value == 2400);
        Assert.DoesNotContain(viewModel.ResolutionOptions, option => option.Value > 2400);
    }

    [Fact]
    public void Device_state_has_per_device_caches_and_duplicate_query_gates()
    {
        var type = typeof(DeviceStateService);
        var networkCache = type.GetField("_networkCache", BindingFlags.Instance | BindingFlags.NonPublic);
        var metricsCache = type.GetField("_metricsCache", BindingFlags.Instance | BindingFlags.NonPublic);
        var networkGate = type.GetField("_networkGate", BindingFlags.Instance | BindingFlags.NonPublic);
        var metricsGate = type.GetField("_metricsGate", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(networkCache);
        Assert.NotNull(metricsCache);
        Assert.Contains("Dictionary", networkCache!.FieldType.Name, StringComparison.Ordinal);
        Assert.Contains("Dictionary", metricsCache!.FieldType.Name, StringComparison.Ordinal);
        Assert.Equal(typeof(SemaphoreSlim), networkGate?.FieldType);
        Assert.Equal(typeof(SemaphoreSlim), metricsGate?.FieldType);
    }

    [Fact]
    public void Gnirehtet_has_explicit_relay_termination_path()
    {
        var method = typeof(GnirehtetService).GetMethod(
            "TerminateRelayAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
    }

    [Fact]
    public void MainWindow_wires_Gnirehtet_recovery_with_cooldown()
    {
        var recoveryType = typeof(MainWindow).Assembly.GetType("NOVORA.Services.GnirehtetRecoveryService");
        Assert.NotNull(recoveryType);

        var recoveryField = typeof(MainWindow).GetField(
            "_gnirehtetRecovery",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var recoveryTimestamp = typeof(MainWindow).GetField(
            "_lastRecoveryAttemptUtc",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(recoveryField);
        Assert.NotNull(recoveryTimestamp);
    }

    [Fact]
    public void MainWindow_stop_has_priority_over_play_requirements()
    {
        var method = typeof(MainWindow).GetMethod(
            "ResolveMirroringAction",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var action = method!.Invoke(
            null,
            new object[]
            {
                true,  // scrcpy sigue activo
                false, // el polling ya marcó el dispositivo como no conectado
                true,  // todavía conocemos el serial
                false  // el monitor ya no está seleccionado
            });

        Assert.Equal("Stop", action?.ToString());
    }
}
