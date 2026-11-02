using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Somning.Core.Update;

/// <summary>
/// Asks the GitHub Releases API for the latest stable release and returns it when it is newer than the running
/// version. An update is only offered when its installer for this CPU architecture is hosted on github.com and the
/// API reports a SHA-256 digest for it, so the download can always be verified.
/// </summary>
public sealed partial class UpdateChecker
{
    public const string DefaultRepository = "JotaEfi/somning";
    private const int MaxSetupSizeBytes = 600 * 1024 * 1024;
    private static readonly HttpClient DefaultHttpClient = CreateDefaultClient();

    private readonly HttpClient httpClient;
    private readonly Uri latestReleaseUri;

    public UpdateChecker(HttpClient? httpClient = null, string repository = DefaultRepository, Uri? latestReleaseUriOverride = null)
    {
        this.httpClient = httpClient ?? DefaultHttpClient;
        latestReleaseUri = latestReleaseUriOverride ?? new Uri($"https://api.github.com/repos/{repository}/releases/latest");
    }

    /// <summary>Returns the update, or null when the running version is current or the release is not installable.</summary>
    public async Task<UpdateInfo?> CheckAsync(Version current, bool arm64, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        using var response = await httpClient.GetAsync(latestReleaseUri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no release published yet
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Parse(json, current, arm64);
    }

    public static UpdateInfo? Parse(string json, Version current, bool arm64)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (Flag(root, "draft") || Flag(root, "prerelease")) return null;
        var tag = Text(root, "tag_name");
        if (tag is null || !TryParseVersion(tag, out var latest) || latest <= current) return null;
        if (!Uri.TryCreate(Text(root, "html_url"), UriKind.Absolute, out var releaseUrl) || !IsGitHub(releaseUrl)) return null;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = Text(asset, "name");
            var match = name is null ? null : SetupName().Match(name);
            if (match is null || !match.Success || match.Groups[2].Success != arm64) continue;
            if (!Version.TryParse(match.Groups[1].Value, out var assetVersion) || assetVersion != latest) continue;
            var digest = Text(asset, "digest");
            if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || !Sha256Hex().IsMatch(digest[7..])) continue;
            if (!Uri.TryCreate(Text(asset, "browser_download_url"), UriKind.Absolute, out var url) || !IsGitHub(url)) continue;
            var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var s) ? s : 0;
            if (size <= 0 || size > MaxSetupSizeBytes) continue;
            return new UpdateInfo(latest, tag, name!, url, digest[7..].ToLowerInvariant(), size, releaseUrl);
        }
        return null;
    }

    /// <summary>Parses tags such as "v1.0.4" into a three-part version; pre-release suffixes are not accepted.</summary>
    public static bool TryParseVersion(string tag, out Version version)
    {
        version = new Version(0, 0, 0);
        var text = tag.TrimStart('v', 'V');
        if (!TagVersion().IsMatch(text)) return false;
        return Version.TryParse(text, out version!);
    }

    private static bool IsGitHub(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Somning-Updater", "1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    [GeneratedRegex(@"^Somning-(\d+\.\d+\.\d+)(-arm64)?-Setup\.exe$")]
    private static partial Regex SetupName();

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex TagVersion();

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Hex();
}
