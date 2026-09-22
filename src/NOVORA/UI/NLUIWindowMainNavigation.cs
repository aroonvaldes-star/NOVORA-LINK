using System;
using System.Threading.Tasks;
using System.Windows;

namespace NOVORA;

/// <summary>
/// Navegación y visibilidad de páginas de la ventana principal.
/// Se mantiene separada para reducir el tamaño del archivo principal y
/// aislar los cambios de UI del resto del runtime.
/// </summary>
public partial class NLUIWindowMain
{
    private async Task FadeToPage14(
        string page)
    {
        _selectedPage14 =
            string.IsNullOrWhiteSpace(page)
                ? "Home"
                : page;

        ShowPage14(_selectedPage14);

        if (string.Equals(
                _selectedPage14,
                "Performance",
                StringComparison.OrdinalIgnoreCase))
        {
            await RefreshPerformanceOnceAsync();
        }
    }

    private void ShowPage14(
        string page)
    {
        SetPageVisibility14(HomePage14, page, "Home");
        SetPageVisibility14(ScreenPage14, page, "Screen");
        SetPageVisibility14(NetworkPage14, page, "Network");
        SetPageVisibility14(PerformancePage14, page, "Performance");
        SetPageVisibility14(GameInputPage14, page, "GameInput");
        SetPageVisibility14(IntegrationPage14, page, "Integration");
        SetPageVisibility14(PrivacyPage14, page, "Privacy");
        SetPageVisibility14(SettingsPage14, page, "Settings");
    }

    private static void SetPageVisibility14(
        FrameworkElement pageElement,
        string current,
        string target)
    {
        pageElement.Visibility =
            string.Equals(
                current,
                target,
                StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }
}
