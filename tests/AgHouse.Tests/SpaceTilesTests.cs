using AgHouse.Core.Themes;

namespace AgHouse.Tests;

public class SpaceTilesTests
{
    public static IEnumerable<object[]> Schemes() =>
        BuiltInSchemes.All.Append(BuiltInSchemes.WindowsDark).Append(BuiltInSchemes.WindowsLight)
            .Select(s => new object[] { s.Name, s.IsLight });

    private static ColorScheme Find(string name, bool light) =>
        BuiltInSchemes.All.Append(BuiltInSchemes.WindowsDark).Append(BuiltInSchemes.WindowsLight)
            .First(s => s.Name == name && s.IsLight == light);

    [Theory]
    [MemberData(nameof(Schemes))]
    public void Tiles_LetterReadsOnEveryTile(string name, bool light)
    {
        var scheme = Find(name, light);
        var tiles = SpaceTiles.For(scheme);
        Assert.Equal(SpaceTiles.Count, tiles.Count);
        foreach (var (fill, ink) in tiles)
            Assert.True(Rgb.Contrast(fill, ink) >= 3.0, $"{name}: {Rgb.ToHex(ink)} on {Rgb.ToHex(fill)} is {Rgb.Contrast(fill, ink):0.00}:1");
    }

    [Theory]
    [MemberData(nameof(Schemes))]
    public void Tiles_ComeFromTheSchemesOwnTable(string name, bool light)
    {
        var scheme = Find(name, light);
        Assert.All(SpaceTiles.For(scheme), t => Assert.Contains(t.Fill, scheme.Table.Select(c => c & 0xFFFFFF)));
    }

    [Fact]
    public void Index_IsStableAndIgnoresCaseAndSpaces()
    {
        var index = SpaceTiles.IndexFor("aghouse");
        Assert.InRange(index, 0, SpaceTiles.Count - 1);
        Assert.Equal(index, SpaceTiles.IndexFor(" AGHOUSE "));
        // FNV-1a, not string.GetHashCode: the same answer on every launch.
        Assert.Equal(index, SpaceTiles.IndexFor("aghouse"));
        Assert.Equal(0, SpaceTiles.IndexFor(null) - SpaceTiles.IndexFor(""));
    }

    [Fact]
    public void Index_SpreadsNamesOverTheColours()
    {
        var names = new[] { "aghouse", "untold", "note-scion", "cinelytics", "api", "web", "docs", "infra", "Home", "scratch" };
        Assert.True(names.Select(SpaceTiles.IndexFor).Distinct().Count() >= 4);
    }

    [Theory]
    [InlineData("aghouse", "A")]
    [InlineData("note-scion", "N")]
    [InlineData("_private", "P")]
    [InlineData("2fa", "2")]
    [InlineData("ébauche", "É")]
    [InlineData("---", "?")]
    [InlineData("", "?")]
    public void Letter_IsTheFirstLetterOrDigit(string name, string letter) =>
        Assert.Equal(letter, SpaceTiles.LetterFor(name));
}
