namespace AgHouse.Core.Terminal;

/// <summary>
/// The PATH a pane's shell starts with, built from the machine's current PATH rather than the
/// snapshot aghouse was launched with.
///
/// A GUI app inherits the environment of whatever started it — Explorer, a dev shell, an agent —
/// and Windows never updates that copy when something later edits PATH. Install nvm, uv or pnpm
/// after aghouse is open and every pane it spawns is blind to them, even though the registry (and
/// a terminal opened from the Start menu) has them. Composing from machine + user PATH fixes that:
/// the panes get what a freshly opened Windows Terminal would get.
/// </summary>
public static class PanePath
{
    private const string Name = "PATH";

    /// <summary>
    /// The PATH for a pane: <paramref name="appDirectory"/> first, so the aghouse CLI beside the app
    /// wins, then the machine and user PATH from the registry, then whatever else this process
    /// carries (a dev build's extras stay visible, behind the real ones).
    /// </summary>
    public static string ForPane(string appDirectory) =>
        Compose(appDirectory, Read(EnvironmentVariableTarget.Machine), Read(EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable(Name));

    /// <summary>
    /// Joins whole PATH values in order into one. Entries are trimmed and unquoted, empty ones
    /// dropped — an empty entry means "the current directory", which no pane wants — and repeats
    /// (ignoring case) keep only their first position, so the registry's entry beats a stale copy
    /// from this process.
    /// </summary>
    /// <param name="layers">PATH values, any of which may be null or empty.</param>
    public static string Compose(params string?[] layers)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>();
        foreach (var layer in layers)
        {
            if (string.IsNullOrWhiteSpace(layer)) continue;
            foreach (var raw in layer.Split(';'))
            {
                var entry = Normalize(raw);
                if (entry.Length == 0 || !seen.Add(entry)) continue;
                kept.Add(entry);
            }
        }
        return string.Join(';', kept);
    }

    private static string Normalize(string raw)
    {
        var entry = raw.Trim();
        if (entry.Length > 1 && entry[0] == '"' && entry[^1] == '"') entry = entry[1..^1].Trim();
        // "C:\dir\" and "C:\dir" are the same place; without this they survive as two entries.
        // The root of a drive keeps its separator: "C:\" is not "C:".
        while (entry.Length > 3 && entry[^1] == '\\' && entry[^2] != ':') entry = entry[..^1];
        return entry;
    }

    private static string? Read(EnvironmentVariableTarget target)
    {
        try { return Environment.GetEnvironmentVariable(Name, target); }
        catch (PlatformNotSupportedException) { return null; } // Off Windows there is no machine/user PATH.
    }
}
