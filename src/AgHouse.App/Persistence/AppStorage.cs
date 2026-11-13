using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AgHouse.Core.Layout;

namespace AgHouse.App.Persistence;

public sealed class AppSettings
{
    /// <summary>Profile id: pwsh, powershell, cmd, gitbash, wsl-&lt;distro&gt;.</summary>
    public string? DefaultProfile { get; set; }
    public string FontFamily { get; set; } = "Cascadia Mono";
    public int FontSize { get; set; } = 11;
    public bool RestoreSession { get; set; } = true;
    public bool Toasts { get; set; } = true;
    /// <summary>"system" | "dark" | "light"</summary>
    public string Theme { get; set; } = "system";
    /// <summary>Ctrl+D splits right (cmux's ⌘D). False gives Ctrl+D back to the terminal (EOF / exit).</summary>
    public bool CtrlDSplits { get; set; } = true;
    /// <summary>Flash the taskbar button when a pane needs you while aghouse is in the background.</summary>
    public bool FlashTaskbar { get; set; } = true;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

/// <summary>Why settings.json couldn't be used. <see cref="Summary"/> fits the sidebar footer; <see cref="Detail"/> is the parser's message.</summary>
public sealed record SettingsProblem(string Summary, string Detail);

public static class AppStorage
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "aghouse");

    public static string SettingsPath => Path.Combine(Dir, "settings.json");
    private static string SessionPath => Path.Combine(Dir, "session.json");

    /// <summary>
    /// Read settings.json. A missing file is created with the defaults so people can find it.
    /// A file that can't be read is never overwritten: the app runs on defaults and
    /// <paramref name="problem"/> says what's wrong, so a typo can't wipe someone's settings.
    /// </summary>
    public static AppSettings LoadSettings(out SettingsProblem? problem)
    {
        problem = null;
        if (!File.Exists(SettingsPath))
        {
            var defaults = new AppSettings();
            Write(SettingsPath, defaults);
            return defaults;
        }
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Json) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            problem = new SettingsProblem(
                ex.LineNumber is { } line ? $"settings.json has an error on line {line + 1}" : "settings.json has an error",
                ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = new SettingsProblem("settings.json couldn't be read", ex.Message);
        }
        Log.Write($"settings: {problem.Summary}: {problem.Detail}");
        return new AppSettings();
    }

    /// <summary>Create settings.json with the defaults if it doesn't exist yet. Never touches an existing file.</summary>
    public static void EnsureSettingsFile()
    {
        if (!File.Exists(SettingsPath)) Write(SettingsPath, new AppSettings());
    }

    /// <summary>
    /// Writes settings from the Settings window into settings.json. Only aghouse's own keys change;
    /// anything else in the file stays. A file that can't be parsed is never overwritten: returns false.
    /// </summary>
    public static bool SaveSettings(AppSettings settings)
    {
        JsonObject root;
        try
        {
            root = File.Exists(SettingsPath)
                ? JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject ?? throw new JsonException("not an object")
                : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Write($"settings: not saving over an unreadable file: {ex.Message}");
            return false;
        }
        foreach (var (key, value) in JsonSerializer.SerializeToNode(settings, Json)!.AsObject())
        {
            root[key] = value?.DeepClone();
        }
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString(Json));
            File.Move(tmp, SettingsPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"settings: write failed: {ex.Message}");
            return false;
        }
    }

    public static bool SessionExists => File.Exists(SessionPath);

    public static SessionDto? LoadSession() => Read<SessionDto>(SessionPath);

    public static void SaveSession(SessionDto session) => Write(SessionPath, session);

    private static T? Read<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Write<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"aghouse: failed to write {path}: {ex.Message}");
        }
    }
}
