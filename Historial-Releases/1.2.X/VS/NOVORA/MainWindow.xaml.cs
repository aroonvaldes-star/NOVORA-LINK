using System.Windows;
using NOVORA.Models;
using NOVORA.Services;
namespace NOVORA;
public partial class MainWindow : Window
{
    private readonly OutputProfileService _profiles = new();
    public MainWindow() { InitializeComponent(); }
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
