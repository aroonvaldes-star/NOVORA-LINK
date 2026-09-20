using System.Diagnostics;
using System.Windows;
using NOVORA.Service;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private readonly CancellationTokenSource _officialReleaseCts14 = new();
    private NLServiceNovoraUpdateInfo? _officialRelease14;

    private async Task CheckOfficialReleaseOnce14Async()
    {
        try
        {
            var release = await _updateService.CheckForUpdatesAsync(_officialReleaseCts14.Token);
            if (_closing) return;
            if (release is null || !release.Available)
            {
                OfficialReleaseStatusText14.Text = "Sin una release oficial posterior disponible. Se comprobará de nuevo al abrir NOVORA.";
                return;
            }
            _officialRelease14 = release;
            var message = $"NOVORA-LINK {release.LatestVersion} oficial disponible";
            OfficialReleaseStatusText14.Text = message + ". Pulsa el aviso superior para ver la publicación y sus instrucciones.";
            UpdateBannerActionButton14.Content = $"VER {release.LatestVersion} OFICIAL";
            UpdateBannerActionButton14.ToolTip = message;
            UpdateBannerActionButton14.Visibility = Visibility.Visible;
            ShowTopMessage14(message, NLUIMessageKind14.Success);
        }
        catch (OperationCanceledException) when (_officialReleaseCts14.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closing)
                OfficialReleaseStatusText14.Text = "No se pudo consultar la release oficial. Puedes revisar Releases en GitHub; se intentará al abrir NOVORA de nuevo.";
            Trace.WriteLine($"NOVORA release check: {ex.GetType().Name}");
        }
    }

    private void OpenOfficialRelease14()
    {
        if (_officialRelease14 is null) return;
        try
        {
            // This URL is validated against the exact official repository by the service.
            Process.Start(new ProcessStartInfo(_officialRelease14.ReleaseUrl) { UseShellExecute = true });
        }
        catch (Exception)
        {
            ShowTopMessage14("No se pudo abrir el navegador. Visita github.com/aroonvaldes-star/NOVORA-LINK/releases.", NLUIMessageKind14.Warning);
        }
    }
}