using System.IO;
using AgHouse.App.Terminal;
using AgHouse.Core;
using AgHouse.Core.Layout;

namespace AgHouse.App.Workspaces;

/// <summary>A sidebar entry: a split tree of panes, plus the summary shown in the sidebar.</summary>
public sealed class Workspace : ObservableObject
{
    private string _name;
    private string? _activePaneId;
    private bool _isSelected;
    private bool _hasUnread;
    private string? _lastMessage;
    private string? _waitingMessages;
    private int _waitingCount;
    private string _attentionStatus = "";
    private string? _location;
    private string? _branch;
    private bool _isRenaming;
    private string? _lastBranchDir;

    public Workspace(string id, string name)
    {
        Id = id;
        _name = name;
        View = new WorkspaceView();
    }

    public string Id { get; }
    public PaneTree Tree { get; set; } = null!;
    public Dictionary<string, PaneSession> Panes { get; } = [];
    public WorkspaceView View { get; }

    public string Name { get => _name; set => Set(ref _name, value); }

    public string? ActivePaneId { get => _activePaneId; set => Set(ref _activePaneId, value); }

    public PaneSession? ActivePane =>
        _activePaneId is not null && Panes.TryGetValue(_activePaneId, out var p) ? p : null;

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public bool HasUnread { get => _hasUnread; private set => Set(ref _hasUnread, value); }

    /// <summary>
    /// The newest message from a pane that still needs the user. Derived from the panes, so it clears
    /// as soon as nothing is waiting: the sidebar never shows a request that was already handled.
    /// </summary>
    public string? LastMessage
    {
        get => _lastMessage;
        private set { if (Set(ref _lastMessage, value)) OnPropertyChanged(nameof(HasLastMessage)); }
    }

    public bool HasLastMessage => !string.IsNullOrEmpty(_lastMessage);

    /// <summary>Every waiting message, newest first, one per line (the sidebar tooltip).</summary>
    public string? WaitingMessages { get => _waitingMessages; private set => Set(ref _waitingMessages, value); }

    /// <summary>How many panes in this workspace need the user.</summary>
    public int WaitingCount { get => _waitingCount; private set => Set(ref _waitingCount, value); }

    /// <summary>The row's status for screen readers (UIA ItemStatus): "Needs you: message", or empty.</summary>
    public string AttentionStatus { get => _attentionStatus; private set => Set(ref _attentionStatus, value); }

    /// <summary>Short cwd of the active pane, e.g. "~\src\aghouse".</summary>
    public string? Location { get => _location; private set => Set(ref _location, value); }

    public string? Branch
    {
        get => _branch;
        private set { if (Set(ref _branch, value)) OnPropertyChanged(nameof(HasBranch)); }
    }

    public bool HasBranch => !string.IsNullOrEmpty(_branch);

    public bool IsRenaming { get => _isRenaming; set => Set(ref _isRenaming, value); }

    public int PaneCount => Panes.Count;

    /// <summary>Recompute everything the sidebar shows from the panes.</summary>
    public void RefreshSummary()
    {
        var waiting = Panes.Values.Where(p => p.HasUnread).OrderByDescending(p => p.UnreadAt).ToList();
        HasUnread = waiting.Count > 0;
        var messages = waiting.Select(p => p.AttentionMessage).OfType<string>().ToList();
        LastMessage = messages.FirstOrDefault();
        WaitingMessages = messages.Count > 0 ? string.Join("\n", messages) : null;
        WaitingCount = waiting.Count;
        AttentionStatus = waiting.Count == 0 ? ""
            : LastMessage is { } m ? $"Needs you: {m}"
            : "Needs you";
        var cwd = ActivePane?.Cwd;
        Location = ShortenPath(cwd);
        if (cwd != _lastBranchDir)
        {
            _lastBranchDir = cwd;
            Branch = GitInfo.TryGetBranch(cwd);
        }
        OnPropertyChanged(nameof(PaneCount));
    }

    /// <summary>Re-read the branch even if cwd didn't change (e.g. after `git checkout`).</summary>
    public void RefreshBranch()
    {
        _lastBranchDir = null;
        RefreshSummary();
    }

    private static string? ShortenPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
            path = "~" + path[home.Length..];

        var parts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 3 ? "…\\" + string.Join('\\', parts[^2..]) : path;
    }
}
