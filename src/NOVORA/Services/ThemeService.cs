using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace NOVORA.Services;

public static class ThemeService
{
    public const string Dark = "Dark";
    public const string Light = "Light";
    public static string CurrentTheme { get; private set; } = Dark;

    public static void Apply(string? theme)
    {
        var selected = string.Equals(theme, Light, StringComparison.OrdinalIgnoreCase) ? Light : Dark;
        CurrentTheme = selected;
        var resources = WpfApplication.Current.Resources;
        SetBrush(resources, "WindowBrush", selected == Dark ? "#070A10" : "#F4F6FA");
        SetBrush(resources, "PanelBrush", selected == Dark ? "#0D121B" : "#FFFFFF");
        SetBrush(resources, "PanelBrush2", selected == Dark ? "#111722" : "#EEF2F7");
        SetBrush(resources, "BorderBrush", selected == Dark ? "#293345" : "#D5DCE7");
        SetBrush(resources, "TextBrush", selected == Dark ? "#F1F4FA" : "#172033");
        SetBrush(resources, "MutedBrush", selected == Dark ? "#8D97AA" : "#5E6A7E");
        SetBrush(resources, "BlueBrush", selected == Dark ? "#5D8DFF" : "#315FC7");
        SetBrush(resources, "PurpleBrush", selected == Dark ? "#A78BFA" : "#6846C9");
        SetBrush(resources, "GreenBrush", selected == Dark ? "#35E06F" : "#166B3A");
        SetBrush(resources, "OrangeBrush", selected == Dark ? "#FF9C45" : "#B85B00");
        SetBrush(resources, "InputBackgroundBrush", selected == Dark ? "#111722" : "#FFFFFF");
        SetBrush(resources, "InputForegroundBrush", selected == Dark ? "#F1F4FA" : "#172033");
        SetBrush(resources, "InputBorderBrush", selected == Dark ? "#3A465A" : "#B9C3D1");
        SetBrush(resources, "InputHoverBorderBrush", selected == Dark ? "#5D8DFF" : "#315FC7");
        SetBrush(resources, "InputHoverBackgroundBrush", selected == Dark ? "#182235" : "#EAF1FF");
        SetBrush(resources, "InputSelectedBackgroundBrush", selected == Dark ? "#20304B" : "#DCE8FF");
        SetBrush(resources, "ConsoleBackgroundBrush", selected == Dark ? "#05070B" : "#F7F9FC");
        SetBrush(resources, "ConsoleForegroundBrush", selected == Dark ? "#D8E1F0" : "#263248");
        SetBrush(resources, "TitleBarBrush", selected == Dark ? "#0A0F17" : "#E9EDF4");
        SetBrush(resources, "WindowBorderBrush", selected == Dark ? "#293345" : "#C8D0DC");
        SetBrush(resources, "DangerForegroundBrush", selected == Dark ? "#FF6B6B" : "#B4232F");
        SetBrush(resources, "DangerBorderBrush", selected == Dark ? "#7A2E38" : "#E2A0A7");
        SetBrush(resources, "DangerBackgroundBrush", selected == Dark ? "#1A1115" : "#FFF0F2");
    }

    private static void SetBrush(ResourceDictionary resources, string key, string hex)
        => resources[key] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(hex));
}