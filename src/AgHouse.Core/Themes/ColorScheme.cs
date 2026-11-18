namespace AgHouse.Core.Themes;

public enum SchemeSource
{
    /// <summary>The native look: Mica, Fluent brushes and the Windows accent around aghouse's own terminal colours.</summary>
    Windows,
    BuiltIn,
    /// <summary>From the <c>schemes</c> array in settings.json (typed by hand or imported from Windows Terminal).</summary>
    Custom,
}

/// <summary>
/// A terminal colour scheme in Windows Terminal's terms, plus the accent aghouse marks selection
/// and "Needs you" with. Colours are 0xRRGGBB. <see cref="Table"/> holds the 16 ANSI colours in
/// Windows Terminal's order: black, red, green, yellow, blue, purple, cyan, white, then the bright ones.
/// </summary>
public sealed record ColorScheme(
    string Name,
    uint Background,
    uint Foreground,
    uint Cursor,
    uint Selection,
    IReadOnlyList<uint> Table,
    uint? Accent,
    SchemeSource Source)
{
    public bool IsLight => Rgb.IsLight(Background);

    public bool IsWindows => Source == SchemeSource.Windows;

    /// <summary>The terminal's own text is under 4.5:1 on its background.</summary>
    public bool LowContrast => Rgb.Contrast(Foreground, Background) < 4.5;

    public static readonly string[] TableNames =
    [
        "black", "red", "green", "yellow", "blue", "purple", "cyan", "white",
        "brightBlack", "brightRed", "brightGreen", "brightYellow", "brightBlue", "brightPurple", "brightCyan", "brightWhite",
    ];
}
