using System;
using System.Windows.Media.Imaging;
using WpfApplication = System.Windows.Application;

namespace NOVORA.Services;

/// <summary>
/// Mantiene el logotipo horizontal de NOVORA-LINK sincronizado con
/// el tema global de la aplicación.
///
/// Dark:
///     Assets/NOVORA-BRAND.png
///
/// Light:
///     Assets/NOVORA-BRAND_LIGHT.png
/// </summary>
public static class BrandThemeService
{
    public const string ResourceKey = "NovoraBrandImageSource";

    private const string DarkBrandUri =
        "pack://application:,,,/NOVORA;component/Assets/NOVORA-BRAND.png";

    private const string LightBrandUri =
        "pack://application:,,,/NOVORA;component/Assets/NOVORA-BRAND_LIGHT.png";

    /// <summary>
    /// Aplica al recurso dinámico de WPF la imagen correspondiente
    /// al tema seleccionado.
    /// </summary>
    public static void Apply(string? theme)
    {
        string selectedTheme =
            string.Equals(
                theme,
                ThemeService.Light,
                StringComparison.OrdinalIgnoreCase)
                ? ThemeService.Light
                : ThemeService.Dark;

        string brandUri =
            selectedTheme == ThemeService.Light
                ? LightBrandUri
                : DarkBrandUri;

        BitmapImage image = LoadBitmap(brandUri);

        WpfApplication application =
            WpfApplication.Current
            ?? throw new InvalidOperationException(
                "La aplicación WPF todavía no está inicializada.");

        application.Resources[ResourceKey] = image;
    }

    /// <summary>
    /// Carga el PNG desde un pack URI de WPF y libera el stream
    /// inmediatamente mediante BitmapCacheOption.OnLoad.
    /// </summary>
    private static BitmapImage LoadBitmap(string packUri)
    {
        var image = new BitmapImage();

        image.BeginInit();

        image.UriSource = new Uri(
            packUri,
            UriKind.Absolute);

        image.CacheOption =
            BitmapCacheOption.OnLoad;

        image.CreateOptions =
            BitmapCreateOptions.PreservePixelFormat;

        image.EndInit();
        image.Freeze();

        return image;
    }
}