using AgHouse.Core.Layout;

namespace AgHouse.Tests;

public class PaneTreeTests
{
    [Fact]
    public void Split_Right_CreatesColumns()
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b");
        var s = Assert.IsType<SplitNode>(t.Root);
        Assert.Equal(SplitOrientation.Columns, s.Orientation);
        Assert.Equal("a", Assert.IsType<PaneLeaf>(s.First).PaneId);
        Assert.Equal("b", Assert.IsType<PaneLeaf>(s.Second).PaneId);
    }

    [Fact]
    public void Split_Up_PutsNewPaneFirst()
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Up, "b");
        var s = Assert.IsType<SplitNode>(t.Root);
        Assert.Equal(SplitOrientation.Rows, s.Orientation);
        Assert.Equal("b", Assert.IsType<PaneLeaf>(s.First).PaneId);
    }

    [Fact]
    public void Remove_MiddlePane_SiblingTakesParentsPlace()
    {
        // a | (b / c)  → remove b → a | c
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b");
        t.Split("b", Direction.Down, "c");

        var next = t.Remove("b");

        Assert.Equal("c", next);
        var root = Assert.IsType<SplitNode>(t.Root);
        Assert.Equal("c", Assert.IsType<PaneLeaf>(root.Second).PaneId);
        Assert.Same(root, root.Second.Parent);
        Assert.Equal(["a", "c"], t.Leaves().Select(l => l.PaneId));
    }

    [Fact]
    public void Remove_LastPane_EmptiesTree()
    {
        var t = new PaneTree("a");
        Assert.Null(t.Remove("a"));
        Assert.True(t.IsEmpty);
    }

    [Fact]
    public void Remove_RootChild_PromotesSibling()
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Down, "b");
        Assert.Equal("a", t.Remove("b"));
        Assert.Null(t.Root!.Parent);
        Assert.Equal("a", Assert.IsType<PaneLeaf>(t.Root).PaneId);
    }

    [Fact]
    public void Layout_FourWay()
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b");
        t.Split("a", Direction.Down, "c");
        t.Split("b", Direction.Down, "d");
        var l = t.Layout();
        Assert.Equal(new NRect(0, 0, 0.5, 0.5), l["a"]);
        Assert.Equal(new NRect(0.5, 0, 0.5, 0.5), l["b"]);
        Assert.Equal(new NRect(0, 0.5, 0.5, 0.5), l["c"]);
        Assert.Equal(new NRect(0.5, 0.5, 0.5, 0.5), l["d"]);
    }

    [Fact]
    public void Neighbor_FourWay()
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b");
        t.Split("a", Direction.Down, "c");
        t.Split("b", Direction.Down, "d");

        Assert.Equal("b", t.Neighbor("a", Direction.Right));
        Assert.Equal("c", t.Neighbor("a", Direction.Down));
        Assert.Equal("a", t.Neighbor("c", Direction.Up));
        Assert.Equal("c", t.Neighbor("d", Direction.Left));
        Assert.Null(t.Neighbor("a", Direction.Left));
        Assert.Null(t.Neighbor("d", Direction.Down));
    }

    [Fact]
    public void Neighbor_PrefersLargestOverlap()
    {
        // a (full height) | b over c ; from a going right should pick b or c by overlap — equal here, so first found wins deterministically.
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b", ratio: 0.5);
        var bSplit = t.Split("b", Direction.Down, "c", ratio: 0.5);
        ((SplitNode)bSplit.Parent!).Ratio = 0.3; // b is 30% high, c 70%
        Assert.Equal("c", t.Neighbor("a", Direction.Right));
    }

    [Fact]
    public void Serialize_RoundTrip()
    {
        var t = new PaneTree("a");
        t.Split("a", Direction.Right, "b", 0.3);
        t.Split("b", Direction.Down, "c");

        var cwds = new Dictionary<string, string> { ["a"] = @"C:\a", ["b"] = @"C:\b", ["c"] = @"C:\c" };
        var dto = PaneLayoutSerializer.ToDto(t.Root!, id => (cwds[id], "pwsh"));

        var created = new List<string>();
        var rebuilt = new PaneTree(PaneLayoutSerializer.FromDto(dto, leaf => { created.Add(leaf.Cwd!); return "n" + created.Count; }));

        Assert.Equal([@"C:\a", @"C:\b", @"C:\c"], created);
        var root = Assert.IsType<SplitNode>(rebuilt.Root);
        Assert.Equal(0.3, root.Ratio, 6);
        Assert.Equal(["n1", "n2", "n3"], rebuilt.Leaves().Select(l => l.PaneId));
    }

    [Fact]
    public void Serialize_Tabs_RoundTripThroughJson()
    {
        var t = new PaneTree("s1");
        t.Split("s1", Direction.Right, "s2");
        var dto = PaneLayoutSerializer.ToDto(t.Root!, id => id == "s1"
            ? new PaneLayoutDto
            {
                Cwd = @"C:\b", Profile = "pwsh", ActiveTab = 1,
                Tabs = [new TabDto { Cwd = @"C:\a", Profile = "pwsh" }, new TabDto { Cwd = @"C:\b", Profile = "pwsh", Name = "server" }],
            }
            : new PaneLayoutDto { Cwd = @"C:\c", Profile = "cmd" });

        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        var back = System.Text.Json.JsonSerializer.Deserialize<PaneLayoutDto>(json)!;

        var leaves = new List<PaneLayoutDto>();
        PaneLayoutSerializer.FromDto(back, leaf => { leaves.Add(leaf); return "x" + leaves.Count; });
        Assert.Equal(2, leaves[0].AllTabs().Count);
        Assert.Equal("server", leaves[0].AllTabs()[1].Name);
        Assert.Equal(1, leaves[0].ActiveTab);
        Assert.Equal([@"C:\c"], leaves[1].AllTabs().Select(tab => tab.Cwd));
    }

    [Fact]
    public void Leaf_WithoutTabs_IsOneTab()
    {
        // Sessions saved before tabs existed: a leaf is just cwd + profile.
        var leaf = new PaneLayoutDto { Cwd = @"C:\old", Profile = "cmd" };
        var only = Assert.Single(leaf.AllTabs());
        Assert.Equal(@"C:\old", only.Cwd);
        Assert.Equal("cmd", only.Profile);
        Assert.Null(only.Name);
    }
}
