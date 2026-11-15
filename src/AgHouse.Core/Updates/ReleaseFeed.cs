using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgHouse.Core.Updates;

/// <summary>A published aghouse release: its version, its page, and the installer to download.</summary>
public sealed record ReleaseInfo(
    Version Version,
    string Tag,
    string PageUrl,
    string? SetupName,
    string? SetupUrl,
    long SetupSize,
    string? ChecksumsUrl)
{
    /// <summary>The release has an installer and the checksums to verify it with.</summary>
    public bool CanInstall => SetupUrl is not null && ChecksumsUrl is not null;
}

/// <summary>
/// Reads aghouse's GitHub Releases. Only the newest full release counts (no drafts, no
/// pre-releases), and only download links inside this repository's releases are accepted.
/// </summary>
public static class ReleaseFeed
{
    public const string ReleasesPage = "https://github.com/pradprat/aghouse/releases";
    public const string LatestApi = "https://api.github.com/repos/pradprat/aghouse/releases/latest";
    private const string DownloadPrefix = ReleasesPage + "/download/";
    public const string ChecksumsName = "SHA256SUMS.txt";

    /// <summary>The installer's file name for a version, as packaging/publish.ps1 names it.</summary>
    public static string SetupName(string version) => $"aghouse-{version}-setup-x64.exe";

    /// <summary>
    /// "v0.1.2", "0.1.2" or "0.1.2+03d5770" as a three-part version. A pre-release ("0.2.0-beta")
    /// or anything else gives null.
    /// </summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        if (s.Contains('-') || !Version.TryParse(s, out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    /// <summary>The running build's version, from the assembly's informational version.</summary>
    public static Version CurrentVersion(Assembly assembly) =>
        ParseVersion(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? ParseVersion(assembly.GetName().Version?.ToString())
        ?? new Version(0, 0, 0);

    /// <summary>
    /// The release in a GitHub "release" JSON object, or null when it's a draft, a pre-release,
    /// or doesn't look like one of aghouse's releases.
    /// </summary>
    public static ReleaseInfo? Parse(string json)
    {
        JsonObject? root;
        try { root = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return null; }
        if (root is null) return null;
        if (Bool(root["draft"]) || Bool(root["prerelease"])) return null;

        var tag = Str(root["tag_name"]);
        var version = ParseVersion(tag);
        if (tag is null || version is null) return null;

        var page = Str(root["html_url"]);
        if (page is null || !page.StartsWith(ReleasesPage + "/", StringComparison.Ordinal)) page = ReleasesPage;

        var bare = tag.TrimStart('v', 'V');
        var setupName = SetupName(bare);
        string? setupUrl = null, sumsUrl = null;
        long setupSize = 0;
        if (root["assets"] is JsonArray assets)
        {
            foreach (var asset in assets.OfType<JsonObject>())
            {
                var name = Str(asset["name"]);
                var url = Str(asset["browser_download_url"]);
                // Only files attached to this very release of this repository.
                if (name is null || url is null || url != $"{DownloadPrefix}{tag}/{name}") continue;
                if (name == setupName)
                {
                    setupUrl = url;
                    setupSize = asset["size"] is JsonValue size && size.TryGetValue<long>(out var n) ? n : 0;
                }
                else if (name == ChecksumsName)
                {
                    sumsUrl = url;
                }
            }
        }
        return new ReleaseInfo(version, tag, page, setupUrl is null ? null : setupName, setupUrl, setupSize, sumsUrl);
    }

    /// <summary>
    /// The SHA-256 for <paramref name="fileName"/> in a sha256sum-style list ("hash  name" per line),
    /// as 64 lowercase hex digits, or null when the file isn't listed.
    /// </summary>
    public static string? FindChecksum(string sums, string fileName)
    {
        foreach (var raw in sums.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            if (space != 64) continue;
            var name = line[space..].Trim().TrimStart('*');
            if (name != fileName) continue;
            var hash = line[..64].ToLowerInvariant();
            return hash.All(Uri.IsHexDigit) ? hash : null;
        }
        return null;
    }

    /// <summary>An HttpClient that GitHub accepts: it asks for a User-Agent naming the client.</summary>
    public static HttpClient CreateClient(Version current)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("aghouse", current.ToString()));
        return http;
    }

    /// <summary>The newest release, or null when there's none (or it can't be used). Network errors throw.</summary>
    public static async Task<ReleaseInfo?> FetchLatestAsync(HttpClient http, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no releases yet
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
    }

    private static bool Bool(JsonNode? node) => node is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
