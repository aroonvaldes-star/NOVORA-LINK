using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NOVORA.Service;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestOfficialRelease
{
    [Theory]
    [InlineData("1.4.1-experimental", "v1.4.0", true)]
    [InlineData("1.4.99-experimental.7+abc", "v1.4", true)]
    [InlineData("1.4.0-experimental.1", "v1.4.0", true)]
    [InlineData("1.3.1", "v1.4.0", true)]
    [InlineData("1.3", "v1.4", true)]
    [InlineData("1.4", "v1.4.0", false)]
    [InlineData("1.4.0", "v1.4", false)]
    [InlineData("1.4.0", "v1.4.0", false)]
    [InlineData("1.4.1", "v1.4.0", false)]
    [InlineData("1.4.1", "v1.4.2", true)]
    [InlineData("1.4.9-experimental", "v1.3.99", false)]
    [InlineData("1.4.1-experimental", "v1.5.0", true)]
    [InlineData("1.4.1-experimental", "v1.4.2-experimental", false)]
    [InlineData("1.3.1", "v1.4.0-rc.1", false)]
    [InlineData("invalid", "v1.4.0", false)]
    [InlineData("1.4.1-experimental", "release", false)]
    [InlineData("1.4.1-experimental", "v1.4.0-", false)]
    public void OfficialTransitionRespectsPublicVersionAndChannel(string current, string next, bool expected)
        => Assert.Equal(expected, NLServiceReleaseVersion.ShouldOffer(current, next));

    [Fact]
    public async Task StartupRequestsShareOneOfficialCheckAndShowPublicVersion()
    {
        using var handler = new Handler(Payload());
        using var http = new HttpClient(handler);
        var service = new NLServiceUpdate(http, "1.4.99-experimental");
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => service.CheckForUpdatesAsync()));
        var release = Assert.IsType<NLServiceNovoraUpdateInfo>(results[0]);
        Assert.All(results, item => Assert.Same(release, item));
        Assert.Equal(1, handler.Count);
        Assert.Equal("https://api.github.com/repos/aroonvaldes-star/NOVORA-LINK/releases/latest", handler.Url);
        Assert.True(release.Available);
        Assert.Equal("1.4", release.LatestVersion);
        Assert.Equal("https://github.com/aroonvaldes-star/NOVORA-LINK/releases/tag/v1.4.0", release.ReleaseUrl);
    }

    [Theory]
    [InlineData(true, false, "v1.4.0")]
    [InlineData(false, true, "v1.4.0")]
    [InlineData(false, false, "v1.4.0-experimental")]
    [InlineData(false, false, "v1.3.1")]
    public async Task DraftsExperimentalAndOlderOfficialAreNotAnnounced(bool draft, bool prerelease, string tag)
    {
        using var handler = new Handler(Payload(draft: draft, prerelease: prerelease, tag: tag));
        using var http = new HttpClient(handler);
        Assert.Null(await new NLServiceUpdate(http, "1.4.1-experimental").CheckForUpdatesAsync());
    }

    [Theory]
    [InlineData("NOVORA-Setup-1.4.0.exe")]
    [InlineData("NOVORA-LINK-1.4.0-Setup-x64.exe")]
    public async Task BothLegacyAndCurrentInstallerNamesAreRecognized(string name)
    {
        using var handler = new Handler(Payload(name: name));
        using var http = new HttpClient(handler);
        Assert.NotNull(await new NLServiceUpdate(http, "1.3.1").CheckForUpdatesAsync());
    }

    [Theory]
    [InlineData("other.exe")]
    [InlineData("NOVORA-Setup-1.4.0-arm64.exe")]
    [InlineData("NOVORA-Setup-1.4.0-x86.exe")]
    [InlineData("NOVORA-LINK.zip")]
    [InlineData("../NOVORA-Setup-1.4.exe")]
    public async Task WrongPlatformOrNonInstallerIsNotOffered(string name)
    {
        using var handler = new Handler(Payload(name: name));
        using var http = new HttpClient(handler);
        Assert.Null(await new NLServiceUpdate(http, "1.4.1-experimental").CheckForUpdatesAsync());
    }

    [Theory]
    [InlineData("https://example.com/NOVORA-Setup-1.4.0.exe")]
    [InlineData("https://github.com/other/NOVORA-LINK/releases/download/v1.4.0/NOVORA-Setup-1.4.0.exe")]
    [InlineData("http://github.com/aroonvaldes-star/NOVORA-LINK/releases/download/v1.4.0/NOVORA-Setup-1.4.0.exe")]
    [InlineData("https://github.com/aroonvaldes-star/NOVORA-LINK/releases/download/v1.5.0/NOVORA-Setup-1.4.0.exe")]
    public async Task ForeignOrMismatchedAssetUrlIsRejected(string url)
    {
        using var handler = new Handler(Payload(url: url));
        using var http = new HttpClient(handler);
        Assert.Null(await new NLServiceUpdate(http, "1.4.1-experimental").CheckForUpdatesAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256:abc")]
    [InlineData("md5:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task MissingOrInvalidDigestIsNotOffered(string? digest)
    {
        using var handler = new Handler(Payload(digest: digest));
        using var http = new HttpClient(handler);
        Assert.Null(await new NLServiceUpdate(http, "1.4.1-experimental").CheckForUpdatesAsync());
    }

    [Fact]
    public async Task NoReleaseYetDoesNotInventAnAnnouncementOrRetry()
    {
        using var handler = new Handler("", HttpStatusCode.NotFound);
        using var http = new HttpClient(handler);
        var service = new NLServiceUpdate(http, "1.4.1-experimental");
        Assert.Null(await service.CheckForUpdatesAsync());
        Assert.Null(await service.CheckForUpdatesAsync());
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task NetworkFailureDoesNotStartAutomaticRetries()
    {
        using var handler = new Handler("", HttpStatusCode.ServiceUnavailable);
        using var http = new HttpClient(handler);
        var service = new NLServiceUpdate(http, "1.4.1-experimental");
        await Assert.ThrowsAsync<HttpRequestException>(() => service.CheckForUpdatesAsync());
        await Assert.ThrowsAsync<HttpRequestException>(() => service.CheckForUpdatesAsync());
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task ClosingCancelsPendingCheckWithoutRetrying()
    {
        using var handler = new WaitingHandler();
        using var http = new HttpClient(handler);
        using var cancel = new CancellationTokenSource();
        var service = new NLServiceUpdate(http, "1.4.1-experimental");
        var first = service.CheckForUpdatesAsync(cancel.Token);
        await handler.Started.Task;
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Same(first, service.CheckForUpdatesAsync());
        Assert.Equal(1, handler.Count);
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        public int Count;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Cancellation was expected");
        }
    }
    private const string Hash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static string Payload(bool draft = false, bool prerelease = false, string tag = "v1.4.0",
        string name = "NOVORA-Setup-1.4.0.exe", string? url = null, string? digest = Hash) =>
        JsonSerializer.Serialize(new { draft, prerelease, tag_name = tag, name = "NOVORA-LINK 1.4 oficial",
            body = "Release oficial", html_url = "https://example.com/untrusted-display-link",
            assets = new[] { new { name, digest, browser_download_url = url ??
                $"https://github.com/aroonvaldes-star/NOVORA-LINK/releases/download/{tag}/{name}" } } });

    private sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Count;
        public string? Url;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            Url = request.RequestUri?.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}