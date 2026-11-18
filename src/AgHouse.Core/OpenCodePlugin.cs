using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgHouse.Core;

/// <summary>
/// aghouse's OpenCode plugin: a folder of its own in OpenCode's global plugin folder
/// (<c>~/.config/opencode/plugins/aghouse</c>), the counterpart of <see cref="ClaudeHooks"/>. OpenCode 2 loads
/// its <c>tui.js</c> into each terminal client, which runs inside the aghouse pane, so the plugin reports
/// that pane's sessions (Working, Needs you, finished) through the aghouse CLI. Installing writes only that
/// folder: OpenCode's own config (opencode.json) and every other plugin stay as they are.
/// </summary>
public static partial class OpenCodePlugin
{
    /// <summary>Bumped whenever the plugin's behaviour changes, so an older copy shows as outdated.</summary>
    public const int Version = 1;

    private const string Marker = "// Managed by aghouse (opencode-plugin ";

    /// <summary>OpenCode's global config folder: $XDG_CONFIG_HOME/opencode, else ~/.config/opencode (on Windows too).</summary>
    public static string ConfigDir { get; } = Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "opencode");

    public static string DefaultDir { get; } = Path.Combine(ConfigDir, "plugins", "aghouse");

    /// <summary>
    /// The CLI the plugin runs: the full path of the aghouse.exe next to this program (never whatever
    /// "aghouse" a PATH entry holds), or plain "aghouse" when there is none beside us (a dev build of the tests).
    /// </summary>
    public static string Executable { get; } = ExecutableFor(Path.Combine(AppContext.BaseDirectory, "aghouse.exe"));

    public static string ExecutableFor(string exePath) =>
        File.Exists(exePath) ? Path.GetFullPath(exePath).Replace('\\', '/') : "aghouse";

    /// <summary>The plugin's tui.js for <paramref name="executable"/>.</summary>
    public static string Script(string? executable = null)
    {
        using var stream = typeof(OpenCodePlugin).Assembly.GetManifestResourceStream("AgHouse.Core.opencode-tui.js")
            ?? throw new InvalidOperationException("The OpenCode plugin template is missing from AgHouse.Core.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd()
            .Replace("__VERSION__", Version.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("__AGHOUSE__", JsonSerializer.Serialize(executable ?? Executable));
    }

    /// <summary>The folder's package.json: an ES module whose "./tui" entry is the plugin.</summary>
    public const string PackageJson = """
        {
          "name": "aghouse-opencode",
          "private": true,
          "type": "module",
          "description": "Reports OpenCode's state to the aghouse pane it runs in. Managed by aghouse.",
          "exports": {
            "./tui": "./tui.js"
          }
        }

        """;

    /// <summary>
    /// <see cref="HookState.Connected"/>: this copy's current plugin is there. <see cref="HookState.Partial"/>: an
    /// aghouse plugin is there, but an older version or another copy's (<paramref name="OtherCopy"/>, its CLI path);
    /// installing updates it. <see cref="HookState.Unreadable"/>: the folder holds something aghouse didn't write,
    /// so it is left alone (<paramref name="Problem"/> says why).
    /// </summary>
    public sealed record Status(HookState State, string? Problem = null, string? OtherCopy = null)
    {
        /// <summary>OpenCode reaches aghouse, through this copy or another.</summary>
        public bool Reaches => State == HookState.Connected || OtherCopy is not null;
    }

    public static Status Inspect(string? dir = null, string? executable = null)
    {
        dir ??= DefaultDir;
        var script = Path.Combine(dir, "tui.js");
        try
        {
            if (!Directory.Exists(dir)) return new Status(HookState.NotConnected);
            if (!File.Exists(script))
                return Directory.EnumerateFileSystemEntries(dir).Any()
                    ? new Status(HookState.Unreadable, $"{dir} holds files aghouse didn't write.")
                    : new Status(HookState.NotConnected);
            var text = File.ReadAllText(script);
            if (!text.Contains(Marker, StringComparison.Ordinal))
                return new Status(HookState.Unreadable, $"{script} wasn't written by aghouse.");
            if (Normalise(text) == Normalise(Script(executable)) && File.Exists(Path.Combine(dir, "package.json")))
                return new Status(HookState.Connected);
            var other = ExecutableIn(text);
            var mine = executable ?? Executable;
            return new Status(HookState.Partial, null,
                other is not null && !other.Equals(mine, StringComparison.OrdinalIgnoreCase) ? other : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Status(HookState.Unreadable, ex.Message);
        }
    }

    /// <summary>
    /// Writes (or updates) the plugin folder. Refuses (throws) when the folder holds files aghouse didn't
    /// write. Returns false when this copy's current plugin was already there.
    /// </summary>
    public static bool Install(string? dir = null, string? executable = null)
    {
        dir ??= DefaultDir;
        var status = Inspect(dir, executable);
        if (status.State == HookState.Unreadable) throw new InvalidOperationException(status.Problem);
        if (status.State == HookState.Connected) return false;
        Directory.CreateDirectory(dir);
        Write(Path.Combine(dir, "package.json"), PackageJson);
        Write(Path.Combine(dir, "tui.js"), Script(executable));
        return true;
    }

    private static void Write(string path, string text)
    {
        var tmp = path + ".aghouse-tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n").Trim();

    /// <summary>The CLI path an aghouse plugin file calls ("const AGHOUSE = ...").</summary>
    private static string? ExecutableIn(string script)
    {
        var m = AghouseConst().Match(script);
        if (!m.Success) return null;
        try { return JsonSerializer.Deserialize<string>(m.Groups[1].Value); }
        catch (JsonException) { return null; }
    }

    [GeneratedRegex("""^const AGHOUSE = ("(?:[^"\\]|\\.)*");""", RegexOptions.Multiline)]
    private static partial Regex AghouseConst();
}
