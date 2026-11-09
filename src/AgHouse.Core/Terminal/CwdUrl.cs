using System.Text.RegularExpressions;

namespace AgHouse.Core.Terminal;

public static partial class CwdUrl
{
    [GeneratedRegex(@"^/[A-Za-z]:")]
    private static partial Regex SlashDrive();

    /// <summary>Parses an OSC 7 payload such as <c>file://host/C:/Users/me%20x</c>.</summary>
    public static bool TryParse(string value, out string path)
    {
        path = "";
        if (!value.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return false;

        var afterScheme = value["file://".Length..];
        var slash = afterScheme.IndexOf('/');
        if (slash < 0) return false;

        var rawPath = afterScheme[slash..];
        string decoded;
        try { decoded = Uri.UnescapeDataString(rawPath); }
        catch (UriFormatException) { return false; }

        path = NormalizeWindowsPath(decoded);
        return path.Length > 0;
    }

    /// <summary><c>/C:/x/y</c> or <c>C:/x/y</c> → <c>C:\x\y</c>; POSIX paths are returned unchanged.</summary>
    public static string NormalizeWindowsPath(string p)
    {
        if (SlashDrive().IsMatch(p)) p = p[1..];
        if (p.Length >= 2 && char.IsAsciiLetter(p[0]) && p[1] == ':')
        {
            p = p.Replace('/', '\\');
            if (p.Length == 2) p += "\\";
        }
        return p;
    }
}
