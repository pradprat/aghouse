namespace AgHouse.Core.Themes;

/// <summary>
/// The letter tiles in front of each workspace in the sidebar: a capital letter on a colour taken from the
/// theme's own ANSI table, so tiles follow the theme like everything else and no hex lives in UI code.
/// Which colour a workspace gets comes from its name, so it stays put across launches.
/// </summary>
public static class SpaceTiles
{
    /// <summary>The ANSI hues tiles use, in Windows Terminal's table order: blue, green, red, purple, cyan, yellow.</summary>
    private static readonly int[] Hues = [4, 2, 1, 5, 6, 3];

    public static int Count => Hues.Length;

    /// <summary>
    /// The letter on a tile reads at this contrast or better: 4.5:1, the small-text floor, because on the rail
    /// the letter is the only thing on screen that names the workspace.
    /// </summary>
    public const double LetterContrast = 4.5;

    /// <summary>A half of the table is preferred while its colour already carries a letter at this contrast; the ink is then pushed to <see cref="LetterContrast"/>.</summary>
    private const double PickContrast = 3.0;

    /// <summary>Channels this close count as grey here: Solarized's base tones (#586E75) are blue-tinted greys, not hues.</summary>
    private const int GreyTolerance = 32;

    /// <summary>
    /// The tile colours for <paramref name="scheme"/>. Dark schemes prefer the bright half of the table (the dark
    /// half sinks into a dark sidebar), light schemes the normal half; the other half stands in when the preferred
    /// colour is a grey (Solarized's brights are its base tones) or can't carry a readable letter. The letter is the
    /// scheme's foreground or background, whichever reads better, pushed to 4.5:1 when neither gets there.
    /// </summary>
    public static IReadOnlyList<(uint Fill, uint Ink)> For(ColorScheme scheme)
    {
        int[] halves = scheme.IsLight ? [0, 8] : [8, 0];
        return [.. Hues.Select(h =>
        {
            var candidates = halves.Select(o => Tile(scheme, scheme.Table[h + o] & 0xFFFFFF)).ToList();
            var best = candidates.FirstOrDefault(t => !Rgb.IsNeutral(t.Fill, GreyTolerance) && t.Contrast >= PickContrast);
            if (best == default) best = candidates.FirstOrDefault(t => !Rgb.IsNeutral(t.Fill, GreyTolerance));
            if (best == default) best = candidates[0];
            var ink = best.Contrast >= LetterContrast ? best.Ink : Rgb.EnsureContrast(best.Ink, best.Fill, LetterContrast);
            return (best.Fill, ink & 0xFFFFFF);
        })];
    }

    private static (uint Fill, uint Ink, double Contrast) Tile(ColorScheme scheme, uint fill)
    {
        var fg = Rgb.Contrast(scheme.Foreground, fill);
        var bg = Rgb.Contrast(scheme.Background, fill);
        return fg >= bg ? (fill, scheme.Foreground & 0xFFFFFF, fg) : (fill, scheme.Background & 0xFFFFFF, bg);
    }

    /// <summary>
    /// The tile colour for a new workspace, next to the ones <paramref name="taken"/> by its neighbours: the hue its
    /// name hashes to when no other workspace wears fewer, else the least-worn hue, searching from the hashed one.
    /// Six hues hashed by name collide by the fourth workspace more often than not; picked, they don't repeat until
    /// the seventh.
    /// </summary>
    public static int Pick(string? name, IEnumerable<int> taken)
    {
        var worn = new int[Count];
        foreach (var t in taken)
            if (t >= 0 && t < Count) worn[t]++;
        var fewest = worn.Min();
        var start = IndexFor(name);
        for (var i = 0; i < Count; i++)
        {
            var hue = (start + i) % Count;
            if (worn[hue] == fewest) return hue;
        }
        return start;
    }

    /// <summary>A stable index (0..<see cref="Count"/>-1) for a workspace name, the same on every launch.</summary>
    public static int IndexFor(string? name)
    {
        // FNV-1a over the lowered name: string.GetHashCode changes from run to run.
        var hash = 2166136261u;
        foreach (var c in (name ?? "").Trim().ToLowerInvariant())
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return (int)(hash % (uint)Count);
    }

    /// <summary>The tile's letter: the name's first letter or digit, capitalised, or "?" for a name without one.</summary>
    public static string LetterFor(string? name)
    {
        foreach (var c in name ?? "")
        {
            if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
        }
        return "?";
    }
}
