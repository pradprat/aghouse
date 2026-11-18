using System.Windows;
using System.Windows.Media;
using AgHouse.Core.Themes;

namespace AgHouse.App.Theming;

/// <summary>
/// One theme as the Settings pickers and preview draw it: swatches from its terminal table and the
/// window colours it implies. Drawn from the scheme itself rather than from the app's resources, so
/// the preview shows the dark and the light theme side by side whichever one is on screen.
/// </summary>
public sealed class ThemeOption
{
    public required string Name { get; init; }
    public required ColorScheme Scheme { get; init; }
    /// <summary>" · low contrast" when the terminal's own text is under 4.5:1, else empty.</summary>
    public string Note => Scheme.LowContrast ? " · low contrast" : "";

    public required Brush Surface { get; init; }
    public required Brush Stroke { get; init; }
    public required Brush Subtle { get; init; }
    public required Brush Ink { get; init; }
    public required Brush Secondary { get; init; }
    public required Brush Accent { get; init; }
    public required Brush TerminalBackground { get; init; }
    public required Brush TerminalForeground { get; init; }
    public required Brush Red { get; init; }
    public required Brush Green { get; init; }
    public required Brush Yellow { get; init; }
    public required Brush Blue { get; init; }
    public required Brush Purple { get; init; }
    public required Brush Cyan { get; init; }
    /// <summary>Red, green, yellow, blue, purple, cyan: the strip in each picker row.</summary>
    public IReadOnlyList<Brush> Swatches => [Red, Green, Yellow, Blue, Purple, Cyan];

    public override string ToString() => Name; // what UI Automation and typing-to-select read

    public static ThemeOption For(ColorScheme scheme)
    {
        uint surface, ink, accent;
        double secondary;
        if (scheme.IsWindows)
        {
            // Roughly Mica with Fluent's own text and the Windows accent, as the native theme shows them.
            surface = scheme.IsLight ? 0xF3F3F3u : 0x202020u;
            ink = scheme.IsLight ? 0x1B1B1Bu : 0xFFFFFFu;
            secondary = scheme.IsLight ? 0.62 : 0.77;
            var a = scheme.IsLight ? SystemColors.AccentColorDark1 : SystemColors.AccentColorLight2;
            accent = Rgb.From(a.R, a.G, a.B);
        }
        else
        {
            var p = new ChromePalette(scheme);
            (surface, ink, secondary, accent) = (p.Surface, p.Ink, p.SecondaryOpacity, p.Accent);
        }

        Brush B(uint rgb, double opacity = 1) => AppTheme.Brush(rgb, (byte)Math.Round(opacity * 255));
        return new ThemeOption
        {
            Name = scheme.Name,
            Scheme = scheme,
            Surface = B(surface),
            Stroke = B(Rgb.Mix(surface, ink, 0.14)),
            Subtle = B(ink, 0.08),
            Ink = B(ink),
            Secondary = B(ink, secondary),
            Accent = B(accent),
            TerminalBackground = B(scheme.Background),
            TerminalForeground = B(scheme.Foreground),
            Red = B(scheme.Table[1]),
            Green = B(scheme.Table[2]),
            Yellow = B(scheme.Table[3]),
            Blue = B(scheme.Table[4]),
            Purple = B(scheme.Table[5]),
            Cyan = B(scheme.Table[6]),
        };
    }
}
