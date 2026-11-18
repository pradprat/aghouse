using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AgHouse.Core;

public enum HookState
{
    /// <summary>Every event aghouse uses runs aghouse claude-hook.</summary>
    Connected,
    /// <summary>Some events do, some don't, or some run an older command (an older snippet, or a hand edit).</summary>
    Partial,
    NotConnected,
    /// <summary>~/.claude/settings.json exists but isn't valid JSON; aghouse won't touch it.</summary>
    Unreadable,
}

/// <summary>
/// Claude Code's hooks in ~/.claude/settings.json: whether aghouse's are there, and adding them.
/// Adding only ever appends aghouse's own entry to an event's list, or updates aghouse's own
/// command in place. Every other setting and hook stays exactly as it was, and a copy of the file
/// is saved first.
/// </summary>
public static partial class ClaudeHooks
{
    /// <summary>The bare command older versions wrote. Still recognised, and upgraded by <see cref="Install"/>.</summary>
    public const string BareCommand = "aghouse claude-hook";

    /// <summary>
    /// The command aghouse writes: the full path of the aghouse.exe next to this program, so the hook
    /// never runs whatever "aghouse" a project folder or PATH entry happens to hold (cmd.exe looks in
    /// the current folder first). Forward slashes and no quotes when the path has no spaces, so Git
    /// Bash, cmd and PowerShell all run it. Falls back to <see cref="BareCommand"/> when there is no
    /// aghouse.exe beside us (a dev build of the tests).
    /// </summary>
    public static string Command { get; } = CommandFor(Path.Combine(AppContext.BaseDirectory, "aghouse.exe"));

    public static string CommandFor(string exePath)
    {
        if (!File.Exists(exePath)) return BareCommand;
        var path = Path.GetFullPath(exePath).Replace('\\', '/');
        return (path.Contains(' ') ? $"\"{path}\"" : path) + " claude-hook";
    }

    /// <summary>
    /// Notification and Stop raise "Needs you"; UserPromptSubmit and PostToolUse show "Working";
    /// SessionStart tells Claude it is in aghouse and how to use the CLI (<see cref="AgentGuide"/>).
    /// </summary>
    public static readonly IReadOnlyList<string> Events = ["Notification", "Stop", "UserPromptSubmit", "PostToolUse", "SessionStart"];

    /// <summary>
    /// A hook event that says Claude is done rather than asking something: Stop, or the Notification Claude
    /// sends after sitting idle at its prompt (notification_type "idle_prompt"; older versions only say
    /// "Claude is waiting for your input"). A permission prompt or a question is asking.
    /// </summary>
    public static bool IsFinished(string? eventName, string? notificationType, string? message) =>
        eventName == "Stop"
        || (eventName == "Notification"
            && (notificationType == "idle_prompt"
                || (notificationType is null && message?.Trim() == "Claude is waiting for your input")));

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    private static readonly JsonDocumentOptions ReadOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// <paramref name="Missing"/>: the events that don't run <see cref="Command"/> yet (absent, or an older command).
    /// <paramref name="OtherCopy"/>: every event already runs an aghouse command, just not this copy's (another
    /// install, a dev build, the portable zip, or the older bare "aghouse claude-hook"). Notifications still arrive,
    /// because every copy talks to the same pipe; State stays <see cref="HookState.Partial"/>, and connecting
    /// points the hooks at this copy.
    /// </summary>
    public sealed record Status(HookState State, IReadOnlyList<string> Missing, string? Problem, string? OtherCopy = null)
    {
        /// <summary>Claude Code reaches aghouse, through this copy or another.</summary>
        public bool Reaches => State == HookState.Connected || OtherCopy is not null;

        /// <summary>The other copy's aghouse.exe path, or null for the bare "aghouse claude-hook" (whatever is on PATH).</summary>
        public string? OtherCopyPath => OtherCopy is null || OtherCopy.Equals(BareCommand, StringComparison.OrdinalIgnoreCase) ? null
            : Regex.Replace(OtherCopy, @"\s+claude-hook\s*$", "", RegexOptions.IgnoreCase).Trim().Trim('"', '\'').Replace('/', '\\');
    }

