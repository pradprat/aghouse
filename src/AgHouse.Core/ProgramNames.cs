namespace AgHouse.Core;

/// <summary>How a running program is named in confirmations: its image name, said the way people say it.</summary>
public static class ProgramNames
{
    /// <summary>
    /// "PING.EXE" → "ping", "node.exe" → "node", "Code.exe" → "Code". The ".exe" goes (every program here
    /// has it), and an all-capitals name (Windows' own system32 tools) is lowered, since capitals read as shouting.
    /// </summary>
    public static string Friendly(string image)
    {
        var name = image.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && image.Length > 4 ? image[..^4] : image;
        return name.Any(char.IsLetter) && !name.Any(char.IsLower) ? name.ToLowerInvariant() : name;
    }
}
