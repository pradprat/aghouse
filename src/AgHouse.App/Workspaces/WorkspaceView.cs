using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AgHouse.Core.Layout;

namespace AgHouse.App.Workspaces;

/// <summary>
/// Renders a <see cref="PaneTree"/> as nested Grids with GridSplitters.
///
/// Each split keeps its Grid from one render to the next, so a split, close or zoom moves only the
/// frames whose place changed. Moving a frame re-parents its terminal's HWND (WPF parks a detached
/// HwndHost's window instead of destroying it) and resizes it, which used to happen to every pane in
/// the workspace on every render.
/// </summary>
public sealed class WorkspaceView : Grid
{
    private const double SplitterSize = 6;

    /// <summary>The Grid drawn for each split node at the last full render.</summary>
    private Dictionary<SplitNode, SplitView> _splits = [];

    /// <summary>A splitter was dragged (the tree's ratios changed).</summary>
    public event Action? RatiosChanged;

    /// <summary>
    /// Lays out the tree, or only <paramref name="zoomedId"/>'s frame when a pane is zoomed. The other
    /// frames are detached, and their terminals keep running out of sight.
    /// </summary>
    public void Render(PaneTree tree, Func<string, FrameworkElement> frameFor, string? zoomedId = null)
    {
        FrameworkElement? root = null;
        if (zoomedId is not null && tree.Leaves().Any(l => l.PaneId == zoomedId))
        {
            // The split grids are kept as they are: un-zooming puts the frame back into its old place.
            root = frameFor(zoomedId);
        }
        else if (tree.Root is not null)
        {
            var used = new Dictionary<SplitNode, SplitView>();
            root = Build(tree.Root, frameFor, used);
            _splits = used; // grids for splits that are gone are dropped
        }

        if (Children.Count == 1 && ReferenceEquals(Children[0], root)) return;
        Children.Clear();
        if (root is null) return;
        Detach(root);
        Children.Add(root);
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

    private FrameworkElement Build(PaneNode node, Func<string, FrameworkElement> frameFor, Dictionary<SplitNode, SplitView> used)
    {
        if (node is PaneLeaf leaf) return frameFor(leaf.PaneId);

        var split = (SplitNode)node;
        var first = Build(split.First, frameFor, used);
        var second = Build(split.Second, frameFor, used);
        if (!_splits.TryGetValue(split, out var view)) view = CreateSplit(split);
        view.Place(0, first);
        view.Place(1, second);
        used[split] = view;
        return view.Grid;
    }

    /// <summary>One split's Grid, and which element sits on each side of its splitter.</summary>
    private sealed class SplitView(Grid grid, SplitOrientation orientation)
    {
        private readonly FrameworkElement?[] _sides = new FrameworkElement?[2];

        public Grid Grid { get; } = grid;

        /// <summary>Puts <paramref name="element"/> on one side (0 first, 1 second), moving it only if it isn't there already.</summary>
        public void Place(int side, FrameworkElement element)
        {
            if (ReferenceEquals(_sides[side], element) && ReferenceEquals(element.Parent, Grid)) return;
            if (_sides[side] is { } old && ReferenceEquals(old.Parent, Grid)) Grid.Children.Remove(old);
            Detach(element);
            // Both, so an element that moved from a column split to a row split keeps no stale position.
            var columns = orientation == SplitOrientation.Columns;
            SetColumn(element, columns ? side * 2 : 0);
            SetRow(element, columns ? 0 : side * 2);
            Grid.Children.Add(element);
            _sides[side] = element;
        }
    }

    private SplitView CreateSplit(SplitNode split)
    {
        var grid = new Grid();
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
            SetColumn(splitter, 1);
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
            SetRow(splitter, 1);
            splitter.Cursor = System.Windows.Input.Cursors.SizeNS;
            splitter.DragCompleted += (_, _) =>
            {
                var total = r0.ActualHeight + r2.ActualHeight;
                if (total > 0) split.Ratio = r0.ActualHeight / total;
                RatiosChanged?.Invoke();
            };
        }

        grid.Children.Add(splitter); // the sides are added by SplitView.Place
        return new SplitView(grid, split.Orientation);
    }
}
