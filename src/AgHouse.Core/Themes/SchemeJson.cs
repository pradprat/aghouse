using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgHouse.Core.Themes;

/// <summary>
/// Schemes as JSON, in Windows Terminal's own shape (name, background, foreground, cursorColor,
/// selectionBackground and the 16 colour names), so a scheme can be pasted between the two.
/// aghouse adds one optional key: <c>accent</c>.
/// </summary>
public static class SchemeJson
{
    /// <summary>A scheme from one JSON object, or why it can't be one.</summary>
    public static ColorScheme? Parse(JsonNode? node, out string? error)
    {
        error = null;
        if (node is not JsonObject o) { error = "a scheme isn't a JSON object"; return null; }
        var name = Text(o, "name")?.Trim();
        if (string.IsNullOrEmpty(name)) { error = "a scheme has no \"name\""; return null; }

        uint? Color(string key, bool required, ref string? problem)
        {
            var raw = Text(o, key);
            if (raw is null)
            {
                if (required) problem ??= $"“{name}” has no \"{key}\"";
                return null;
            }
            var c = Rgb.Parse(raw);
            if (c is null) problem ??= $"“{name}” has \"{key}\": \"{raw}\", which isn't a #RRGGBB colour";
            return c;
        }

        string? p = null;
        var background = Color("background", true, ref p);
        var foreground = Color("foreground", true, ref p);
        var table = ColorScheme.TableNames.Select(k => Color(k, true, ref p)).ToArray();
        var cursor = Color("cursorColor", false, ref p);
        var selection = Color("selectionBackground", false, ref p);
        var accent = Color("accent", false, ref p);
        if (p is not null) { error = p; return null; }

        return new ColorScheme(name, background!.Value, foreground!.Value,
            cursor ?? foreground.Value,
            selection ?? Rgb.Mix(background.Value, foreground.Value, 0.25),
            table.Select(c => c!.Value).ToArray(), accent, SchemeSource.Custom);
    }

    /// <summary>The scheme in Windows Terminal's shape, for settings.json.</summary>
    public static JsonObject ToJson(ColorScheme s)
    {
        var o = new JsonObject
        {
            ["name"] = s.Name,
            ["background"] = Rgb.ToHex(s.Background),
            ["foreground"] = Rgb.ToHex(s.Foreground),
            ["cursorColor"] = Rgb.ToHex(s.Cursor),
            ["selectionBackground"] = Rgb.ToHex(s.Selection),
        };
        for (var i = 0; i < 16; i++) o[ColorScheme.TableNames[i]] = Rgb.ToHex(s.Table[i]);
        if (s.Accent is { } a) o["accent"] = Rgb.ToHex(a);
        return o;
    }

    /// <summary>
    /// The "schemes" array of a Windows Terminal settings.json, which may have comments and
    /// trailing commas. Throws <see cref="JsonException"/> when the file isn't JSON at all.
    /// </summary>
    public static JsonArray ReadWindowsTerminalSchemes(string settingsText)
    {
        var root = JsonNode.Parse(settingsText, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        return root?["schemes"] as JsonArray ?? [];
    }

    private static string? Text(JsonObject o, string key)
    {
        // Windows Terminal's keys are camelCase; accept any casing, like its own parser.
        foreach (var (k, v) in o)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return v is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
        }
        return null;
    }
}
