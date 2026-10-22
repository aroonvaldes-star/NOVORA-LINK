using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Widget;
using Android.OS;
using Android.Provider;
using NOVORA.Control;
using System.Security.Cryptography;

namespace NOVORA.AndroidUI;

internal sealed record UpdateLINKResult(bool Available, string Message, long VersionCode = 0,
    string? VersionName = null, string? ReleaseUrl = null);

/// <summary>
/// Compares the installed LINK Control with the APK verified by an authorized NOVORA PC.
/// The GitHub repository is private, so this never embeds a token or claims anonymous access.
/// </summary>
internal static class UpdateLINK
{
    private const string PackageId = "com.linkcontrol.novora";
    private const string ReleaseBase =
        "https://github.com/aroonvaldes-star/NOVORA-AppControl/releases/tag/v";

    internal static UpdateLINKResult Detect(Activity activity, NLControlAppUpdate? offered)
    {
        if (offered is null)
            return new(false, "Conecta con NOVORA PC para comprobar actualizaciones.");
        if (offered.PackageId != PackageId || activity.PackageName != PackageId ||
            offered.VersionCode <= 0 || offered.VersionName != "1.4." + offered.VersionCode ||
            offered.Sha256.Length != 64 || !offered.Sha256.All(Uri.IsHexDigit) ||
            offered.Size is <= 0 or > 268435456 ||
            offered.ReleaseUrl != ReleaseBase + offered.VersionName)
            return new(false, "La información de actualización de la PC no es válida.");

        long installed = activity.PackageManager?.GetPackageInfo(PackageId, PackageInfoFlags.MatchAll)
            ?.LongVersionCode ?? 0;
        if (installed <= 0)
            return new(false, "No se pudo leer la versión instalada de LINK Control.");
        if (offered.VersionCode <= installed)
            return new(false, "LINK Control está actualizado (versión 1.4." + installed + ").");
        return new(true, "LINK Control " + offered.VersionName + " está disponible.",
            offered.VersionCode, offered.VersionName, offered.ReleaseUrl);
    }

    internal static void OpenRelease(Activity activity, UpdateLINKResult result)
    {
        if (!result.Available || result.VersionName is null ||
            result.ReleaseUrl != ReleaseBase + result.VersionName)
            return;
        var intent = new Intent(Intent.ActionView,
            Android.Net.Uri.Parse(result.ReleaseUrl));
        intent.AddCategory(Intent.CategoryBrowsable);
        activity.StartActivity(intent);
    }

    internal static async Task<string> DownloadAsync(Activity activity, NLControlSession session,
        NLControlAppUpdate offer, CancellationToken token = default)
    {
        var checkedOffer = Detect(activity, offer);
        if (!checkedOffer.Available) throw new InvalidDataException(checkedOffer.Message);
        string path = Path.Combine(activity.CacheDir!.AbsolutePath,
            "LinkControl-" + offer.VersionCode + ".apk");
        if (File.Exists(path) && new FileInfo(path).Length == offer.Size)
        {
            using var existing = File.OpenRead(path);
            if (string.Equals(Convert.ToHexString(SHA256.HashData(existing)),
                    offer.Sha256, StringComparison.OrdinalIgnoreCase)) return path;
        }
        if (File.Exists(path)) File.Delete(path);
        long generation = session.Current.Generation;
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 32768, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (long offset = 0; offset < offer.Size;)
            {
                token.ThrowIfCancellationRequested();
                if (session.Current.Generation != generation ||
                    session.Current.Phase != NLControlSessionPhase.Connected)
                    throw new IOException("La sesión con NOVORA PC cambió durante la actualización.");
                NLControlReply reply = await session.SendAsync("update.chunk",
                    offer.VersionCode + ":" + offset);
                if (!reply.Success || reply.Value is null)
                    throw new IOException(reply.Message);
                byte[] chunk = Convert.FromBase64String(reply.Value);
                if (chunk.Length == 0 || chunk.Length > 32768 || offset + chunk.Length > offer.Size)
                    throw new InvalidDataException("La PC entregó un bloque APK no válido.");
                await output.WriteAsync(chunk, token);
                hash.AppendData(chunk);
                offset += chunk.Length;
            }
            await output.FlushAsync(token);
            if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()),
                    offer.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El SHA-256 del APK descargado no coincide.");
            return path;
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }
}

public sealed partial class NLAndroidUIActivity
{
    private TextView _updateLinkStatus = null!;
    private Button _updateLinkButton = null!;
    private UpdateLINKResult _updateLinkResult = new(false,
        "Conecta con NOVORA PC para comprobar actualizaciones.");
    private long _updateLinkPromptedVersion;
    private bool _updateLinkInProgress;
    private string? _updateLinkPendingApk;
    private NLControlAppUpdate? _updateLinkPendingOffer;

