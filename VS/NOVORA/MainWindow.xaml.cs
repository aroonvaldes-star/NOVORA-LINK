using System.Windows;
using NOVORA.Services;
namespace NOVORA;
public partial class MainWindow : Window
{
    private readonly OutputProfileService _profiles = new();
    private readonly UpdateService _updates = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var update = await _updates.CheckAsync();
            if (update is null) return;

            var result = MessageBox.Show(
                $"Hay una actualización de NOVORA disponible ({update.Version}).\n\n¿Descargarla e instalarla ahora?",
                "Actualización de NOVORA",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes) return;

            await _updates.InstallAsync(update);
            MessageBox.Show("La actualización se descargó. NOVORA se cerrará para instalarla.",
                "NOVORA", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown();
        }
        catch
        {
            // Una actualización nunca debe impedir que NOVORA arranque.
        }
    }

    private void BitrateTest_Click(object sender, RoutedEventArgs e)
    {
        var tests = new[] { "4.5 Mbps", "5 Mbps", "5 MB/s", "10M", "20 Mbps" };
        var results = tests.Select(OutputProfileService.NormalizeBitrateForTest).ToArray();
        ProfileText.Text = "Bitrate: " + string.Join(" · ", results);
    }

    private void RecoveryTest_Click(object sender, RoutedEventArgs e)
    {
        RecoveryText.Text = "Recovery: flujo preparado (ADB → Internet → Gnirehtet → verificación)";
    }

    private void ConnectTest_Click(object sender, RoutedEventArgs e)
    {
        ConnectionText.Text = "● MODO PRUEBA — DISPOSITIVO SIMULADO";
        PollingText.Text = "Polling: controlado; no se inicia monitoreo agresivo";
    }
}
