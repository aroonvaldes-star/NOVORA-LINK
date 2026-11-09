namespace NOVORA.Linux.Control;

internal static class LinuxConnectionSelection
{
    // Dynamic connectivity: engines use whichever route is authorized; the user never has to pick
    // USB or LAN first. A running capture owns its route until it stops. A preference only decides
    // between two authorized routes, and an unauthorized preferred route never blocks the other.
    // USB is preferred when both are authorized (lower latency, no Wi-Fi dependency).
    public static string? Resolve(string? preferred, bool usbAuthorized, bool lanAuthorized, string? activeCapture)
    {
        if (activeCapture is "USB" or "LAN") return activeCapture;
        if (preferred == "USB" && usbAuthorized) return "USB";
        if (preferred == "LAN" && lanAuthorized) return "LAN";
        if (usbAuthorized) return "USB";
        if (lanAuthorized) return "LAN";
        return null;
    }
}
