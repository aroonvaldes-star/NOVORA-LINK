using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Fluent;
using Avalonia.Markup.Xaml.Styling;

namespace NOVORA.Linux;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://NOVORA-LINK.Linux/"))
        {
            Source = new Uri("avares://NOVORA-LINK.Linux/WindowsVisualStyles.axaml")
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            // Only the real application connects automatically; UI tests build windows without it.
            window.StartAutomaticConnection();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
