using System.Diagnostics;
using System.Windows;
using NOVORA.Service;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private readonly CancellationTokenSource _officialReleaseCts14 = new();
    private NLServiceNovoraUpdateInfo? _officialRelease14;
    private bool _officialReleaseInstalling14;

    internal static string UpdateActionText14(string version, bool downloading) =>
        downloading ? $"DESCARGANDO {version}…" : $"DESCARGAR E INSTALAR {version}";

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
            OfficialReleaseStatusText14.Text = message + ". Pulsa el aviso superior para descargar, verificar e instalar la actualización.";
            UpdateBannerActionButton14.Content = UpdateActionText14(release.LatestVersion, downloading: false);
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

    private async Task InstallOfficialRelease14Async()
    {
        if (_officialRelease14 is not { } release || _officialReleaseInstalling14 || _closing) return;
        _officialReleaseInstalling14 = true;
        UpdateBannerActionButton14.IsEnabled = false;
        UpdateBannerActionButton14.Content = UpdateActionText14(release.LatestVersion, downloading: true);
        OfficialReleaseStatusText14.Text = $"Descargando NOVORA-LINK {release.LatestVersion} y comprobando su integridad…";

        var progress = new Progress<int>(percent =>
        {
            UpdateBannerActionButton14.Content = $"DESCARGANDO {release.LatestVersion} · {percent}%";
            OfficialReleaseStatusText14.Text = $"Descargando y verificando la actualización oficial: {percent}%";
        });

        try
        {
            await _updateService.InstallAndRestartAsync(release, progress, _officialReleaseCts14.Token);
        }
        catch (OperationCanceledException) when (_officialReleaseCts14.IsCancellationRequested) { }
        catch (Exception)
        {
            if (_closing) return;
            _officialReleaseInstalling14 = false;
            UpdateBannerActionButton14.IsEnabled = true;
            UpdateBannerActionButton14.Content = UpdateActionText14(release.LatestVersion, downloading: false);
            OfficialReleaseStatusText14.Text = "No se pudo descargar o verificar el instalador oficial. Comprueba Internet y vuelve a intentarlo.";
            ShowTopMessage14("La actualización no se instaló. NOVORA continúa abierto sin cambios.", NLUIMessageKind14.Warning);
        }
    }
}
