using AgHouse.Cli;
using AgHouse.Core;

namespace AgHouse.Tests;

public class AgentGuideTests
{
    [Fact]
    public void Guide_OnlyNamesCommandsThatExist()
    {
        var mentioned = AgentGuide.CommandsMentioned().ToList();
        Assert.Contains("send-text", mentioned);
        Assert.Contains("commands", mentioned);
        Assert.All(mentioned, name => Assert.True(Program.IsCommand(name), $"The guide mentions 'aghouse {name}', which isn't a command."));
    }

    [Fact]
    public void Primer_SaysWhereTheAgentIs()
    {
        var text = AgentGuide.Primer(new PaneContext("p3", "api", "backend", [("p5", "tests")]));
        Assert.Contains("pane p3 (\"api\") of workspace \"backend\"", text);
        Assert.Contains("p5 (\"tests\")", text);
        Assert.Contains("Never send text or keys to your own pane", text);
    }

    [Fact]
    public void Primer_WithoutContext_StillTeaches()
    {
        var text = AgentGuide.Primer(null);
        Assert.StartsWith("You are running inside aghouse", text);
        Assert.DoesNotContain("Other panes", text);
        Assert.Contains("aghouse send-keys", text);
    }

    [Fact]
    public void Primer_FitsClaudeCodesContextLimit()
    {
        // Claude Code caps a hook's additionalContext at 10,000 characters; stay well under it with a busy workspace.
        var many = Enumerable.Range(1, 30).Select(i => ($"p{i}", $"pane number {i}")).ToList();
        Assert.True(AgentGuide.Primer(new PaneContext("p0", "me", "workspace", many)).Length < 6_000);
    }

    [Fact]
    public void Markdown_IsATableAndRules()
    {
        var md = AgentGuide.Markdown();
        Assert.StartsWith("## aghouse", md);
        Assert.Contains("| `aghouse notify --body \"<message>\"` |", md);
        Assert.Contains("AGHOUSE_PANE_ID", md);
    }
}

public class EchoTests
{
    [Theory]
    [InlineData("PS C:\\src> npm test", true)]          // the whole command echoed after the prompt
    [InlineData("PS C:\\src> npm te", true)]            // ConPTY repainting the line while it's typed
    [InlineData("npm test", true)]
    [InlineData("  3 passing (40ms)", false)]
    [InlineData("> app@1.0.0 test", false)]
    [InlineData("PS C:\\src> npm", false)]              // too short a prefix to call it an echo
    public void IsEcho(string line, bool expected)
    {
        Assert.Equal(expected, Program.IsEcho(line, ["npm test"]));
    }

    [Fact]
    public void NothingTyped_NothingIsEcho()
    {
        Assert.False(Program.IsEcho("npm test", []));
    }
}
