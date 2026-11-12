using NOVORA.Model;
using System.Runtime.InteropServices;

namespace NOVORA.Service;

public sealed class NLServiceMonitor
{
    private const int EnumCurrentSettings = -1;

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(
        string? deviceName,
        int modeNum,
        ref NLServiceDevMode devMode);

    public IReadOnlyList<NLModelMonitorInfo> GetMonitors()
    {
        return Screen.AllScreens
            .Select(
                screen =>
                    new NLModelMonitorInfo(
                        screen.DeviceName,
                        GetDisplayLabel(screen),
                        screen.Bounds.Left,
                        screen.Bounds.Top,
                        screen.Bounds.Width,
                        screen.Bounds.Height,
                        GetRefreshRate(
                            screen.DeviceName),
                        screen.Primary))
            .ToArray();
    }

    public NLModelMonitorInfo? GetBestMonitor(
        IReadOnlyList<NLModelMonitorInfo> monitors)
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
            new NLServiceDevMode
            {
                dmSize =
                    (short)Marshal.SizeOf<NLServiceDevMode>()
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

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct NLServiceDevMode
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
