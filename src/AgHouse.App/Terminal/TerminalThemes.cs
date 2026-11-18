using AgHouse.Core.Themes;
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

    /// <summary>The scheme as the terminal control takes it.</summary>
    public static TerminalTheme From(ColorScheme scheme) => new()
    {
        DefaultBackground = ColorRef(scheme.Background),
        DefaultForeground = ColorRef(scheme.Foreground),
        DefaultSelectionBackground = ColorRef(scheme.Selection),
        CursorStyle = CursorStyle.BlinkingBar,
        ColorTable = scheme.Table.Select(ColorRef).ToArray(),
    };

    /// <summary>The terminal background as a WPF color, so pane frames can match the terminal.</summary>
    public static System.Windows.Media.Color BackgroundOf(ColorScheme scheme) =>
        System.Windows.Media.Color.FromRgb(Rgb.R(scheme.Background), Rgb.G(scheme.Background), Rgb.B(scheme.Background));

    /// <summary>0xRRGGBB → COLORREF (0x00BBGGRR).</summary>
    private static uint ColorRef(uint rgb) =>
        ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
}
