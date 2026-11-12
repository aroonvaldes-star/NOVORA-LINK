using System;
using System.Windows.Media.Imaging;
using WpfApplication = System.Windows.Application;

namespace NOVORA.Service;

/// <summary>
/// Mantiene el logotipo horizontal de NOVORA-LINK sincronizado con
/// el tema global de la aplicación.
///
/// Dark:
///     Asset/NLAssetNOVORABRAND.png
///
/// Light:
///     Asset/NLAssetNOVORABRANDLIGHT.png
/// </summary>
public static class NLServiceBrandTheme
{
    public const string ResourceKey = "NovoraBrandImageSource";
    public const string TitleEmblemResourceKey = "NovoraTitleEmblemSource";
    public const string TitleWordmarkResourceKey = "NovoraTitleWordmarkSource";

    private const string DarkBrandUri =
        "pack://application:,,,/NOVORA-LINK;component/Asset/NLAssetNOVORABRAND.png";

    private const string LightBrandUri =
        "pack://application:,,,/NOVORA-LINK;component/Asset/NLAssetNOVORABRANDLIGHT.png";

    /// <summary>
    /// Aplica al recurso dinámico de WPF la imagen correspondiente
    /// al tema seleccionado.
    /// </summary>
    public static void Apply(string? theme)
    {
        string selectedTheme =
            string.Equals(
                theme,
                NLServiceTheme.Light,
                StringComparison.OrdinalIgnoreCase)
                ? NLServiceTheme.Light
                : NLServiceTheme.Dark;

        string brandUri =
            selectedTheme == NLServiceTheme.Light
                ? LightBrandUri
                : DarkBrandUri;

        BitmapImage image = LoadBitmap(brandUri);

        WpfApplication application =
            WpfApplication.Current
            ?? throw new InvalidOperationException(
                "La aplicación WPF todavía no está inicializada.");

        application.Resources[ResourceKey] = image;
        application.Resources[TitleEmblemResourceKey] = LoadBitmap(
            selectedTheme == NLServiceTheme.Light
                ? "pack://application:,,,/NOVORA-LINK;component/Asset/Emblema NOVORA blanco sobre fondo transparente - copia.png"
                : "pack://application:,,,/NOVORA-LINK;component/Asset/Emblema NOVORA blanco sobre fondo transparente.png");
        application.Resources[TitleWordmarkResourceKey] = LoadBitmap(
            selectedTheme == NLServiceTheme.Light
                ? "pack://application:,,,/NOVORA-LINK;component/Asset/NOVORA-LINK.png"
                : "pack://application:,,,/NOVORA-LINK;component/Asset/NOVORA-LINK en blanco sobre transparencia.png");
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
