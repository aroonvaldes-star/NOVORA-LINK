using NOVORA.Models;
using System.Runtime.InteropServices;

namespace NOVORA.Services;

public sealed class MonitorService
{
    private const int EnumCurrentSettings = -1;

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(
        string? deviceName,
        int modeNum,
        ref DevMode devMode);

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        return Screen.AllScreens
            .Select(
                screen =>
                    new MonitorInfo(
                        screen.DeviceName,
                        GetDisplayLabel(screen),
                        screen.WorkingArea.Left,
                        screen.WorkingArea.Top,
                        screen.WorkingArea.Width,
                        screen.WorkingArea.Height,
                        GetRefreshRate(
                            screen.DeviceName),
                        screen.Primary))
            .ToArray();
    }

    public MonitorInfo? GetBestMonitor(
        IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors is null ||
            monitors.Count == 0)
        {
            return null;
        }

        return monitors.FirstOrDefault(
                   monitor =>
                       monitor.IsPrimary)
               ??
               monitors.First();
    }

    private static string GetDisplayLabel(
        Screen screen)
    {
        return screen.Primary
            ? $"{screen.DeviceName} · PRINCIPAL"
            : screen.DeviceName;
    }

    private static double GetRefreshRate(
        string deviceName)
    {
        var mode =
            new DevMode
            {
                dmSize =
                    (short)Marshal.SizeOf<DevMode>()
            };

        if (EnumDisplaySettings(
                deviceName,
                EnumCurrentSettings,
                ref mode))
        {
            if (mode.dmDisplayFrequency > 0)
            {
                return mode.dmDisplayFrequency;
            }
        }

        return 60d;
    }

    // IMPORTANTE:
    // Este es el ÚNICO StructLayout del archivo.
    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 32)]
        public string dmDeviceName;

        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;

        public int dmFields;

        public int dmPositionX;
        public int dmPositionY;

        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 32)]
        public string dmFormName;

        public short dmLogPixels;

        public int dmBitsPerPel;

        public int dmPelsWidth;
        public int dmPelsHeight;

        public int dmDisplayFlags;
        public int dmDisplayFrequency;

        public int dmICMMethod;
        public int dmICMIntent;

        public int dmMediaType;
        public int dmDitherType;

        public int dmReserved1;
        public int dmReserved2;

        public int dmPanningWidth;
        public int dmPanningHeight;
    }
}