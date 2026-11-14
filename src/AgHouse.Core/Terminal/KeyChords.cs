using System.Text;

namespace AgHouse.Core.Terminal;

/// <summary>
/// Key names to the bytes a terminal sends for them (xterm conventions, which ConPTY and
/// Windows Terminal follow), for <c>aghouse send-keys</c>: "ctrl+c", "enter", "up", "shift+tab",
/// "alt+x", "ctrl+left", "f5". A single character stands for itself ("y", ":").
/// </summary>
public static class KeyChords
{
    // Cursor keys: ESC [ final, or ESC [ 1 ; mod final with modifiers.
    private static readonly Dictionary<string, char> CsiKeys = new(StringComparer.Ordinal)
    {
        ["up"] = 'A', ["down"] = 'B', ["right"] = 'C', ["left"] = 'D', ["home"] = 'H', ["end"] = 'F',
    };

    // Editing and function keys: ESC [ n ~, or ESC [ n ; mod ~ with modifiers.
    private static readonly Dictionary<string, int> TildeKeys = new(StringComparer.Ordinal)
    {
        ["insert"] = 2, ["ins"] = 2, ["delete"] = 3, ["del"] = 3,
        ["pgup"] = 5, ["pageup"] = 5, ["pgdn"] = 6, ["pagedown"] = 6,
        ["f5"] = 15, ["f6"] = 17, ["f7"] = 18, ["f8"] = 19, ["f9"] = 20, ["f10"] = 21, ["f11"] = 23, ["f12"] = 24,
    };

    // F1-F4: ESC O letter, or ESC [ 1 ; mod letter with modifiers.
    private static readonly Dictionary<string, char> Ss3Keys = new(StringComparer.Ordinal)
    {
        ["f1"] = 'P', ["f2"] = 'Q', ["f3"] = 'R', ["f4"] = 'S',
    };

    private static readonly string[] Plain = ["enter", "return", "tab", "esc", "escape", "backspace", "space"];

    /// <summary>Every key name understood, for help and error messages.</summary>
    public static IReadOnlyList<string> Names { get; } =
        [.. Plain, .. CsiKeys.Keys, "insert", "delete", "pgup", "pgdn", .. Enumerable.Range(1, 12).Select(i => $"f{i}")];

    /// <summary>The bytes for several chords, one after another.</summary>
    public static string ToSequence(IEnumerable<string> chords)
    {
        var sb = new StringBuilder();
        foreach (var chord in chords) sb.Append(ToSequence(chord));
        return sb.ToString();
    }

    /// <summary>The bytes for one chord. Throws <see cref="ArgumentException"/> for a name it doesn't know.</summary>
    public static string ToSequence(string chord)
    {
        if (string.IsNullOrEmpty(chord)) throw Unknown(chord);
        if (chord.Length == 1) return chord; // a character stands for itself, '+' included

        var parts = chord.Trim().ToLowerInvariant().Split('+');
        if (parts[^1].Length == 0 && parts.Length >= 2) parts = [.. parts[..^2], "+"]; // "ctrl++"
        var key = parts[^1];
        bool ctrl = false, alt = false, shift = false;
        foreach (var mod in parts[..^1])
        {
            switch (mod)
            {
                case "ctrl" or "control": ctrl = true; break;
                case "alt" or "meta": alt = true; break;
                case "shift": shift = true; break;
                default: throw Unknown(chord);
            }
        }
        var mods = 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (ctrl ? 4 : 0);

        if (CsiKeys.TryGetValue(key, out var final))
            return mods == 1 ? $"\x1b[{final}" : $"\x1b[1;{mods}{final}";
        if (TildeKeys.TryGetValue(key, out var n))
            return mods == 1 ? $"\x1b[{n}~" : $"\x1b[{n};{mods}~";
        if (Ss3Keys.TryGetValue(key, out var ss3))
            return mods == 1 ? $"\x1bO{ss3}" : $"\x1b[1;{mods}{ss3}";

        var plain = key switch
        {
            "enter" or "return" => "\r",
            "tab" when shift => "\x1b[Z",
            "tab" => "\t",
            "esc" or "escape" => "\x1b",
            "backspace" or "bs" => ctrl ? "\b" : "\x7f",
            "space" => ctrl ? "\0" : " ",
            _ when key.Length == 1 => Character(key[0], ctrl, shift) ?? throw Unknown(chord),
            _ => throw Unknown(chord),
        };
        return alt ? "\x1b" + plain : plain;
    }

    private static string? Character(char c, bool ctrl, bool shift)
    {
        if (!ctrl) return shift ? char.ToUpperInvariant(c).ToString() : c.ToString();
        return c switch
        {
            >= 'a' and <= 'z' => ((char)(c - 'a' + 1)).ToString(),
            '@' or ' ' or '2' => "\0",
            '[' => "\x1b",
            '\\' => "\x1c",
            ']' => "\x1d",
            '^' or '6' => "\x1e",
            '_' or '-' => "\x1f",
            '?' => "\x7f",
            _ => null,
        };
    }

    private static ArgumentException Unknown(string chord) => new(
        $"Unknown key '{chord}'. Use a single character, ctrl+<letter>, alt+<key>, or one of: {string.Join(", ", Names)} " +
        "(cursor, editing and function keys also take ctrl+, alt+ and shift+).");
}
