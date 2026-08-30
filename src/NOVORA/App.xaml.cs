using System.Windows;

namespace NOVORA;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            LogException(args.Exception);
            MessageBox.Show(args.Exception.Message, "Error no controlado", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogException(args.ExceptionObject as Exception);
    }

    private static void LogException(Exception? ex)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "NOVORA_error.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {ex}\n\n");
        }
        catch { }
    }
}
