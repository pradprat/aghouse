using AgHouse.Core;

namespace AgHouse.Tests;

/// <summary>Which Claude Code hook events are "finished" (they wait behind panes that are asking).</summary>
public class HookKindTests
{
    [Theory]
    [InlineData("Stop", null, null, true)]
    [InlineData("Notification", "idle_prompt", "Claude is waiting for your input", true)]
    [InlineData("Notification", null, "Claude is waiting for your input", true)] // older Claude Code: no notification_type
    [InlineData("Notification", "permission_prompt", "Claude needs your permission to use Bash", false)]
    [InlineData("Notification", null, "Claude needs your permission to use Bash", false)]
    [InlineData("Notification", "elicitation_dialog", "Pick one", false)]
    [InlineData("Notification", null, null, false)]
    [InlineData("SubagentStop", null, null, false)]
    public void IsFinished(string eventName, string? type, string? message, bool finished) =>
        Assert.Equal(finished, ClaudeHooks.IsFinished(eventName, type, message));
}

/// <summary>Hooks that all run aghouse, only another copy of it, are "connected through another copy", not "missing".</summary>
public class HookOtherCopyTests
{
    private static System.Text.Json.Nodes.JsonObject AllEventsRun(string command) =>
        new()
        {
            ["hooks"] = new System.Text.Json.Nodes.JsonObject(ClaudeHooks.Events.Select(e =>
                new KeyValuePair<string, System.Text.Json.Nodes.JsonNode?>(e, System.Text.Json.Nodes.JsonNode.Parse(
                    $$"""[ { "hooks": [ { "type": "command", "command": {{System.Text.Json.JsonSerializer.Serialize(command)}} } ] } ]""")))),
        };

    [Fact]
    public void AnotherCopy_IsNamed_AndStillReaches()
    {
        var status = ClaudeHooks.Inspect(AllEventsRun("C:/Users/me/AppData/Local/Programs/aghouse/aghouse.exe claude-hook"), "D:/dev/aghouse.exe claude-hook");
        Assert.Equal(HookState.Partial, status.State); // connecting still has something to do
        Assert.True(status.Reaches);
        Assert.Equal(@"C:\Users\me\AppData\Local\Programs\aghouse\aghouse.exe", status.OtherCopyPath);
    }

    [Fact]
    public void QuotedPath_LosesItsQuotes()
    {
        var status = ClaudeHooks.Inspect(AllEventsRun("\"C:/Program Files/aghouse/aghouse.exe\" claude-hook"), "D:/dev/aghouse.exe claude-hook");
        Assert.Equal(@"C:\Program Files\aghouse\aghouse.exe", status.OtherCopyPath);
    }

    [Fact]
    public void BareCommand_IsAnotherCopyWithoutAPath()
    {
        var status = ClaudeHooks.Inspect(AllEventsRun(ClaudeHooks.BareCommand), "D:/dev/aghouse.exe claude-hook");
        Assert.NotNull(status.OtherCopy);
        Assert.Null(status.OtherCopyPath);
        Assert.True(status.Reaches);
    }

    [Fact]
    public void SomeEventsMissing_IsPartial_NotAnotherCopy()
    {
        var root = AllEventsRun("C:/x/aghouse.exe claude-hook");
        root["hooks"]!.AsObject().Remove("Stop");
        var status = ClaudeHooks.Inspect(root, "D:/dev/aghouse.exe claude-hook");
        Assert.Equal(HookState.Partial, status.State);
        Assert.Null(status.OtherCopy);
        Assert.False(status.Reaches);
    }

    [Fact]
    public void ThisCopy_IsConnected()
    {
        const string mine = "D:/dev/aghouse.exe claude-hook";
        var status = ClaudeHooks.Inspect(AllEventsRun(mine), mine);
        Assert.Equal(HookState.Connected, status.State);
        Assert.Null(status.OtherCopy);
    }
}

public class ProgramNamesTests
{
    [Theory]
    [InlineData("PING.EXE", "ping")]
    [InlineData("node.exe", "node")]
    [InlineData("Code.exe", "Code")]
    [InlineData("claude.exe", "claude")]
    [InlineData("7z.exe", "7z")]
    [InlineData("python3", "python3")]
    public void Friendly(string image, string expected) => Assert.Equal(expected, ProgramNames.Friendly(image));
}
