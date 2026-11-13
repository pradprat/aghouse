using System.Windows;
using System.Windows.Controls;

namespace AgHouse.App.Terminal;

/// <summary>
/// The pane header's text row: the pane's name, then "Needs you · message", left to right. When the
/// pane is too narrow for both, the name trims first, down to <see cref="MinName"/>, because the
/// request is what the user came to read. (A DockPanel or Auto grid column would do the opposite and
/// let a long name like "Windows PowerShell (2)" squeeze "Needs you" to "Ne…".)
/// Children: [0] the name, [1] the attention text. Either may be collapsed.
/// </summary>
public sealed class AttentionFirstRow : Panel
{
    /// <summary>The least width the name keeps while the attention text is shown (about eight characters).</summary>
    public double MinName { get; set; } = 56;

    private double _nameWidth;
    private double _attentionWidth;

    protected override Size MeasureOverride(Size available)
    {
        var (name, attention) = (InternalChildren[0], InternalChildren[1]);
        var unbounded = new Size(double.PositiveInfinity, available.Height);
        name.Measure(unbounded);
        attention.Measure(unbounded);

        var width = double.IsInfinity(available.Width) ? name.DesiredSize.Width + attention.DesiredSize.Width : available.Width;
        var nameFloor = Math.Min(name.DesiredSize.Width, MinName);
        _attentionWidth = Math.Min(attention.DesiredSize.Width, Math.Max(0, width - nameFloor));
        _nameWidth = Math.Min(name.DesiredSize.Width, Math.Max(0, width - _attentionWidth));

        // Measure again at the widths they'll get, so trimming happens.
        name.Measure(new Size(_nameWidth, available.Height));
        attention.Measure(new Size(_attentionWidth, available.Height));
        return new Size(_nameWidth + _attentionWidth, Math.Max(name.DesiredSize.Height, attention.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (name, attention) = (InternalChildren[0], InternalChildren[1]);
        name.Arrange(new Rect(0, 0, _nameWidth, final.Height));
        attention.Arrange(new Rect(_nameWidth, 0, _attentionWidth, final.Height));
        return final;
    }
}
