using Avalonia;
using Avalonia.Media;

namespace NOVORA.Linux;

// Values transcribed from Windows NLServiceTheme, Platinum Air / Dark Carbon.
internal static class LinuxVisualPalette
{
    private static readonly Dictionary<string, (string Dark, string Light)> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PanelBrush"] = ("#8C141B1E", "#A6FFFFFF"),
        ["PanelBrush2"] = ("#801D2729", "#99EDF1F8"),
        ["SectionHeaderBrush"] = ("#841A2224", "#99F1F4F9"),
        ["BorderBrush"] = ("#663D4E53", "#99D7DDE8"),
        ["WindowBorderBrush"] = ("#455257", "#CBD3DE"),
        ["TextBrush"] = ("#F2F5F7", "#121722"),
        ["MutedBrush"] = ("#AAB5B8", "#657087"),
        ["BlueBrush"] = ("#35D7E9", "#304FFE"),
        ["AccentTextBrush"] = ("#51DFEF", "#304FFE"),
        ["BlueDarkBrush"] = ("#6EE9F5", "#5671FF"),
        ["PrimaryTextBrush"] = ("#0A0F11", "#FFFFFF"),
        ["CyanBrush"] = ("#35D7E9", "#304FFE"),
        ["PurpleBrush"] = ("#B5A0FF", "#7257C8"),
        ["GreenBrush"] = ("#26C587", "#16845B"),
        ["OrangeBrush"] = ("#FFBE55", "#976C00"),
        ["TrackBrush"] = ("#303B3F", "#D7DDE8"),
        ["BatteryTrackBrush"] = ("#273034", "#E1E6EF"),
        ["BatteryTextBrush"] = ("#FFFFFF", "#121722"),
        ["InputBackgroundBrush"] = ("#121719", "#FFFFFF"),
        ["InputForegroundBrush"] = ("#F2F5F7", "#121722"),
        ["InputBorderBrush"] = ("#3D494D", "#D7DDE8"),
        ["InputHoverBorderBrush"] = ("#35D7E9", "#304FFE"),
        ["InputHoverBackgroundBrush"] = ("#1B292D", "#F0F3FF"),
        ["InputSelectedBackgroundBrush"] = ("#20363B", "#E6EBFF"),
        ["SuccessBackgroundBrush"] = ("#153328", "#E9F6EF"),
        ["SuccessBorderBrush"] = ("#2D644E", "#ADD9C1"),
        ["SuccessTextBrush"] = ("#83D8B0", "#176744"),
        ["InfoBackgroundBrush"] = ("#122A31", "#EAF0FF"),
        ["InfoBorderBrush"] = ("#28525C", "#C1CFFF"),
        ["InfoTextBrush"] = ("#79D9E5", "#304FFE"),
        ["TitleBarBrush"] = ("#8C11181A", "#99E9EDF4"),
        ["TitleSurfaceTextBrush"] = ("#FFFFFF", "#FFFFFF"),
        ["TitleSurfaceAccentBrush"] = ("#83D8B0", "#16845B"),
        ["TitleSurfaceMutedBrush"] = ("#D7E1DF", "#657087"),
        ["ConsoleBackgroundBrush"] = ("#0A0F11", "#F8FAFD"),
        ["ConsoleForegroundBrush"] = ("#DCE5E7", "#273043"),
        ["DangerForegroundBrush"] = ("#FF8585", "#B3262E"),
        ["DangerBorderBrush"] = ("#713940", "#E6A7AD"),
        ["DangerBackgroundBrush"] = ("#2A191D", "#FFF0F1"),
    };
    private static readonly Dictionary<string,string> Legacy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#111c2c"]="TitleBarBrush", ["#131f31"]="PanelBrush", ["#26364d"]="BorderBrush",
        ["#f1f5f9"]="TextBrush", ["#dbe5f1"]="TextBrush", ["#cbd5e1"]="TextBrush",
        ["#a8b7ca"]="MutedBrush", ["#8fa3bd"]="MutedBrush", ["#60a5fa"]="BlueBrush",
        ["#2563eb"]="BlueBrush", ["#93c5fd"]="AccentTextBrush", ["#22c55e"]="GreenBrush",
        ["#f87171"]="DangerForegroundBrush", ["#fbbf24"]="OrangeBrush",
        ["#fcd34d"]="OrangeBrush", ["#1b2a3e"]="InputBackgroundBrush",
        ["#34475f"]="InputBorderBrush", ["#18283d"]="PanelBrush2", ["#314761"]="BorderBrush",
        ["#172b45"]="InfoBackgroundBrush", ["#1d3150"]="InputSelectedBackgroundBrush"
    };
    public static string Resolve(string color, string theme)
    {
        string key=Legacy.GetValueOrDefault(color,color);
        return Colors.TryGetValue(key,out var pair) ? theme==LinuxTheme.Dark ? pair.Dark : pair.Light : LinuxTheme.ResolveColor(color,theme);
    }
    public static IBrush Surface(string key,string theme)
    {
        bool dark=theme==LinuxTheme.Dark;
        string[]? stops=key switch
        {
            "WindowBrush" => dark ? ["#05080A","#0B171B","#050809"] : ["#F7F9FD","#E6ECF8","#F3F6FC"],
            "GlassEdgeBrush" => dark ? ["#40FFFFFF","#0AFFFFFF"] : ["#CCFFFFFF","#33D7DDE8"],
            _=>null
        };
        if(stops is null) return new SolidColorBrush(Color.Parse(Resolve(key,theme)));
        var brush=new LinearGradientBrush { StartPoint=new RelativePoint(0,0,RelativeUnit.Relative), EndPoint=new RelativePoint(1,1,RelativeUnit.Relative) };
        for(int i=0;i<stops.Length;i++) brush.GradientStops.Add(new GradientStop(Color.Parse(stops[i]),(double)i/(stops.Length-1)));
        return brush;
    }
    public static void ApplyResources(Application application,string theme)
    {
        foreach(string key in Colors.Keys) application.Resources[key]=Surface(key,theme);
        application.Resources["WindowBrush"]=Surface("WindowBrush",theme);
        application.Resources["GlassEdgeBrush"]=Surface("GlassEdgeBrush",theme);
    }
}
