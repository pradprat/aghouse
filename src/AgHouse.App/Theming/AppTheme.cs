using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AgHouse.App.Surfaces;
using AgHouse.Core.Themes;

namespace AgHouse.App.Theming;

/// <summary>
/// Recolours every aghouse window for a scheme. The "Windows" theme is the Fluent theme untouched:
/// Mica, Fluent's brushes, the Windows accent. Any other scheme overrides Fluent's colour resources
/// at the application level (Fluent's control templates look their brushes up dynamically, so they
/// all follow), paints windows solid instead of Mica, and colours the system title bar to match.
/// </summary>
public static class AppTheme
{
    private static readonly List<object> Overrides = [];
    private static readonly Dictionary<IntPtr, int> OriginalBackdrop = [];
    private static bool _hooked;

    /// <summary>The colours in effect, or null under the native "Windows" theme (or a Windows contrast theme).</summary>
    public static ChromePalette? Active { get; private set; }

    /// <summary>
    /// Shows <paramref name="scheme"/> in every window, now and as new ones open. Call after
    /// <see cref="Application.ThemeMode"/> is set, since the overrides are built from Fluent's
    /// dictionary for the mode in effect.
    /// </summary>
    public static void Apply(ColorScheme scheme)
    {
        var app = Application.Current;
        if (!_hooked)
        {
            _hooked = true;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
            {
                if (s is Window w) ApplyTo(w);
            }));
        }

        foreach (var key in Overrides) app.Resources.Remove(key);
        Overrides.Clear();

        // Contrast themes are an accessibility setting: they always win.
        Active = scheme.IsWindows || SystemParameters.HighContrast ? null : new ChromePalette(scheme);
        if (Active is { } palette)
        {
            foreach (var (key, value) in BuildOverrides(app.Resources, palette))
            {
                app.Resources[key] = value;
                Overrides.Add(key);
            }
        }
        foreach (Window w in app.Windows) ApplyTo(w);
    }

    // ───────────── Resources ─────────────

    private static Dictionary<object, object> BuildOverrides(ResourceDictionary root, ChromePalette p)
    {
        var fluent = new Dictionary<object, object>();
        Collect(root, fluent, top: true);

        var fluentBase = fluent.TryGetValue("ApplicationBackgroundColor", out var bg) && bg is Color b
            ? ToRgb(b)
            : p.Light ? 0xFAFAFAu : 0x202020u;
        var accents = new HashSet<uint>(new[]
        {
            SystemColors.AccentColor, SystemColors.AccentColorLight1, SystemColors.AccentColorLight2, SystemColors.AccentColorLight3,
            SystemColors.AccentColorDark1, SystemColors.AccentColorDark2, SystemColors.AccentColorDark3,
        }.Select(ToRgb));

        Color Map(Color c)
        {
            var rgb = ToRgb(c);
            if (accents.Contains(rgb)) return ToColor(p.Accent, c.A);
            var argb = p.RemapNeutral(((uint)c.A << 24) | rgb, fluentBase);
            return ToColor(argb, Rgb.A(argb));
        }

        var result = new Dictionary<object, object>();
        foreach (var (key, value) in fluent)
        {
            switch (value)
            {
                case Color c when Map(c) is var m && m != c:
                    result[key] = m;
                    break;
                case SolidColorBrush s when Map(s.Color) is var m && m != s.Color:
                    result[key] = Frozen(new SolidColorBrush(m) { Opacity = s.Opacity });
                    break;
                case LinearGradientBrush g:
                    var copy = g.Clone();
                    foreach (var stop in copy.GradientStops) stop.Color = Map(stop.Color);
                    result[key] = Frozen(copy);
                    break;
            }
        }

        // The few that need more than a remap.
        void Set(string name, Color c)
        {
            result[name] = c;
            result[name + "Brush"] = Frozen(new SolidColorBrush(c));
        }
        var surface = ToColor(p.Surface);
        var flyout = ToColor(p.Flyout);
        var ink = ToColor(p.Ink);
        var accent = ToColor(p.Accent);
        Set("TextFillColorPrimary", ink);
        Set("TextFillColorSecondary", ToColor(p.Ink, (byte)Math.Round(p.SecondaryOpacity * 255)));
        Set("ApplicationBackgroundColor", surface);
        Set("AcrylicBackgroundFillColorDefault", flyout);
        Set("TextOnAccentFillColorPrimary", ToColor(p.OnAccent));
        Set("TextOnAccentFillColorSelectedText", ToColor(p.OnAccent));
        foreach (var name in new[] { "WindowBackground", "NavigationWindowBackground", "LoadingScreenBackground" })
            result[name] = Frozen(new SolidColorBrush(surface));
        result["WindowForeground"] = Frozen(new SolidColorBrush(ink));
        result["FlyoutBackground"] = Frozen(new SolidColorBrush(flyout));
        foreach (var name in new[] { "", "Light1", "Light2", "Light3", "Dark1", "Dark2", "Dark3" })
            result["SystemAccentColor" + name] = accent;

        // Anything that asks WPF for the system accent directly gets the scheme's.
        var accentBrush = Frozen(new SolidColorBrush(accent));
        foreach (var key in new object[]
                 {
                     SystemColors.AccentColorKey, SystemColors.AccentColorLight1Key, SystemColors.AccentColorLight2Key,
                     SystemColors.AccentColorLight3Key, SystemColors.AccentColorDark1Key, SystemColors.AccentColorDark2Key,
                     SystemColors.AccentColorDark3Key,
                 })
            result[key] = accent;
        foreach (var key in new object[]
                 {
                     SystemColors.AccentColorBrushKey, SystemColors.AccentColorLight1BrushKey, SystemColors.AccentColorLight2BrushKey,
                     SystemColors.AccentColorLight3BrushKey, SystemColors.AccentColorDark1BrushKey, SystemColors.AccentColorDark2BrushKey,
                     SystemColors.AccentColorDark3BrushKey,
                 })
            result[key] = accentBrush;
        return result;
    }

    /// <summary>Fluent's colours and brushes, with later dictionaries winning as WPF's own lookup does.</summary>
    private static void Collect(ResourceDictionary dictionary, Dictionary<object, object> into, bool top)
    {
        foreach (var merged in dictionary.MergedDictionaries) Collect(merged, into, top: false);
        if (top) return; // App.xaml's own keys (and any overrides still there) aren't Fluent's
        foreach (var key in dictionary.Keys)
        {
            object? value;
            try { value = dictionary[key]; }
            catch (Exception ex) when (ex is InvalidOperationException or System.Windows.Markup.XamlParseException) { continue; }
            if (value is Color or SolidColorBrush or LinearGradientBrush) into[key] = value;
        }
    }

    // ───────────── Windows ─────────────

    private static void ApplyTo(Window w)
    {
        var p = Active;
        if (w is not FlyoutWindow)
        {
            // Fluent leaves window backgrounds transparent so Mica shows through.
            if (p is null) w.ClearValue(Control.BackgroundProperty);
            else w.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
        }

        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return; // Loaded comes back for it
        if (p is null)
        {
            if (OriginalBackdrop.Remove(hwnd, out var backdrop)) SetInt(hwnd, DwmwaSystemBackdropType, backdrop);
            SetInt(hwnd, DwmwaCaptionColor, unchecked((int)DwmwaColorDefault));
            SetInt(hwnd, DwmwaTextColor, unchecked((int)DwmwaColorDefault));
            SetInt(hwnd, DwmwaBorderColor, unchecked((int)DwmwaColorDefault));
            return;
        }
        if (!OriginalBackdrop.ContainsKey(hwnd) && GetInt(hwnd, DwmwaSystemBackdropType) is { } original)
        {
            OriginalBackdrop[hwnd] = original;
            w.Closed += (_, _) => OriginalBackdrop.Remove(hwnd);
        }
        SetInt(hwnd, DwmwaSystemBackdropType, 1 /* DWMSBT_NONE: solid, no Mica */);
        SetInt(hwnd, DwmwaCaptionColor, ColorRef(p.Surface));
        SetInt(hwnd, DwmwaTextColor, ColorRef(p.Ink));
        SetInt(hwnd, DwmwaBorderColor, ColorRef(Rgb.Mix(p.Surface, p.Ink, 0.15)));
    }

    // ───────────── Helpers ─────────────

    public static Color ToColor(uint rgb, byte alpha = 255) =>
        Color.FromArgb(alpha, Rgb.R(rgb), Rgb.G(rgb), Rgb.B(rgb));

    public static SolidColorBrush Brush(uint rgb, byte alpha = 255) => Frozen(new SolidColorBrush(ToColor(rgb, alpha)));

    private static uint ToRgb(Color c) => Rgb.From(c.R, c.G, c.B);

    private static T Frozen<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>0xRRGGBB → COLORREF (0x00BBGGRR).</summary>
    private static int ColorRef(uint rgb) => (int)((Rgb.B(rgb) << 16) | (Rgb.G(rgb) << 8) | Rgb.R(rgb));

    // Windows 11 22000+ only; on older builds DWM rejects them and the call is a no-op.
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int DwmwaSystemBackdropType = 38;
    private const uint DwmwaColorDefault = 0xFFFFFFFF;

    private static void SetInt(IntPtr hwnd, int attribute, int value) =>
        _ = DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    private static int? GetInt(IntPtr hwnd, int attribute) =>
        DwmGetWindowAttribute(hwnd, attribute, out var value, sizeof(int)) == 0 ? value : null;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
