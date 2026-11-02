using System.Net;
using System.Security.Cryptography;
using System.Text;
using Somning.Core.Update;
using Xunit;

namespace Somning.Core.Tests.Update;

public sealed class UpdateTests
{
    private const string Hash = "67351c99ae2b8f4ad4093ad1b23496f18b9392b7f81373ceba3b08e9a5f5185d";

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static string Release(string tag = "v1.0.4", string name = "Somning-1.0.4-Setup.exe", string digest = "sha256:" + Hash,
        string url = "https://github.com/JotaEfi/somning/releases/download/v1.0.4/Somning-1.0.4-Setup.exe",
        long size = 1000, bool prerelease = false) =>
        $$"""
        {"tag_name":"{{tag}}","html_url":"https://github.com/JotaEfi/somning/releases/tag/{{tag}}","draft":false,"prerelease":{{prerelease.ToString().ToLowerInvariant()}},
         "assets":[{"name":"{{name}}","digest":"{{digest}}","size":{{size}},"browser_download_url":"{{url}}"}]}
        """;

    [Fact]
    public void Newer_release_is_offered_with_its_verified_digest()
    {
        var update = UpdateChecker.Parse(Release(), new Version(1, 0, 3), arm64: false);

        Assert.NotNull(update);
        Assert.Equal(new Version(1, 0, 4), update.Version);
        Assert.Equal(Hash, update.Sha256);
    }

    [Fact]
    public void Same_or_older_release_is_not_offered()
    {
        Assert.Null(UpdateChecker.Parse(Release(), new Version(1, 0, 4), false));
        Assert.Null(UpdateChecker.Parse(Release(), new Version(1, 1, 0), false));
    }

    [Fact]
    public void Versions_compare_numerically_not_alphabetically()
    {
        var json = Release("v1.0.10", "Somning-1.0.10-Setup.exe");
        Assert.NotNull(UpdateChecker.Parse(json, new Version(1, 0, 9), false));
    }

    [Fact]
    public void Prereleases_missing_digests_and_foreign_hosts_are_never_offered()
    {
        Assert.Null(UpdateChecker.Parse(Release(prerelease: true), new Version(1, 0, 3), false));
        Assert.Null(UpdateChecker.Parse(Release(digest: ""), new Version(1, 0, 3), false));
        Assert.Null(UpdateChecker.Parse(Release(digest: "sha256:abc"), new Version(1, 0, 3), false));
        Assert.Null(UpdateChecker.Parse(Release(url: "https://evil.example.com/Somning-1.0.4-Setup.exe"), new Version(1, 0, 3), false));
        Assert.Null(UpdateChecker.Parse(Release(url: "http://github.com/x/Somning-1.0.4-Setup.exe"), new Version(1, 0, 3), false));
        Assert.Null(UpdateChecker.Parse(Release(tag: "v1.0.4-beta"), new Version(1, 0, 3), false));
    }

    [Fact]
    public void Architecture_selects_the_matching_installer()
    {
        var json = Release(name: "Somning-1.0.4-arm64-Setup.exe");
        Assert.Null(UpdateChecker.Parse(json, new Version(1, 0, 3), arm64: false));
        Assert.NotNull(UpdateChecker.Parse(json, new Version(1, 0, 3), arm64: true));
    }

    [Fact]
    public async Task Check_returns_null_when_no_release_exists()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        Assert.Null(await checker.CheckAsync(new Version(1, 0, 3), false, TestContext.Current.CancellationToken));
    }

    private static (UpdateInfo Update, string Dir, UpdateDownloader Downloader) Download(byte[] served, byte[] declared)
    {
        var update = new UpdateInfo(new Version(1, 0, 4), "v1.0.4", "Somning-1.0.4-Setup.exe",
            new Uri("https://github.com/x/Somning-1.0.4-Setup.exe"), Convert.ToHexString(SHA256.HashData(declared)).ToLowerInvariant(),
            declared.Length, new Uri("https://github.com/x/releases"));
        var downloader = new UpdateDownloader(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(served) })));
        return (update, Path.Combine(Path.GetTempPath(), "somning-update-test-" + Guid.NewGuid().ToString("N")), downloader);
    }

    [Fact]
    public async Task Downloader_returns_the_file_when_the_hash_matches()
    {
        var bytes = Encoding.ASCII.GetBytes("installer-bytes");
        var (update, dir, downloader) = Download(bytes, bytes);
        try
        {
            var path = await downloader.DownloadAsync(update, dir, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Downloader_rejects_and_removes_a_tampered_file()
    {
        var (update, dir, downloader) = Download(Encoding.ASCII.GetBytes("tampered-bytes!"), Encoding.ASCII.GetBytes("installer-bytes"));
        try
        {
            await Assert.ThrowsAsync<UpdateVerificationException>(() => downloader.DownloadAsync(update, dir, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
