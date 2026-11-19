using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Windows.UI.ViewManagement;

namespace AgHouse.App.Theming;

/// <summary>
/// Follows Windows' Settings › Accessibility › Text size, which WPF doesn't do by itself. The chrome's
/// type ramp (TypeCaption … TypeTitle in App.xaml) and the text of Fluent's stock controls are scaled by
/// the factor, and change live when the setting does. Icons, tile letters and the Settings preview
/// drawings stay their size, as Windows' own icons do.
/// </summary>
public static class Typography
{
    private static readonly string[] Ramp =
        ["TypeCaption", "TypeMeta", "TypeRow", "TypeBody", "TypeTitle", "TypeMetaLine", "TypeMetaTwoLines"];

    /// <summary>
    /// Stock controls whose text Fluent sizes with a fixed value, so overriding its resource keys does nothing.
    /// Each gets an implicit style based on Fluent's own that reads its size from a scaled resource instead.
    /// </summary>
    private static readonly Type[] Controls =
    [
        typeof(Button), typeof(ToggleButton), typeof(CheckBox), typeof(RadioButton), typeof(ComboBox),
        typeof(ComboBoxItem), typeof(TextBox), typeof(PasswordBox), typeof(MenuItem), typeof(ToolTip),
    ];

    /// <summary>Each key's size at 100%, read before anything is overridden.</summary>
    private static readonly Dictionary<string, double> Base = [];
    private static UISettings? _settings;

    public static double Scale { get; private set; } = 1;

    public static void Start(Application app)
    {
        foreach (var key in Ramp)
            if (app.TryFindResource(key) is double size) Base[key] = size;
        try
        {
            _settings = new UISettings();
            // Raised on a background thread.
            _settings.TextScaleFactorChanged += (s, _) =>
            {
                var factor = s.TextScaleFactor;
                app.Dispatcher.BeginInvoke(() => Apply(app, factor));
            };
            Apply(app, _settings.TextScaleFactor);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or TypeLoadException or PlatformNotSupportedException)
        {
            Log.Write($"Text size setting unavailable: {ex.Message}");
        }
    }

    private static void Apply(Application app, double factor)
    {
        // Windows offers 100% to 225%.
        factor = Math.Clamp(factor, 1, 2.25);
        if (Math.Abs(factor - Scale) < 0.001) return;
        Scale = factor;
        // The control styles go in the first time text is scaled; at 100% Fluent's own stay untouched.
        foreach (var type in Controls) DeriveStyle(app, type);
        foreach (var (key, size) in Base) app.Resources[key] = Math.Round(size * factor, 1);
    }

    /// <summary>An implicit style for <paramref name="type"/> on top of Fluent's, sized from "TypeControl.&lt;name&gt;".</summary>
    private static void DeriveStyle(Application app, Type type)
    {
        var key = "TypeControl." + type.Name;
        if (Base.ContainsKey(key)) return;
        var fluent = app.TryFindResource(type) as Style;
        Base[key] = FontSizeIn(fluent) ?? 14;
        var style = new Style(type, fluent);
        style.Setters.Add(new Setter(Control.FontSizeProperty, new DynamicResourceExtension(key)));
        app.Resources[type] = style;
    }

    /// <summary>The FontSize a style (or one it's based on) sets, when it's a plain number.</summary>
    private static double? FontSizeIn(Style? style)
    {
        for (; style is not null; style = style.BasedOn)
            foreach (var setter in style.Setters.OfType<Setter>())
                if (setter.Property == Control.FontSizeProperty)
                    if (setter.Value is double size) return size;
        return null;
    }
}
