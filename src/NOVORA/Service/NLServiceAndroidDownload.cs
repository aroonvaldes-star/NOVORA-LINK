using System.IO;
using System.Net.Http;
using System.Text.Json;
using QRCoder;

namespace NOVORA.Service;

public sealed record NLAndroidPublicDownload(string TagName, bool IsPrerelease, Uri DownloadUri, long Size);

/// <summary>Resolves an anonymous public APK only when explicitly requested by the user.</summary>
public sealed class NLServiceAndroidDownload
{
    public const string ReleasesUrl = "https://github.com/aroonvaldes-star/NOVORA-LINK/releases";
    private const string ReleasesApi = "https://api.github.com/repos/aroonvaldes-star/NOVORA-LINK/releases?per_page=30";
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 2_000_000 };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NOVORA-LINK");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    public async Task<NLAndroidPublicDownload?> FindPublicDownloadAsync(CancellationToken token) =>
        SelectDownload(await Http.GetStringAsync(ReleasesApi, token).ConfigureAwait(false));

    public static NLAndroidPublicDownload? SelectDownload(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Respuesta de descargas no válida.");
        var candidates = new List<(DateTimeOffset Published, NLAndroidPublicDownload Download)>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (!release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
                !release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array ||
                !release.TryGetProperty("published_at", out var published) || !published.TryGetDateTimeOffset(out var publishedAt) ||
                !release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String) continue;
            string tagName = tag.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(tagName) || tagName.Length > 100 || tagName.Any(char.IsControl)) continue;
            var matching = assets.EnumerateArray().Where(asset =>
                asset.TryGetProperty("name", out var name) && name.GetString() == "LinkControl.apk").ToArray();
            if (matching.Length != 1) continue;
            var apk = matching[0];
            if (!apk.TryGetProperty("state", out var state) || state.GetString() != "uploaded" ||
                !apk.TryGetProperty("size", out var size) || !size.TryGetInt64(out long bytes) || bytes <= 0 ||
                !apk.TryGetProperty("browser_download_url", out var url) ||
                !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) || !IsPublicApkUri(uri)) continue;
            bool prerelease = release.TryGetProperty("prerelease", out var preview) && preview.ValueKind == JsonValueKind.True;
            candidates.Add((publishedAt, new(tagName, prerelease, uri, bytes)));
        }
        return candidates.OrderByDescending(item => item.Published).FirstOrDefault().Download;
    }

    public static bool IsPublicApkUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.Port == 443 &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
        uri.AbsoluteUri.Length <= 2048 &&
        uri.AbsolutePath.StartsWith("/aroonvaldes-star/NOVORA-LINK/releases/download/", StringComparison.Ordinal) &&
        uri.AbsolutePath.EndsWith("/LinkControl.apk", StringComparison.Ordinal);

    public static bool IsPrivateApkUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.Port == 443 &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
        uri.AbsoluteUri.Length <= 2048 &&
        uri.AbsolutePath.StartsWith("/aroonvaldes-star/NOVORA-AppControl/releases/download/", StringComparison.Ordinal) &&
        uri.AbsolutePath.EndsWith("/LinkControl.apk", StringComparison.Ordinal);

    // GitHub returns a short-lived signed asset URL for private releases. The
    // authorization token stays on PC; the phone only receives this URL.
    public static bool IsTemporaryPrivateAssetUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) &&
        uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Fragment) && uri.AbsoluteUri.Length <= 4096 &&
        uri.AbsolutePath.StartsWith("/github-production-release-asset/", StringComparison.Ordinal) &&
        uri.Query.Contains("sig=", StringComparison.Ordinal) &&
        uri.Query.Contains("se=", StringComparison.Ordinal);

    public static byte[] GeneratePrivateQrCode(NLAndroidPublicDownload download)
    {
        if (!IsTemporaryPrivateAssetUri(download.DownloadUri)) throw new InvalidDataException("Destino privado temporal no permitido.");
        using var data = QRCodeGenerator.GenerateQrCode(download.DownloadUri.AbsoluteUri, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(6);
    }

    public static byte[] GenerateQrCode(NLAndroidPublicDownload download)
    {
        if (!IsPublicApkUri(download.DownloadUri)) throw new InvalidDataException("Destino de descarga no público o no permitido.");
        using var data = QRCodeGenerator.GenerateQrCode(download.DownloadUri.AbsoluteUri, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(6);
    }
}
