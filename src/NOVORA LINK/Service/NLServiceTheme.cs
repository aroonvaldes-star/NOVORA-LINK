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

        // Glass: el fondo es un degradado y las superficies son translucidas,
        // para que el degradado y el brillo de fondo se vean a traves de ellas.
        SetGradient(resources, "WindowBrush", dark ? "#05080A" : "#F7F9FD", dark ? "#0B171B" : "#E6ECF8", dark ? "#050809" : "#F3F6FC");
        SetBrush(resources, "PanelBrush", dark ? "#8C141B1E" : "#A6FFFFFF");
        SetBrush(resources, "PanelBrush2", dark ? "#801D2729" : "#99EDF1F8");
        SetBrush(resources, "SectionHeaderBrush", dark ? "#841A2224" : "#99F1F4F9");
        SetBrush(resources, "BorderBrush", dark ? "#663D4E53" : "#99D7DDE8");
        SetBrush(resources, "WindowBorderBrush", dark ? "#455257" : "#CBD3DE");
        SetGradient(resources, "GlassEdgeBrush", dark ? "#40FFFFFF" : "#CCFFFFFF", dark ? "#0AFFFFFF" : "#33D7DDE8");

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
        // En Dark el azul de acción es cian claro: el texto debe ser oscuro para leerse.
        SetBrush(resources, "PrimaryTextBrush", dark ? "#0A0F11" : "#FFFFFF");
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

        SetBrush(resources, "TitleBarBrush", dark ? "#8C11181A" : "#99E9EDF4");

        // Texto que va ENCIMA de las tarjetas azules de la cabecera.
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

    // Degradado diagonal con los colores repartidos a lo largo del recorrido.
    private static void SetGradient(
        ResourceDictionary resources,
        string key,
        params string[] hexes)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1),
        };

        for (int i = 0; i < hexes.Length; i++)
        {
            brush.GradientStops.Add(
                new GradientStop(
                    (WpfColor)WpfColorConverter.ConvertFromString(hexes[i]),
                    hexes.Length == 1 ? 0 : (double)i / (hexes.Length - 1)));
        }

        resources[key] = brush;
    }
}
