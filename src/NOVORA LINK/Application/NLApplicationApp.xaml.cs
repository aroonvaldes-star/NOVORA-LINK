using NOVORA.Service;
using System.IO;
using System.Windows;

namespace NOVORA;

public partial class NLApplicationApp :
    System.Windows.Application
{
    private NLServiceTray? _trayServiceNV;
    private NLServiceSingleInstance? _singleInstanceNV;

    internal static bool IsExplicitExitNV
    {
        get;
        private set;
    }

    protected override async void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceNV = NLServiceSingleInstance.TryAcquireNV();
        if (_singleInstanceNV is null)
        {
            Shutdown();
            return;
        }

        bool autoStartNV =
            NLServiceAutoStart
                .IsAutoStartLaunchNV(
                    e.Args);

        ConfigureUnhandledExceptionNV();

        var settingsServiceNV =
            new NLServiceSettings();

        NLServiceNovoraSettings settingsNV =
            settingsServiceNV.Load();

        if (settingsNV.StartWithWindows ||
            settingsNV.AutoStartSuppressed)
        {
            settingsNV.StartWithWindows =
                false;

            settingsNV.AutoStartSuppressed =
                false;

            settingsServiceNV.Save(
                settingsNV);
        }

        NLServiceAutoStart
            .ApplyRegistrationNV(
                enabled: false);

        if (autoStartNV)
        {
            Shutdown();

            return;
        }

        ApplySavedTheme();

        NLUIWindowStartup? startupWindowNV =
            null;

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

        try
        {
            var mainWindowNV =
                new NLUIWindowMain();

            MainWindow =
                mainWindowNV;

            InitializeTrayNV(
                mainWindowNV);

            mainWindowNV.Show();
        }
        catch (Exception ex)
        {
            LogException(
                ex);

            MessageBox.Show(
                ex.Message,
                "NOVORA - Error de inicio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

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
        NLUIWindowMain mainWindowNV)
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

        _singleInstanceNV?.Dispose();
        _singleInstanceNV = null;

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

                MessageBox.Show(
                    args.Exception.Message,
                    "Error no controlado",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

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
                NLServiceTheme.Light);
        }
        catch
        {
            NLServiceTheme.Apply(
                NLServiceTheme.Light);
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
