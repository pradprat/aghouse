using System.Text.Json.Nodes;
using AgHouse.Core.Themes;

namespace AgHouse.Tests;

public class ThemeTests
{
    public static IEnumerable<object[]> BuiltIns() => BuiltInSchemes.All.Select(s => new object[] { s.Name });

    private static ColorScheme Named(string name) => BuiltInSchemes.All.Single(s => s.Name == name);

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void BuiltIn_AccentIsItsOwnAndReads(string name)
    {
        var s = Named(name);
        var chrome = new ChromePalette(s);
        Assert.Equal(16, s.Table.Count);
        // The declared accent survives the 3:1 checks, so no built-in falls back to blue or grey.
        Assert.Equal(s.Accent, chrome.Accent);
        Assert.True(Rgb.Contrast(chrome.Accent, chrome.Surface) >= 3.0, $"{name}: accent on window");
        Assert.True(Rgb.Contrast(chrome.Accent, s.Background) >= 3.0, $"{name}: accent on terminal");
    }

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void BuiltIn_ChromeTextReads(string name)
    {
        var chrome = new ChromePalette(Named(name));
        Assert.True(Rgb.Contrast(chrome.Ink, chrome.Surface) >= 4.5, $"{name}: primary text");
        var secondary = Rgb.Over(chrome.Ink, chrome.SecondaryOpacity, chrome.Surface);
        Assert.True(Rgb.Contrast(secondary, chrome.Surface) >= 4.5, $"{name}: secondary text");
    }

    [Fact]
    public void BuiltIns_HaveBothSidesAndUniqueNames()
    {
        Assert.Equal(9, BuiltInSchemes.All.Count(s => !s.IsLight));
        Assert.Equal(6, BuiltInSchemes.All.Count(s => s.IsLight));
        Assert.Equal(BuiltInSchemes.All.Count, BuiltInSchemes.All.Select(s => s.Name.ToLowerInvariant()).Distinct().Count());
        Assert.False(BuiltInSchemes.WindowsDark.IsLight);
        Assert.True(BuiltInSchemes.WindowsLight.IsLight);
    }

    [Fact]
    public void Surface_StepsFromTerminalTowardText()
    {
        var chrome = new ChromePalette(Named("Dracula"));
        Assert.NotEqual(Named("Dracula").Background, chrome.Surface);
        Assert.True(Rgb.Luminance(chrome.Surface) > Rgb.Luminance(Named("Dracula").Background)); // dark: a step lighter
        Assert.True(Rgb.Luminance(chrome.Flyout) > Rgb.Luminance(chrome.Surface));               // flyouts one more

        var latte = new ChromePalette(Named("Catppuccin Latte"));
        Assert.True(Rgb.Luminance(latte.Surface) < Rgb.Luminance(Named("Catppuccin Latte").Background)); // light: a step darker
    }

    [Fact]
    public void RemapNeutral_MovesFluentGreysOntoTheScheme_AndLeavesColourAlone()
    {
        var chrome = new ChromePalette(Named("Nord"));
        // Fluent dark: window #202020, text white. White becomes the ink, the base becomes the surface.
        Assert.Equal(Rgb.WithAlpha(chrome.Ink, 0xFF), chrome.RemapNeutral(0xFFFFFFFF, 0x202020));
        Assert.Equal(Rgb.WithAlpha(chrome.Surface, 0xFF), chrome.RemapNeutral(0xFF202020, 0x202020));
        // A 6% white hover fill keeps its 6%.
        Assert.Equal(0x0Fu, Rgb.A(chrome.RemapNeutral(0x0FFFFFFF, 0x202020)));
        // Caution yellow isn't grey: unchanged.
        Assert.Equal(0xFFFCE100u, chrome.RemapNeutral(0xFFFCE100, 0x202020));
    }

    [Fact]
    public void Accent_FallsBackPastGreyCursorToBlue_ThenToText()
    {
        var table = Named("Campbell").Table.ToArray();
        var noAccent = new ColorScheme("X", 0x0C0C0C, 0xCCCCCC, 0xFFFFFF, 0x264F78, table, null, SchemeSource.Custom);
        Assert.Equal(table[12], new ChromePalette(noAccent).Accent); // white cursor skipped, bright blue used

        var unreadable = noAccent with { Accent = 0x101010, Cursor = 0x111111, Table = Enumerable.Repeat(0x0D0D0Du, 16).ToArray() };
        var chrome = new ChromePalette(unreadable);
        Assert.True(Rgb.Contrast(chrome.Accent, chrome.Surface) >= 3.0);
    }

