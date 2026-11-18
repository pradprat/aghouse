using AgHouse.Core;
using Xunit;

namespace AgHouse.Tests;

public class AgentProgramsTests
{
    [Theory]
    [InlineData("claude.exe", "Claude Code")]
    [InlineData("CLAUDE.EXE", "Claude Code")]
    [InlineData("opencode.exe", "OpenCode")]
    [InlineData("codex.exe", "Codex")]
    [InlineData("codex-x86_64-pc-windows-msvc.exe", "Codex")]
    [InlineData("cursor-agent.exe", "Cursor Agent")]
    public void Names_a_known_agent(string image, string expected) =>
        Assert.Equal(expected, AgentPrograms.NameOf([image]));

    [Theory]
    [InlineData("node.exe")]
    [InlineData("vim.exe")]
    [InlineData("claudette.exe")]
    [InlineData("codex-helper.exe")]
    public void Ignores_other_programs(string image) =>
        Assert.Null(AgentPrograms.NameOf([image]));

    [Fact]
    public void Nearest_known_agent_wins_and_its_children_dont_hide_it() =>
        // Claude Code under the shell, with its MCP servers (node) under it.
        Assert.Equal("Claude Code", AgentPrograms.NameOf(["claude.exe", "node.exe", "opencode.exe"]));

    [Fact]
    public void Find_gives_the_key_and_monogram_for_the_mark()
    {
        var codex = AgentPrograms.Find(["codex-x86_64-pc-windows-msvc.exe"]);
        Assert.Equal(new AgentProgram("codex", "Codex", "Cx"), codex);
        Assert.Equal("claude", AgentPrograms.Find(["claude.exe"])?.Key);
    }

    [Fact]
    public void Nothing_running_is_no_agent() => Assert.Null(AgentPrograms.NameOf([]));

    [Theory]
    [InlineData("⠋ Fix the build", true)]
    [InlineData("  ⠹ Fix the build", true)]
    [InlineData("✳ Fix the build", false)]
    [InlineData("Claude Code", false)]
    [InlineData("⠀ blank frame", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Spinner_title_means_working(string? title, bool expected) =>
        Assert.Equal(expected, AgentPrograms.TitleShowsWork(title));
}
