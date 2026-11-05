using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace NOVORA.Linux;

internal static class LinuxTheme
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    private static readonly Dictionary<string, string> LightColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#0b1220"] = "#F4F6FB",
        ["#111c2c"] = "#E9EDF4",
        ["#131f31"] = "#FFFFFF",
        ["#26364d"] = "#D7DDE8",
        ["#f1f5f9"] = "#121722",
        ["#a8b7ca"] = "#657087",
        ["#60a5fa"] = "#304FFE",
        ["#2563eb"] = "#304FFE",
        ["#8fa3bd"] = "#657087",
        ["#1d3150"] = "#E6EBFF",
        ["#cbd5e1"] = "#121722",
        ["#172b45"] = "#EAF0FF",
        ["#93c5fd"] = "#304FFE",
        ["#7c2d12"] = "#FFF0E1",
        ["#fdba74"] = "#976C00",
        ["#18283d"] = "#EAF0FF",
        ["#314761"] = "#C1CFFF",
        ["#dbe5f1"] = "#121722",
        ["#1b2a3e"] = "#EDF1F8",
        ["#34475f"] = "#D7DDE8",
        ["#3a2a19"] = "#FFF4D6",
        ["#fcd34d"] = "#976C00",
        ["#22c55e"] = "#16845B",
        ["#f87171"] = "#B3262E"
    };

    private static readonly List<(WeakReference<SolidColorBrush> Brush, string DarkColor)> Brushes = [];
    private static string _theme = Light;

    public static string Current => _theme;
    public static bool IsDark => _theme == Dark;
    public static event EventHandler? Changed;

    public static void Load()
    {
        string path = GetThemePath();
        if (File.Exists(path))
        {
            string value = File.ReadAllText(path).Trim();
            if (value is not (Dark or Light))
                throw new InvalidDataException($"El tema guardado no es válido: {path}");
            _theme = value;
        }
        ApplyAvaloniaTheme();
    }

    public static void Apply(string theme)
    {
        if (theme is not (Dark or Light))
            throw new ArgumentOutOfRangeException(nameof(theme), "El tema debe ser Dark o Light.");

        _theme = theme;
        ApplyAvaloniaTheme();
        for (int index = Brushes.Count - 1; index >= 0; index--)
        {
            if (!Brushes[index].Brush.TryGetTarget(out SolidColorBrush? brush))
            {
                Brushes.RemoveAt(index);
                continue;
            }
            brush.Color = Color.Parse(ResolveColor(Brushes[index].DarkColor, theme));
        }
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Save()
    {
        string path = GetThemePath();
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("No se pudo resolver el directorio de configuración del tema.");
        Directory.CreateDirectory(directory);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough
            };
            if (OperatingSystem.IsLinux())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            using (var file = new FileStream(temporary, options))
            using (var writer = new StreamWriter(file))
            {
                writer.Write(_theme);
                writer.Flush();
                file.Flush(true);
            }
            File.Move(temporary, path, true);
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static SolidColorBrush CreateBrush(string color)
    {
        string normalized = color.ToUpperInvariant();
        var brush = new SolidColorBrush(Color.Parse(ResolveColor(normalized, _theme)));
        Brushes.Add((new WeakReference<SolidColorBrush>(brush), normalized));
        return brush;
    }

    internal static string ResolveColor(string color, string theme) =>
        theme == Light && LightColors.TryGetValue(color, out string? lightColor)
            ? lightColor
            : color;

    public static string GetAssetPath(string assetName) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", assetName);

    private static void ApplyAvaloniaTheme()
    {
        if (Application.Current is { } application)
            application.RequestedThemeVariant = _theme == Dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private static string GetThemePath()
    {
        string? configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        string root = !string.IsNullOrWhiteSpace(configHome) && Path.IsPathFullyQualified(configHome)
            ? configHome
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(root, "novora-link-linux", "theme");
    }
}