    [Fact]
    public void Parse_ReadsWindowsTerminalShape()
    {
        var json = SchemeJson.ToJson(Named("Gruvbox Dark"));
        json.Remove("accent");
        json["name"] = "My Gruvbox";
        var s = SchemeJson.Parse(json, out var error);
        Assert.Null(error);
        Assert.Equal("My Gruvbox", s!.Name);
        Assert.Equal(SchemeSource.Custom, s.Source);
        Assert.Equal(Named("Gruvbox Dark").Table, s.Table);
        Assert.Null(s.Accent);
    }

    [Theory]
    [InlineData("brightCyan", null, "has no \"brightCyan\"")]
    [InlineData("background", "blue", "isn't a #RRGGBB colour")]
    [InlineData("name", "", "has no \"name\"")]
    public void Parse_SaysWhatsWrong(string key, string? value, string expected)
    {
        var json = SchemeJson.ToJson(Named("Nord"));
        json["name"] = "Mine";
        if (value is null) json.Remove(key); else json[key] = value;
        Assert.Null(SchemeJson.Parse(json, out var error));
        Assert.Contains(expected, error);
    }

    [Fact]
    public void Catalog_SkipsBrokenAndDuplicateCustomSchemes_AndFallsBackToWindows()
    {
        var good = SchemeJson.ToJson(Named("Nord")); good["name"] = "Mine";
        var twice = good.DeepClone();
        var builtInName = SchemeJson.ToJson(Named("Nord"));
        var broken = new JsonObject { ["name"] = "Broken" };
        var catalog = new ThemeCatalog([good, twice, builtInName, broken]);

        Assert.Single(catalog.Custom);
        Assert.Equal(3, catalog.Problems.Count);
        Assert.Equal("Mine", catalog.Find("mine", light: false)?.Name);
        Assert.Contains(catalog.For(light: false), s => s.Name == "Mine");
        Assert.DoesNotContain(catalog.For(light: true), s => s.Name == "Mine");
        Assert.Same(BuiltInSchemes.WindowsLight, catalog.Resolve("Gone", light: true));
        Assert.Same(BuiltInSchemes.WindowsDark, catalog.Find("windows", light: false));
    }

    [Fact]
    public void Import_CopiesNewSchemesFromJsonc_AndSkipsKnownOnes()
    {
        var dir = Directory.CreateTempSubdirectory("aghouse-wt-");
        try
        {
            var mine = SchemeJson.ToJson(Named("Tokyo Night")); mine["name"] = "Night Owl";
            var dupe = SchemeJson.ToJson(Named("Tokyo Night")); dupe["name"] = "Already Here";
            var path = Path.Combine(dir.FullName, "settings.json");
            File.WriteAllText(path, $$"""
                // Windows Terminal writes comments and trailing commas
                {
                    "profiles": { "list": [] },
                    "schemes": [ {{mine.ToJsonString()}}, {{dupe.ToJsonString()}}, { "name": "Broken" }, ],
                }
                """);
            var existing = SchemeJson.ToJson(Named("Nord")); existing["name"] = "Already Here";
            JsonArray custom = [existing];

            var result = ThemeCatalog.ImportWindowsTerminal(custom, [path, Path.Combine(dir.FullName, "missing.json")]);

            Assert.True(result.FoundSettings);
            Assert.Equal(["Night Owl"], result.Added);
            Assert.Equal(2, result.Skipped);
            Assert.Equal(2, custom.Count);
            Assert.Contains("Imported 1 scheme: Night Owl", result.Summary);
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Import_SaysWhenWindowsTerminalIsntThere()
    {
        var result = ThemeCatalog.ImportWindowsTerminal([], [Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")]);
        Assert.False(result.FoundSettings);
        Assert.Equal("No Windows Terminal settings found.", result.Summary);
    }
}
