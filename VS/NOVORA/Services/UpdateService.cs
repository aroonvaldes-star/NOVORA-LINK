using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace NOVORA.Services;

public sealed record UpdateInfo(string Version, string TagName, string DownloadUrl, string FileName);

public sealed class UpdateService
{
    private const string ReleasesApi = "https://api.github.com/repos/aroonvaldes-star/NOVORA-LINK/releases/latest";
    private const string CurrentVersion = "1.2.0";
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NOVORA-Updater/1.2");
        return client;
    }

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(ReleasesApi, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var versionText = tag.TrimStart('v', 'V');

        if (!Version.TryParse(versionText.Split('-')[0], out var remoteVersion)) return null;
        if (!Version.TryParse(CurrentVersion, out var localVersion) || remoteVersion <= localVersion) return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                name.StartsWith("NOVORA-", StringComparison.OrdinalIgnoreCase))
                return new UpdateInfo(versionText, tag, url, name);
        }

        return null;
    }

    public async Task InstallAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "NOVORA-Update", update.Version);
        Directory.CreateDirectory(tempRoot);
        var zipPath = Path.Combine(tempRoot, update.FileName);
        var extractPath = Path.Combine(tempRoot, "extracted");

        await using (var input = await Http.GetStreamAsync(update.DownloadUrl, cancellationToken))
        await using (var output = File.Create(zipPath))
            await input.CopyToAsync(output, cancellationToken);

        if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
        ZipFile.ExtractToDirectory(zipPath, extractPath);

        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var script = Path.Combine(appDir, "Tools", "UpdateLauncher.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("No se encontró el lanzador de actualización.", script);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File "{script}" -Source "{extractPath}" -Target "{appDir}" -ProcessId {Environment.ProcessId}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = appDir
        };
        Process.Start(psi);
    }
}
