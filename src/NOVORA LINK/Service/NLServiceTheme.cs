using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace NOVORA.Service;

/// <summary>
/// Paleta visual central de NOVORA-LINK 1.4.
/// NLUIWindowMain y los controles consumen estos recursos mediante DynamicResource,
/// por lo que Dark/Light se actualizan sin dejar colores claros u oscuros aislados.
/// </summary>
public static class NLServiceTheme
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    public static string CurrentTheme { get; private set; } = Light;

    public static void Apply(string? theme)
    {
        string selected =
            string.Equals(
                theme,
                Light,
                StringComparison.OrdinalIgnoreCase)
                ? Light
                : Dark;

        CurrentTheme = selected;

        NLServiceBrandTheme.Apply(selected);

        ResourceDictionary resources =
            WpfApplication.Current.Resources;

        bool dark =
            selected == Dark;

        // ============================================================
        // PLATINUM AIR / DARK CARBON
        // ============================================================

        SetBrush(resources, "WindowBrush", dark ? "#0A0F11" : "#F4F6FB");
        SetBrush(resources, "PanelBrush", dark ? "#171C1E" : "#FFFFFF");
        SetBrush(resources, "PanelBrush2", dark ? "#1F2629" : "#EDF1F8");
        SetBrush(resources, "SectionHeaderBrush", dark ? "#1B2325" : "#F1F4F9");
        SetBrush(resources, "BorderBrush", dark ? "#354044" : "#D7DDE8");
        SetBrush(resources, "WindowBorderBrush", dark ? "#455257" : "#CBD3DE");

        // ============================================================
        // TEXTO GENERAL
        // ============================================================

        SetBrush(resources, "TextBrush", dark ? "#F2F5F7" : "#121722");
        SetBrush(resources, "MutedBrush", dark ? "#AAB5B8" : "#657087");

        // ============================================================
        // IDENTIDAD NOVORA
        // ============================================================

        SetBrush(resources, "BlueBrush", dark ? "#35D7E9" : "#304FFE");

        // Texto azul: mas brillante en Dark y mas oscuro en Light
        // para mantener contraste legible.
        SetBrush(resources, "AccentTextBrush", dark ? "#51DFEF" : "#304FFE");
        SetBrush(resources, "BlueDarkBrush", dark ? "#6EE9F5" : "#5671FF");
        SetBrush(resources, "PrimaryTextBrush", "#FFFFFF");
        SetBrush(resources, "CyanBrush", dark ? "#35D7E9" : "#304FFE");
        SetBrush(resources, "PurpleBrush", dark ? "#B5A0FF" : "#7257C8");

        // ============================================================
        // ESTADOS
        // ============================================================

        SetBrush(resources, "GreenBrush", dark ? "#26C587" : "#16845B");
        SetBrush(resources, "OrangeBrush", dark ? "#FFBE55" : "#976C00");
        SetBrush(resources, "TrackBrush", dark ? "#303B3F" : "#D7DDE8");
        SetBrush(resources, "BatteryTrackBrush", dark ? "#273034" : "#E1E6EF");
        SetBrush(resources, "BatteryTextBrush", dark ? "#FFFFFF" : "#121722");

        // ============================================================
        // ENTRADAS Y COMBOBOX
        // ============================================================

        SetBrush(resources, "InputBackgroundBrush", dark ? "#121719" : "#FFFFFF");
        SetBrush(resources, "InputForegroundBrush", dark ? "#F2F5F7" : "#121722");
        SetBrush(resources, "InputBorderBrush", dark ? "#3D494D" : "#D7DDE8");
        SetBrush(resources, "InputHoverBorderBrush", dark ? "#35D7E9" : "#304FFE");
        SetBrush(resources, "InputHoverBackgroundBrush", dark ? "#1B292D" : "#F0F3FF");
        SetBrush(resources, "InputSelectedBackgroundBrush", dark ? "#20363B" : "#E6EBFF");

        // ============================================================
        // TARJETAS DE INFORMACION / ESTADO
        // ============================================================

        SetBrush(resources, "SuccessBackgroundBrush", dark ? "#153328" : "#E9F6EF");
        SetBrush(resources, "SuccessBorderBrush", dark ? "#2D644E" : "#ADD9C1");
        SetBrush(resources, "SuccessTextBrush", dark ? "#83D8B0" : "#176744");

        SetBrush(resources, "InfoBackgroundBrush", dark ? "#122A31" : "#EAF0FF");
        SetBrush(resources, "InfoBorderBrush", dark ? "#28525C" : "#C1CFFF");
        SetBrush(resources, "InfoTextBrush", dark ? "#79D9E5" : "#304FFE");

        // ============================================================
        // BARRA SUPERIOR / SUPERFICIE AZUL
        // ============================================================

        SetBrush(resources, "TitleBarBrush", dark ? "#11181A" : "#E9EDF4");

        // Texto que va ENCIMA de las tarjetas azules del NLUIWindowSettings.
        // Lo dejamos bonito tanto en Dark como en Light.
        SetBrush(resources, "TitleSurfaceTextBrush", "#FFFFFF");
        SetBrush(resources, "TitleSurfaceAccentBrush", dark ? "#83D8B0" : "#16845B");
        SetBrush(resources, "TitleSurfaceMutedBrush", dark ? "#D7E1DF" : "#657087");

        // ============================================================
        // CONSOLA
        // ============================================================

        SetBrush(resources, "ConsoleBackgroundBrush", dark ? "#0A0F11" : "#F8FAFD");
        SetBrush(resources, "ConsoleForegroundBrush", dark ? "#DCE5E7" : "#273043");

        // ============================================================
        // ERRORES
        // ============================================================

        SetBrush(resources, "DangerForegroundBrush", dark ? "#FF8585" : "#B3262E");
        SetBrush(resources, "DangerBorderBrush", dark ? "#713940" : "#E6A7AD");
        SetBrush(resources, "DangerBackgroundBrush", dark ? "#2A191D" : "#FFF0F1");
    }

    private static void SetBrush(
        ResourceDictionary resources,
        string key,
        string hex)
    {
        resources[key] =
            new SolidColorBrush(
                (WpfColor)WpfColorConverter.ConvertFromString(hex));
    }
}