    public static Status Inspect(string? path = null, string? command = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return new Status(HookState.NotConnected, Events, null);
            return Inspect(JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) as JsonObject, command);
        }
        catch (JsonException ex) { return new Status(HookState.Unreadable, Events, ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new Status(HookState.Unreadable, Events, ex.Message); }
    }

    public static Status Inspect(JsonObject? root, string? command = null)
    {
        command ??= Command;
        if (root is null) return new Status(HookState.Unreadable, Events, "The file doesn't hold a JSON object.");
        var missing = Events.Where(e => !OurCommands(root, e).Any(c => c.GetValue<string>() == command)).ToList();
        var none = Events.All(e => !OurCommands(root, e).Any());
        var state = missing.Count == 0 ? HookState.Connected : none ? HookState.NotConnected : HookState.Partial;
        // Every event runs aghouse, only not this copy: say which copy, not "missing".
        var other = missing.Count > 0 && Events.All(e => OurCommands(root, e).Any())
            ? OurCommands(root, missing[0]).Select(c => c.GetValue<string>()).First(c => c != command).Trim()
            : null;
        return new Status(state, missing, null, other);
    }

    /// <summary>
    /// Makes each event run <paramref name="command"/> (default <see cref="Command"/>): adds aghouse's
    /// hook where it lacks one, and rewrites an older aghouse command in place. Returns the events changed.
    /// </summary>
    public static IReadOnlyList<string> Merge(JsonObject root, string? command = null)
    {
        command ??= Command;
        if (root["hooks"] is not JsonObject hooks)
        {
            if (root["hooks"] is not null) throw new InvalidOperationException("\"hooks\" in Claude Code's settings isn't an object.");
            hooks = [];
            root["hooks"] = hooks;
        }
        var changed = new List<string>();
        foreach (var name in Events)
        {
            var ours = OurCommands(root, name).ToList();
            if (ours.Count > 0)
            {
                var stale = ours.Where(v => v.GetValue<string>() != command).ToList();
                foreach (var v in stale) v.ReplaceWith(JsonValue.Create(command));
                if (stale.Count > 0) changed.Add(name);
                continue;
            }
            if (hooks[name] is not JsonArray list)
            {
                if (hooks[name] is not null) throw new InvalidOperationException($"\"hooks.{name}\" in Claude Code's settings isn't a list.");
                list = [];
                hooks[name] = list;
            }
            list.Add(new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command }),
            });
            changed.Add(name);
        }
        return changed;
    }

    /// <summary>
    /// Adds the hooks to the file (or updates an older aghouse command), creating it if needed. An
    /// existing file is copied to settings.json.aghouse-backup first. Refuses (throws) when the file
    /// can't be read as JSON. Returns the events changed and the backup's path (null when there was no file).
    /// </summary>
    public static (IReadOnlyList<string> Added, string? Backup) Install(string? path = null, string? command = null)
    {
        path ??= DefaultPath;
        JsonObject root;
        string? backup = null;
        if (File.Exists(path))
        {
            root = JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) as JsonObject
                   ?? throw new InvalidOperationException("Claude Code's settings.json doesn't hold a JSON object.");
        }
        else root = [];

        var added = Merge(root, command);
        if (added.Count == 0) return (added, null);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            backup = path + ".aghouse-backup";
            File.Copy(path, backup, overwrite: true);
        }
        var tmp = path + ".aghouse-tmp";
        File.WriteAllText(tmp, root.ToJsonString(WriteOptions) + Environment.NewLine);
        File.Move(tmp, path, overwrite: true);
        return (added, backup);
    }

    /// <summary>"aghouse claude-hook", "aghouse.exe claude-hook", or a path ending in aghouse(.exe), quoted or not.</summary>
    [GeneratedRegex("""(^|[\s"'/\\])aghouse(\.exe)?["']?\s+claude-hook(\s|$)""", RegexOptions.IgnoreCase)]
    private static partial Regex OurCommand();

    public static bool IsOurCommand(string command) => OurCommand().IsMatch(command);

    /// <summary>The "command" values of aghouse's hooks for one event.</summary>
    private static IEnumerable<JsonValue> OurCommands(JsonObject root, string eventName) =>
        root["hooks"]?[eventName] is JsonArray entries
            ? entries.OfType<JsonObject>()
                .SelectMany(entry => entry["hooks"] is JsonArray commands ? commands.OfType<JsonObject>() : [])
                .Select(c => c["command"] as JsonValue)
                .Where(v => v is not null && v.TryGetValue<string>(out var cmd) && IsOurCommand(cmd))
                .Select(v => v!)
            : [];
}
