using AgHouse.App.Persistence;

namespace AgHouse.App.Workspaces;

/// <summary>
/// The grouped sidebar (setting groupWorkspaces): workspaces under one header per project, meaning their
/// git repo (worktrees included) or else their folder. Grouping is only a view: <see cref="Workspaces"/> keeps
/// its order, and a group sits where its first workspace is. MainWindow turns the view's grouping on and off.
/// </summary>
public sealed partial class WorkspaceController
{
    /// <summary>Every group that has workspaces, by project key. Kept up to date in the flat view too.</summary>
    private readonly Dictionary<string, SidebarGroup> _groups = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Keys of collapsed groups, including saved ones whose workspaces haven't found their project yet.</summary>
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The sidebar shows groups. Bound to the toggle next to "Workspaces".</summary>
    public bool GroupWorkspaces => _settings.GroupWorkspaces;

    /// <summary>
    /// A workspace changed group, or the groups changed order: a grouped view has to group and sort again.
    /// (The view doesn't use live grouping: it would add new groups in arrival order and drop the selection later.)
    /// </summary>
    public event Action? SidebarGroupsChanged;

    /// <summary>The sidebar toggle and the palette: applied now and saved, as if changed in Settings.</summary>
    public void SetGroupWorkspaces(bool on)
    {
        if (on == _settings.GroupWorkspaces) return;
        var changed = _settings.Clone();
        changed.GroupWorkspaces = on;
        ApplySettings(changed);
        if (_settingsProblem is null && !AppStorage.SaveSettings(_settings)) Log.Write("settings: groupWorkspaces not saved");
    }

    /// <summary>After ApplySettings: the view regroups (MainWindow listens), and the selection stays in sight.</summary>
    private void GroupingSettingApplied()
    {
        if (_settings.GroupWorkspaces && _selected?.Group is { IsExpanded: false } g) g.IsExpanded = true;
        OnPropertyChanged(nameof(GroupWorkspaces));
    }

    /// <summary>
    /// Workspaces in the order the sidebar shows them: by group (groups where their first workspace is),
    /// then in their own order. Ctrl+1..9, Ctrl+Tab and the numbers in the palette and the CLI follow it.
    /// </summary>
    public List<Workspace> SidebarOrder() =>
        !_settings.GroupWorkspaces
            ? [.. Workspaces]
            : [.. Workspaces.Select((w, i) => (w, i)).OrderBy(x => x.w.Group?.Order ?? int.MaxValue).ThenBy(x => x.i).Select(x => x.w)];

    /// <summary>The workspace knows its project (or found a new one, after a cd): it joins that project's group.</summary>
    private void AssignGroup(Workspace ws)
    {
        if (!ws.HasProject || !Workspaces.Contains(ws)) return;
        var project = ws.Project;
        if (!_groups.TryGetValue(project.Key, out var group))
        {
            group = new SidebarGroup(project.Key, project.Label) { IsExpanded = !_collapsedGroups.Contains(project.Key) };
            group.ExpandedChanged += OnGroupExpandedChanged;
            _groups[project.Key] = group;
        }
        else group.Label = project.Label;
        if (ReferenceEquals(ws.Group, group)) return;
        ws.Group = group;
        UpdateGroups(regrouped: true);
    }

    /// <summary>Recounts every group, puts them in order, and drops the empty ones. <paramref name="regrouped"/>: a workspace changed group.</summary>
    private void UpdateGroups(bool regrouped = false)
    {
        var orderChanged = false;
        var order = 0;
        var members = new Dictionary<SidebarGroup, List<Workspace>>();
        foreach (var ws in Workspaces)
        {
            if (ws.Group is not { } g) continue;
            if (!members.TryGetValue(g, out var list))
            {
                members[g] = list = [];
                orderChanged |= g.Order != order;
                g.Order = order++;
            }
            list.Add(ws);
        }
        foreach (var g in _groups.Values.ToList())
        {
            if (!members.TryGetValue(g, out var list))
            {
                // Its last workspace closed or moved on: a later group with that key starts expanded.
                _groups.Remove(g.Key);
                _collapsedGroups.Remove(g.Key);
                g.ExpandedChanged -= OnGroupExpandedChanged;
                continue;
            }
            g.Update(list);
            g.ContainsSelected = ReferenceEquals(_selected?.Group, g);
        }
        if (orderChanged || regrouped) SidebarGroupsChanged?.Invoke();
    }

    private void OnGroupExpandedChanged(SidebarGroup g)
    {
        if (g.IsExpanded) _collapsedGroups.Remove(g.Key); else _collapsedGroups.Add(g.Key);
        ScheduleSave();
    }

    /// <summary>"Collapse all" / "Expand all" in a group header's menu.</summary>
    public void SetAllGroupsExpanded(bool expanded)
    {
        foreach (var g in _groups.Values) g.IsExpanded = expanded;
    }

    /// <summary>Collapsed groups for session.json. Saved keys nothing has claimed yet are kept until every workspace knows its project.</summary>
    private List<string>? SavedCollapsedGroups()
    {
        if (Workspaces.All(w => w.HasProject)) _collapsedGroups.IntersectWith(_groups.Keys);
        return _collapsedGroups.Count == 0 ? null : [.. _collapsedGroups.Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Move up/down by one row as the sidebar shows it. Grouped, that's within the workspace's own group
    /// (its group comes from its folder, so it can't be moved into another).
    /// </summary>
    public void MoveWorkspaceBy(Workspace ws, int delta)
    {
        if (!_settings.GroupWorkspaces || ws.Group is null)
        {
            MoveWorkspace(ws, Workspaces.IndexOf(ws) + delta);
            return;
        }
        var members = SidebarOrder().Where(w => ReferenceEquals(w.Group, ws.Group)).ToList();
        var i = members.IndexOf(ws) + delta;
        if (i < 0 || i >= members.Count) return;
        MoveWorkspace(ws, Workspaces.IndexOf(members[i]));
    }

    /// <summary>The groups in the order the sidebar shows them.</summary>
    public IReadOnlyList<SidebarGroup> GroupsInOrder() => [.. _groups.Values.OrderBy(g => g.Order)];

    /// <summary>
    /// Drags a group header to the gap before group <paramref name="gap"/> (0..count, in sidebar order).
    /// Its workspaces move together; each group's workspaces keep their order.
    /// </summary>
    public void MoveGroup(SidebarGroup group, int gap)
    {
        var groups = GroupsInOrder().ToList();
        var from = groups.IndexOf(group);
        if (from < 0) return;
        groups.RemoveAt(from);
        if (gap > from) gap--;
        gap = Math.Clamp(gap, 0, groups.Count);
        if (gap == from) return;
        groups.Insert(gap, group);

        List<Workspace> target =
        [
            .. groups.SelectMany(g => Workspaces.Where(w => ReferenceEquals(w.Group, g))),
            .. Workspaces.Where(w => w.Group is null),
        ];
        for (var i = 0; i < target.Count; i++)
        {
            var at = Workspaces.IndexOf(target[i]);
            if (at != i) Workspaces.Move(at, i);
        }
        Log.Write($"move group {group.Key} -> {gap}");
        UpdateGroups();
        OnPropertyChanged(nameof(Selected)); // the list can drop its selection when the selected row moves
        ScheduleSave();
    }
}
