using System.Globalization;
using System.Text.Json.Nodes;

namespace AgHouse.Core.Settings;

public enum SettingType { String, Int, Bool, Choice }

/// <summary>One key of settings.json that people (and <c>aghouse settings set</c>) may change.</summary>
public sealed record SettingKey(
    string Name,
    SettingType Type,
    string Description,
    JsonNode? Default,
    IReadOnlyList<string>? Choices = null,
    int Min = 0,
    int Max = 0)
{
    /// <summary>"true|false", "system|dark|light", "6..48", or "text".</summary>
    public string ValueHint => Type switch
    {
        SettingType.Bool => "true|false",
        SettingType.Choice => string.Join('|', Choices ?? []),
        SettingType.Int => $"{Min}..{Max}",
        _ => "text",
    };
}

/// <summary>
/// The settings a script may read and change, how to spell them, and how to read a value typed
/// on a command line. The names are the JSON keys in settings.json.
/// </summary>
public static class SettingKeys
{
    public const int MinFontSize = 6;
    public const int MaxFontSize = 48;

    public static IReadOnlyList<SettingKey> All { get; } =
    [
        new("defaultProfile", SettingType.String,
            "Shell for new workspaces (a profile id from 'aghouse profiles'). Unset: the first shell found.", null),
        new("fontFamily", SettingType.String, "Terminal font.", "Cascadia Mono"),
        new("fontSize", SettingType.Int, "Terminal font size in points.", 11, Min: MinFontSize, Max: MaxFontSize),
        new("theme", SettingType.Choice, "Colours of the app and terminals.", "system", Choices: ["system", "dark", "light"]),
        new("restoreSession", SettingType.Bool, "Reopen the last workspaces, splits and folders at launch.", true),
        new("toasts", SettingType.Bool, "Show a Windows notification when a pane needs you and aghouse isn't in front.", true),
        new("flashTaskbar", SettingType.Bool, "Flash the taskbar button when a pane needs you and aghouse isn't in front.", true),
        new("ctrlDSplits", SettingType.Bool, "Ctrl+D splits right. Off gives Ctrl+D (end of input) back to the terminal.", true),
        new("warnMultilinePaste", SettingType.Bool, "Ask before a paste whose line breaks would run commands.", true),
        new("updates", SettingType.Choice,
            "New versions from GitHub Releases. auto: download in the background and install when aghouse quits. "
            + "notify: only say one is out. off: never check by itself.", "auto", Choices: ["auto", "notify", "off"]),
    ];

    /// <summary>The key, whatever the spelling: fontSize, font-size, font_size and FONTSIZE all work. Null if unknown.</summary>
    public static SettingKey? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var bare = Bare(name);
        return All.FirstOrDefault(k => Bare(k.Name) == bare);
    }

    /// <summary>Like <see cref="Find"/>, but an unknown key throws with the list of known ones.</summary>
    public static SettingKey Require(string? name) =>
        Find(name) ?? throw new ArgumentException(
            $"Unknown setting '{name}'. Settings: {string.Join(", ", All.Select(k => k.Name))}.");

    /// <summary>
    /// The JSON value for <paramref name="raw"/> as typed on a command line. Throws
    /// <see cref="ArgumentException"/> with a message fit to show when it isn't a valid value.
    /// </summary>
    public static JsonNode? Parse(SettingKey key, string? raw)
    {
        var text = raw?.Trim() ?? "";
        switch (key.Type)
        {
            case SettingType.Bool:
                return text.ToLowerInvariant() switch
                {
                    "true" or "on" or "yes" or "1" => JsonValue.Create(true),
                    "false" or "off" or "no" or "0" => JsonValue.Create(false),
                    _ => throw new ArgumentException($"{key.Name} is true or false, not '{raw}'."),
                };

            case SettingType.Int:
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    throw new ArgumentException($"{key.Name} is a whole number ({key.ValueHint}), not '{raw}'.");
                if (n < key.Min || n > key.Max)
                    throw new ArgumentException($"{key.Name} must be between {key.Min} and {key.Max}.");
                return JsonValue.Create(n);

            case SettingType.Choice:
                var choice = key.Choices?.FirstOrDefault(c => string.Equals(c, text, StringComparison.OrdinalIgnoreCase));
                return choice is not null
                    ? JsonValue.Create(choice)
                    : throw new ArgumentException($"{key.Name} is one of {string.Join(", ", key.Choices ?? [])}, not '{raw}'.");

            default:
                if (text.Length == 0) throw new ArgumentException($"{key.Name} can't be empty. Use 'aghouse settings reset {key.Name}' for the default.");
                return JsonValue.Create(text);
        }
    }

    private static string Bare(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
