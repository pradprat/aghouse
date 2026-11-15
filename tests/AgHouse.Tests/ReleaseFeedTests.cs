using AgHouse.Core.Updates;

namespace AgHouse.Tests;

public class ReleaseFeedTests
{
    private const string Dl = "https://github.com/pradprat/aghouse/releases/download/";

    private static string Release(string tag, bool draft = false, bool prerelease = false, string? setupUrl = null, string? sumsUrl = null)
    {
        var bare = tag.TrimStart('v');
        setupUrl ??= $"{Dl}{tag}/aghouse-{bare}-setup-x64.exe";
        sumsUrl ??= $"{Dl}{tag}/SHA256SUMS.txt";
        return $$"""
            {
              "tag_name": "{{tag}}",
              "html_url": "https://github.com/pradprat/aghouse/releases/tag/{{tag}}",
              "draft": {{(draft ? "true" : "false")}},
              "prerelease": {{(prerelease ? "true" : "false")}},
              "assets": [
                { "name": "aghouse-{{bare}}-setup-x64.exe", "size": 81234567, "browser_download_url": "{{setupUrl}}" },
                { "name": "aghouse-{{bare}}-win-x64.zip", "size": 90000000, "browser_download_url": "{{Dl}}{{tag}}/aghouse-{{bare}}-win-x64.zip" },
                { "name": "SHA256SUMS.txt", "size": 200, "browser_download_url": "{{sumsUrl}}" }
              ]
            }
            """;
    }

    [Theory]
    [InlineData("v0.1.2", "0.1.2")]
    [InlineData("0.1.2", "0.1.2")]
    [InlineData("0.1.2+03d5770abc", "0.1.2")]
    [InlineData("0.1.2.0", "0.1.2")]
    [InlineData("1.2", "1.2.0")]
    public void ParseVersion_Releases(string text, string expected) =>
        Assert.Equal(Version.Parse(expected), ReleaseFeed.ParseVersion(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.2.0-beta")]
    [InlineData("latest")]
    public void ParseVersion_RejectsOthers(string? text) => Assert.Null(ReleaseFeed.ParseVersion(text));

    [Fact]
    public void Parse_FindsInstallerAndChecksums()
    {
        var r = ReleaseFeed.Parse(Release("v0.1.3"))!;
        Assert.Equal(new Version(0, 1, 3), r.Version);
        Assert.Equal("aghouse-0.1.3-setup-x64.exe", r.SetupName);
        Assert.Equal($"{Dl}v0.1.3/aghouse-0.1.3-setup-x64.exe", r.SetupUrl);
        Assert.Equal(81234567, r.SetupSize);
        Assert.Equal($"{Dl}v0.1.3/SHA256SUMS.txt", r.ChecksumsUrl);
        Assert.Equal("https://github.com/pradprat/aghouse/releases/tag/v0.1.3", r.PageUrl);
        Assert.True(r.CanInstall);
    }

    [Fact]
    public void Parse_SkipsDraftsAndPrereleases()
    {
        Assert.Null(ReleaseFeed.Parse(Release("v0.1.3", draft: true)));
        Assert.Null(ReleaseFeed.Parse(Release("v0.1.3", prerelease: true)));
        Assert.Null(ReleaseFeed.Parse(Release("v0.2.0-rc1")));
    }

    [Theory]
    [InlineData("https://evil.example/aghouse-0.1.3-setup-x64.exe")]
    [InlineData("https://github.com/someone/aghouse/releases/download/v0.1.3/aghouse-0.1.3-setup-x64.exe")]
    [InlineData("https://github.com/pradprat/aghouse/releases/download/v0.1.2/aghouse-0.1.3-setup-x64.exe")]
    [InlineData("http://github.com/pradprat/aghouse/releases/download/v0.1.3/aghouse-0.1.3-setup-x64.exe")]
    public void Parse_IgnoresDownloadsOutsideThisRelease(string url)
    {
        var r = ReleaseFeed.Parse(Release("v0.1.3", setupUrl: url))!;
        Assert.Null(r.SetupUrl);
        Assert.False(r.CanInstall);
    }

    [Fact]
    public void Parse_WithoutChecksumsCantInstall()
    {
        var r = ReleaseFeed.Parse(Release("v0.1.3", sumsUrl: "https://evil.example/SHA256SUMS.txt"))!;
        Assert.NotNull(r.SetupUrl);
        Assert.False(r.CanInstall);
    }

    [Fact]
    public void Parse_ForeignPageFallsBackToReleases()
    {
        var json = Release("v0.1.3").Replace("https://github.com/pradprat/aghouse/releases/tag/v0.1.3", "https://evil.example/");
        Assert.Equal(ReleaseFeed.ReleasesPage, ReleaseFeed.Parse(json)!.PageUrl);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Parse_Garbage(string json) => Assert.Null(ReleaseFeed.Parse(json));

    [Fact]
    public void FindChecksum_MatchesExactName()
    {
        var a = new string('a', 64);
        var b = new string('B', 64);
        var sums = $"{a}  aghouse-0.1.3-setup-x64.exe.old\r\n{b}  aghouse-0.1.3-setup-x64.exe\r\n{a}  aghouse-0.1.3-win-x64.zip\r\n";
        Assert.Equal(new string('b', 64), ReleaseFeed.FindChecksum(sums, "aghouse-0.1.3-setup-x64.exe"));
        Assert.Null(ReleaseFeed.FindChecksum(sums, "aghouse-0.1.4-setup-x64.exe"));
    }

    [Fact]
    public void FindChecksum_RejectsMalformed()
    {
        Assert.Null(ReleaseFeed.FindChecksum("abc  file.exe", "file.exe"));
        Assert.Null(ReleaseFeed.FindChecksum($"{new string('z', 64)}  file.exe", "file.exe"));
        Assert.Equal(new string('0', 64), ReleaseFeed.FindChecksum($"{new string('0', 64)} *file.exe", "file.exe"));
    }
}
