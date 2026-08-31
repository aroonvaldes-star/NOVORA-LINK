using NOVORA.Models;
using NOVORA.Services;
using System.Reflection;

namespace NOVORA.Tests;

public sealed class RuntimeRegressionTests
{
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
    public async Task Device_state_never_returns_network_cache_from_another_device()
    {
        var paths = new NovoraPaths(Path.Combine(Path.GetTempPath(), "NOVORA.Tests", Guid.NewGuid().ToString("N")));
        var adb = new AdbService(paths);
        var state = new DeviceStateService(new NetworkService(adb), new DeviceMetricsService(adb));

        SetPrivateField(state, "_serial", "device-b");
        SetPrivateField(state, "_networkCache", new NetworkStatus("10.0.0.1", "wlan0", true, 10));
        SetPrivateField(state, "_networkAt", DateTimeOffset.UtcNow);

        var deviceB = new DeviceInfo { Serial = "device-b", Connected = true, Model = "B" };

        await Assert.ThrowsAnyAsync<Exception>(() => state.GetNetworkAsync(deviceB));
    }

    [Fact]
    public void Primary_monitor_label_contains_principal_only_once()
    {
        var monitor = new MonitorInfo("DISPLAY1", "DISPLAY1 · PRINCIPAL", 0, 0, 1920, 1080, 60, true);
        var occurrences = CountOccurrences(monitor.DisplayLabel, "Principal");
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void Cpu_parser_handles_android_top_summary_format()
    {
        const string sample = "400%cpu 35%user 0%nice 20%sys 345%idle 0%iow 0%irq 0%sirq 0%host";
        var method = typeof(DeviceMetricsService).GetMethod("ParseCpu", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ParseCpu no encontrado.");

        var value = (double)(method.Invoke(null, new object[] { sample }) ?? 0d);
        Assert.InRange(value, 13.0, 15.0);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Campo {fieldName} no encontrado.");
        field.SetValue(target, value);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
