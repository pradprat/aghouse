using AgHouse.Core.Terminal;

namespace AgHouse.Tests;

public class TitleTextTests
{
    [Theory]
    [InlineData("✳ Casual greeting and check-in", "Casual greeting and check-in")]
    [InlineData("⠋ Fix the build", "Fix the build")]
    [InlineData("⠐  Fix the build", "Fix the build")]
    [InlineData("· Thinking", "Thinking")]
    [InlineData("✶✻ Two frames", "Two frames")]
    [InlineData("🤖 Emoji first", "Emoji first")]
    [InlineData("vim", "vim")]
    [InlineData("  ping -t 127.0.0.1 ", "ping -t 127.0.0.1")]
    [InlineData("Fix ✳ in the middle", "Fix ✳ in the middle")]
    [InlineData("#123 open", "#123 open")]
    [InlineData("\"quoted\" title", "\"quoted\" title")]
    public void Drops_leading_status_glyphs(string title, string expected) =>
        Assert.Equal(expected, TitleText.WithoutStatusGlyphs(title));

    [Fact]
    public void Keeps_a_title_that_is_only_a_glyph() =>
        Assert.Equal("✳", TitleText.WithoutStatusGlyphs(" ✳ "));
}
