namespace AgHouse.Core.Layout;

/// <summary>Serialized pane layout (session restore). A node is either a split or a leaf.</summary>
public sealed class PaneLayoutDto
{
    public string Type { get; set; } = "leaf";          // "leaf" | "split"
    public SplitOrientation Orientation { get; set; }
    public double Ratio { get; set; } = 0.5;
    public PaneLayoutDto? First { get; set; }
    public PaneLayoutDto? Second { get; set; }

    // Leaf data
    public string? Cwd { get; set; }
    public string? Profile { get; set; }
}

public sealed class WorkspaceDto
{
    public string Name { get; set; } = "";
    public PaneLayoutDto? Layout { get; set; }
}

public sealed class SessionDto
{
    public List<WorkspaceDto> Workspaces { get; set; } = [];
    public int SelectedIndex { get; set; }
}

public static class PaneLayoutSerializer
{
    public static PaneLayoutDto ToDto(PaneNode node, Func<string, (string? Cwd, string? Profile)> leafInfo) => node switch
    {
        PaneLeaf l => LeafDto(leafInfo(l.PaneId)),
        SplitNode s => new PaneLayoutDto
        {
            Type = "split",
            Orientation = s.Orientation,
            Ratio = s.Ratio,
            First = ToDto(s.First, leafInfo),
            Second = ToDto(s.Second, leafInfo),
        },
        _ => throw new InvalidOperationException(),
    };

    private static PaneLayoutDto LeafDto((string? Cwd, string? Profile) info) =>
        new() { Type = "leaf", Cwd = info.Cwd, Profile = info.Profile };

    /// <summary>Rebuilds a tree. <paramref name="createLeaf"/> returns the new pane id for each leaf DTO.</summary>
    public static PaneNode FromDto(PaneLayoutDto dto, Func<PaneLayoutDto, string> createLeaf)
    {
        if (dto.Type == "split" && dto.First is not null && dto.Second is not null)
        {
            return new SplitNode(dto.Orientation, FromDto(dto.First, createLeaf), FromDto(dto.Second, createLeaf), dto.Ratio);
        }
        // A malformed split degrades to whichever child exists.
        if (dto.Type == "split" && (dto.First ?? dto.Second) is { } only) return FromDto(only, createLeaf);
        return new PaneLeaf(createLeaf(dto));
    }
}
