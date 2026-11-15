namespace NOVORA;

public partial class NLUIWindowMain
{
    internal static string? ResolveExInSerialVE(
        string? connectedDeviceSerial,
        string? authorizedUsbSerial,
        string? activeVisionSerial,
        string? runningExInSerial = null)
        // VE's LAN marker is a presentation identity, never an ADB route.
        // Preserve a running HID binding during brief ADB enumeration gaps.
        => new[] { connectedDeviceSerial, authorizedUsbSerial, activeVisionSerial, runningExInSerial }
            .Select(value => value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) &&
                !value.StartsWith("LAN:", StringComparison.OrdinalIgnoreCase));
}
