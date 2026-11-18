namespace AgHouse.Core;

/// <summary>
/// Recognises a coding agent from the programs running under a pane's shell, so the sidebar's Agents
/// list can show one that never reported in: an agent without hooks (OpenCode, Codex), or Claude Code
/// before its first hook call. Matching is by image name, which is enough for agents that ship a native
/// binary. One that runs as a plain node.exe or bun.exe script can't be told from any other script and
/// shows up only once it reports in (hooks, <c>aghouse notify</c>, <c>aghouse set-status agent</c>).
/// </summary>
public static class AgentPrograms
{
    private static readonly Dictionary<string, AgentProgram> Known = new AgentProgram[]
    {
        new("claude", "Claude Code", "Cl"),
        new("opencode", "OpenCode", "Oc"),
        new("codex", "Codex", "Cx"),
        new("gemini", "Gemini CLI", "Gm"),
        new("copilot", "Copilot CLI", "Co"),
        new("cursor-agent", "Cursor Agent", "Cu"),
        new("aider", "Aider", "Ai"),
        new("amp", "Amp", "Am"),
        new("goose", "Goose", "Go"),
        new("crush", "Crush", "Cr"),
        new("qwen", "Qwen Code", "Qw"),
        new("kiro-cli", "Kiro", "Ki"),
        new("droid", "Droid", "Dr"),
        new("auggie", "Auggie", "Au"),
    }.ToDictionary(a => a.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The agent's name for the first image (nearest the shell first) that is a known agent, or null.
    /// "claude.exe" → "Claude Code"; a platform-suffixed build ("codex-x86_64-pc-windows-msvc.exe",
    /// which Codex's npm wrapper starts) counts as its base name.
    /// </summary>
    public static string? NameOf(IEnumerable<string> images) => Find(images)?.Name;

    /// <summary><see cref="NameOf"/> with the agent's key ("claude") and monogram ("Cl"), for its mark.</summary>
    public static AgentProgram? Find(IEnumerable<string> images)
    {
        foreach (var image in images)
        {
            var stem = image.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? image[..^4] : image;
            if (Known.TryGetValue(stem, out var agent)) return agent;
            var dash = stem.IndexOf('-');
            while (dash > 0)
            {
                // "cursor-agent" is itself a name, so try every prefix that ends at a dash, longest last.
                if (Known.TryGetValue(stem[..dash], out agent) && LooksLikePlatformSuffix(stem[(dash + 1)..])) return agent;
                dash = stem.IndexOf('-', dash + 1);
            }
        }
        return null;
    }

    /// <summary>"x86_64-pc-windows-msvc", "aarch64-pc-windows-msvc", "win32-x64": a build target, not a different program.</summary>
    private static bool LooksLikePlatformSuffix(string rest) =>
        rest.Contains("windows", StringComparison.OrdinalIgnoreCase)
        || rest.StartsWith("win", StringComparison.OrdinalIgnoreCase)
        || rest.StartsWith("x86_64", StringComparison.OrdinalIgnoreCase)
        || rest.StartsWith("aarch64", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The title starts with a braille spinner frame ("⠋ Fix the build"): Claude Code and other agents
    /// animate their terminal title this way while they work, and show a still glyph ("✳") at rest.
    /// </summary>
    public static bool TitleShowsWork(string? title)
    {
        if (string.IsNullOrEmpty(title)) return false;
        foreach (var rune in title.TrimStart().EnumerateRunes())
            return rune.Value is >= 0x2801 and <= 0x28FF; // U+2800 is the blank pattern, used as a pause frame
        return false;
    }
}

/// <summary>
/// A known agent: <paramref name="Key"/> is its program's image name without ".exe" ("claude"), which the
/// app uses to pick its mark; <paramref name="Monogram"/> is two letters for agents without a drawn mark.
/// </summary>
public sealed record AgentProgram(string Key, string Name, string Monogram);
