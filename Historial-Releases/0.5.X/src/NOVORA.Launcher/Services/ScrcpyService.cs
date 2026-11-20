using System.Diagnostics;
using System.Globalization;
using NOVORA.Launcher.Models;

namespace NOVORA.Launcher.Services;

public sealed class ScrcpyService
{
    private readonly NovoraPaths _paths;

    public ScrcpyService(NovoraPaths paths) => _paths = paths;

    public Process StartOptimized(DeviceInfo device, MonitorInfo monitor, bool audioEnabled = true)
    {
        var profile = new OutputProfileService().Calculate(device, monitor);
        return StartOptimized(device, monitor, profile, audioEnabled);
    }

    public Process StartOptimized(DeviceInfo device, MonitorInfo monitor, OutputProfile profile, bool audioEnabled = true)
    {
        if (device is null || !device.Connected || string.IsNullOrWhiteSpace(device.Serial))
            throw new InvalidOperationException("No hay un dispositivo Android ADB disponible.");

        var args = new List<string>
        {
            "--serial", device.Serial,
            "--video-codec", "h264",
            "--video-bit-rate", profile.Bitrate,
            "--max-fps", profile.TargetFps.ToString(CultureInfo.InvariantCulture),
            "--max-size", profile.MaxSize.ToString(CultureInfo.InvariantCulture),
            "--stay-awake",
            "--disable-screensaver",
            "--render-driver", "opengl",
            "--render-fit", "letterbox",
            "--window-x", (monitor.Left + Math.Max(0, (int)(monitor.Width * 0.04))).ToString(CultureInfo.InvariantCulture),
            "--window-y", (monitor.Top + Math.Max(0, (int)(monitor.Height * 0.04))).ToString(CultureInfo.InvariantCulture),
            "--window-width", ((int)(monitor.Width * 0.92)).ToString(CultureInfo.InvariantCulture),
            "--window-height", ((int)(monitor.Height * 0.88)).ToString(CultureInfo.InvariantCulture),
            "--window-title", $"NOVORA — {device.Model}"
        };

        if (!audioEnabled)
            args.Add("--no-audio");

        return new ProcessService().Start(_paths.Scrcpy, args, _paths.ToolBundleDirectory);
    }
}
