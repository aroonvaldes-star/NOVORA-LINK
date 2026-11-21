using System.Runtime.InteropServices;

namespace NOVORA.Linux;

/// <summary>
/// Resolves platform-specific paths, native dynamic libraries, and CLI tools
/// across Linux and macOS while keeping shared Avalonia/engine code unified.
/// </summary>
internal static class PlatformRuntime
{
    public static bool IsMacOS => OperatingSystem.IsMacOS();
    public static string PlatformName => IsMacOS ? "macOS" : "Linux";
    public static string DefaultRecordingFolderName => IsMacOS ? "Movies" : "Videos";

    public static string AssemblyName =>
        typeof(PlatformRuntime).Assembly.GetName().Name
        ?? (IsMacOS ? "NOVORA-LINK.macOS" : "NOVORA-LINK.Linux");

    public static Uri AvaresUri(string relativePath) =>
        new($"avares://{AssemblyName}/{relativePath.TrimStart('/')}");

    public static string GetConfigDirectory()
    {
        string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdg) && Path.IsPathFullyQualified(xdg))
            return Path.Combine(xdg, "novora-link-linux");

        string home = GetHomeDirectory();
        if (IsMacOS)
            return Path.Combine(home, "Library", "Application Support", "NOVORA-LINK");

        return Path.Combine(home, ".config", "novora-link-linux");
    }

    public static string GetDataDirectory()
    {
        string? xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg) && Path.IsPathFullyQualified(xdg))
            return Path.Combine(xdg, "NOVORA-LINK");

        string home = GetHomeDirectory();
        if (IsMacOS)
            return Path.Combine(home, "Library", "Application Support", "NOVORA-LINK", "Data");

        return Path.Combine(home, ".local", "share", "NOVORA-LINK");
    }

    public static string GetStateDirectory()
    {
        string? xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrWhiteSpace(xdg) && Path.IsPathFullyQualified(xdg))
            return Path.Combine(xdg, "novora-link-linux");

        string home = GetHomeDirectory();
        if (IsMacOS)
            return Path.Combine(home, "Library", "Logs", "NOVORA-LINK");

        return Path.Combine(home, ".local", "state", "novora-link-linux");
    }

    public static void ApplyPrivateDirectoryPermissions(string directory)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static void ApplyPrivateFilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static void ApplyPrivateFileMode(FileStreamOptions options)
    {
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    }

    public static IntPtr LoadSdl3(string componentName)
    {
        if (!IsMacOS)
        {
            if (NativeLibrary.TryLoad("libSDL3.so.0", out IntPtr linuxHandle))
                return linuxHandle;

            throw new DllNotFoundException(
                $"{componentName} necesita SDL3 (libSDL3.so.0). Instala el paquete SDL3 de tu distribución.");
        }

        string[] candidates = ["libSDL3.0.dylib", "libSDL3.dylib"];
        if (TryLoadDylib(candidates, GetMacLibraryDirectories("sdl3"), out IntPtr macHandle))
            return macHandle;

        throw new DllNotFoundException(
            $"{componentName} necesita SDL3 (libSDL3.0.dylib). Instálalo con Homebrew: brew install sdl3");
    }

    public static (IntPtr AvUtil, IntPtr AvCodec) LoadFfmpegAudioLibraries()
    {
        if (!IsMacOS)
        {
            IntPtr linuxUtil = NativeLibrary.Load("libavutil.so.60");
            try
            {
                IntPtr linuxCodec = NativeLibrary.Load("libavcodec.so.62");
                return (linuxUtil, linuxCodec);
            }
            catch
            {
                NativeLibrary.Free(linuxUtil);
                throw;
            }
        }

        IReadOnlyList<string> searchDirs = GetMacLibraryDirectories("ffmpeg");
        if (!TryLoadDylib(["libavutil.60.dylib", "libavutil.59.dylib", "libavutil.dylib"], searchDirs, out IntPtr avUtil))
        {
            throw new DllNotFoundException(
                "No se encontró libavutil de FFmpeg en macOS. Instálalo con Homebrew: brew install ffmpeg");
        }

        try
        {
            if (!TryLoadDylib(["libavcodec.62.dylib", "libavcodec.61.dylib", "libavcodec.dylib"], searchDirs, out IntPtr avCodec))
            {
                throw new DllNotFoundException(
                    "No se encontró libavcodec de FFmpeg en macOS. Instálalo con Homebrew: brew install ffmpeg");
            }
            return (avUtil, avCodec);
        }
        catch
        {
            NativeLibrary.Free(avUtil);
            throw;
        }
    }

    public static string ResolveTool(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        foreach (string dir in GetToolSearchDirectories(toolName))
        {
            string candidate = Path.Combine(dir, toolName);
            if (File.Exists(candidate))
                return candidate;
        }

        return toolName;
    }

    private static string GetHomeDirectory()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        return string.IsNullOrWhiteSpace(home) ? Path.GetTempPath() : home;
    }

    private static bool TryLoadDylib(string[] names, IReadOnlyList<string> searchDirs, out IntPtr handle)
    {
        foreach (string dir in searchDirs)
        {
            foreach (string name in names)
            {
                string fullPath = Path.Combine(dir, name);
                if (File.Exists(fullPath) && NativeLibrary.TryLoad(fullPath, out handle))
                    return true;
            }
        }

        foreach (string name in names)
        {
            if (NativeLibrary.TryLoad(name, out handle))
                return true;
        }

        handle = IntPtr.Zero;
        return false;
    }

    private static IReadOnlyList<string> GetMacLibraryDirectories(string brewFormula)
    {
        var dirs = new List<string>();
        string baseDir = AppContext.BaseDirectory;
        AddIfValid(dirs, baseDir);
        AddIfValid(dirs, Path.GetFullPath(Path.Combine(baseDir, "..", "Frameworks")));
        AddIfValid(dirs, Path.Combine(baseDir, "Tools"));
        AddIfValid(dirs, $"/opt/homebrew/opt/{brewFormula}/lib");
        AddIfValid(dirs, "/opt/homebrew/lib");
        AddIfValid(dirs, $"/usr/local/opt/{brewFormula}/lib");
        AddIfValid(dirs, "/usr/local/lib");
        AddIfValid(dirs, "/opt/local/lib");
        return dirs;
    }

    private static IEnumerable<string> GetToolSearchDirectories(string toolName)
    {
        string baseDir = AppContext.BaseDirectory;
        yield return baseDir;
        yield return Path.Combine(baseDir, "Tools");

        if (IsMacOS)
        {
            yield return Path.GetFullPath(Path.Combine(baseDir, "..", "Resources"));
            yield return Path.GetFullPath(Path.Combine(baseDir, "..", "SharedSupport"));
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                yield return dir;
        }

        if (IsMacOS)
        {
            yield return "/opt/homebrew/bin";
            yield return "/usr/local/bin";
            yield return "/opt/homebrew/sbin";
            yield return "/opt/local/bin";

            if (string.Equals(toolName, "adb", StringComparison.OrdinalIgnoreCase))
            {
                string home = GetHomeDirectory();
                yield return Path.Combine(home, "Library", "Android", "sdk", "platform-tools");

                string? androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME")
                    ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
                if (!string.IsNullOrWhiteSpace(androidHome))
                    yield return Path.Combine(androidHome, "platform-tools");

                yield return "/opt/homebrew/share/android-commandlinetools/platform-tools";
            }
        }
    }

    private static void AddIfValid(List<string> dirs, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && !dirs.Contains(path, StringComparer.Ordinal))
            dirs.Add(path);
    }
}
