using System.IO;
using AgHouse.App.Terminal;
using AgHouse.Core;
using AgHouse.Core.Layout;

namespace AgHouse.App.Workspaces;

/// <summary>One chip in a sidebar row. <see cref="Port"/> is set for a listening port, which opens in the browser.</summary>
public sealed record SidebarChip(string Text, string? ToolTip, int? Port);

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
    /// <summary>Every terminal in the workspace, tabs included, by pane id (AGHOUSE_PANE_ID).</summary>
    public Dictionary<string, PaneSession> Panes { get; } = [];

    /// <summary>The layout cells; the tree's leaves hold slot ids. Each slot holds one or more panes as tabs.</summary>
    public Dictionary<string, PaneSlot> Slots { get; } = [];

    public PaneSlot? SlotOf(PaneSession pane) => Slots.GetValueOrDefault(pane.SlotId);

    /// <summary>The slot filling the whole workspace (pane zoom, Ctrl+Shift+Enter), or null.</summary>
    public string? ZoomedSlotId { get; set; }

    /// <summary>Every pane in layout order: slots left to right and top to bottom, then tabs left to right.</summary>
    public IEnumerable<PaneSession> PanesInOrder() =>
        Tree?.Root is null ? [] : Tree.Leaves().SelectMany(l => Slots.TryGetValue(l.PaneId, out var s) ? s.Tabs : []);
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

    private string _waitingSuffix = "";

    /// <summary>" · message" after the row's "Needs you", or empty when the pane didn't say why (a bell).</summary>
    public string WaitingSuffix { get => _waitingSuffix; private set => Set(ref _waitingSuffix, value); }

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

    // ───────────── What agents and scripts report (cmux-style sidebar metadata) ─────────────
    // Held at runtime only, never saved: a restored session has no agents running yet.

    /// <summary>The key hooks use for an agent's own state ("Working"). One entry per pane, not per workspace.</summary>
    public const string AgentKey = "agent";

    private readonly List<StatusEntry> _status = [];
    private IReadOnlyList<int> _ports = [];
    private IReadOnlyList<SidebarChip> _chips = [];
    private double? _progress;
    private string? _progressLabel;
    private readonly List<LogEntry> _log = [];
    private string? _pullRequest;
    private string _details = "";

    private sealed record StatusEntry(string Key, string Text, string? PaneId);
    private sealed record LogEntry(string Text, bool IsProblem);

    /// <summary>Neutral chips under the location: agent state, `aghouse set-status` entries, listening ports.</summary>
    public IReadOnlyList<SidebarChip> Chips
    {
        get => _chips;
        private set { _chips = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChips)); }
    }

    public bool HasChips => _chips.Count > 0;

    /// <summary>0..1, or null when nothing is reporting progress.</summary>
    public double? Progress
    {
        get => _progress;
        private set
        {
            if (!Set(ref _progress, value)) return;
            OnPropertyChanged(nameof(HasProgress));
            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(ProgressText));
        }
    }

    public bool HasProgress => _progress is not null;
    public double ProgressPercent => (_progress ?? 0) * 100;

    /// <summary>"Tests 40%", or "40%" without a label.</summary>
    public string ProgressText =>
        _progress is not { } p ? "" : string.IsNullOrWhiteSpace(_progressLabel) ? $"{Math.Round(p * 100)}%" : $"{_progressLabel} {Math.Round(p * 100)}%";

    /// <summary>The newest `aghouse log` line.</summary>
    public string? LastLog => _log.Count > 0 ? _log[^1].Text : null;
    public bool HasLog => _log.Count > 0;
    public bool LastLogIsProblem => _log.Count > 0 && _log[^1].IsProblem;

    /// <summary>The last 20 log lines, newest first (the log line's tooltip).</summary>
    public string? LogHistory => _log.Count == 0 ? null : string.Join("\n", Enumerable.Reverse(_log).Select(l => l.Text));

    /// <summary>"#123 open" for the active pane's branch, from gh. Null when there's none or gh can't say.</summary>
    public string? PullRequest
    {
        get => _pullRequest;
        set { if (Set(ref _pullRequest, value)) { OnPropertyChanged(nameof(HasPullRequest)); RefreshDetails(); } }
    }

    public bool HasPullRequest => !string.IsNullOrEmpty(_pullRequest);

    /// <summary>The row's help text for screen readers: where it is, then everything the chips, bar and log say.</summary>
    public string Details { get => _details; private set => Set(ref _details, value); }

    public void SetStatus(string key, string text, string? paneId)
    {
        var agent = key == AgentKey;
        var i = _status.FindIndex(s => s.Key == key && (!agent || s.PaneId == paneId));
        var entry = new StatusEntry(key, text.Trim(), agent ? paneId : null);
        if (i >= 0) _status[i] = entry; else _status.Add(entry);
        RebuildChips();
    }

    /// <summary>Clears one status. The agent key clears only that pane's entry.</summary>
    public void ClearStatus(string key, string? paneId)
    {
        var agent = key == AgentKey;
        if (_status.RemoveAll(s => s.Key == key && (!agent || s.PaneId == paneId)) > 0) RebuildChips();
    }

    /// <summary>A pane went away: its agent can't be working any more.</summary>
    public void ForgetPane(string paneId)
    {
        if (_status.RemoveAll(s => s.PaneId == paneId) > 0) RebuildChips();
    }

    public void SetProgress(double? value, string? label)
    {
        _progressLabel = value is null ? null : label?.Trim();
        Progress = value is { } v ? Math.Clamp(v, 0, 1) : null;
        OnPropertyChanged(nameof(ProgressText)); // the label can change with the same value
        RefreshDetails();
    }

    public void AddLog(string text, bool isProblem)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        if (line.Length == 0) return;
        _log.Add(new LogEntry(line, isProblem));
        if (_log.Count > 20) _log.RemoveAt(0);
        OnPropertyChanged(nameof(LastLog));
        OnPropertyChanged(nameof(HasLog));
        OnPropertyChanged(nameof(LastLogIsProblem));
        OnPropertyChanged(nameof(LogHistory));
        RefreshDetails();
    }

    public void SetPorts(IReadOnlyList<int> ports)
    {
        if (ports.SequenceEqual(_ports)) return;
        _ports = ports;
        RebuildChips();
    }

    /// <summary>The most chips a row shows before the rest fold into "+N".</summary>
    private const int MaxChips = 5;

    private void RebuildChips()
    {
        var all = new List<SidebarChip>();
        // Agents first, grouped by what they say: "Working", or "2 working" (like "2 waiting").
        foreach (var group in _status.Where(s => s.Key == AgentKey).GroupBy(s => s.Text, StringComparer.OrdinalIgnoreCase))
        {
            var count = group.Count();
            all.Add(new SidebarChip(count == 1 ? group.Key : $"{count} {group.Key.ToLowerInvariant()}", null, null));
        }
        foreach (var s in _status.Where(s => s.Key != AgentKey)) all.Add(new SidebarChip(s.Text, $"{s.Key}: {s.Text}", null));
        foreach (var port in _ports) all.Add(new SidebarChip($":{port}", $"Open http://localhost:{port}", port));

        if (all.Count > MaxChips + 1)
        {
            var rest = all.Skip(MaxChips).ToList();
            all = [.. all.Take(MaxChips), new SidebarChip($"+{rest.Count}", string.Join("\n", rest.Select(c => c.Text)), null)];
        }
        Chips = all;
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        var parts = new List<string>();
        if (Location is { } loc) parts.Add(loc);
        if (Branch is { } b) parts.Add($"branch {b}");
        if (PullRequest is { } pr) parts.Add($"pull request {pr}");
        var chipText = _status.Select(s => s.Text).Concat(_ports.Select(p => $"port {p}")).ToList();
        if (chipText.Count > 0) parts.Add(string.Join(", ", chipText));
        if (HasProgress) parts.Add(ProgressText);
        if (LastLog is { } log) parts.Add(log);
        Details = string.Join("; ", parts);
    }

    /// <summary>Recompute everything the sidebar shows from the panes.</summary>
    public void RefreshSummary()
    {
        var waiting = Panes.Values.Where(p => p.HasUnread).OrderByDescending(p => p.UnreadAt).ToList();
        HasUnread = waiting.Count > 0;
        var messages = waiting.Select(p => p.AttentionMessage).OfType<string>().ToList();
        LastMessage = messages.FirstOrDefault();
        WaitingSuffix = LastMessage is { } newest ? " · " + newest : "";
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
            _ = RefreshPullRequestAsync();
        }
        OnPropertyChanged(nameof(PaneCount));
        RefreshDetails();
        RefreshZoomNote(waiting);
    }

    /// <summary>
    /// While a pane is zoomed, its header says how many panes are hidden, or names the hidden pane
    /// that needs you (newest first), so zooming in never hides a request.
    /// </summary>
    private void RefreshZoomNote(List<PaneSession> waitingNewestFirst)
    {
        foreach (var slot in Slots.Values)
        {
            if (slot.Id != ZoomedSlotId) { slot.SetZoom(0, null); continue; }
            var waiting = waitingNewestFirst.FirstOrDefault(p => p.SlotId != slot.Id);
            slot.SetZoom(Math.Max(1, Slots.Count - 1), waiting?.Label);
        }
    }

    private int _prRequest;

    /// <summary>Ask gh about the active pane's branch. A newer request wins if the user switches folders meanwhile.</summary>
    public async Task RefreshPullRequestAsync()
    {
        var request = ++_prRequest;
        var (cwd, branch) = (ActivePane?.Cwd, Branch);
        if (branch is null) { PullRequest = null; return; }
        var pr = await PullRequests.LookupAsync(cwd, branch); // resumes on the UI thread
        if (request == _prRequest) PullRequest = pr;
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
