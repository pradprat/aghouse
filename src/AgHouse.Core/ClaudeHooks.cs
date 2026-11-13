using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgHouse.Core;

public enum HookState
{
    /// <summary>Every event aghouse uses runs aghouse claude-hook.</summary>
    Connected,
    /// <summary>Some events do, some don't (an older snippet, or a hand edit).</summary>
    Partial,
    NotConnected,
    /// <summary>~/.claude/settings.json exists but isn't valid JSON; aghouse won't touch it.</summary>
    Unreadable,
}

/// <summary>
/// Claude Code's hooks in ~/.claude/settings.json: whether aghouse's are there, and adding them.
/// Adding only ever appends aghouse's own entry to an event's list. Every other setting and hook
/// stays exactly as it was, and a copy of the file is saved first.
/// </summary>
public static class ClaudeHooks
{
    public const string Command = "aghouse claude-hook";

    /// <summary>Notification and Stop raise "Needs you"; UserPromptSubmit and PostToolUse show "Working".</summary>
    public static readonly IReadOnlyList<string> Events = ["Notification", "Stop", "UserPromptSubmit", "PostToolUse"];

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    private static readonly JsonDocumentOptions ReadOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public sealed record Status(HookState State, IReadOnlyList<string> Missing, string? Problem);

    public static Status Inspect(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return new Status(HookState.NotConnected, Events, null);
            return Inspect(JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) as JsonObject);
        }
        catch (JsonException ex) { return new Status(HookState.Unreadable, Events, ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new Status(HookState.Unreadable, Events, ex.Message); }
    }

    public static Status Inspect(JsonObject? root)
    {
        if (root is null) return new Status(HookState.Unreadable, Events, "The file doesn't hold a JSON object.");
        var missing = Events.Where(e => !HasOurHook(root, e)).ToList();
        var state = missing.Count == 0 ? HookState.Connected : missing.Count == Events.Count ? HookState.NotConnected : HookState.Partial;
        return new Status(state, missing, null);
    }

    /// <summary>Adds aghouse's hook to each event that lacks it. Returns the events it added.</summary>
    public static IReadOnlyList<string> Merge(JsonObject root)
    {
        if (root["hooks"] is not JsonObject hooks)
        {
            if (root["hooks"] is not null) throw new InvalidOperationException("\"hooks\" in Claude Code's settings isn't an object.");
            hooks = [];
            root["hooks"] = hooks;
        }
        var added = new List<string>();
        foreach (var name in Events)
        {
            if (HasOurHook(root, name)) continue;
            if (hooks[name] is not JsonArray list)
            {
                if (hooks[name] is not null) throw new InvalidOperationException($"\"hooks.{name}\" in Claude Code's settings isn't a list.");
                list = [];
                hooks[name] = list;
            }
            list.Add(new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = Command }),
            });
            added.Add(name);
        }
        return added;
    }

    /// <summary>
    /// Adds the hooks to the file, creating it if needed. An existing file is copied to
    /// settings.json.aghouse-backup first. Refuses (throws) when the file can't be read as JSON.
    /// Returns the events added and the backup's path (null when there was no file).
    /// </summary>
    public static (IReadOnlyList<string> Added, string? Backup) Install(string? path = null)
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

        var added = Merge(root);
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

    private static bool HasOurHook(JsonObject root, string eventName) =>
        root["hooks"]?[eventName] is JsonArray entries
        && entries.OfType<JsonObject>().Any(entry =>
            entry["hooks"] is JsonArray commands
            && commands.OfType<JsonObject>().Any(c =>
                c["command"] is JsonValue v && v.TryGetValue<string>(out var cmd)
                && cmd.Contains(Command, StringComparison.OrdinalIgnoreCase)));
}
