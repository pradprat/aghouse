using System.Collections;

namespace AgHouse.Core.Terminal;

/// <summary>
/// Variables a pane's shell must not inherit from whatever launched aghouse. Started from a
/// Claude Code tool shell (a dev loop does this), the app carries that session's markers and
/// the NO_COLOR it sets for its tools, so a Claude Code started in a pane would render without
/// color and believe it is a child session. Only the markers go; user settings such as
/// CLAUDE_CODE_USE_BEDROCK stay.
/// </summary>
public static class InheritedEnv
{
    /// <summary>Set by Claude Code for the processes it runs.</summary>
    public static readonly IReadOnlyList<string> ClaudeSessionMarkers =
    [
        "CLAUDECODE",
        "CLAUDE_CODE_CHILD_SESSION",
        "CLAUDE_CODE_SESSION_ID",
        "CLAUDE_CODE_SESSION_ATTENDED",
        "CLAUDE_CODE_ENTRYPOINT",
        "CLAUDE_CODE_EXECPATH",
        "CLAUDE_CODE_MESSAGING_SOCKET",
        "CLAUDE_CODE_MESSAGING_TOKEN",
        "CLAUDE_PID",
        "CLAUDE_EFFORT",
        "AI_AGENT",
    ];

    /// <summary>
    /// The names to unset in a pane, given the app's own environment. NO_COLOR is only dropped
    /// when it came in with a Claude Code session: set by the user, it is theirs to keep.
    /// </summary>
    public static IReadOnlyList<string> ToUnset(IDictionary current)
    {
        var present = ClaudeSessionMarkers.Where(current.Contains).ToList();
        if (current.Contains("CLAUDECODE") && current.Contains("NO_COLOR")) present.Add("NO_COLOR");
        return present;
    }
}
