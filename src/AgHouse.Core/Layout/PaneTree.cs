namespace AgHouse.Core.Layout;

/// <summary>Columns = children side by side (vertical divider). Rows = children stacked.</summary>
public enum SplitOrientation { Columns, Rows }

public enum Direction { Left, Right, Up, Down }

public readonly record struct NRect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
}

public abstract class PaneNode
{
    public SplitNode? Parent { get; internal set; }
}

public sealed class PaneLeaf(string paneId) : PaneNode
{
    public string PaneId { get; } = paneId;
}

public sealed class SplitNode : PaneNode
{
    private PaneNode _first = null!;
    private PaneNode _second = null!;
    private double _ratio = 0.5;

    public SplitNode(SplitOrientation orientation, PaneNode first, PaneNode second, double ratio = 0.5)
    {
        Orientation = orientation;
        First = first;
        Second = second;
        Ratio = ratio;
    }

    public SplitOrientation Orientation { get; }

    /// <summary>Fraction of the space given to <see cref="First"/>.</summary>
    public double Ratio
    {
        get => _ratio;
        set => _ratio = Math.Clamp(double.IsFinite(value) ? value : 0.5, 0.05, 0.95);
    }

    public PaneNode First
    {
        get => _first;
        internal set { _first = value; value.Parent = this; }
    }

    public PaneNode Second
    {
        get => _second;
        internal set { _second = value; value.Parent = this; }
    }
}

/// <summary>Binary split tree for one workspace. Pure model: no UI.</summary>
public sealed class PaneTree
{
    private const double Eps = 1e-6;

    public PaneTree(string firstPaneId) : this(new PaneLeaf(firstPaneId)) { }

    public PaneTree(PaneNode root)
    {
        root.Parent = null;
        Root = root;
    }

    public PaneNode? Root { get; private set; }

    public bool IsEmpty => Root is null;

    public IReadOnlyList<PaneLeaf> Leaves()
    {
        var list = new List<PaneLeaf>();
        Collect(Root, list);
        return list;

        static void Collect(PaneNode? n, List<PaneLeaf> acc)
        {
            switch (n)
            {
                case PaneLeaf l: acc.Add(l); break;
                case SplitNode s: Collect(s.First, acc); Collect(s.Second, acc); break;
            }
        }
    }

    public PaneLeaf? Find(string paneId) => Leaves().FirstOrDefault(l => l.PaneId == paneId);

    /// <summary>Splits <paramref name="paneId"/>; the new pane goes on the <paramref name="side"/> of it.</summary>
    public PaneLeaf Split(string paneId, Direction side, string newPaneId, double ratio = 0.5)
    {
        var target = Find(paneId) ?? throw new ArgumentException($"Pane '{paneId}' not found.", nameof(paneId));
        var parent = target.Parent;
        var newLeaf = new PaneLeaf(newPaneId);

        var orientation = side is Direction.Left or Direction.Right ? SplitOrientation.Columns : SplitOrientation.Rows;
        var newFirst = side is Direction.Left or Direction.Up;
        var split = newFirst
            ? new SplitNode(orientation, newLeaf, target, 1 - ratio)
            : new SplitNode(orientation, target, newLeaf, ratio);

        Replace(parent, target, split);
        return newLeaf;
    }

    /// <summary>
    /// Removes a pane; its sibling takes the parent's place.
    /// Returns the pane that should receive focus next, or null when the tree is now empty.
    /// </summary>
    public string? Remove(string paneId)
    {
        var target = Find(paneId) ?? throw new ArgumentException($"Pane '{paneId}' not found.", nameof(paneId));
        var parent = target.Parent;
        if (parent is null)
        {
            Root = null;
            return null;
        }

        var sibling = ReferenceEquals(parent.First, target) ? parent.Second : parent.First;
        Replace(parent.Parent, parent, sibling);
        return FirstLeaf(sibling).PaneId;
    }

    /// <summary>Normalized (0..1) rectangles of each pane.</summary>
    public Dictionary<string, NRect> Layout()
    {
        var map = new Dictionary<string, NRect>();
        if (Root is not null) Walk(Root, new NRect(0, 0, 1, 1), map);
        return map;

        static void Walk(PaneNode n, NRect r, Dictionary<string, NRect> acc)
        {
            switch (n)
            {
                case PaneLeaf l:
                    acc[l.PaneId] = r;
                    break;
                case SplitNode s when s.Orientation == SplitOrientation.Columns:
                    var w1 = r.W * s.Ratio;
                    Walk(s.First, r with { W = w1 }, acc);
                    Walk(s.Second, r with { X = r.X + w1, W = r.W - w1 }, acc);
                    break;
                case SplitNode s:
                    var h1 = r.H * s.Ratio;
                    Walk(s.First, r with { H = h1 }, acc);
                    Walk(s.Second, r with { Y = r.Y + h1, H = r.H - h1 }, acc);
                    break;
            }
        }
    }

    /// <summary>The pane adjacent to <paramref name="paneId"/> in <paramref name="dir"/>, if any.</summary>
    public string? Neighbor(string paneId, Direction dir)
    {
        var layout = Layout();
        if (!layout.TryGetValue(paneId, out var from)) return null;

        string? best = null;
        double bestGap = double.MaxValue, bestOverlap = -1;

        foreach (var (id, r) in layout)
        {
            if (id == paneId) continue;

            double gap, overlap;
            switch (dir)
            {
                case Direction.Right when r.X >= from.Right - Eps:
                    gap = r.X - from.Right; overlap = Overlap(from.Y, from.Bottom, r.Y, r.Bottom); break;
                case Direction.Left when r.Right <= from.X + Eps:
                    gap = from.X - r.Right; overlap = Overlap(from.Y, from.Bottom, r.Y, r.Bottom); break;
                case Direction.Down when r.Y >= from.Bottom - Eps:
                    gap = r.Y - from.Bottom; overlap = Overlap(from.X, from.Right, r.X, r.Right); break;
                case Direction.Up when r.Bottom <= from.Y + Eps:
                    gap = from.Y - r.Bottom; overlap = Overlap(from.X, from.Right, r.X, r.Right); break;
                default:
                    continue;
            }

            if (overlap <= Eps) continue;
            if (gap < bestGap - Eps || (Math.Abs(gap - bestGap) <= Eps && overlap > bestOverlap))
            {
                best = id; bestGap = gap; bestOverlap = overlap;
            }
        }
        return best;

        static double Overlap(double a1, double a2, double b1, double b2) => Math.Min(a2, b2) - Math.Max(a1, b1);
    }

    public static PaneLeaf FirstLeaf(PaneNode n) => n switch
    {
        PaneLeaf l => l,
        SplitNode s => FirstLeaf(s.First),
        _ => throw new InvalidOperationException(),
    };

    private void Replace(SplitNode? parent, PaneNode oldChild, PaneNode newChild)
    {
        if (parent is null)
        {
            newChild.Parent = null;
            Root = newChild;
        }
        else if (ReferenceEquals(parent.First, oldChild)) parent.First = newChild;
        else parent.Second = newChild;
    }
}
