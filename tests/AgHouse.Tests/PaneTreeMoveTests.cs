using AgHouse.Core.Layout;

namespace AgHouse.Tests;

/// <summary>Rearranging panes by dragging their header: <see cref="PaneTree.Move"/> and <see cref="PaneTree.Swap"/>.</summary>
public class PaneTreeMoveTests
{
    /// <summary>a | b | c, side by side.</summary>
    private static PaneTree ThreeColumns()
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b");
        t.Split("b", Direction.Right, "c");
        return t;
    }

    private static IEnumerable<string> Ids(PaneTree t) => t.Leaves().Select(l => l.PaneId);

    [Theory]
    [InlineData(Direction.Left, SplitOrientation.Columns, "a", "c")]
    [InlineData(Direction.Right, SplitOrientation.Columns, "c", "a")]
    [InlineData(Direction.Up, SplitOrientation.Rows, "a", "c")]
    [InlineData(Direction.Down, SplitOrientation.Rows, "c", "a")]
    public void Move_ToEachSideOfAnotherPane(Direction side, SplitOrientation orientation, string first, string second)
    {
        var t = ThreeColumns();

        Assert.True(t.Move("a", "c", side));

        // a left its place to b; c's place is now split between a and c.
        var root = Assert.IsType<SplitNode>(t.Root);
        Assert.Equal("b", Assert.IsType<PaneLeaf>(root.First).PaneId);
        var split = Assert.IsType<SplitNode>(root.Second);
        Assert.Equal(orientation, split.Orientation);
        Assert.Equal(first, Assert.IsType<PaneLeaf>(split.First).PaneId);
        Assert.Equal(second, Assert.IsType<PaneLeaf>(split.Second).PaneId);
        Assert.Equal(0.5, split.Ratio);
        Assert.Same(split, split.First.Parent);
        Assert.Null(t.Root!.Parent);
    }

    [Fact]
    public void Move_NextToItsOwnSibling_ChangesTheSplit()
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b"); // a | b

        Assert.True(t.Move("b", "a", Direction.Up)); // b over a

        var root = Assert.IsType<SplitNode>(t.Root);
        Assert.Equal(SplitOrientation.Rows, root.Orientation);
        Assert.Equal(["b", "a"], Ids(t));
        Assert.Null(root.Parent);
    }

    [Fact]
    public void Move_ToTheOtherSideOfItsSibling_SwapsThem()
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b");

        Assert.True(t.Move("b", "a", Direction.Left));

        Assert.Equal(["b", "a"], Ids(t));
        Assert.Equal(SplitOrientation.Columns, Assert.IsType<SplitNode>(t.Root).Orientation);
    }

    [Theory]
    [InlineData("a", "a", Direction.Left)]      // onto itself
    [InlineData("a", "zzz", Direction.Left)]    // unknown target
    [InlineData("zzz", "a", Direction.Left)]    // unknown pane
    [InlineData("a", "b", Direction.Left)]      // a is already left of b
    [InlineData("b", "a", Direction.Right)]     // b is already right of a
    public void Move_WithNothingToDo_LeavesTheTreeAlone(string pane, string target, Direction side)
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b");
        var root = t.Root;

        Assert.False(t.Move(pane, target, side));

        Assert.Same(root, t.Root);
        Assert.Equal(["a", "b"], Ids(t));
    }

    [Fact]
    public void Move_OutOfADeepTree_CollapsesTheEmptiedSplit()
    {
        // a | (b / (c | d)) → move c next to a, below it
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b");
        t.Split("b", Direction.Down, "c");
        t.Split("c", Direction.Right, "d");

        Assert.True(t.Move("c", "a", Direction.Down));

        Assert.Equal(["a", "c", "b", "d"], Ids(t));
        var layout = t.Layout();
        Assert.Equal(new NRect(0, 0, 0.5, 0.5), layout["a"]);
        Assert.Equal(new NRect(0, 0.5, 0.5, 0.5), layout["c"]);
        Assert.Equal(new NRect(0.5, 0, 0.5, 0.5), layout["b"]);
        Assert.Equal(new NRect(0.5, 0.5, 0.5, 0.5), layout["d"]); // d took c's place and all its width
        foreach (var leaf in t.Leaves()) Assert.NotNull(leaf.Parent);
    }

    [Fact]
    public void Swap_Siblings_KeepsTheRatio()
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b", ratio: 0.3);
        var root = Assert.IsType<SplitNode>(t.Root);
        var ratio = root.Ratio;

        Assert.True(t.Swap("a", "b"));

        Assert.Same(root, t.Root);
        Assert.Equal(["b", "a"], Ids(t));
        Assert.Equal(ratio, root.Ratio);
    }

    [Fact]
    public void Swap_Cousins_TradePlaces()
    {
        // (a / b) | (c / d)
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "c");
        t.Split("a", Direction.Down, "b");
        t.Split("c", Direction.Down, "d");
        var before = t.Layout();

        Assert.True(t.Swap("a", "d"));

        var after = t.Layout();
        Assert.Equal(before["a"], after["d"]);
        Assert.Equal(before["d"], after["a"]);
        Assert.Equal(before["b"], after["b"]);
        Assert.Equal(before["c"], after["c"]);
        Assert.Same(t.Find("d")!.Parent, ((SplitNode)t.Root!).First);
    }

    [Fact]
    public void Swap_ARootChildWithANestedPane()
    {
        // a | (b / c)
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b");
        t.Split("b", Direction.Down, "c");

        Assert.True(t.Swap("a", "c"));

        Assert.Equal(["c", "b", "a"], Ids(t));
        Assert.Same(t.Root, t.Find("c")!.Parent);
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("a", "zzz")]
    public void Swap_WithNothingToDo_ReturnsFalse(string x, string y)
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b");
        Assert.False(t.Swap(x, y));
        Assert.Equal(["a", "b"], Ids(t));
    }
}
