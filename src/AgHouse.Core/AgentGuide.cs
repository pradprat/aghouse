using System.Text;

namespace AgHouse.Core;

/// <summary>Where an agent is running: its pane, its workspace and the panes next to it.</summary>
public sealed record PaneContext(string PaneId, string? PaneName, string? Workspace, IReadOnlyList<(string Id, string Name)> OtherPanes);

/// <summary>
/// What a coding agent needs to know to use aghouse, in one place. Claude Code gets
/// <see cref="Primer"/> when a session starts in a pane (the SessionStart hook); other agents get
/// <see cref="Markdown"/> from <c>aghouse agent-guide</c>, to put in AGENTS.md or CLAUDE.md.
/// Kept short on purpose: <c>aghouse commands --json</c> has the rest.
/// </summary>
public static class AgentGuide
{
    public sealed record Entry(string Command, string What);

    public static IReadOnlyList<Entry> Essentials { get; } =
    [
        new("aghouse new-split right [--cwd DIR]", "open another shell beside yours; prints its pane id"),
        new("aghouse new-tab", "open another shell as a tab of your pane; prints its pane id"),
        new("aghouse send-text \"<command>\" --enter --pane ID --wait-for \"<regex>\" --timeout 300",
            "run a command in that pane and wait for output matching regex (exit 3 on timeout)"),
        new("aghouse read-pane --pane ID [--lines N]", "what a pane printed, as plain text"),
        new("aghouse send-keys ctrl+c --pane ID", "press keys: ctrl+c, enter, esc, up, tab, y ..."),
        new("aghouse close-pane ID", "close a pane you opened"),
        new("aghouse notify --body \"<message>\"", "tell the user you need them (marks your pane; a toast if they're away)"),
        new("aghouse set-status <key> <text>", "a short status chip on your workspace's sidebar row"),
        new("aghouse list", "every workspace and pane (JSON)"),
    ];

    public static IReadOnlyList<string> Rules { get; } =
    [
        "--pane defaults to your own pane. Never send text or keys to your own pane: that types into you. Open another pane for commands you want to watch.",
        "Your own command is echoed into the pane's output, so wait for something only the output prints, or anchor the pattern ('^done$').",
        "Leave panes you didn't open alone unless the user asks: they may hold the user's work or other agents.",
        "Full-screen programs (vim, other agents) read back as fragments; read-pane suits ordinary command output.",
        "'aghouse commands --json' lists everything else (settings, app actions, renaming, restarting, quit); 'aghouse help <command>' explains one.",
    ];

    /// <summary>The short text Claude Code gets at the start of a session in an aghouse pane.</summary>
    public static string Primer(PaneContext? here)
    {
        var sb = new StringBuilder();
        sb.Append("You are running inside aghouse, a terminal app for coding agents");
        if (here is not null)
        {
            sb.Append($", in pane {here.PaneId}");
            if (!string.IsNullOrWhiteSpace(here.PaneName)) sb.Append($" (\"{here.PaneName}\")");
            if (!string.IsNullOrWhiteSpace(here.Workspace)) sb.Append($" of workspace \"{here.Workspace}\"");
        }
        sb.AppendLine(".");
        if (here is { OtherPanes.Count: > 0 })
        {
            sb.AppendLine("Other panes in this workspace: "
                + string.Join(", ", here.OtherPanes.Select(p => $"{p.Id} (\"{p.Name}\")")) + ".");
        }
        sb.AppendLine("Its CLI, `aghouse`, is on PATH. Use it to open shells, run and watch commands in them, and tell the user when you need them:");
        foreach (var e in Essentials) sb.AppendLine($"- `{e.Command}`: {e.What}");
        sb.AppendLine("Rules:");
        foreach (var rule in Rules) sb.AppendLine("- " + rule);
        return Unix(sb);
    }

    private static string Unix(StringBuilder sb) => sb.ToString().Replace("\r\n", "\n").TrimEnd();

    /// <summary>The same guidance as Markdown, for AGENTS.md / CLAUDE.md (<c>aghouse agent-guide</c>).</summary>
    public static string Markdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("## aghouse");
        sb.AppendLine();
        sb.AppendLine("When `AGHOUSE_PANE_ID` is set, you are running in a pane of aghouse, a terminal app for coding agents. " +
                      "Its CLI, `aghouse`, is on PATH. Use it to open shells, run and watch commands in them, and tell the user when you need them.");
        sb.AppendLine();
        sb.AppendLine("| Command | What it does |");
        sb.AppendLine("|---|---|");
        foreach (var e in Essentials) sb.AppendLine($"| `{e.Command}` | {e.What} |");
        sb.AppendLine();
        foreach (var rule in Rules) sb.AppendLine("- " + rule);
        return Unix(sb);
    }

    /// <summary>The aghouse commands the guide tells agents to run (the word after "aghouse").</summary>
    public static IEnumerable<string> CommandsMentioned() =>
        Essentials.Select(e => e.Command)
            .Concat(Rules)
            .SelectMany(text => System.Text.RegularExpressions.Regex.Matches(text, @"aghouse ([a-z][a-z-]*)").Select(m => m.Groups[1].Value))
            .Distinct();
}
