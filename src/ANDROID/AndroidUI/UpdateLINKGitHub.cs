using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace NOVORA.AndroidUI;

/// <summary>A LINK Control release published in the public LINK-CONTROL repository.</summary>
internal sealed record UpdateLINKRelease(long VersionCode, string VersionName, string Tag, string Sha256,
    long Size, Uri ApkUrl, int MinSdk);

/// <summary>
/// Finds, downloads and verifies LINK Control releases from the public GitHub repository
/// aroonvaldes-star/LINK-CONTROL. Portable (no Android types) so it is tested on the PC.
/// Integrity: the APK must match the size and SHA-256 published with the release.
/// Authenticity: Android only installs it over the current app when the signing key matches.
/// </summary>
internal static class UpdateLINKGitHub
{
    internal const string PackageId = "com.linkcontrol.novora";
    internal const string Repository = "aroonvaldes-star/LINK-CONTROL";
    internal static readonly Uri LatestReleaseApi = new($"https://api.github.com/repos/{Repository}/releases/latest");
    internal const string ApkAsset = "LinkControl.apk";
    internal const string DescriptorAsset = "NLAndroidRelease.json";
    internal const long MaximumApkBytes = 256L * 1024 * 1024;
    private const int MaximumMetadataBytes = 256 * 1024;

    internal static HttpClient CreateClient(string userAgentVersion)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 })
        { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LINK-Control-Updater", userAgentVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>Latest valid release, or null when the repository has none usable.</summary>
    internal static async Task<UpdateLINKRelease?> FindLatestAsync(HttpClient client, int deviceSdk,
        CancellationToken cancellationToken)
    {
        string releaseJson = await ReadSmallAsync(client, LatestReleaseApi, cancellationToken).ConfigureAwait(false);
        (string tag, Uri apk, long apkSize, Uri descriptor) = ParseRelease(releaseJson);
        string descriptorJson = await ReadSmallAsync(client, descriptor, cancellationToken).ConfigureAwait(false);
        return ParseDescriptor(descriptorJson, tag, apk, apkSize, deviceSdk);
    }

    internal static (string Tag, Uri Apk, long ApkSize, Uri Descriptor) ParseRelease(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True ||
            root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
            throw new InvalidDataException("La última publicación no es estable.");
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!tag.StartsWith("v1.4.", StringComparison.Ordinal) || tag.Length > 32)
            throw new InvalidDataException("Etiqueta de publicación no válida.");
        Uri? apk = null, descriptor = null;
        long apkSize = 0;
        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (name is not (ApkAsset or DescriptorAsset)) continue;
            var url = new Uri(asset.GetProperty("browser_download_url").GetString() ?? "", UriKind.Absolute);
            if (!IsReleaseAsset(url, tag, name))
                throw new InvalidDataException("La descarga no pertenece al repositorio de LINK Control.");
            if (name == ApkAsset)
            {
                apk = url;
                apkSize = asset.GetProperty("size").GetInt64();
            }
            else descriptor = url;
        }
        if (apk is null || descriptor is null)
            throw new InvalidDataException("La publicación no contiene el APK y su descriptor.");
        if (apkSize is <= 0 or > MaximumApkBytes)
            throw new InvalidDataException("Tamaño de APK no válido.");
        return (tag, apk, apkSize, descriptor);
    }

    internal static UpdateLINKRelease ParseDescriptor(string json, string tag, Uri apk, long apkSize, int deviceSdk)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        int schema = root.GetProperty("SchemaVersion").GetInt32();
        string package = root.GetProperty("PackageId").GetString() ?? "";
        long code = root.GetProperty("VersionCode").GetInt64();
        string name = root.GetProperty("VersionName").GetString() ?? "";
        int minSdk = root.GetProperty("MinSdk").GetInt32();
        string sha = root.GetProperty("Sha256").GetString() ?? "";
        if (schema != 1 || package != PackageId || code <= 0 || name != "1.4." + code || tag != "v" + name ||
            sha.Length != 64 || !sha.All(Uri.IsHexDigit))
            throw new InvalidDataException("El descriptor de la publicación no es válido.");
        if (minSdk > deviceSdk)
            throw new InvalidDataException($"LINK Control {name} requiere Android API {minSdk}.");
        return new UpdateLINKRelease(code, name, tag, sha.ToUpperInvariant(), apkSize, apk, minSdk);
    }

    /// <summary>Only https://github.com/aroonvaldes-star/LINK-CONTROL/releases/download/{tag}/{asset}.</summary>
    internal static bool IsReleaseAsset(Uri url, string tag, string asset) =>
        url.Scheme == Uri.UriSchemeHttps && url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        url.IsDefaultPort && string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.Fragment) &&
        url.AbsolutePath == $"/{Repository}/releases/download/{tag}/{asset}";

    /// <summary>Downloads the APK to <paramref name="path"/>; deletes it unless size and SHA-256 match.</summary>
    internal static async Task DownloadAsync(HttpClient client, UpdateLINKRelease release, string path,
        IProgress<long>? progress, CancellationToken cancellationToken)
    {
        if (File.Exists(path) && new FileInfo(path).Length == release.Size && await MatchesAsync(path, release.Sha256))
            return;
        string partial = path + ".partial";
        try
        {
            using HttpResponseMessage response = await client.GetAsync(release.ApkUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
                throw new InvalidDataException("El tamaño del APK no coincide con la publicación.");
            await using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > release.Size)
                        throw new InvalidDataException("El APK descargado excede el tamaño publicado.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    progress?.Report(total);
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (total != release.Size ||
                    !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("El SHA-256 del APK descargado no coincide con la publicación.");
            }
            File.Move(partial, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    private static async Task<bool> MatchesAsync(string path, string sha256)
    {
        await using var stream = File.OpenRead(path);
        byte[] digest = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(digest), sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadSmallAsync(HttpClient client, Uri url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumMetadataBytes)
            throw new InvalidDataException("Respuesta de publicación demasiado grande.");
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaximumMetadataBytes)
                throw new InvalidDataException("Respuesta de publicación demasiado grande.");
            buffer.Write(chunk, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
