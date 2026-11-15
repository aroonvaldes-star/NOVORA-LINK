using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;

namespace NOVORA;

/// <summary>
/// Movimiento de la ventana principal: transiciones cortas con desaceleración,
/// al estilo de las interfaces de Apple. Se desactiva si Windows pide menos animación.
/// </summary>
public partial class NLUIWindowMain
{
    private static readonly Duration MotionBlock14 = new(TimeSpan.FromMilliseconds(520));
    private static readonly Duration MotionShell14 = new(TimeSpan.FromMilliseconds(560));
    private static readonly Duration MotionNav14 = new(TimeSpan.FromMilliseconds(400));
    private static readonly Duration MotionTheme14 = new(TimeSpan.FromMilliseconds(440));
    private const double MotionBlockOffset14 = 26;
    private const int MotionStaggerMs14 = 85;
    private const int MotionStaggerMaxSteps14 = 8;

    private static readonly Duration MotionPageOut14 = new(TimeSpan.FromMilliseconds(240));

    // Sección que está visible ahora y contador de peticiones de cambio de sección.
    private string _displayedPage14 = "Home";
    private int _pageSwitchToken14;

    private static bool MotionEnabled14 =>
        SystemParameters.ClientAreaAnimation;

    private static CubicEase EaseOut14() =>
        new() { EasingMode = EasingMode.EaseOut };

    private FrameworkElement? PageElement14(string page) =>
        page switch
        {
            "Home" => HomePage14,
            "Screen" => ScreenPage14,
            "Network" => NetworkPage14,
            "GameInput" => GameInputPage14,
            "Integration" => IntegrationPage14,
            "Streaming" => StreamingPage14,
            "Settings" => SettingsPage14,
            "Performance" => PerformancePage14,
            _ => null
        };

    // La sección saliente se desvanece; al terminar se limpia la animación para que
    // la siguiente vez vuelva a verse con opacidad completa.
    private static void FadeOutPage14(FrameworkElement page, Action done)
    {
        if (!MotionEnabled14)
        {
            done();
            return;
        }

        var fade = new DoubleAnimation(0, MotionPageOut14)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            page.BeginAnimation(UIElement.OpacityProperty, null);
            page.Opacity = 1;
            done();
        };
        page.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    // Los bloques de la página (tarjetas, filas) aparecen en cascada: fundido y ascenso corto.
    // Con HoldEnd la animación conserva el valor final (opacidad 1, sin desplazamiento).
    private void AnimatePageIn14(FrameworkElement? page)
    {
        if (page is null || !MotionEnabled14) return;

        if (page is ScrollViewer { Content: System.Windows.Controls.Panel blocks })
        {
            for (int i = 0; i < blocks.Children.Count; i++)
            {
                int delay = Math.Min(i, MotionStaggerMaxSteps14) * MotionStaggerMs14;
                RevealElement14(blocks.Children[i], delay);
            }
            return;
        }

        RevealElement14(page, 0);
    }

    private static void RevealElement14(UIElement element, int delayMs)
    {
        if (element.Visibility != Visibility.Visible) return;
        if (element.RenderTransform is not null and not TranslateTransform) return;

        var offset = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = offset;

        var begin = TimeSpan.FromMilliseconds(delayMs);
        element.Opacity = 0;
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, MotionBlock14) { BeginTime = begin, EasingFunction = EaseOut14() });
        offset.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(MotionBlockOffset14, 0, MotionBlock14) { BeginTime = begin, EasingFunction = EaseOut14() });
    }

    // La interfaz completa aparece con un fundido al abrir la ventana.
    private void AnimateShellIn14(UIElement shell)
    {
        if (!MotionEnabled14) return;

        shell.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, MotionShell14) { EasingFunction = EaseOut14() });
    }

    // Al cambiar de tema la interfaz se atenúa y vuelve, en lugar de cambiar de golpe.
    private void AnimateThemeChange14(UIElement shell)
    {
        if (!MotionEnabled14) return;

        shell.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0.55, 1, MotionTheme14) { EasingFunction = EaseOut14() });
    }

    // El fondo y el color del texto de cada sección cambian en transición, no de golpe.
    // Mientras corre la animación se usa un pincel local; al terminar se vuelve a enlazar
    // al recurso del tema para que siga respondiendo a Dark/Light.
    private void TransitionNavBrush14(WpfButton button, DependencyProperty property, string? resourceKey)
    {
        if (!MotionEnabled14)
        {
            SetNavBrush14(button, property, resourceKey);
            return;
        }

        WpfColor target = resourceKey is null
            ? WpfColors.Transparent
            : ((SolidColorBrush)FindResource(resourceKey)).Color;
        WpfColor from = (button.GetValue(property) as SolidColorBrush)?.Color ?? target;

        var animated = new SolidColorBrush(from);
        button.SetValue(property, animated);

        var animation = new ColorAnimation(target, MotionNav14) { EasingFunction = EaseOut14() };
        animation.Completed += (_, _) => SetNavBrush14(button, property, resourceKey);
        animated.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private static void SetNavBrush14(WpfButton button, DependencyProperty property, string? resourceKey)
    {
        if (resourceKey is null)
            button.SetValue(property, WpfBrushes.Transparent);
        else
            button.SetResourceReference(property, resourceKey);
    }
}
