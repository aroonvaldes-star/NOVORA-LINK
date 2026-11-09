using NOVORA.AndroidUI;

/// <summary>LINK Control self-update from the public LINK-CONTROL repository.</summary>
internal static class UpdateGitHubChecks
{
    private const string Tag = "v1.4.125";
    private static readonly Uri Apk = new($"https://github.com/aroonvaldes-star/LINK-CONTROL/releases/download/{Tag}/LinkControl.apk");
    private const string Sha = "81FACAD6D39D7C8BA6FFBF64C1B4F87A8757172367ECA3AD71091367EDC78210";

    public static void Run()
    {
        string release = Release(Tag, "aroonvaldes-star/LINK-CONTROL");
        var parsed = UpdateLINKGitHub.ParseRelease(release);
        Assert(parsed.Tag == Tag && parsed.Apk == Apk && parsed.ApkSize == 14_035_363, "Valid release was misparsed.");
        Rejects(() => UpdateLINKGitHub.ParseRelease(Release(Tag, "attacker/LINK-CONTROL")), "Assets from another repository.");
        Rejects(() => UpdateLINKGitHub.ParseRelease(Release(Tag, "aroonvaldes-star/LINK-CONTROL", tagInPath: "v1.4.1")),
            "Assets from another release.");
        Rejects(() => UpdateLINKGitHub.ParseRelease(Release(Tag, "aroonvaldes-star/LINK-CONTROL", prerelease: true)), "Pre-release.");
        Rejects(() => UpdateLINKGitHub.ParseRelease(Release(Tag, "aroonvaldes-star/LINK-CONTROL", size: 0)), "Empty APK.");
        Assert(!UpdateLINKGitHub.IsReleaseAsset(new Uri(Apk + "?x=1"), Tag, "LinkControl.apk") &&
            !UpdateLINKGitHub.IsReleaseAsset(new Uri(Apk.ToString().Replace("https", "http")), Tag, "LinkControl.apk"),
            "Query strings and plain HTTP must be rejected.");

        var descriptor = UpdateLINKGitHub.ParseDescriptor(Descriptor(125, "1.4.125"), Tag, Apk, 14_035_363, 34);
        Assert(descriptor.VersionCode == 125 && descriptor.Sha256 == Sha && descriptor.MinSdk == 30, "Descriptor misparsed.");
        Rejects(() => UpdateLINKGitHub.ParseDescriptor(Descriptor(125, "1.4.124"), Tag, Apk, 1, 34), "Name/code mismatch.");
        Rejects(() => UpdateLINKGitHub.ParseDescriptor(Descriptor(126, "1.4.126"), Tag, Apk, 1, 34), "Tag/version mismatch.");
        Rejects(() => UpdateLINKGitHub.ParseDescriptor(Descriptor(125, "1.4.125", package: "com.evil"), Tag, Apk, 1, 34), "Package.");
        Rejects(() => UpdateLINKGitHub.ParseDescriptor(Descriptor(125, "1.4.125"), Tag, Apk, 1, 29), "MinSdk above device.");
        Console.WriteLine("LINK Control GitHub update: repository/tag-bound assets, stable releases, descriptor and MinSdk checks passed.");
    }

    /// <summary>Real network check against the published repository (explicit mode only).</summary>
    public static async Task RunLiveAsync()
    {
        using var client = UpdateLINKGitHub.CreateClient("test");
        var release = await UpdateLINKGitHub.FindLatestAsync(client, 34, CancellationToken.None)
            ?? throw new Exception("No release found.");
        string path = Path.Combine(Path.GetTempPath(), "novora-update-" + Guid.NewGuid().ToString("N") + ".apk");
        try
        {
            await UpdateLINKGitHub.DownloadAsync(client, release, path, null, CancellationToken.None);
            Assert(new FileInfo(path).Length == release.Size, "Downloaded size mismatch.");
            Console.WriteLine($"Live GitHub update check passed: {release.VersionName} ({release.Size} bytes, SHA-256 verified).");
        }
        finally { File.Delete(path); }
    }

    private static string Release(string tag, string repository, string? tagInPath = null, bool prerelease = false, long size = 14_035_363)
    {
        string path = tagInPath ?? tag;
        return $$"""
        {"tag_name":"{{tag}}","draft":false,"prerelease":{{(prerelease ? "true" : "false")}},"assets":[
          {"name":"LinkControl.apk","size":{{size}},"browser_download_url":"https://github.com/{{repository}}/releases/download/{{path}}/LinkControl.apk"},
          {"name":"NLAndroidRelease.json","size":257,"browser_download_url":"https://github.com/{{repository}}/releases/download/{{path}}/NLAndroidRelease.json"},
          {"name":"SHA256SUMS.txt","size":82,"browser_download_url":"https://github.com/{{repository}}/releases/download/{{path}}/SHA256SUMS.txt"}]}
        """;
    }

    private static string Descriptor(long code, string name, string package = "com.linkcontrol.novora") =>
        $$"""{"SchemaVersion":1,"PackageId":"{{package}}","VersionCode":{{code}},"VersionName":"{{name}}","MinSdk":30,"ControlProtocol":1,"PcSeries":"1.4","Sha256":"{{Sha.ToLowerInvariant()}}"}""";

    private static void Rejects(Action action, string scenario)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Update check accepted: " + scenario);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
