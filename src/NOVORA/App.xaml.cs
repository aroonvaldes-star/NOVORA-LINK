using NOVORA.Services;
using System;
using System.IO;
using System.Windows;

namespace NOVORA;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

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

        StartupWindow? startupWindow =
            null;

        try
        {
            ApplySavedTheme();

            startupWindow =
                new StartupWindow();

            startupWindow.Show();

            await startupWindow.PlayIntroAsync();
        }
        catch (Exception ex)
        {
            // La animación nunca debe impedir que NOVORA abra.
            LogException(ex);
        }

        try
        {
            var mainWindow =
                new MainWindow();

            MainWindow =
                mainWindow;

            mainWindow.Show();
        }
        catch (Exception ex)
        {
            LogException(ex);

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
            if (startupWindow is not null)
            {
                try
                {
                    startupWindow.Close();
                }
                catch
                {
                }
            }
        }
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
