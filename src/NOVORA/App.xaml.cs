using NOVORA.Services;
using System.IO;
using System.Windows;

namespace NOVORA;

public partial class App :
    System.Windows.Application
{
    private TrayServiceNV? _trayServiceNV;
    private bool _backgroundStartNV;

    internal static bool IsExplicitExitNV
    {
        get;
        private set;
    }

    protected override async void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        bool autoStartNV =
            AutoStartServiceNV
                .IsAutoStartLaunchNV(
                    e.Args);

        _backgroundStartNV =
            autoStartNV;

        ConfigureUnhandledExceptionNV();

        var settingsServiceNV =
            new SettingsService();

        NovoraSettings settingsNV =
            settingsServiceNV.Load();

        if (autoStartNV)
        {
            if (!settingsNV.StartWithWindows ||
                settingsNV.AutoStartSuppressed)
            {
                AutoStartServiceNV
                    .ApplyRegistrationNV(
                        enabled: false);

                Shutdown();

                return;
            }

            await Task.Delay(
                TimeSpan.FromSeconds(5));
        }
        else
        {
            if (settingsNV.AutoStartSuppressed)
            {
                settingsNV.AutoStartSuppressed =
                    false;

                settingsServiceNV.Save(
                    settingsNV);
            }

            AutoStartServiceNV
                .ApplyRegistrationNV(
                    settingsNV.StartWithWindows);
        }

        ApplySavedTheme();

        StartupWindow? startupWindowNV =
            null;

        if (!autoStartNV)
        {
            try
            {
                startupWindowNV =
                    new StartupWindow();

                startupWindowNV.Show();

                await startupWindowNV
                    .PlayIntroAsync();
            }
            catch (Exception ex)
            {
                LogException(
                    ex);
            }
        }

        try
        {
            var mainWindowNV =
                new MainWindow();

            MainWindow =
                mainWindowNV;

            InitializeTrayNV(
                mainWindowNV,
                settingsServiceNV);

            if (autoStartNV)
            {
                /*
                 * Show() dispara Loaded y con ello inicializa motores,
                 * Remote y DiscoveryEngine. Opacity 0 evita cualquier
                 * destello y después la ventana queda escondida.
                 */
                mainWindowNV.Opacity =
                    0d;

                mainWindowNV.ShowInTaskbar =
                    false;

                mainWindowNV.Show();

                mainWindowNV.Hide();

                mainWindowNV.Opacity =
                    1d;

                mainWindowNV.ShowInTaskbar =
                    true;
            }
            else
            {
                mainWindowNV.Show();
            }
        }
        catch (Exception ex)
        {
            LogException(
                ex);

            if (!autoStartNV)
            {
                MessageBox.Show(
                    ex.Message,
                    "NOVORA - Error de inicio",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            Shutdown(1);

            return;
        }
        finally
        {
            if (startupWindowNV is not null)
            {
                try
                {
                    startupWindowNV.Close();
                }
                catch
                {
                }
            }
        }
    }

    private void InitializeTrayNV(
        MainWindow mainWindowNV,
        SettingsService settingsServiceNV)
    {
        _trayServiceNV =
            new TrayServiceNV();

        _trayServiceNV.OpenRequestedNV +=
            (_, _) =>
            {
                Dispatcher.Invoke(
                    mainWindowNV.ShowFromTrayNV);
            };

        _trayServiceNV.SettingsRequestedNV +=
            (_, _) =>
            {
                Dispatcher.Invoke(
                    mainWindowNV.OpenSettingsFromTrayNV);
            };

        _trayServiceNV.ExitRequestedNV +=
            (_, _) =>
            {
                Dispatcher.Invoke(
                    () =>
                    {
                        NovoraSettings settingsNV =
                            settingsServiceNV.Load();

                        settingsNV.AutoStartSuppressed =
                            true;

                        settingsServiceNV.Save(
                            settingsNV);

                        AutoStartServiceNV
                            .ApplyRegistrationNV(
                                enabled: false);

                        IsExplicitExitNV =
                            true;

                        _trayServiceNV?.HideNV();

                        mainWindowNV.Close();
                    });
            };

        _trayServiceNV.ShowNV();
    }

    protected override void OnSessionEnding(
        SessionEndingCancelEventArgs e)
    {
        IsExplicitExitNV =
            true;

        base.OnSessionEnding(
            e);
    }

    protected override void OnExit(
        ExitEventArgs e)
    {
        try
        {
            _trayServiceNV?.Dispose();
        }
        catch
        {
        }

        _trayServiceNV =
            null;

        base.OnExit(
            e);
    }

    private void ConfigureUnhandledExceptionNV()
    {
        DispatcherUnhandledException +=
            (_, args) =>
            {
                LogException(
                    args.Exception);

                if (!_backgroundStartNV)
                {
                    MessageBox.Show(
                        args.Exception.Message,
                        "Error no controlado",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                args.Handled =
                    true;
            };

        AppDomain.CurrentDomain.UnhandledException +=
            (_, args) =>
                LogException(
                    args.ExceptionObject as Exception);
    }

    private static void ApplySavedTheme()
    {
        try
        {
            var settingsService =
                new SettingsService();

            var settings =
                settingsService.Load();

            ThemeService.Apply(
                settings.Theme);
        }
        catch
        {
            ThemeService.Apply(
                ThemeService.Dark);
        }
    }

    private static void LogException(
        Exception? ex)
    {
        try
        {
            var path =
                Path.Combine(
                    Path.GetTempPath(),
                    "NOVORA_error.log");

            File.AppendAllText(
                path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {ex}\n\n");
        }
        catch
        {
        }
    }
}
