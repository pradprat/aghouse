using System.Windows;
using System.Windows.Controls;

namespace AgHouse.App.Terminal;

/// <summary>
/// Lays tabs out left to right with the new-tab button right after the last one (the last child).
/// Tabs keep their natural width up to <see cref="MaxTab"/>; when they don't all fit, they share the
/// width equally and their names trim.
/// </summary>
public sealed class TabStripPanel : Panel
{
    public double MaxTab { get; set; } = 180;

    /// <summary>
    /// A tab that keeps its natural width when the strip is crowded: one that needs you, so "Needs you"
    /// is never trimmed to "Ne…" (the Say-It Rule). The other tabs share what's left.
    /// </summary>
    public static readonly DependencyProperty KeepWideProperty = DependencyProperty.RegisterAttached(
        "KeepWide", typeof(bool), typeof(TabStripPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static void SetKeepWide(UIElement element, bool value) => element.SetValue(KeepWideProperty, value);
    public static bool GetKeepWide(UIElement element) => (bool)element.GetValue(KeepWideProperty);

    /// <summary>Wide tabs together never take more than this share of a crowded strip.</summary>
    private const double KeepWideShare = 0.6;

    private readonly List<double> _widths = [];

    protected override Size MeasureOverride(Size available)
    {
        _widths.Clear();
        var count = InternalChildren.Count;
        if (count == 0) return default;

        // Natural widths first. A tab that fits keeps exactly this measure: measuring it again at its own
        // width can come out a fraction of a pixel short after layout rounding, and a trimmed name loses
        // two letters to the ellipsis ("GitH…").
        var unbounded = new Size(double.PositiveInfinity, available.Height);
        foreach (UIElement child in InternalChildren) child.Measure(unbounded);

        var plus = InternalChildren[count - 1].DesiredSize.Width;
        var tabs = count - 1;
        var natural = new double[tabs];
        for (var i = 0; i < tabs; i++) natural[i] = Math.Ceiling(InternalChildren[i].DesiredSize.Width);
        var total = natural.Sum(w => Math.Min(w, MaxTab));
        var room = double.IsInfinity(available.Width) ? total : Math.Max(0, available.Width - plus);
        var widths = new double[tabs];
        for (var i = 0; i < tabs; i++) widths[i] = Math.Min(natural[i], MaxTab);
        if (tabs > 0 && total > room)
        {
            // Crowded: tabs that must stay readable first (up to a share of the strip), the rest split what's left.
            var wide = Enumerable.Range(0, tabs).Where(i => GetKeepWide(InternalChildren[i])).ToList();
            var others = tabs - wide.Count;
            var wideWant = wide.Sum(i => widths[i]);
            var wideCap = others == 0 ? room : room * KeepWideShare;
            var scale = wideWant > wideCap ? wideCap / wideWant : 1;
            foreach (var i in wide) widths[i] = Math.Floor(widths[i] * scale);
            var rest = others == 0 ? 0 : Math.Floor((room - wide.Sum(i => widths[i])) / others);
            for (var i = 0; i < tabs; i++)
            {
                if (!wide.Contains(i)) widths[i] = rest;
            }
        }

        var height = InternalChildren[count - 1].DesiredSize.Height;
        for (var i = 0; i < tabs; i++)
        {
            var child = InternalChildren[i];
            var width = widths[i];
            // Only a tab that's narrower than its name is measured again, so its name trims.
            if (width < natural[i]) child.Measure(new Size(width, available.Height));
            _widths.Add(width);
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(_widths.Sum() + plus, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var x = 0.0;
        var count = InternalChildren.Count;
        for (var i = 0; i < count; i++)
        {
            var child = InternalChildren[i];
            var w = i < _widths.Count ? _widths[i] : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, w, final.Height));
            x += w;
        }
        return final;
    }
}
