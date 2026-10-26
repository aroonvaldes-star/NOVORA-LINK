using Microsoft.Win32;
using System.Diagnostics;

namespace NOVORA.Service;

public static class NLServiceAutoStart
{
    private const string RunKeyPathNV =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueNameNV =
        "NOVORA-LINK";

    public const string AutoStartArgumentNV =
        "--autostart";

    public static bool IsAutoStartLaunchNV(
        IEnumerable<string>? arguments)
    {
        return arguments?.Any(
            argument =>
                string.Equals(
                    argument?.Trim(),
                    AutoStartArgumentNV,
                    StringComparison.OrdinalIgnoreCase)) == true;
    }

    public static void ApplyRegistrationNV(
        bool enabled)
    {
        using RegistryKey key =
            Registry.CurrentUser.CreateSubKey(
                RunKeyPathNV,
                writable: true)
            ??
            throw new InvalidOperationException(
                "No se pudo abrir HKCU Run para NOVORA.");

        if (!enabled)
        {
            key.DeleteValue(
                ValueNameNV,
                throwOnMissingValue: false);

            return;
        }

        string executable =
            Environment.ProcessPath
            ??
            Process.GetCurrentProcess()
                .MainModule?
                .FileName
            ??
            throw new InvalidOperationException(
                "NOVORA no pudo resolver su ejecutable.");

        string command =
            $"\"{executable}\" {AutoStartArgumentNV}";

        key.SetValue(
            ValueNameNV,
            command,
            RegistryValueKind.String);
    }
}
