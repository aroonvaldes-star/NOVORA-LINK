using NOVORA.Service;
using System.IO;
using System.Windows;

namespace NOVORA;

public partial class NLApplicationApp :
    System.Windows.Application
{
    private NLServiceTray? _trayServiceNV;
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
            NLServiceAutoStart
                .IsAutoStartLaunchNV(
                    e.Args);

        _backgroundStartNV =
            autoStartNV;

        ConfigureUnhandledExceptionNV();

        var settingsServiceNV =
            new NLServiceSettings();

        NLServiceNovoraSettings settingsNV =
            settingsServiceNV.Load();

        if (autoStartNV)
        {
            if (!settingsNV.StartWithWindows ||
                settingsNV.AutoStartSuppressed)
            {
                NLServiceAutoStart
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

            NLServiceAutoStart
                .ApplyRegistrationNV(
                    settingsNV.StartWithWindows);
        }

        ApplySavedTheme();

        NLUIWindowStartup? startupWindowNV =
            null;

        if (!autoStartNV)
        {
            try
            {
                startupWindowNV =
                    new NLUIWindowStartup();

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
                new NLUIWindowMain();

            MainWindow =
                mainWindowNV;

            InitializeTrayNV(
                mainWindowNV,
                settingsServiceNV);

            if (autoStartNV)
            {
                /*
                 * Show() dispara Loaded y con ello inicializa motores,
                 * y detección de dispositivos ADB. Opacity 0 evita cualquier
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
        NLUIWindowMain mainWindowNV,
        NLServiceSettings settingsServiceNV)
    {
        _trayServiceNV =
            new NLServiceTray();

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
                        NLServiceNovoraSettings settingsNV =
                            settingsServiceNV.Load();

                        settingsNV.AutoStartSuppressed =
                            true;

                        settingsServiceNV.Save(
                            settingsNV);

                        NLServiceAutoStart
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
                new NLServiceSettings();

            var settings =
                settingsService.Load();

            NLServiceTheme.Apply(
                settings.Theme);
        }
        catch
        {
            NLServiceTheme.Apply(
                NLServiceTheme.Dark);
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
