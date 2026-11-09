using AgHouse.Core.Layout;

namespace AgHouse.Tests;

public class PaneTreeEdgeTests
{
    [Fact]
    public void Split_UnknownPane_Throws()
    {
        var t = new PaneTree("a");
        Assert.Throws<ArgumentException>(() => t.Split("nope", Direction.Right, "b"));
        Assert.Single(t.Leaves());
    }

    [Fact]
    public void Remove_UnknownPane_Throws()
    {
        var t = new PaneTree("a");
        Assert.Throws<ArgumentException>(() => t.Remove("nope"));
        Assert.False(t.IsEmpty);
    }

    [Fact]
    public void Find_And_Neighbor_ForUnknownPane()
    {
        var t = new PaneTree("a");
        Assert.Null(t.Find("zzz"));
        Assert.Null(t.Neighbor("zzz", Direction.Right));
    }

    [Fact]
    public void SinglePane_HasNoNeighbors()
    {
        var t = new PaneTree("a");
        foreach (var d in Enum.GetValues<Direction>()) Assert.Null(t.Neighbor("a", d));
    }

    [Theory]
    [InlineData(0.0, 0.05)]
    [InlineData(-1.0, 0.05)]
    [InlineData(1.0, 0.95)]
    [InlineData(7.0, 0.95)]
    [InlineData(double.NaN, 0.5)]
    [InlineData(double.PositiveInfinity, 0.5)]
    [InlineData(0.4, 0.4)]
    public void Ratio_IsClamped(double value, double expected)
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b");
        var s = (SplitNode)t.Root!;
        s.Ratio = value;
        Assert.Equal(expected, s.Ratio, 6);
    }

    [Theory]
    [InlineData(Direction.Left, SplitOrientation.Columns, "b")]
    [InlineData(Direction.Up, SplitOrientation.Rows, "b")]
    [InlineData(Direction.Right, SplitOrientation.Columns, "a")]
    [InlineData(Direction.Down, SplitOrientation.Rows, "a")]
    public void Split_Side_DecidesOrientationAndOrder(Direction side, SplitOrientation orientation, string firstPane)
    {
        var t = new PaneTree("a");
        t.Split("a", side, "b");
        var s = Assert.IsType<SplitNode>(t.Root);
        Assert.Equal(orientation, s.Orientation);
        Assert.Equal(firstPane, Assert.IsType<PaneLeaf>(s.First).PaneId);
    }

    [Fact]
    public void Split_Ratio_IsTheExistingPanesShare_OnEverySide()
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Left, "b", ratio: 0.3);
        // On Left/Up the new pane is the first child, so the stored split ratio is mirrored (1 - 0.3):
        // the existing pane "a" keeps 30 % and the new pane "b" gets 70 %. This pins that behaviour.
        Assert.Equal(0.7, ((SplitNode)t.Root!).Ratio, 6);
        var layout = t.Layout();
        Assert.Equal(0.7, layout["b"].W, 6);
        Assert.Equal(0.3, layout["a"].W, 6);
    }

    [Fact]
    public void FirstLeaf_FindsTheLeftmostTopmostPane()
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b");
        t.Split("a", Direction.Up, "c");
        Assert.Equal("c", PaneTree.FirstLeaf(t.Root!).PaneId);
    }

    [Fact]
    public void Remove_FirstChild_FocusMovesToFirstLeafOfTheSibling()
    {
        // a | (b / c); removing a hands focus to b (top of the surviving split).
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b");
        t.Split("b", Direction.Down, "c");
        Assert.Equal("b", t.Remove("a"));
        Assert.IsType<SplitNode>(t.Root);
        Assert.Null(t.Root!.Parent);
    }

    [Fact]
    public void Remove_KeepsTheRatiosOfTheOtherSplits()
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b", 0.3);
        t.Split("b", Direction.Down, "c", 0.6);
        t.Split("c", Direction.Right, "d", 0.5);
        t.Remove("d");
        var rows = (SplitNode)((SplitNode)t.Root!).Second;
        Assert.Equal(0.6, rows.Ratio, 6);
        Assert.Equal(0.3, ((SplitNode)t.Root).Ratio, 6);
    }

    [Fact]
    public void Layout_OfEmptyTree_IsEmpty()
    {
        var t = new PaneTree("a");
        t.Remove("a");
        Assert.Empty(t.Layout());
        Assert.Empty(t.Leaves());
        Assert.Null(t.Find("a"));
    }

    [Fact]
    public void Layout_AlwaysTilesTheWholeArea_AfterMixedSplitsAndRemovals()
    {
        var t = new PaneTree("p0");
        var sides = new[] { Direction.Right, Direction.Down, Direction.Left, Direction.Up };
        for (var i = 1; i <= 9; i++)
        {
            var target = t.Leaves()[(i * 7) % t.Leaves().Count].PaneId;
            t.Split(target, sides[i % 4], "p" + i, ratio: 0.2 + 0.07 * i);
        }
        foreach (var victim in new[] { "p3", "p0", "p8" }) t.Remove(victim);

        var rects = t.Layout().Values.ToList();
        Assert.Equal(t.Leaves().Count, rects.Count);
        Assert.Equal(1.0, rects.Sum(r => r.W * r.H), 6);
        Assert.All(rects, r =>
        {
            Assert.InRange(r.X, -1e-9, 1 + 1e-9);
            Assert.InRange(r.Y, -1e-9, 1 + 1e-9);
            Assert.InRange(r.Right, -1e-9, 1 + 1e-9);
            Assert.InRange(r.Bottom, -1e-9, 1 + 1e-9);
            Assert.True(r.W > 0 && r.H > 0);
        });
        // No two panes overlap.
        for (var i = 0; i < rects.Count; i++)
            for (var j = i + 1; j < rects.Count; j++)
            {
                var a = rects[i]; var b = rects[j];
                var ox = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
                var oy = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
                Assert.False(ox > 1e-9 && oy > 1e-9, $"panes {i} and {j} overlap");
            }
    }

    [Fact]
    public void ParentLinks_StayConsistent_AfterSplitsAndRemovals()
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b");
        t.Split("b", Direction.Down, "c");
        t.Split("a", Direction.Down, "d");
        t.Remove("b");

        Assert.Null(t.Root!.Parent);
        Check(t.Root);

        static void Check(PaneNode n)
        {
            if (n is not SplitNode s) return;
            Assert.Same(s, s.First.Parent);
            Assert.Same(s, s.Second.Parent);
            Check(s.First);
            Check(s.Second);
        }
    }

    [Fact]
    public void Neighbor_AcrossNestedSplits_PicksTheAdjacentPane()
    {
        // a | (b / c) : from a going right there are two candidates; the one with the larger overlap wins.
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b");
        t.Split("b", Direction.Down, "c", ratio: 0.8); // b is 80 % tall
        Assert.Equal("b", t.Neighbor("a", Direction.Right));
        Assert.Equal("a", t.Neighbor("c", Direction.Left));
        Assert.Equal("b", t.Neighbor("c", Direction.Up));
    }
}
