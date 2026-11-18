using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgHouse.Core.Themes;

/// <summary>
/// Every theme aghouse can show: the native "Windows" look, the built-ins, and the custom schemes
/// in settings.json. Custom schemes that can't be used are left out and listed in <see cref="Problems"/>.
/// </summary>
public sealed class ThemeCatalog
{
    public IReadOnlyList<ColorScheme> Custom { get; }
    public IReadOnlyList<string> Problems { get; }

    public ThemeCatalog(JsonArray? custom)
    {
        var schemes = new List<ColorScheme>();
        var problems = new List<string>();
        foreach (var node in custom ?? [])
        {
            var scheme = SchemeJson.Parse(node, out var error);
            if (scheme is null) { problems.Add(error!); continue; }
            if (IsReserved(scheme.Name)) { problems.Add($"“{scheme.Name}” has the same name as a built-in theme"); continue; }
            if (schemes.Any(s => SameName(s.Name, scheme.Name))) { problems.Add($"“{scheme.Name}” is in settings.json twice; the first one is used"); continue; }
            schemes.Add(scheme);
        }
        Custom = schemes;
        Problems = problems;
    }

    /// <summary>The themes for one picker: "Windows" first, then built-ins, then custom schemes of that lightness.</summary>
    public IReadOnlyList<ColorScheme> For(bool light) =>
    [
        light ? BuiltInSchemes.WindowsLight : BuiltInSchemes.WindowsDark,
        .. BuiltInSchemes.All.Where(s => s.IsLight == light),
        .. Custom.Where(s => s.IsLight == light),
    ];

    /// <summary>
    /// The theme called <paramref name="name"/>, for the given mode ("Windows" has a dark and a light
    /// side). Null when there's no such theme.
    /// </summary>
    public ColorScheme? Find(string? name, bool light)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (SameName(name, BuiltInSchemes.WindowsName)) return light ? BuiltInSchemes.WindowsLight : BuiltInSchemes.WindowsDark;
        return BuiltInSchemes.All.FirstOrDefault(s => SameName(s.Name, name))
               ?? Custom.FirstOrDefault(s => SameName(s.Name, name));
    }

    /// <summary>The theme to show: the named one, or "Windows" when the name is unknown.</summary>
    public ColorScheme Resolve(string? name, bool light) =>
        Find(name, light) ?? (light ? BuiltInSchemes.WindowsLight : BuiltInSchemes.WindowsDark);

    /// <summary>Every theme name, "Windows" first, then built-ins, then custom.</summary>
    public IEnumerable<ColorScheme> All() =>
        new[] { BuiltInSchemes.WindowsDark }.Concat(BuiltInSchemes.All).Concat(Custom);

    public static bool SameName(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsReserved(string name) =>
        SameName(name, BuiltInSchemes.WindowsName) || BuiltInSchemes.All.Any(s => SameName(s.Name, name));

    // ───────────── Import from Windows Terminal ─────────────

    /// <summary>Where Windows Terminal keeps settings.json: the Store and Preview packages, and unpackaged installs.</summary>
    public static IEnumerable<string> WindowsTerminalSettingsPaths()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json");
        yield return Path.Combine(local, "Packages", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "LocalState", "settings.json");
        yield return Path.Combine(local, "Microsoft", "Windows Terminal", "settings.json");
    }

    /// <summary>
    /// Copies Windows Terminal's schemes into <paramref name="custom"/> (aghouse's "schemes" array),
    /// skipping names aghouse already has and schemes it can't read. A copy, not a link: later
    /// changes in Windows Terminal don't follow.
    /// </summary>
    public static ImportResult ImportWindowsTerminal(JsonArray custom, IEnumerable<string>? settingsFiles = null)
    {
        var found = false;
        var added = new List<string>();
        var skipped = 0;
        var existing = new ThemeCatalog(custom);
        foreach (var path in settingsFiles ?? WindowsTerminalSettingsPaths())
        {
            string text;
            try
            {
                if (!File.Exists(path)) continue;
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            found = true;

            JsonArray schemes;
            try { schemes = SchemeJson.ReadWindowsTerminalSchemes(text); }
            catch (JsonException) { continue; }

            foreach (var node in schemes)
            {
                var scheme = SchemeJson.Parse(node, out _);
                if (scheme is null || IsReserved(scheme.Name)
                    || existing.Custom.Any(s => SameName(s.Name, scheme.Name))
                    || added.Any(n => SameName(n, scheme.Name)))
                {
                    skipped++;
                    continue;
                }
                custom.Add(SchemeJson.ToJson(scheme));
                added.Add(scheme.Name);
            }
        }
        return new ImportResult(found, added, skipped);
    }
}

/// <summary>What an import did. <see cref="FoundSettings"/> is false when Windows Terminal's settings.json wasn't anywhere.</summary>
public sealed record ImportResult(bool FoundSettings, IReadOnlyList<string> Added, int Skipped)
{
    /// <summary>One line for Settings and the CLI.</summary>
    public string Summary => !FoundSettings
        ? "No Windows Terminal settings found."
        : Added.Count == 0
            ? (Skipped == 0 ? "Windows Terminal has no schemes of its own to import." : "No new schemes: aghouse already has them all.")
            : $"Imported {Added.Count} scheme{(Added.Count == 1 ? "" : "s")}: {string.Join(", ", Added)}.";
}
