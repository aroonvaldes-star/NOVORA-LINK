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
    private void HomeOpenSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string page } && !string.IsNullOrWhiteSpace(page))
            _ = FadeToPage14(page);
    }

    private Task FadeToPage14(
        string page)
    {
        string target =
            string.IsNullOrWhiteSpace(page)
                ? "Home"
                : page;

        _selectedPage14 = target;
        int token = ++_pageSwitchToken14;
        FrameworkElement? outgoing = PageElement14(_displayedPage14);

        // La sección anterior se desvanece antes de mostrar la nueva; si el usuario
        // cambia otra vez durante la animación, sólo gana la última petición.
        if (!MotionEnabled14 ||
            outgoing is null ||
            outgoing.Visibility != Visibility.Visible ||
            ReferenceEquals(outgoing, PageElement14(target)))
        {
            CommitPage14(target);
            return Task.CompletedTask;
        }

        UpdateNavigationState14(target);
        FadeOutPage14(outgoing, () =>
        {
            if (token == _pageSwitchToken14)
                CommitPage14(target);
        });

        return Task.CompletedTask;
    }

    private void CommitPage14(string target)
    {
        _displayedPage14 = target;
        ShowPage14(target);
        AnimatePageIn14(PageElement14(target));

        // Las métricas del teléfono sólo se consultan cuando el usuario abre la sección.
        if (string.Equals(target, "Performance", StringComparison.OrdinalIgnoreCase))
            _ = RefreshPerformanceOnceAsync();
    }

    private void ShowPage14(
        string page)
    {
        _displayedPage14 = page;
        SetPageVisibility14(HomePage14, page, "Home");
        SetPageVisibility14(ScreenPage14, page, "Screen");
        SetPageVisibility14(NetworkPage14, page, "Network");
        SetPageVisibility14(GameInputPage14, page, "GameInput");
        SetPageVisibility14(IntegrationPage14, page, "Integration");
        SetPageVisibility14(StreamingPage14, page, "Streaming");
        SetPageVisibility14(SettingsPage14, page, "Settings");
        SetPageVisibility14(PerformancePage14, page, "Performance");
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
            NavStreaming14,
            NavSettings14
        ];

        foreach (WpfButton button in navigationButtons)
        {
            bool isSelected =
                string.Equals(
                    button.Tag as string,
                    page,
                    StringComparison.OrdinalIgnoreCase);

            TransitionNavBrush14(
                button,
                System.Windows.Controls.Control.BackgroundProperty,
                isSelected ? "InputSelectedBackgroundBrush" : null);
            TransitionNavBrush14(
                button,
                System.Windows.Controls.Control.ForegroundProperty,
                isSelected ? "AccentTextBrush" : "TextBrush");
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
