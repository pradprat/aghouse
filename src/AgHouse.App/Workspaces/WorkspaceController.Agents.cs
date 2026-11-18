using System.Collections.ObjectModel;
using System.Windows.Threading;
using AgHouse.App.Terminal;
using AgHouse.Core;
using AgHouse.Core.Terminal;

namespace AgHouse.App.Workspaces;

/// <summary>
/// The sidebar's Agents list: every pane whose agent has reported in, or whose shell is running a known
/// agent program (<see cref="AgentPrograms"/>), under "Needs you", "Working" and "Idle". It is rebuilt (once
/// per burst) when a workspace says an agent changed or a title changes, and on every port scan, which also
/// says what runs under each shell.
/// </summary>
public sealed partial class WorkspaceController
{
    private readonly Dictionary<string, AgentRow> _agentRows = [];
    private readonly Dictionary<AgentState, AgentGroupHeader> _agentHeaders = new()
    {
        [AgentState.Waiting] = new(AgentState.Waiting),
        [AgentState.Working] = new(AgentState.Working),
        [AgentState.Idle] = new(AgentState.Idle),
    };

    /// <summary>What runs under each shell (image names, nearest first), from the last port scan; null until the first one.</summary>
    private Dictionary<int, IReadOnlyList<string>>? _runningUnder;
    private bool _agentRefreshQueued;
    private bool _hasAgents;

    /// <summary>Headers (<see cref="AgentGroupHeader"/>) and rows (<see cref="AgentRow"/>), in the order shown.</summary>
    public ObservableCollection<object> AgentItems { get; } = [];

    public bool HasAgents { get => _hasAgents; private set => Set(ref _hasAgents, value); }

    /// <summary>Something an agent row shows may have changed: look again once the burst is over.</summary>
    private void QueueAgentRefresh()
    {
        if (_agentRefreshQueued || _shuttingDown) return;
        _agentRefreshQueued = true;
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Background, RefreshAgents);
    }

    private void RefreshAgents()
    {
        _agentRefreshQueued = false;
        if (_shuttingDown) return;
        var now = DateTime.UtcNow;
        var current = new List<AgentRow>();
        foreach (var ws in SidebarOrder())
        {
            foreach (var pane in ws.PanesInOrder())
            {
                IReadOnlyList<string>? programs = null;
                var known = _runningUnder is { } running && pane.ShellPid is { } pid && running.TryGetValue(pid, out programs);
                // An agent recognised by its program: one without hooks (OpenCode), or Claude Code before its first hook call.
                var spotted = known ? AgentPrograms.Find(programs!) : null;
                var reported = ws.AgentStateOf(pane.Id) is not null;
                if (!pane.AgentSeen && !reported && spotted is null) continue;
                // An agent that has exited leaves only its shell behind: it's no longer an agent. One that
                // quit mid-run (Ctrl+C, a crash) never said it stopped, so its "Working" goes with it.
                if (known && programs!.Count == 0 && !pane.HasUnread)
                {
                    if (reported) ws.ClearStatus(Workspace.AgentKey, pane.Id);
                    continue;
                }

                // Working: the agent said so (hook, set-status), or its title shows a spinner. Hooks miss
                // agents that have none, and the title alone misses agents that don't animate it.
                var working = reported || AgentPrograms.TitleShowsWork(pane.ProgramTitle);
                var state = pane.HasUnread ? AgentState.Waiting : working ? AgentState.Working : AgentState.Idle;

                if (!_agentRows.TryGetValue(pane.Id, out var row) || !ReferenceEquals(row.Pane, pane))
                    _agentRows[pane.Id] = row = new AgentRow(ws, pane);
                var task = pane.ProgramTitle is { } title ? TitleText.WithoutStatusGlyphs(title) : null;
                // Until the agent sets a title, the shell's is the command line it was started with ("claude"):
                // that says nothing the agent's own name doesn't say better.
                if (spotted is not null && task is not null && task.Split(' ')[0] is var command
                    && (command.Equals(spotted.Key, StringComparison.OrdinalIgnoreCase)
                        || command.Equals(spotted.Key + ".exe", StringComparison.OrdinalIgnoreCase)))
                    task = null;
                row.Update(state, string.IsNullOrWhiteSpace(task) ? spotted?.Name : task, spotted, now);
                current.Add(row);
            }
        }
        foreach (var gone in _agentRows.Keys.Except(current.Select(r => r.Pane.Id)).ToList()) _agentRows.Remove(gone);
        // Four Claude Code marks in a row tell four rows apart by nothing: quiet them while every agent is the same program.
        var alike = current.Count > 1 && current.All(r => r.AgentKey is not null && r.AgentKey == current[0].AgentKey);
        foreach (var row in current) row.MarkQuiet = alike;

        var items = new List<object>();
        foreach (var state in new[] { AgentState.Waiting, AgentState.Working, AgentState.Idle })
        {
            var rows = current.Where(r => r.State == state).ToList();
            if (rows.Count == 0) continue;
            // Waiting in the waiting order (asking before finished, newest first); the others newest first.
            rows = state == AgentState.Waiting
                ? [.. PaneSession.ByUrgency(rows.Select(r => r.Pane)).Select(p => rows.First(r => ReferenceEquals(r.Pane, p)))]
                : [.. rows.OrderByDescending(r => r.Since)];
            var header = _agentHeaders[state];
            header.Count = rows.Count;
            items.Add(header);
            items.AddRange(rows);
        }
        if (!items.SequenceEqual(AgentItems))
        {
            AgentItems.Clear();
            foreach (var item in items) AgentItems.Add(item);
        }
        HasAgents = current.Count > 0;
    }

    /// <summary>A click on an agent row: its workspace and pane come forward, as from the waiting list.</summary>
    public void GoToAgent(AgentRow row)
    {
        if (!Workspaces.Contains(row.Workspace) || !row.Workspace.Panes.ContainsKey(row.Pane.Id)) return;
        FocusPane(row.Pane.Id);
    }
}
