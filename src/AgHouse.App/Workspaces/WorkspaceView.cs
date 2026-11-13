using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AgHouse.Core.Layout;

namespace AgHouse.App.Workspaces;

/// <summary>
/// Renders a <see cref="PaneTree"/> as nested Grids with GridSplitters.
/// Pane frames are re-parented on every render; their HWNDs survive this because WPF parks
/// a detached HwndHost's window instead of destroying it.
/// </summary>
public sealed class WorkspaceView : Grid
{
    private const double SplitterSize = 6;

    /// <summary>A splitter was dragged (the tree's ratios changed).</summary>
    public event Action? RatiosChanged;

    /// <summary>
    /// Lays out the tree, or only <paramref name="zoomedId"/>'s frame when a pane is zoomed. The other
    /// frames are detached, and their terminals keep running out of sight.
    /// </summary>
    public void Render(PaneTree tree, Func<string, FrameworkElement> frameFor, string? zoomedId = null)
    {
        var leaves = tree.Leaves();
        foreach (var leaf in leaves) Detach(frameFor(leaf.PaneId));
        Children.Clear();
        if (zoomedId is not null && leaves.Any(l => l.PaneId == zoomedId)) Children.Add(frameFor(zoomedId));
        else if (tree.Root is not null) Children.Add(Build(tree.Root, frameFor));
    }

    public static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel p: p.Children.Remove(element); break;
            case Decorator d: d.Child = null; break;
            case ContentControl c: c.Content = null; break;
        }
    }

    private FrameworkElement Build(PaneNode node, Func<string, FrameworkElement> frameFor)
    {
        if (node is PaneLeaf leaf) return frameFor(leaf.PaneId);

        var split = (SplitNode)node;
        var grid = new Grid();
        var first = Build(split.First, frameFor);
        var second = Build(split.Second, frameFor);
        var splitter = new GridSplitter
        {
            Background = Brushes.Transparent,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Focusable = false,
        };

        if (split.Orientation == SplitOrientation.Columns)
        {
            var c0 = new ColumnDefinition { Width = new GridLength(split.Ratio, GridUnitType.Star), MinWidth = 60 };
            var c2 = new ColumnDefinition { Width = new GridLength(1 - split.Ratio, GridUnitType.Star), MinWidth = 60 };
            grid.ColumnDefinitions.Add(c0);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SplitterSize) });
            grid.ColumnDefinitions.Add(c2);
            SetColumn(first, 0);
            SetColumn(splitter, 1);
            SetColumn(second, 2);
            splitter.Cursor = System.Windows.Input.Cursors.SizeWE;
            splitter.DragCompleted += (_, _) =>
            {
                var total = c0.ActualWidth + c2.ActualWidth;
                if (total > 0) split.Ratio = c0.ActualWidth / total;
                RatiosChanged?.Invoke();
            };
        }
        else
        {
            var r0 = new RowDefinition { Height = new GridLength(split.Ratio, GridUnitType.Star), MinHeight = 40 };
            var r2 = new RowDefinition { Height = new GridLength(1 - split.Ratio, GridUnitType.Star), MinHeight = 40 };
            grid.RowDefinitions.Add(r0);
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SplitterSize) });
            grid.RowDefinitions.Add(r2);
            SetRow(first, 0);
            SetRow(splitter, 1);
            SetRow(second, 2);
            splitter.Cursor = System.Windows.Input.Cursors.SizeNS;
            splitter.DragCompleted += (_, _) =>
            {
                var total = r0.ActualHeight + r2.ActualHeight;
                if (total > 0) split.Ratio = r0.ActualHeight / total;
                RatiosChanged?.Invoke();
            };
        }

        grid.Children.Add(first);
        grid.Children.Add(splitter);
        grid.Children.Add(second);
        return grid;
    }
}
