using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;

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

        await Task.CompletedTask;
    }

    private void ShowPage14(
        string page)
    {
        SetPageVisibility14(HomePage14, page, "Home");
        SetPageVisibility14(ScreenPage14, page, "Screen");
        SetPageVisibility14(NetworkPage14, page, "Network");
        SetPageVisibility14(GameInputPage14, page, "GameInput");
        SetPageVisibility14(IntegrationPage14, page, "Integration");
        SetPageVisibility14(PrivacyPage14, page, "Privacy");
        SetPageVisibility14(SettingsPage14, page, "Settings");
        UpdateNavigationState14(page);
    }

    private void UpdateNavigationState14(
        string page)
    {
        IEnumerable<WpfButton> navigationButtons =
        [
            NavHome14,
            NavScreen14,
            NavNetwork14,
            NavGameInput14,
            NavIntegration14,
            NavPrivacy14,
            NavSettings14
        ];

        foreach (WpfButton button in navigationButtons)
        {
            bool isSelected =
                string.Equals(
                    button.Tag as string,
                    page,
                    StringComparison.OrdinalIgnoreCase);

            button.Background = isSelected
                ? (WpfBrush)FindResource("InputSelectedBackgroundBrush")
                : WpfBrushes.Transparent;
            button.Foreground = isSelected
                ? (WpfBrush)FindResource("AccentTextBrush")
                : (WpfBrush)FindResource("TextBrush");
            button.Opacity = isSelected ? 1 : 0.72;
            AutomationProperties.SetHelpText(
                button,
                isSelected ? "Sección actual" : string.Empty);
        }
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
