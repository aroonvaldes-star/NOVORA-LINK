using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace NOVORA.Services;

public sealed record NovoraUpdateInfo(
    Version Version,
    string TagName,
    string ReleaseName,
    string AssetName,
    string ExpectedSha256,
    Uri DownloadUri,
    string ReleaseUrl,
    string ReleaseNotes = "")
{
    public bool Available => Version > UpdateService.ReadCurrentVersion();
    public string LatestVersion => Version.ToString();
}

public sealed class UpdateService
{
    private const string Repository = "aroonvaldes-star/NOVORA-LINK";
    private const string ReleasesApi = "https://api.github.com/repos/aroonvaldes-star/NOVORA-LINK/releases/latest";
    private static readonly HttpClient Http = CreateHttpClient();

    public Version CurrentVersion => ReadCurrentVersion();

    internal static Version ReadCurrentVersion()
    {
        var text = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(text)) return new Version(1, 3, 0);
        var separator = text.IndexOfAny(new[] { '-', '+' });
        if (separator >= 0) text = text[..separator];
        return Version.TryParse(text, out var version) ? version : new Version(1, 3, 0);
    }

    public Task<NovoraUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        => CheckForUpdateAsync(cancellationToken);

    public async Task<NovoraUpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(ReleasesApi, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if ((root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) ||
            (root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())) return null;

        var tag = root.TryGetProperty("tag_name", out var tagNode) ? tagNode.GetString() ?? string.Empty : string.Empty;
        var version = ParseVersion(tag);
        if (version <= CurrentVersion) return null;
        var releaseName = root.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? tag : tag;
        var releaseNotes = root.TryGetProperty("body", out var bodyNode) ? bodyNode.GetString() ?? string.Empty : string.Empty;
        var releaseUrl = root.TryGetProperty("html_url", out var urlNode) ? urlNode.GetString() ?? $"https://github.com/{Repository}/releases" : $"https://github.com/{Repository}/releases";
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;
            if (!name.StartsWith("NOVORA-Setup-", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps) continue;
            if (!TryParseSha256Digest(digest, out var sha256)) continue;
            return new NovoraUpdateInfo(version, tag, releaseName, name, sha256, downloadUri, releaseUrl, releaseNotes);
        }
        return null;
    }

    public async Task<string> DownloadInstallerAsync(NovoraUpdateInfo update, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.DownloadUri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("La actualización no utiliza HTTPS.");
        if (!string.Equals(Path.GetFileName(update.AssetName), update.AssetName, StringComparison.Ordinal) || update.AssetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("El nombre del instalador no es válido.");

        var folder = Path.Combine(Path.GetTempPath(), "NOVORA", "Updates");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, update.AssetName);
        using var response = await Http.GetAsync(update.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true);
        var buffer = new byte[128 * 1024];
        long copied = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;
            if (total is > 0) progress?.Report((int)Math.Clamp(copied * 100 / total.Value, 0, 100));
        }
        await output.FlushAsync(cancellationToken);
        var actual = await ComputeSha256Async(path, cancellationToken);
        if (!actual.Equals(update.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(path);
            throw new InvalidDataException("El instalador descargado no coincide con el SHA-256 publicado.");
        }
        progress?.Report(100);
        return path;
    }

    public async Task InstallAndRestartAsync(NovoraUpdateInfo update, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var installer = await DownloadInstallerAsync(update, progress, cancellationToken);
        var info = new ProcessStartInfo
        {
            FileName = installer,
            UseShellExecute = true,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS"
        };
        if (Process.Start(info) is null)
        {
            TryDelete(installer);
            throw new InvalidOperationException("NOVORA no pudo iniciar el instalador de actualización.");
        }
        Environment.Exit(0);
    }

    private static bool TryParseSha256Digest(string? digest, out string sha256)
    {
        sha256 = string.Empty;
        const string prefix = "sha256:";
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var value = digest[prefix.Length..].Trim();
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c))) return false;
        sha256 = value;
        return true;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static Version ParseVersion(string tag)
    {
        var clean = (tag ?? string.Empty).Trim().TrimStart('v', 'V');
        var split = clean.IndexOfAny(new[] { '-', '+' });
        if (split >= 0) clean = clean[..split];
        return Version.TryParse(clean, out var version) ? version : new Version(0, 0, 0);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NOVORA", "1.3"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
