using System.Globalization;
using System.IO;
using NOVORA.Control;
using NOVORA.Service;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private readonly NLServiceAppControlRepository _appControlRepository = new();
    private System.Windows.Threading.DispatcherTimer? _appControlUpdateTimer;
    private CancellationTokenSource? _appControlUpdateCancellation;
    private bool _appControlUpdateRefreshing;

    private void InitializeAndroidUpdateLINK()
    {
        Loaded += (_, _) =>
        {
            _appControlUpdateCancellation = new CancellationTokenSource();
            _appControlUpdateTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMinutes(15) };
            _appControlUpdateTimer.Tick += (_, _) => _ = RefreshAndroidUpdateLINKAsync();
            _appControlUpdateTimer.Start();
            _ = RefreshAndroidUpdateLINKAsync();
        };
        Closed += (_, _) =>
        {
            _appControlUpdateTimer?.Stop();
            _appControlUpdateCancellation?.Cancel();
            _appControlUpdateCancellation?.Dispose();
            _appControlUpdateCancellation = null;
        };
    }

    private async Task RefreshAndroidUpdateLINKAsync()
    {
        if (_closing || _appControlUpdateRefreshing || _appControlUpdateCancellation is null) return;
        _appControlUpdateRefreshing = true;
        try
        {
            long bundled = CaptureAndroidAppUpdate()?.VersionCode ?? 0;
            NLServiceAndroidPackageInfo? package = await _appControlRepository.FindNewerAsync(
                bundled, _appControlUpdateCancellation.Token);
            if (package is null || _closing || package.VersionCode <= bundled) return;
            _repositoryAppUpdate = new NLControlAppUpdate(package.PackageId,
                package.VersionCode, package.VersionName, package.Sha256,
                "https://github.com/aroonvaldes-star/NOVORA-AppControl/releases/tag/v" + package.VersionName,
                new FileInfo(package.ApkPath).Length);
            _repositoryAppUpdateApkPath = package.ApkPath;
            _androidControlRevision++;
            QueueAndroidControlSnapshot();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            // The bundled APK remains available when the private repository or gh login is absent.
        }
        finally { _appControlUpdateRefreshing = false; }
    }

    private async Task<NLControlReply> ReadAndroidUpdateChunkAsync(NLControlRequest request)
    {
        NLControlReply Failure(string message) => new(NLControlProtocol.Version, request.Id, false, message);
        NLControlAppUpdate? offer = CaptureAndroidAppUpdate();
        string[] parts = (request.Value ?? "").Split(':');
        if (offer is null || parts.Length != 2 ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long version) ||
            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long offset) ||
            version != offer.VersionCode || offset < 0 || offset >= offer.Size || offset % 32768 != 0)
            return Failure("La actualización cambió o el bloque no es válido.");
        string? path = _repositoryAppUpdate?.VersionCode == version
            ? _repositoryAppUpdateApkPath : _localAppUpdateApkPath;
        if (path is null) return Failure("El APK verificado ya no está disponible.");
        try
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 32768, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (input.Length != offer.Size) return Failure("El APK cambió durante la descarga.");
            input.Position = offset;
            byte[] chunk = new byte[(int)Math.Min(32768, offer.Size - offset)];
            await input.ReadExactlyAsync(chunk);
            return new NLControlReply(NLControlProtocol.Version, request.Id, true,
                "Bloque de actualización", Value: Convert.ToBase64String(chunk));
        }
        catch (IOException) { return Failure("No se pudo leer el APK de actualización."); }
    }
}
