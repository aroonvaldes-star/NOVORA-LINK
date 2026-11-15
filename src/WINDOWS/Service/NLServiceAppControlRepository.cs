using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace NOVORA.Service;

/// <summary>Reads private AppControl releases through the PC user's GitHub CLI login.</summary>
public sealed class NLServiceAppControlRepository
{
    private const string Repository = "aroonvaldes-star/NOVORA-AppControl";
    private static readonly Regex Tag = new(@"^v1\.4\.(\d+)$", RegexOptions.CultureInvariant);
    private NLServiceAndroidPackageInfo? _cached;

    public async Task<NLAndroidPublicDownload> FindLatestDownloadAsync(CancellationToken token)
    {
        string json = await RunGhAsync(["release", "view", "--repo", Repository,
            "--json", "tagName,assets"], token);
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string tag = root.GetProperty("tagName").GetString() ?? "";
        if (!Tag.IsMatch(tag)) throw new InvalidDataException("La release no tiene una versión LINK Control válida.");
        var assets = root.GetProperty("assets").EnumerateArray().Where(asset =>
            asset.GetProperty("name").GetString() == "LinkControl.apk").ToArray();
        if (assets.Length != 1 || !Uri.TryCreate(assets[0].GetProperty("url").GetString(),
                UriKind.Absolute, out Uri? uri) ||
            !NLServiceAndroidDownload.IsPrivateApkUri(uri) ||
            !uri.AbsolutePath.Contains("/" + tag + "/", StringComparison.Ordinal) ||
            !assets[0].GetProperty("size").TryGetInt64(out long size) || size <= 0)
            throw new InvalidDataException("La release privada no contiene un APK válido.");
        return new NLAndroidPublicDownload(tag, false, uri, size);
    }

    public async Task<NLServiceAndroidPackageInfo?> FindNewerAsync(long installedOnPc,
        CancellationToken cancellationToken)
    {
        string tag = (await RunGhAsync(["release", "view", "--repo", Repository,
            "--json", "tagName", "-q", ".tagName"], cancellationToken)).Trim();
        Match match = Tag.Match(tag);
        if (!match.Success || !long.TryParse(match.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out long code) || code <= installedOnPc)
            return null;
        if (_cached?.VersionCode == code) return _cached;

        string root = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "NOVORA-LINK", "AppControlUpdates");
        Directory.CreateDirectory(root);
        string directory = Path.Combine(root, tag + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        // gh uses the PC user's credential store. No GitHub token enters Android,
        // a command argument, a log, or the control protocol.
        await RunGhAsync(["release", "download", tag, "--repo", Repository,
            "--pattern", "LinkControl.apk", "--pattern", "NLAndroidRelease.json",
            "--dir", directory], cancellationToken);
        NLServiceAndroidPackageInfo package = await Task.Run(
            () => NLServiceAndroidPackage.Load(directory), cancellationToken);
        if (package.VersionCode != code || package.VersionName != "1.4." + code)
            throw new InvalidDataException("La release privada no coincide con su descriptor.");
        _cached = package;
        return package;
    }

    private static async Task<string> RunGhAsync(string[] arguments, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var start = new ProcessStartInfo("gh")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new IOException("GitHub CLI no está disponible en esta PC.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(deadline.Token);
        await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        string result = await output.ConfigureAwait(false);
        _ = await error.ConfigureAwait(false); // Never expose credentials or remote error text.
        if (process.ExitCode != 0) throw new IOException("No se pudo consultar la release privada de AppControl.");
        return result;
    }
}
