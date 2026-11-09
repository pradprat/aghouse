using Microsoft.Terminal.Wpf;
using Microsoft.Win32;

namespace AgHouse.App.Terminal;

public static class TerminalThemes
{
    public static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch (System.Security.SecurityException) { return false; }
    }

    public static bool IsLight(string setting) => setting switch
    {
        "light" => true,
        "dark" => false,
        _ => SystemUsesLightTheme(),
    };

    // Background chosen to sit quietly next to Mica (dark ~#202020, light ~#F3F3F3).
    public static TerminalTheme Dark { get; } = Build(
        background: 0x1C1C1C, foreground: 0xCCCCCC, selection: 0x264F78,
        // Campbell
        0x0C0C0C, 0xC50F1F, 0x13A10E, 0xC19C00, 0x0037DA, 0x881798, 0x3A96DD, 0xCCCCCC,
        0x767676, 0xE74856, 0x16C60C, 0xF9F1A5, 0x3B78FF, 0xB4009E, 0x61D6D6, 0xF2F2F2);

    public static TerminalTheme Light { get; } = Build(
        background: 0xFBFBFB, foreground: 0x383A42, selection: 0xBFCEFF,
        // One Half Light
        0x383A42, 0xE45649, 0x50A14F, 0xC18401, 0x0184BC, 0xA626A4, 0x0997B3, 0xFAFAFA,
        0x4F525D, 0xDF6C75, 0x98C379, 0xE4C07A, 0x61AFEF, 0xC577DD, 0x56B5C1, 0xFFFFFF);

    /// <summary>Background as a WPF color, so pane frames can match the terminal.</summary>
    public static System.Windows.Media.Color BackgroundOf(bool light) =>
        light ? System.Windows.Media.Color.FromRgb(0xFB, 0xFB, 0xFB) : System.Windows.Media.Color.FromRgb(0x1C, 0x1C, 0x1C);

    private static TerminalTheme Build(uint background, uint foreground, uint selection, params uint[] table) => new()
    {
        DefaultBackground = ColorRef(background),
        DefaultForeground = ColorRef(foreground),
        DefaultSelectionBackground = ColorRef(selection),
        CursorStyle = CursorStyle.BlinkingBar,
        ColorTable = table.Select(ColorRef).ToArray(),
    };

    /// <summary>0xRRGGBB → COLORREF (0x00BBGGRR).</summary>
    private static uint ColorRef(uint rgb) =>
        ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
}