    private void RenderUpdateLINK(NLControlSnapshot? snapshot)
    {
        try { _updateLinkResult = UpdateLINK.Detect(this, snapshot?.AppUpdate); }
        catch (Android.Content.PM.PackageManager.NameNotFoundException)
        {
            _updateLinkResult = new(false, "No se pudo leer la versión instalada de LINK Control.");
        }
        if (!_updateLinkInProgress) _updateLinkStatus.Text = _updateLinkResult.Message;
        _updateLinkButton.Visibility = _updateLinkResult.Available
            ? Android.Views.ViewStates.Visible : Android.Views.ViewStates.Gone;
        if (!_updateLinkResult.Available || _updateLinkPromptedVersion == _updateLinkResult.VersionCode)
            return;
        if (snapshot?.VideoRunning == true || snapshot?.Engines?.LinkRunning == true)
        {
            _updateLinkStatus.Text = _updateLinkResult.Message +
                " La instalación automática comenzará al detener los motores.";
            return;
        }
        _updateLinkPromptedVersion = _updateLinkResult.VersionCode;
        _ = DownloadAndInstallUpdateLINKAsync();
    }

    private async Task OpenUpdateLINKAsync()
    {
        await DownloadAndInstallUpdateLINKAsync();
    }

    private async Task DownloadAndInstallUpdateLINKAsync()
    {
        if (_updateLinkInProgress || !_updateLinkResult.Available ||
            _service?.Session.Current is not { Phase: NLControlSessionPhase.Connected,
                Snapshot: { AppUpdate: { } offer } } state ||
            offer.VersionCode != _updateLinkResult.VersionCode) return;
        _updateLinkInProgress = true;
        _updateLinkButton.Enabled = false;
        _updateLinkStatus.Text = "Descargando LINK Control " + offer.VersionName + " desde NOVORA PC…";
        try
        {
            string apk = await UpdateLINK.DownloadAsync(this, _service.Session, offer);
            if (!_active || _service.Session.Current.Generation != state.Generation) return;
            _updateLinkPendingApk = apk;
            _updateLinkPendingOffer = offer;
            BeginUpdateLINKInstall(apk, offer);
        }
        catch (Exception ex)
        {
            _updateLinkStatus.Text = "No se pudo preparar la actualización (" +
                ex.GetType().Name + "). Pulsa Instalar actualización para reintentar.";
        }
        finally
        {
            _updateLinkInProgress = false;
            _updateLinkButton.Enabled = true;
        }
    }

    private void OpenUpdateLINK()
    {
        try { UpdateLINK.OpenRelease(this, _updateLinkResult); }
        catch (ActivityNotFoundException)
        {
            _updateLinkStatus.Text = "No hay un navegador disponible para abrir la descarga.";
        }
    }

    private void BeginUpdateLINKInstall(string apk, NLControlAppUpdate offer)
    {
        if (PackageManager?.CanRequestPackageInstalls() != true)
        {
            _updateLinkStatus.Text = "Autoriza a LINK Control para instalar actualizaciones y vuelve a la app.";
            StartActivity(new Intent(Settings.ActionManageUnknownAppSources,
                Android.Net.Uri.Parse("package:" + PackageName)));
            return;
        }
        _updateLinkPendingApk = null;
        _updateLinkPendingOffer = null;
        var installer = PackageManager.PackageInstaller;
        using var parameters = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
        parameters.SetAppPackageName(PackageName);
        parameters.SetSize(offer.Size);
        int sessionId = installer.CreateSession(parameters);
        using var session = installer.OpenSession(sessionId);
        using (var input = File.OpenRead(apk))
        using (var output = session.OpenWrite("LinkControl.apk", 0, offer.Size))
        {
            input.CopyTo(output);
            session.Fsync(output);
        }
        var flags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(31)) flags |= PendingIntentFlags.Mutable;
        var callback = PendingIntent.GetActivity(this, sessionId,
            new Intent(this, typeof(UpdateLINKInstallActivity)), flags);
        session.Commit(callback!.IntentSender);
        _updateLinkStatus.Text = "APK verificado. Confirma la instalación cuando Android la solicite.";
    }

    private void ResumeUpdateLINKInstall()
    {
        if (_updateLinkPendingApk is { } apk && _updateLinkPendingOffer is { } offer &&
            PackageManager?.CanRequestPackageInstalls() == true)
            BeginUpdateLINKInstall(apk, offer);
    }
}

[Activity(Name = "com.linkcontrol.novora.UpdateInstallActivity", Exported = false,
    NoHistory = true, Theme = "@android:style/Theme.Translucent.NoTitleBar")]
public sealed class UpdateLINKInstallActivity : Activity
{
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        var status = (PackageInstallStatus)(Intent?.GetIntExtra(
            PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure) ??
            (int)PackageInstallStatus.Failure);
        if (status == PackageInstallStatus.PendingUserAction)
        {
            #pragma warning disable CS0618, CA1422
            Intent? confirmation = Intent?.GetParcelableExtra(Intent.ExtraIntent) as Intent;
            #pragma warning restore CS0618, CA1422
            if (confirmation is not null) StartActivity(confirmation);
        }
        else if (status != PackageInstallStatus.Success)
            Toast.MakeText(this, "Android no instaló LINK Control. Revisa la firma y el espacio disponible.",
                ToastLength.Long)?.Show();
        Finish();
    }
}
