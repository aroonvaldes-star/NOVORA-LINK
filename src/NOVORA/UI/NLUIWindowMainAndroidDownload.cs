using NOVORA.Service;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private CancellationTokenSource? _androidDownloadRequest;
    private NLAndroidPublicDownload? _androidDownload;

    private void CloseAndroidDownload() => _androidDownloadRequest?.Cancel();

    private async void CheckAndroidUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_closing || _androidDownloadRequest is not null) return;
        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _androidDownloadRequest = request;
        CheckAndroidUpdateButton14.IsEnabled = false;
        AndroidDownloadStatus14.Text = "Consultando la release privada de LINK Control…";
        try
        {
            var download = await _appControlRepository.FindLatestDownloadAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (_closing) return;
            _androidDownload = download;
            AndroidDownloadStatus14.Text = $"LINK Control {download.TagName} disponible. Pulsa Mostrar QR: el enlace temporal permite descargar el APK privado sin iniciar sesión en el teléfono y caduca pronto.";
        }
        catch (OperationCanceledException)
        {
            if (!_closing) AndroidDownloadStatus14.Text = "La consulta se canceló o tardó demasiado. Vuelve a intentarlo.";
        }
        catch (Exception)
        {
            if (!_closing) AndroidDownloadStatus14.Text = "No se pudo consultar la release privada de AppControl. Revisa GitHub CLI y su inicio de sesión.";
        }
        finally
        {
            _androidDownloadRequest = null;
            if (!_closing) CheckAndroidUpdateButton14.IsEnabled = true;
        }
    }

    private async void ShowAndroidDownloadQr_Click(object sender, RoutedEventArgs e)
    {
        if (_closing || _androidDownloadRequest is not null) return;
        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _androidDownloadRequest = request;
        _androidDownload = null;
        AndroidDownloadQrFrame14.Visibility = Visibility.Collapsed;
        AndroidDownloadQrImage14.Source = null;
        AndroidDownloadCopyButton14.IsEnabled = false;
        AndroidDownloadQrButton14.IsEnabled = false;
        AndroidDownloadStatus14.Text = "Buscando la APK privada de LINK Control…";
        try
        {
            var download = await _appControlRepository.FindLatestDownloadAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (_closing) return;
            byte[] png = await Task.Run(() => NLServiceAndroidDownload.GeneratePrivateQrCode(download), request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (_closing) return;
            using var stream = new MemoryStream(png);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            _androidDownload = download;
            AndroidDownloadQrImage14.Source = image;
            AndroidDownloadQrFrame14.Visibility = Visibility.Visible;
            AndroidDownloadCopyButton14.IsEnabled = true;
            AndroidDownloadStatus14.Text = $"LINK Control · {download.TagName}\nEscanea ahora: el enlace de descarga es temporal. Android confirmará la instalación.";
        }
        catch (OperationCanceledException)
        { if (!_closing) AndroidDownloadStatus14.Text = "La consulta se canceló o tardó demasiado. Pulsa Mostrar QR para reintentar."; }
        catch (Exception ex)
        { if (!_closing) AndroidDownloadStatus14.Text = "No se pudo consultar la release privada. Revisa GitHub CLI y vuelve a intentar. " + ex.GetType().Name; }
        finally
        {
            _androidDownloadRequest = null;
            if (!_closing) AndroidDownloadQrButton14.IsEnabled = true;
        }
    }

    private async void CopyAndroidDownloadLink_Click(object sender, RoutedEventArgs e)
    {
        if (_closing || _androidDownloadRequest is not null) return;
        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _androidDownloadRequest = request;
        AndroidDownloadCopyButton14.IsEnabled = false;
        try
        {
            var download = await _appControlRepository.FindLatestDownloadAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (_closing) return;
            System.Windows.Clipboard.SetText(download.DownloadUri.AbsoluteUri);
            _androidDownload = download;
            AndroidDownloadStatus14.Text = "Enlace temporal directo copiado. Ábrelo pronto en el teléfono.";
        }
        catch (Exception)
        { if (!_closing) AndroidDownloadStatus14.Text = "No se pudo generar el enlace temporal. Revisa la sesión de GitHub en PC y vuelve a intentar."; }
        finally
        {
            _androidDownloadRequest = null;
            if (!_closing) AndroidDownloadCopyButton14.IsEnabled = true;
        }
    }
}
