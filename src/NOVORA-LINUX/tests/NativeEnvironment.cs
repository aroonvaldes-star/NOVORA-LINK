using System.Runtime.InteropServices;

/// <summary>
/// On Unix, Environment.SetEnvironmentVariable only updates .NET's managed copy; native libraries
/// such as SDL read libc's environment with getenv. Tests that select SDL drivers must set both.
/// </summary>
internal static class NativeEnvironment
{
    public static void Set(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value);
        if (OperatingSystem.IsWindows()) return;
        if (value is null) _ = unsetenv(name);
        else _ = setenv(name, value, 1);
    }

    [DllImport("libc", CharSet = CharSet.Ansi)]
    private static extern int setenv(string name, string value, int overwrite);

    [DllImport("libc", CharSet = CharSet.Ansi)]
    private static extern int unsetenv(string name);
}
