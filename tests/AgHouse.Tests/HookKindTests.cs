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
