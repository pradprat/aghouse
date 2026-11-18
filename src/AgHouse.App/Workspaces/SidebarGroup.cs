namespace AgHouse.App.Workspaces;

/// <summary>
/// One project header in the grouped sidebar: the workspaces in one git repo (its worktrees included) or
/// one folder. The ListBox groups on <see cref="Workspace.Group"/>, so this object is the WPF group's
/// Name, and the header binds to it. A collapsed header still says when something in it needs you.
/// </summary>
public sealed class SidebarGroup(string key, string label) : ObservableObject
{
    private string _label = label;
    private bool _isExpanded = true;
    private int _count;
    private int _waitingCount;
    private int _workingCount;
    private bool _containsSelected;

    /// <summary>Stable and case-insensitive (<see cref="AgHouse.Core.ProjectKey.Key"/>); saved for collapsed groups.</summary>
    public string Key { get; } = key;

    public string Label { get => _label; set { if (Set(ref _label, value)) OnPropertyChanged(nameof(Summary)); } }

    /// <summary>Where the group sits: the position of its first workspace in the sidebar's own order.</summary>
    public int Order { get; set; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!Set(ref _isExpanded, value)) return;
            OnPropertyChanged(nameof(RowsHidden));
            OnPropertyChanged(nameof(ShowsRows));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(Note));
            OnPropertyChanged(nameof(ShowsWaiting));
            ExpandedChanged?.Invoke(this);
        }
    }

    public event Action<SidebarGroup>? ExpandedChanged;

    public int Count
    {
        get => _count;
        private set
        {
            if (!Set(ref _count, value)) return;
            OnPropertyChanged(nameof(HasHeader));
            OnPropertyChanged(nameof(RowsHidden));
            OnPropertyChanged(nameof(ShowsRows));
        }
    }

    /// <summary>
    /// A project with one workspace shows just its row: a header over it would only repeat the row's name.
    /// The header appears once a second workspace joins. Its row then drags as the whole group.
    /// </summary>
    public bool HasHeader => _count > 1;

    /// <summary>Collapsed, and there's a header to say what it hides (a lone row is never hidden).</summary>
    public bool RowsHidden => !_isExpanded && HasHeader;

    /// <summary>
    /// What the group's Expander binds to, so screen readers (WPF's group peer reads the Expander) hear the
    /// state on screen: a lone row always shows. Setting it is expanding or collapsing the group.
    /// </summary>
    public bool ShowsRows
    {
        get => !RowsHidden;
        set => IsExpanded = value;
    }

    /// <summary>Panes in this group's workspaces that need you.</summary>
    public int WaitingCount
    {
        get => _waitingCount;
        private set { if (Set(ref _waitingCount, value)) OnPropertyChanged(nameof(HasWaiting)); }
    }

    public bool HasWaiting => _waitingCount > 0;

    /// <summary>Agents in this group's workspaces that say they're working.</summary>
    public int WorkingCount
    {
        get => _workingCount;
        private set { if (Set(ref _workingCount, value)) OnPropertyChanged(nameof(WorkingText)); }
    }

    /// <summary>"Working" or "2 working", like the row chips; empty when nothing is.</summary>
    public string WorkingText => _workingCount switch { 0 => "", 1 => "Working", var n => $"{n} working" };

    /// <summary>
    /// What a collapsed header says about the rows it hides: "2 waiting", else "Working". Empty while
    /// expanded, where the rows say it themselves.
    /// </summary>
    public string Note => !RowsHidden ? "" : _waitingCount > 0 ? $"{_waitingCount} waiting" : WorkingText;

    /// <summary>A collapsed header shows the attention dot when a hidden pane needs you.</summary>
    public bool ShowsWaiting => RowsHidden && _waitingCount > 0;

    /// <summary>The selected workspace is in here: a collapsed header then carries the selection pill.</summary>
    public bool ContainsSelected { get => _containsSelected; set => Set(ref _containsSelected, value); }

    /// <summary>For screen readers: "aghouse, 3 workspaces, collapsed, 1 waiting".</summary>
    public string Summary =>
        $"{Label}, {Count} workspace{(Count == 1 ? "" : "s")}{(RowsHidden ? ", collapsed" : "")}"
        + (WaitingCount > 0 ? $", {WaitingCount} waiting" : "")
        + (WorkingCount > 0 ? $", {WorkingText.ToLowerInvariant()}" : "");

    /// <summary>Recounts from the group's workspaces.</summary>
    public void Update(IReadOnlyCollection<Workspace> members)
    {
        Count = members.Count;
        WaitingCount = members.Sum(w => w.WaitingCount);
        WorkingCount = members.Sum(w => w.WorkingCount);
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(ShowsWaiting));
    }

    /// <summary>What WPF shows for the group when no template does (UI Automation's name for the GroupItem).</summary>
    public override string ToString() => Label;
}
