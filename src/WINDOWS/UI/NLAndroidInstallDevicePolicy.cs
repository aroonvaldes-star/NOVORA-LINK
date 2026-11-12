namespace NOVORA;

internal static class NLAndroidInstallDevicePolicy
{
    internal static bool CanInstallOverAdb(
        bool connected,
        string? serial)
        => connected &&
           !string.IsNullOrWhiteSpace(serial);
}
