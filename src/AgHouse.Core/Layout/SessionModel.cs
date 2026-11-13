namespace AgHouse.Core.Layout;

/// <summary>Serialized pane layout (session restore). A node is either a split or a leaf.</summary>
public sealed class PaneLayoutDto
{
    public string Type { get; set; } = "leaf";          // "leaf" | "split"
    public SplitOrientation Orientation { get; set; }
    public double Ratio { get; set; } = 0.5;
    public PaneLayoutDto? First { get; set; }
    public PaneLayoutDto? Second { get; set; }

    // Leaf data. A leaf with one terminal keeps the original Cwd/Profile shape; a leaf with tabs adds
    // Tabs (Cwd/Profile then mirror the active tab, so an older aghouse still restores something sensible).
    public string? Cwd { get; set; }
    public string? Profile { get; set; }
    public List<TabDto>? Tabs { get; set; }
    public int ActiveTab { get; set; }

    /// <summary>The leaf's tabs: <see cref="Tabs"/>, or the single terminal described by Cwd/Profile.</summary>
    public IReadOnlyList<TabDto> AllTabs() =>
        Tabs is { Count: > 0 } tabs ? tabs : [new TabDto { Cwd = Cwd, Profile = Profile }];
}

/// <summary>One terminal in a pane's tab strip.</summary>
public sealed class TabDto
{
    public string? Cwd { get; set; }
    public string? Profile { get; set; }

    /// <summary>A name the user gave the tab; null when it's named after what it runs.</summary>
    public string? Name { get; set; }
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

    /// <summary>
    /// Set once the first notification arrives (hook, OSC 9/777, or `aghouse notify`). Until then the
    /// sidebar footer suggests setting up agent hooks, since nothing else tells a new user they exist.
    /// </summary>
    public bool NotifiedOnce { get; set; }

    /// <summary>Where the window was and how wide the sidebar was. Null in sessions from before 0.1.</summary>
    public WindowDto? Window { get; set; }
}

/// <summary>Window placement in device-independent pixels (the restore bounds when maximized).</summary>
public sealed class WindowDto
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
    public double SidebarWidth { get; set; }
}

public static class PaneLayoutSerializer
{
    public static PaneLayoutDto ToDto(PaneNode node, Func<string, (string? Cwd, string? Profile)> leafInfo) =>
        ToDto(node, id => LeafDto(leafInfo(id)));

    /// <summary>Serializes a tree; <paramref name="leafDto"/> describes each leaf (its tabs, cwd, profile).</summary>
    public static PaneLayoutDto ToDto(PaneNode node, Func<string, PaneLayoutDto> leafDto) => node switch
    {
        PaneLeaf l => leafDto(l.PaneId),
        SplitNode s => new PaneLayoutDto
        {
            Type = "split",
            Orientation = s.Orientation,
            Ratio = s.Ratio,
            First = ToDto(s.First, leafDto),
            Second = ToDto(s.Second, leafDto),
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
