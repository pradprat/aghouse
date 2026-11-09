using System.Text.Json;
using System.Text.Json.Serialization;
using AgHouse.Core.Layout;

namespace AgHouse.Tests;

/// <summary>
/// Session persistence, Core side. <c>AppStorage</c> (App project) saves <see cref="SessionDto"/> with the
/// options below; <c>AppStorage.Json</c> is private and the file path is fixed to %APPDATA%, so the tests mirror
/// the options. If those options change in AppStorage, update <see cref="Json"/> here too.
/// </summary>
public class SessionModelTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static SessionDto Sample()
    {
        var tree = new PaneTree("a");
        tree.Split("a", Direction.Right, "b", 0.3);
        tree.Split("b", Direction.Down, "c", 0.6);
        var info = new Dictionary<string, (string?, string?)>
        {
            ["a"] = (@"C:\work\one", "pwsh"),
            ["b"] = (@"C:\work\two", "gitbash"),
            ["c"] = (null, null),
        };
        return new SessionDto
        {
            SelectedIndex = 1,
            Workspaces =
            [
                new WorkspaceDto { Name = "agents", Layout = PaneLayoutSerializer.ToDto(tree.Root!, id => info[id]) },
                new WorkspaceDto { Name = "solo", Layout = new PaneLayoutDto { Cwd = @"C:\solo" } },
            ],
        };
    }

    [Fact]
    public void SessionDto_JsonRoundTrip_PreservesEverything()
    {
        var back = JsonSerializer.Deserialize<SessionDto>(JsonSerializer.Serialize(Sample(), Json), Json)!;

        Assert.Equal(1, back.SelectedIndex);
        Assert.Equal(["agents", "solo"], back.Workspaces.Select(w => w.Name));

        var root = back.Workspaces[0].Layout!;
        Assert.Equal("split", root.Type);
        Assert.Equal(SplitOrientation.Columns, root.Orientation);
        Assert.Equal(0.3, root.Ratio, 6);
        Assert.Equal(@"C:\work\one", root.First!.Cwd);
        Assert.Equal("pwsh", root.First.Profile);

        var inner = root.Second!;
        Assert.Equal(SplitOrientation.Rows, inner.Orientation);
        Assert.Equal(0.6, inner.Ratio, 6);
        Assert.Equal(@"C:\work\two", inner.First!.Cwd);
        Assert.Null(inner.Second!.Cwd);
        Assert.Null(inner.Second.Profile);

        Assert.Equal("leaf", back.Workspaces[1].Layout!.Type);
        Assert.Equal(@"C:\solo", back.Workspaces[1].Layout!.Cwd);
    }

    [Fact]
    public void SessionFile_Format_IsCamelCase_WithStringEnums()
    {
        // This is what lands in %APPDATA%\aghouse\session.json; renaming a property would orphan existing files.
        var json = JsonSerializer.Serialize(Sample(), Json);
        Assert.Contains("\"selectedIndex\": 1", json);
        Assert.Contains("\"workspaces\"", json);
        Assert.Contains("\"orientation\": \"columns\"", json);
        Assert.Contains("\"orientation\": \"rows\"", json);
        Assert.Contains("\"type\": \"split\"", json);
        Assert.Contains("\"ratio\": 0.3", json);
    }

    [Fact]
    public void HandWrittenSessionFile_Loads()
    {
        const string file = """
            {
              "selectedIndex": 0,
              "workspaces": [
                {
                  "name": "dev",
                  "layout": {
                    "type": "split", "orientation": "rows", "ratio": 0.25,
                    "first":  { "type": "leaf", "cwd": "C:\\a", "profile": "pwsh" },
                    "second": { "type": "leaf", "cwd": "C:\\b" }
                  }
                }
              ]
            }
            """;

        var session = JsonSerializer.Deserialize<SessionDto>(file, Json)!;
        var layout = session.Workspaces.Single().Layout!;
        Assert.Equal(SplitOrientation.Rows, layout.Orientation);
        Assert.Equal(0.25, layout.Ratio, 6);
        Assert.Equal(@"C:\b", layout.Second!.Cwd);
        Assert.Null(layout.Second.Profile);
    }

    [Fact]
    public void EmptyObject_GivesEmptySession()
    {
        var session = JsonSerializer.Deserialize<SessionDto>("{}", Json)!;
        Assert.Empty(session.Workspaces);
        Assert.Equal(0, session.SelectedIndex);
    }

    [Fact]
    public void UnknownFields_AreIgnored_SoNewerFilesStillLoad()
    {
        var session = JsonSerializer.Deserialize<SessionDto>("""{"selectedIndex":2,"futureThing":{"x":1},"workspaces":[]}""", Json)!;
        Assert.Equal(2, session.SelectedIndex);
    }

    [Theory]
    [InlineData("{ truncated")]
    [InlineData("")]
    [InlineData("""{"workspaces":"not a list"}""")]
    [InlineData("""{"workspaces":[{"layout":{"orientation":"diagonal"}}]}""")]
    public void CorruptSessionFile_ThrowsJsonException(string text)
    {
        // AppStorage.Read catches JsonException and starts fresh instead of crashing at launch.
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<SessionDto>(text, Json));
    }

    [Fact]
    public void FromDto_Leaf_CallsFactoryOncePerLeaf_WithItsDto()
    {
        var dto = new PaneLayoutDto { Cwd = @"C:\x", Profile = "cmd" };
        var seen = new List<PaneLayoutDto>();
        var node = PaneLayoutSerializer.FromDto(dto, d => { seen.Add(d); return "p1"; });

        Assert.Equal("p1", Assert.IsType<PaneLeaf>(node).PaneId);
        Assert.Same(dto, Assert.Single(seen));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FromDto_SplitWithOneChild_DegradesToThatChild(bool hasFirst, bool hasSecond)
    {
        var child = new PaneLayoutDto { Cwd = "only" };
        var dto = new PaneLayoutDto
        {
            Type = "split",
            First = hasFirst ? child : null,
            Second = hasSecond ? child : null,
        };

        var node = PaneLayoutSerializer.FromDto(dto, d => d.Cwd!);
        Assert.Equal("only", Assert.IsType<PaneLeaf>(node).PaneId);
    }

    [Fact]
    public void FromDto_SplitWithNoChildren_BecomesALeaf()
    {
        var node = PaneLayoutSerializer.FromDto(new PaneLayoutDto { Type = "split" }, _ => "fresh");
        Assert.Equal("fresh", Assert.IsType<PaneLeaf>(node).PaneId);
    }

    [Fact]
    public void FromDto_UnknownType_IsTreatedAsALeaf()
    {
        var node = PaneLayoutSerializer.FromDto(new PaneLayoutDto { Type = "tabs" }, _ => "x");
        Assert.IsType<PaneLeaf>(node);
    }

    [Theory]
    [InlineData(-3.0, 0.05)]
    [InlineData(0.0, 0.05)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 0.95)]
    [InlineData(42.0, 0.95)]
    public void FromDto_OutOfRangeRatio_IsClamped(double stored, double expected)
    {
        var dto = new PaneLayoutDto
        {
            Type = "split",
            Ratio = stored,
            First = new PaneLayoutDto(),
            Second = new PaneLayoutDto(),
        };
        var n = 0;
        var split = Assert.IsType<SplitNode>(PaneLayoutSerializer.FromDto(dto, _ => "p" + n++));
        Assert.Equal(expected, split.Ratio, 6);
    }

    [Fact]
    public void FromDto_SetsParentLinks_SoTheTreeIsUsable()
    {
        var dto = JsonSerializer.Deserialize<SessionDto>(JsonSerializer.Serialize(Sample(), Json), Json)!.Workspaces[0].Layout!;
        var n = 0;
        var tree = new PaneTree(PaneLayoutSerializer.FromDto(dto, _ => "n" + ++n));

        Assert.Equal(["n1", "n2", "n3"], tree.Leaves().Select(l => l.PaneId));
        Assert.All(tree.Leaves(), l => Assert.NotNull(l.Parent));
        // The restored tree must support normal operations: split and remove.
        tree.Split("n3", Direction.Left, "n4");
        Assert.Equal("n3", tree.Remove("n4"));
    }

    [Fact]
    public void Layout_SurvivesAFullSaveAndRestoreCycle()
    {
        var tree = new PaneTree("a");
        tree.Split("a", Direction.Right, "b", 0.3);
        tree.Split("b", Direction.Down, "c", 0.6);
        tree.Split("a", Direction.Up, "d", 0.4);
        var before = tree.Layout();

        var dto = PaneLayoutSerializer.ToDto(tree.Root!, _ => (null, null));
        var json = JsonSerializer.Serialize(new WorkspaceDto { Name = "w", Layout = dto }, Json);
        var restoredDto = JsonSerializer.Deserialize<WorkspaceDto>(json, Json)!.Layout!;

        // Leaves are recreated in the same depth-first order, so new ids map positionally onto the old ones.
        var oldIds = tree.Leaves().Select(l => l.PaneId).ToList();
        var i = 0;
        var restored = new PaneTree(PaneLayoutSerializer.FromDto(restoredDto, _ => oldIds[i++]));
        var after = restored.Layout();

        Assert.Equal(before.Keys.OrderBy(k => k), after.Keys.OrderBy(k => k));
        foreach (var (id, rect) in before)
        {
            Assert.Equal(rect.X, after[id].X, 6);
            Assert.Equal(rect.Y, after[id].Y, 6);
            Assert.Equal(rect.W, after[id].W, 6);
            Assert.Equal(rect.H, after[id].H, 6);
        }
    }

    [Fact]
    public void ToDto_AsksForInfoOnceForEachPane()
    {
        var tree = new PaneTree("a");
        tree.Split("a", Direction.Right, "b");
        tree.Split("b", Direction.Down, "c");
        var asked = new List<string>();

        PaneLayoutSerializer.ToDto(tree.Root!, id => { asked.Add(id); return (null, null); });

        Assert.Equal(["a", "b", "c"], asked);
    }
}
