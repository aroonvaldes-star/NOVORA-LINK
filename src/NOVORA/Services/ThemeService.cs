using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace NOVORA.Services;

/// <summary>
/// Paleta visual central de NOVORA-LINK 1.4.
/// MainWindow y los controles consumen estos recursos mediante DynamicResource,
/// por lo que Dark/Light se actualizan sin dejar colores claros u oscuros aislados.
/// </summary>
public static class ThemeService
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    public static string CurrentTheme { get; private set; } = Dark;

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

        BrandThemeService.Apply(selected);

        ResourceDictionary resources =
            WpfApplication.Current.Resources;

        bool dark =
            selected == Dark;

        // ============================================================
        // SUPERFICIES MATTE
        // ============================================================

        SetBrush(resources, "WindowBrush", dark ? "#151A20" : "#E8ECEF");
        SetBrush(resources, "PanelBrush", dark ? "#1B2128" : "#F4F6F7");
        SetBrush(resources, "PanelBrush2", dark ? "#222A33" : "#EEF2F4");
        SetBrush(resources, "SectionHeaderBrush", dark ? "#202830" : "#DDE3E7");
        SetBrush(resources, "BorderBrush", dark ? "#36414B" : "#C7D0D7");
        SetBrush(resources, "WindowBorderBrush", dark ? "#40505E" : "#BFC8CF");

        // ============================================================
        // TEXTO GENERAL
        // ============================================================

        SetBrush(resources, "TextBrush", dark ? "#F2F5F7" : "#101923");
        SetBrush(resources, "MutedBrush", dark ? "#A8B1B9" : "#5F6A74");

        // ============================================================
        // IDENTIDAD NOVORA
        // ============================================================

        SetBrush(resources, "BlueBrush", "#00AEEF");

        // Texto azul: mas brillante en Dark y mas oscuro en Light
        // para mantener contraste legible.
        SetBrush(resources, "AccentTextBrush", dark ? "#35C4F5" : "#006B95");
        SetBrush(resources, "BlueDarkBrush", dark ? "#35C4F5" : "#008FCA");
        SetBrush(resources, "CyanBrush", dark ? "#39D4E5" : "#00AFC5");
        SetBrush(resources, "PurpleBrush", dark ? "#B5A0FF" : "#7257C8");

        // ============================================================
        // ESTADOS
        // ============================================================

        SetBrush(resources, "GreenBrush", "#16C784");
        SetBrush(resources, "OrangeBrush", dark ? "#FFAD5A" : "#C66A12");
        SetBrush(resources, "TrackBrush", dark ? "#34404A" : "#D2DAE0");
        SetBrush(resources, "BatteryTrackBrush", dark ? "#283139" : "#DCE3E7");
        SetBrush(resources, "BatteryTextBrush", dark ? "#FFFFFF" : "#101923");

        // ============================================================
        // ENTRADAS Y COMBOBOX
        // ============================================================

        SetBrush(resources, "InputBackgroundBrush", dark ? "#111820" : "#FFFFFF");
        SetBrush(resources, "InputForegroundBrush", dark ? "#F5F7F9" : "#101923");
        SetBrush(resources, "InputBorderBrush", dark ? "#44515D" : "#B8C2CA");
        SetBrush(resources, "InputHoverBorderBrush", "#00AEEF");
        SetBrush(resources, "InputHoverBackgroundBrush", dark ? "#1B2A34" : "#E5F6FC");
        SetBrush(resources, "InputSelectedBackgroundBrush", dark ? "#123C4E" : "#CDEFFB");

        // ============================================================
        // TARJETAS DE INFORMACION / ESTADO
        // ============================================================

        SetBrush(resources, "SuccessBackgroundBrush", dark ? "#123228" : "#E4F5EE");
        SetBrush(resources, "SuccessBorderBrush", dark ? "#24684F" : "#A9DCC8");
        SetBrush(resources, "SuccessTextBrush", dark ? "#7FE2BC" : "#0C6448");

        SetBrush(resources, "InfoBackgroundBrush", dark ? "#102D3A" : "#E2F3FB");
        SetBrush(resources, "InfoBorderBrush", dark ? "#1B5D76" : "#B3DDEC");
        SetBrush(resources, "InfoTextBrush", dark ? "#7FDFFF" : "#006D94");

        // ============================================================
        // BARRA SUPERIOR / SUPERFICIE AZUL
        // ============================================================

        SetBrush(resources, "TitleBarBrush", "#123A5A");

        // Texto que va ENCIMA de las tarjetas azules del SettingsWindow.
        // Lo dejamos bonito tanto en Dark como en Light.
        SetBrush(resources, "TitleSurfaceTextBrush", "#FFFFFF");
        SetBrush(resources, "TitleSurfaceAccentBrush", dark ? "#8FE5C1" : "#97F0CC");
        SetBrush(resources, "TitleSurfaceMutedBrush", dark ? "#CFE9DE" : "#D6F5E6");

        // ============================================================
        // CONSOLA
        // ============================================================

        SetBrush(resources, "ConsoleBackgroundBrush", dark ? "#0C1116" : "#F7F9FA");
        SetBrush(resources, "ConsoleForegroundBrush", dark ? "#DCE5EA" : "#26323A");

        // ============================================================
        // ERRORES
        // ============================================================

        SetBrush(resources, "DangerForegroundBrush", dark ? "#FF7B7B" : "#B4232F");
        SetBrush(resources, "DangerBorderBrush", dark ? "#7A3640" : "#E2A0A7");
        SetBrush(resources, "DangerBackgroundBrush", dark ? "#2B181D" : "#FFF0F2");
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