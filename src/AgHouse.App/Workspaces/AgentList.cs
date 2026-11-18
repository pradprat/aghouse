using AgHouse.Core;
using AgHouse.App.Surfaces;
using AgHouse.App.Terminal;

namespace AgHouse.App.Workspaces;

/// <summary>Where an agent stands, in the order the sidebar's Agents list shows them.</summary>
public enum AgentState
{
    /// <summary>Asked for the user or finished: the pane says "Needs you".</summary>
    Waiting,
    /// <summary>Its hook said "Working" (Claude Code: a prompt was submitted, or a tool ran).</summary>
    Working,
    /// <summary>Still running, but neither working nor waiting: the user has seen its answer.</summary>
    Idle,
}

/// <summary>A state's heading in the Agents list: "Needs you  2", "Working  1", "Idle  3".</summary>
public sealed class AgentGroupHeader(AgentState state) : ObservableObject
{
    private int _count;

    public AgentState State { get; } = state;

    /// <summary>The One-Word Rule: a waiting agent "Needs you"; a busy one is "Working".</summary>
    public string Label { get; } = state switch
    {
        AgentState.Waiting => "Needs you",
        AgentState.Working => "Working",
        _ => "Idle",
    };

    public int Count { get => _count; set { if (Set(ref _count, value)) OnPropertyChanged(nameof(Summary)); } }

    /// <summary>For screen readers: "Needs you, 2 agents".</summary>
    public string Summary => $"{Label}, {_count} agent{(_count == 1 ? "" : "s")}";
}

/// <summary>
/// One agent in the sidebar's Agents list: a pane whose agent has reported in (a Claude Code hook, or
/// <c>aghouse notify</c> / <c>set-status agent</c>) and is still running. Clicking it goes to the pane.
/// </summary>
public sealed class AgentRow(Workspace workspace, PaneSession pane) : ObservableObject
{
    private AgentState _state = AgentState.Idle;
    private DateTime _since = DateTime.UtcNow;
    private string _workspaceName = workspace.Name;
    private string? _task;
    private string? _message;
    private string _age = "now";
    private int _tileIndex = workspace.TileIndex;

    public Workspace Workspace { get; } = workspace;
    public PaneSession Pane { get; } = pane;

    public AgentState State
    {
        get => _state;
        private set
        {
            if (!Set(ref _state, value)) return;
            OnPropertyChanged(nameof(IsWaiting));
            OnPropertyChanged(nameof(IsWorking));
            OnPropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsWaiting => _state == AgentState.Waiting;
    public bool IsWorking => _state == AgentState.Working;
    public bool IsIdle => _state == AgentState.Idle;

    /// <summary>When it entered its current state (for a waiting agent, when it asked).</summary>
    public DateTime Since => _since;

    public string WorkspaceName { get => _workspaceName; private set => Set(ref _workspaceName, value); }

    /// <summary>
    /// What the row leads with, in Primary Text: for an agent that needs you, its request (the words the workspace
    /// row and the pane header show, so all three agree); otherwise its task (the title it set). The workspace's
    /// name when there is neither.
    /// </summary>
    public string Headline =>
        (_state == AgentState.Waiting && !string.IsNullOrEmpty(_message) ? _message
            : !string.IsNullOrEmpty(_task) ? _task
            : null) ?? _workspaceName;

    /// <summary>" · workspace" after the headline, in Secondary Text; empty when the headline is the workspace's name.</summary>
    public string Context => Headline == _workspaceName ? "" : " · " + _workspaceName;

    private bool _markQuiet;

    /// <summary>
    /// Every agent in the list is the same program: its mark says nothing a row doesn't share, so a brand-coloured
    /// mark (Claude Code's terracotta, Amp's red) steps down to Secondary Text instead of repeating down the list.
    /// </summary>
    public bool MarkQuiet { get => _markQuiet; set => Set(ref _markQuiet, value); }

    /// <summary>The workspace's tile colour (kept for the rail and tooltips; the row's mark is the agent's own).</summary>
    public int TileIndex { get => _tileIndex; private set => Set(ref _tileIndex, value); }

    /// <summary>
    /// Which agent this is ("claude", "opencode"), from the program running in the pane; it picks the row's
    /// mark. Null for an agent known only because it reported in, which keeps the generic agent glyph.
    /// </summary>
    public string? AgentKey => _agent?.Key;

    /// <summary>Two letters for an agent with no drawn mark ("Cx" for Codex).</summary>
    public string? Monogram => _agent?.Monogram;

    private AgentProgram? _agent;

    /// <summary>"4 min" since it entered its state, as the waiting list says it.</summary>
    public string Age { get => _age; private set => Set(ref _age, value); }

    /// <summary>"aghouse › Claude Code\nNeeds you · Approve edit to App.cs\nFix the build", or "…\nWorking for 4 min".</summary>
    public string ToolTip =>
        $"{_workspaceName} › {Pane.Label}\n"
        + _state switch
        {
            // The row leads with the request, so the task (what it was doing) moves here.
            AgentState.Waiting => "Needs you" + (string.IsNullOrEmpty(_message) ? "" : " · " + _message)
                + (string.IsNullOrEmpty(_task) || _task == Headline ? "" : "\n" + _task),
            AgentState.Working => $"Working for {_age}",
            _ => $"Idle for {_age}",
        };

    /// <summary>For screen readers: "Approve edit to App.cs, aghouse, Needs you, 4 min", the row's own order.</summary>
    public string AutomationName =>
        $"{Headline}{(Headline == _workspaceName ? "" : ", " + _workspaceName)}, "
        + (_state switch { AgentState.Waiting => "Needs you", AgentState.Working => "Working", _ => "Idle" })
        + $", {_age}";

    /// <summary>Reads the pane again. A new state restarts the clock (a waiting one from when it asked).</summary>
    public void Update(AgentState state, string? task, AgentProgram? agent, DateTime now)
    {
        // Kept once known: a scan that missed the program (it just started a child) shouldn't flip the mark.
        if (agent is not null && agent != _agent)
        {
            _agent = agent;
            OnPropertyChanged(nameof(AgentKey));
            OnPropertyChanged(nameof(Monogram));
        }
        if (state != _state)
        {
            _since = state == AgentState.Waiting && Pane.UnreadAt != default ? Pane.UnreadAt : now;
            State = state;
        }
        else if (state == AgentState.Waiting && Pane.UnreadAt > _since) _since = Pane.UnreadAt; // asked again
        WorkspaceName = Workspace.Name;
        TileIndex = Workspace.TileIndex;
        _task = task;
        _message = Pane.AttentionMessage;
        OnPropertyChanged(nameof(Headline));
        OnPropertyChanged(nameof(Context));
        Age = WaitingRow.FormatAge(now - _since);
        OnPropertyChanged(nameof(ToolTip));
        OnPropertyChanged(nameof(AutomationName));
    }
}
