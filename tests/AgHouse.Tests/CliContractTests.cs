using System.Text.RegularExpressions;
using AgHouse.Cli;

namespace AgHouse.Tests;

/// <summary>The CLI surface that 1.0 promises to keep: which commands exist and which options each takes.</summary>
public class CliContractTests
{
    [Fact]
    public void EveryCommand_RejectsAMisspeltOption()
    {
        Assert.All(Program.AllCommands, c => Assert.Equal("pnae", Program.UnknownOption(c, ["pnae"])));
    }

    [Fact]
    public void EveryCommand_TakesHelpAndJson()
    {
        Assert.All(Program.AllCommands, c => Assert.Null(Program.UnknownOption(c, ["help", "h", "json"])));
    }

    [Fact]
    public void EveryCommand_TakesTheOptionsItsSynopsisShows()
    {
        foreach (var c in Program.AllCommands)
        {
            var shown = Regex.Matches(c.Synopsis, @"--([a-z][a-z-]*)").Select(m => m.Groups[1].Value).ToList();
            Assert.Null(Program.UnknownOption(c, shown));
        }
    }

    [Theory]
    // Options that work without being in the synopsis: scripts written before 1.0 use them.
    [InlineData("select-workspace", "workspace")]
    [InlineData("rename-workspace", "name", "workspace")]
    [InlineData("close-workspace", "workspace")]
    [InlineData("new-split", "direction", "pane", "profile", "cwd")]
    [InlineData("focus-pane", "pane")]
    [InlineData("close-pane", "pane")]
    [InlineData("close-tab", "pane")]
    [InlineData("zoom-pane", "pane")]
    [InlineData("move-pane", "side", "to", "pane")]
    [InlineData("restart-pane", "pane", "profile", "cwd")]
    [InlineData("send-text", "text", "pane", "enter", "wait-for", "timeout", "ignore-case")]
    [InlineData("set-status", "pane", "workspace")]
    [InlineData("clear-status", "pane", "workspace")]
    [InlineData("set-progress", "pane", "label", "workspace")]
    [InlineData("log", "pane", "level", "workspace")]
    [InlineData("notify", "title", "body", "pane", "finished")]
    [InlineData("hooks", "yes")]
    public void Command_TakesItsOptions(string name, params string[] options)
    {
        var c = Program.CommandNamed(name);
        Assert.NotNull(c);
        Assert.Null(Program.UnknownOption(c, options));
    }

    [Fact]
    public void OptionNames_IgnoreCase()
    {
        Assert.Null(Program.UnknownOption(Program.CommandNamed("send-text")!, ["Enter", "PANE"]));
    }

    [Theory]
    [InlineData("list", "pane")]          // list has no pane to aim at
    [InlineData("waiting", "workspace")]
    [InlineData("quit", "yes")]           // it's --force
    [InlineData("send-keys", "enter")]    // send-keys enter, not --enter
    public void Command_RejectsOptionsItDoesntTake(string name, string option)
    {
        Assert.Equal(option, Program.UnknownOption(Program.CommandNamed(name)!, [option]));
    }

    [Fact]
    public void CheckUpdate_ReplacesUpdate()
    {
        Assert.True(Program.IsCommand("check-update"));
        Assert.False(Program.IsCommand("update"));
    }
}
