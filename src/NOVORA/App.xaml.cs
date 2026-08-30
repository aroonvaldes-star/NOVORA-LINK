using System.IO;
using System.Windows;
// Alias para evitar ambigüedad con System.Windows.Forms.MessageBox
using MessageBox = System.Windows.MessageBox;

namespace NOVORA;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Manejador global de excepciones no controladas en la UI
        DispatcherUnhandledException += (s, args) =>
        {
            LogException(args.Exception);
            MessageBox.Show(args.Exception.Message, "Error no controlado",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true; // Evita que la app se cierre
        };

        // Manejador de excepciones en otros hilos
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogException(args.ExceptionObject as Exception);
        };
    }

    private void LogException(Exception? ex)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "NOVORA_error.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {ex}\n\n");
        }
        catch { }
    }
}