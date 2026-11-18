using System.Windows;
using System.Windows.Controls;

namespace AgHouse.App.Terminal;

/// <summary>
/// The pane header's layout: the name (or tab strip, or find bar) on the left, the drag grip centred
/// in the header, and the size readout, zoom note and ✕ on the right. The grip stays centred while the
/// left side fits before it; a longer name or tab strip pushes it right, towards the buttons, and only
/// then does the left side trim. A collapsed grip gives the left side the whole row.
/// Children: [0] left, [1] grip, [2] right.
/// </summary>
public sealed class PaneHeaderRow : Panel
{
    private const double Gap = 8;

    private double _leftWidth;
    private double _gripX;

    protected override Size MeasureOverride(Size available)
    {
        var (left, grip, right) = (InternalChildren[0], InternalChildren[1], InternalChildren[2]);
        var unbounded = new Size(double.PositiveInfinity, available.Height);
        right.Measure(unbounded);
        grip.Measure(unbounded);
        var hasGrip = grip.Visibility != Visibility.Collapsed;

        var width = double.IsInfinity(available.Width) ? 0 : available.Width;
        var rightW = right.DesiredSize.Width;
        var gripW = hasGrip ? grip.DesiredSize.Width : 0;

        if (double.IsInfinity(available.Width))
        {
            left.Measure(unbounded);
            _leftWidth = left.DesiredSize.Width;
            width = _leftWidth + (hasGrip ? Gap + gripW + Gap : 0) + rightW;
        }
        else if (hasGrip)
        {
            // Room left of the grip at its rightmost spot (just before the buttons).
            var room = Math.Max(0, width - rightW - gripW - Gap * 2);
            left.Measure(new Size(room, available.Height));
            _leftWidth = Math.Min(left.DesiredSize.Width, room);
        }
        else
        {
            _leftWidth = Math.Max(0, width - rightW);
            left.Measure(new Size(_leftWidth, available.Height));
        }

        if (hasGrip)
        {
            var centred = (width - gripW) / 2;
            var earliest = _leftWidth + Gap;
            var latest = Math.Max(0, width - rightW - gripW - Gap);
            _gripX = Math.Min(Math.Max(centred, earliest), Math.Max(earliest, latest));
        }

        var height = Math.Max(left.DesiredSize.Height, Math.Max(grip.DesiredSize.Height, right.DesiredSize.Height));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (left, grip, right) = (InternalChildren[0], InternalChildren[1], InternalChildren[2]);
        var rightW = right.DesiredSize.Width;
        right.Arrange(new Rect(Math.Max(0, final.Width - rightW), 0, rightW, final.Height));
        if (grip.Visibility != Visibility.Collapsed)
        {
            // The left side gets up to the grip, so a stretching child (the find bar) never runs under it.
            left.Arrange(new Rect(0, 0, Math.Max(0, _gripX - Gap), final.Height));
            grip.Arrange(new Rect(_gripX, 0, grip.DesiredSize.Width, final.Height));
        }
        else
        {
            left.Arrange(new Rect(0, 0, Math.Max(0, final.Width - rightW), final.Height));
        }
        return final;
    }
}
