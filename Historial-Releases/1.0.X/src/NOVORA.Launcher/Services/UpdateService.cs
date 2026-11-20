using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace NOVORA.Launcher.Services;

public sealed record NovoraUpdateInfo(
    Version Version,
    string TagName,
    string ReleaseName,
    string AssetName,
    Uri DownloadUri,
    string ReleaseUrl);

public sealed class UpdateService
{
    public const string OfficialPortalUrl = "https://novora-link.aroonvaldes.workers.dev/";
    private const string Repository = "aroonvaldes-star/NOVORA-LINK";
    private const string OfficialReleasesApi = "https://novora-link.aroonvaldes.workers.dev/api/releases/latest";
    private static readonly string GitHubReleasesApi = $"https://api.github.com/repos/{Repository}/releases/latest";
    private static readonly HttpClient Http = CreateHttpClient();

    public Version CurrentVersion
    {
        get
        {
            var text = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            return Version.TryParse(text, out var version) ? version : new Version(1, 0, 1);
        }
    }

    public async Task<NovoraUpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var endpoints = new[] { OfficialReleasesApi, GitHubReleasesApi };
        Exception? lastError = null;

        foreach (var endpoint in endpoints)
        {
            try
            {
                var update = await CheckEndpointAsync(endpoint, cancellationToken);
                if (update is not null)
                    return update;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (lastError is not null)
            throw lastError;

        return null;
    }

    private async Task<NovoraUpdateInfo?> CheckEndpointAsync(string endpoint, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            return null;
        if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
            return null;

        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var version = ParseVersion(tag);
        if (version <= CurrentVersion)
            return null;

        var releaseName = root.TryGetProperty("name", out var name)
            ? name.GetString() ?? tag
            : tag;

        var releaseUrl = root.TryGetProperty("html_url", out var html)
            ? html.GetString() ?? OfficialPortalUrl
            : OfficialPortalUrl;

        if (!root.TryGetProperty("assets", out var assets))
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var assetName = asset.TryGetProperty("name", out var assetNameElement)
                ? assetNameElement.GetString()
                : null;
            var urlText = asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(urlText))
                continue;

            if (!IsInstallerAsset(assetName))
                continue;
            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                continue;

            return new NovoraUpdateInfo(version, tag, releaseName, assetName, uri, releaseUrl);
        }

        return null;
    }

    private static bool IsInstallerAsset(string assetName)
    {
        return assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
               (assetName.EndsWith("_Setup.exe", StringComparison.OrdinalIgnoreCase) ||
                assetName.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase) ||
                assetName.EndsWith("-Setup-x64.exe", StringComparison.OrdinalIgnoreCase) ||
                assetName.Contains("Setup", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> DownloadInstallerAsync(NovoraUpdateInfo update, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (update.DownloadUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("La actualización no utiliza HTTPS.");

        var tempFolder = Path.Combine(Path.GetTempPath(), "NOVORA", "Updates");
        Directory.CreateDirectory(tempFolder);
        var filePath = Path.Combine(tempFolder, update.AssetName);

        using var response = await Http.GetAsync(update.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);

        var buffer = new byte[128 * 1024];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            readTotal += read;
            if (total is > 0)
                progress?.Report((int)Math.Clamp(readTotal * 100L / total.Value, 0, 100));
        }

        await output.FlushAsync(cancellationToken);
        return filePath;
    }

    private static Version ParseVersion(string tag)
    {
        var clean = tag.Trim().TrimStart('v', 'V');
        var dash = clean.IndexOf('-');
        if (dash >= 0) clean = clean[..dash];
        var plus = clean.IndexOf('+');
        if (plus >= 0) clean = clean[..plus];

        return Version.TryParse(clean, out var version) ? version : new Version(0, 0, 0);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NOVORA-LINK", "1.0.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }
}
