namespace AgHouse.Core.Themes;

/// <summary>
/// The window colours a scheme implies, derived by fixed rules so any 16-colour scheme (built in,
/// typed by hand or imported) gets a coherent window: surfaces step off the terminal background
/// toward its foreground, text is the foreground held to 4.5:1, and the accent is the scheme's own,
/// held to 3:1. Pure arithmetic on 0xRRGGBB, so it's testable without WPF.
/// </summary>
public sealed class ChromePalette
{
    /// <summary>How far the window surface steps from the terminal background toward the foreground.</summary>
    public const double SurfaceStep = 0.04;
    /// <summary>Dark flyouts (palette, waiting list, menus) step this far, one step past the window.</summary>
    public const double FlyoutStep = 0.09;

    public ColorScheme Scheme { get; }
    public bool Light { get; }

    /// <summary>The window and sidebar.</summary>
    public uint Surface { get; }
    /// <summary>Owned flyouts, menus and dropdowns.</summary>
    public uint Flyout { get; }
    /// <summary>Primary text: the scheme's foreground, pushed to 4.5:1 on <see cref="Surface"/> if it falls short.</summary>
    public uint Ink { get; }
    /// <summary>Opacity of <see cref="Ink"/> for secondary text, the lowest that still reads at 4.5:1 on the surface.</summary>
    public double SecondaryOpacity { get; }
    /// <summary>Selection and "Needs you".</summary>
    public uint Accent { get; }
    /// <summary>Text or a glyph drawn on <see cref="Accent"/>.</summary>
    public uint OnAccent { get; }

    public ChromePalette(ColorScheme scheme)
    {
        Scheme = scheme;
        Light = scheme.IsLight;
        Surface = Rgb.Mix(scheme.Background, scheme.Foreground, SurfaceStep);
        // A light window's flyouts sit on the terminal's own (lighter) background; a dark one's step further up.
        Flyout = Light ? scheme.Background : Rgb.Mix(scheme.Background, scheme.Foreground, FlyoutStep);
        Ink = Rgb.EnsureContrast(scheme.Foreground, Surface, 4.5);
        SecondaryOpacity = 1.0;
        for (var a = 0.70; a < 1.0; a += 0.02)
        {
            if (Rgb.Contrast(Rgb.Over(Ink, a, Surface), Surface) >= 4.5) { SecondaryOpacity = Math.Round(a, 2); break; }
        }
        Accent = ResolveAccent(scheme, Surface);
        OnAccent = Rgb.Contrast(Accent, 0x000000) >= Rgb.Contrast(Accent, 0xFFFFFF) ? 0x000000u : 0xFFFFFFu;
    }

    /// <summary>
    /// The scheme's accent if it has one that reads, else its cursor, bright blue or blue, whichever
    /// first reaches 3:1 on both the window and the terminal. Grey candidates are skipped (a white
    /// cursor would make the accent look like text). The foreground is the last resort.
    /// </summary>
    public static uint ResolveAccent(ColorScheme scheme, uint surface)
    {
        var candidates = new List<uint>();
        if (scheme.Accent is { } a) candidates.Add(a);
        candidates.Add(scheme.Cursor);
        if (scheme.Table.Count >= 16) { candidates.Add(scheme.Table[12]); candidates.Add(scheme.Table[4]); }
        foreach (var c in candidates)
        {
            if (Rgb.IsNeutral(c, 24)) continue;
            if (Rgb.Contrast(c, surface) >= 3.0 && Rgb.Contrast(c, scheme.Background) >= 3.0) return c;
        }
        return Rgb.EnsureContrast(scheme.Foreground, surface, 4.5);
    }

    /// <summary>
    /// Re-expresses one of Fluent's neutral colours in this scheme. Fluent's greys sit on a line from
    /// its window background (<paramref name="fluentBase"/>) to its text colour (white in dark, black
    /// in light); the same position on the line from <see cref="Surface"/> to <see cref="Ink"/> gives
    /// the scheme's version, so a 6% white hover fill becomes a 6% foreground hover fill. Alpha stays.
    /// Colours that aren't grey (accent, caution) come back unchanged.
    /// </summary>
    public uint RemapNeutral(uint argb, uint fluentBase)
    {
        if (!Rgb.IsNeutral(argb)) return argb;
        double fluentInk = Light ? 0 : 255, from = Rgb.G(fluentBase);
        var span = fluentInk - from;
        var t = Math.Abs(span) < 1 ? 0 : (Rgb.G(argb) - from) / span;
        return Rgb.WithAlpha(Rgb.Mix(Surface, Ink, t), Rgb.A(argb));
    }
}
