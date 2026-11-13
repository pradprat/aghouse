using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using AgHouse.App.Input;
using AgHouse.App.Interop;
using AgHouse.App.Notifications;
using AgHouse.App.Persistence;
using AgHouse.App.Terminal;
using AgHouse.Core.Ipc;
using AgHouse.Core.Layout;
using AgHouse.Core.Terminal;

namespace AgHouse.App.Workspaces;

public enum FooterState { Hint, Setup, Waiting, Problem }

/// <summary>
/// Owns all workspaces and panes. Every mutation (UI, keyboard, pipe API) goes through here,
/// always on the UI thread.
/// </summary>
public sealed partial class WorkspaceController : ObservableObject, IDisposable
{
    private readonly Window _window;
    private readonly Grid _host;
    private readonly AppSettings _settings;
    private readonly SettingsProblem? _settingsProblem;
    private readonly ToastService _toasts;
    private readonly ShortcutRouter _shortcuts;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _portTimer;
    private readonly DispatcherTimer _prTimer;
    private bool _scanningPorts;
    private bool _light;
    private string _fontFamily;
    private Workspace? _selected;
    private int _nextId;
    private bool _shuttingDown;
    private FooterState _footerKind;
    private string _footerText = "";
    private string? _footerDetail;
    private bool _notifiedOnce;
    private string _windowTitle = "aghouse";

    public WorkspaceController(Window window, Grid host, AppSettings settings, SettingsProblem? settingsProblem,
        ShellProfiles profiles, ToastService toasts)
    {
        _window = window;
        _host = host;
        _settings = settings;
        _settingsProblem = settingsProblem;
        Profiles = profiles;
        _toasts = toasts;
        _light = TerminalThemes.IsLight(settings.Theme);
        _fontFamily = ResolveFont(settings.FontFamily);

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveSession(); };

        _shortcuts = new ShortcutRouter
        {
            Handler = OnShortcut,
            CtrlDSplits = settings.CtrlDSplits,
            RightClickHandler = OnRightClick,
            CtrlWheelHandler = (hwnd, step) =>
            {
                if (!_window.IsActive) return false;
                var pane = Workspaces.SelectMany(w => w.Panes.Values).FirstOrDefault(p => p.OwnsWindow(hwnd));
                pane?.Zoom(step);
                return pane is not null;
            },
            IsTerminalWindow = hwnd => Workspaces.SelectMany(w => w.Panes.Values).Any(p => p.OwnsWindow(hwnd)),
        };
        // The context menu lives outside the visual tree, so it reads its gesture text from app resources.
        Application.Current.Resources["SplitRightGesture"] = settings.CtrlDSplits ? "Ctrl+D" : "Alt+Shift+=";
        _shortcuts.MouseDown += OnMouseDown;
        _shortcuts.AttachTo(_window);

        // Listening ports per workspace, like cmux's sidebar. Cheap (one process and one TCP snapshot), off the UI thread.
        _portTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _portTimer.Tick += async (_, _) => await RefreshPortsAsync();
        _portTimer.Start();

        // gh answers change without anything aghouse can see (a PR gets opened or merged), so ask again now and then.
        _prTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _prTimer.Tick += (_, _) => { foreach (var ws in Workspaces) _ = ws.RefreshPullRequestAsync(); };
        _prTimer.Start();

        _window.Activated += (_, _) => ClearFocusedUnread();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _toasts.Activated += (ws, pane) => _window.Dispatcher.BeginInvoke(() => OnToastActivated(ws, pane));
        UpdateFooter();
    }

    public ObservableCollection<Workspace> Workspaces { get; } = [];

    // ───────────────────────────── Sidebar footer ─────────────────────────────

    /// <summary>
    /// The footer line under the workspace list. In priority order: a settings.json problem
    /// (until restart), how many panes are waiting, how to set up agent hooks (until the first
    /// notification ever), or the Ctrl+Shift+U shortcut hint.
    /// Wording: "waiting" counts panes, "Needs you" is a pane's state; never "unread".
    /// </summary>
    public FooterState FooterKind { get => _footerKind; private set => Set(ref _footerKind, value); }

    public string FooterText { get => _footerText; private set => Set(ref _footerText, value); }

    public string? FooterDetail { get => _footerDetail; private set => Set(ref _footerDetail, value); }

    /// <summary>"aghouse", or "aghouse (2 waiting)": the count reaches Alt+Tab and the taskbar while the window is behind others.</summary>
    public string WindowTitle { get => _windowTitle; private set => Set(ref _windowTitle, value); }

    private void UpdateFooter()
    {
        var waiting = Workspaces.Sum(w => w.WaitingCount);
        WindowTitle = waiting > 0 ? $"aghouse ({waiting} waiting)" : "aghouse";
        if (_settingsProblem is { } problem)
        {
            FooterKind = FooterState.Problem;
            FooterText = problem.Summary;
            FooterDetail = $"{problem.Detail}\n\naghouse is running on default settings. Fix the file, then restart aghouse.";
        }
        else if (waiting > 0)
        {
            FooterKind = FooterState.Waiting;
            FooterText = $"{waiting} waiting · Ctrl+Shift+U";
            FooterDetail = "Show what's waiting (Ctrl+Shift+I). Ctrl+Shift+U jumps to the pane that asked most recently.";
        }
        else if (!_notifiedOnce && !_hooksConnected)
        {
            // First run: nothing on screen says agents can ask for you, so say how, until one does.
            FooterKind = FooterState.Setup;
            FooterText = "Connect Claude Code so it can say when it needs you";
            FooterDetail = "Opens Settings › Integrations, which adds aghouse's hook to ~/.claude/settings.json after asking. "
                + "(Or run aghouse hooks install claude in any pane.) Then a pane where Claude Code is waiting for you "
                + "says “Needs you”, and its workspace gets a dot.\n\n"
                + "Other tools and scripts can run aghouse notify. This hint goes away once either happens.";
        }
        else
        {
            FooterKind = FooterState.Hint;
            FooterText = "Ctrl+Shift+U  jump to a waiting pane";
            FooterDetail = "Show what's waiting and what you already handled (Ctrl+Shift+I)";
        }
    }

    private bool _hooksConnected = AgHouse.Core.ClaudeHooks.Inspect().State == AgHouse.Core.HookState.Connected;

    /// <summary>Re-reads whether Claude Code is connected (after Settings or the welcome screen connected it).</summary>
    public void RefreshFooter()
    {
        _hooksConnected = AgHouse.Core.ClaudeHooks.Inspect().State == AgHouse.Core.HookState.Connected;
        UpdateFooter();
    }

    public ShellProfiles Profiles { get; }

    public Workspace? Selected
    {
        get => _selected;
        set
        {
            if (value is null || ReferenceEquals(_selected, value)) return;
            _selected = value;
            foreach (var ws in Workspaces)
            {
                ws.IsSelected = ReferenceEquals(ws, value);
                // Hidden (not Collapsed) keeps the terminals' size, so switching doesn't reflow them.
                ws.View.Visibility = ws.IsSelected ? Visibility.Visible : Visibility.Hidden;
            }
            OnPropertyChanged();
            if (value.ActivePane is { } pane)
            {
                SetActive(value, pane.Id);
                _window.Dispatcher.BeginInvoke(DispatcherPriority.Input, pane.FocusTerminal);
            }
            ScheduleSave();
        }
    }

    // ───────────────────────────── Workspaces ─────────────────────────────

    public Workspace NewWorkspace(string? cwd = null, string? profileId = null, string? name = null,
        PaneLayoutDto? layout = null, bool select = true)
    {
        var ws = new Workspace(NewId("w"), name?.Trim() ?? "");
        ws.View.RatiosChanged += ScheduleSave;
        ws.View.Visibility = Visibility.Hidden;
        ws.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Workspace.WaitingCount)) UpdateFooter();
            else if (e.PropertyName == nameof(Workspace.Name)) UpdateLabels(ws); // a folder that matches the name stops being a useful label
        };

        if (layout is not null)
        {
            var root = PaneLayoutSerializer.FromDto(layout, leaf =>
            {
                var slot = CreateSlot(ws);
                foreach (var tab in leaf.AllTabs())
                {
                    CreatePane(ws, slot, Profiles.Resolve(tab.Profile), tab.Cwd).CustomName = tab.Name;
                }
                if (slot.Tabs.ElementAtOrDefault(leaf.ActiveTab) is { } active) slot.Show(active);
                return slot.Id;
            });
            ws.Tree = new PaneTree(root);
        }
        else
        {
            var slot = CreateSlot(ws);
            CreatePane(ws, slot, Profiles.Resolve(profileId), cwd ?? DefaultCwd());
            ws.Tree = new PaneTree(slot.Id);
        }

        ws.ActivePaneId = ws.Slots[ws.Tree.Leaves()[0].PaneId].ActiveTab!.Id;
        // Named after its folder, so the sidebar says where each workspace is ("aghouse", not "Workspace 3").
        if (string.IsNullOrWhiteSpace(ws.Name)) ws.Name = UniqueName(ws.ActivePane?.FolderName ?? "Workspace");
        _host.Children.Add(ws.View);
        Render(ws);
        Workspaces.Add(ws);
        SetActive(ws, ws.ActivePaneId);
        if (select) Selected = ws;
        ScheduleSave();
        return ws;
    }

    public void CloseWorkspace(Workspace ws)
    {
        var cwd = ws.ActivePane?.Cwd;
        foreach (var pane in ws.Panes.Values.ToList()) DisposePane(pane);
        ws.Panes.Clear();
        RemoveWorkspace(ws, cwd);
    }

    private void RemoveWorkspace(Workspace ws, string? cwd)
    {
        var index = Workspaces.IndexOf(ws);
        if (index < 0) return;
        _host.Children.Remove(ws.View);
        Workspaces.RemoveAt(index);
        UpdateFooter();

        if (Workspaces.Count == 0)
        {
            // The workbench stays open: closing the last pane opens a fresh one in the same folder
            // instead of quitting. Quit with the window's close button (or Alt+F4).
            _selected = null;
            if (!_shuttingDown) NewWorkspace(cwd: cwd);
            return;
        }
        if (ReferenceEquals(_selected, ws))
        {
            _selected = null;
            Selected = Workspaces[Math.Min(index, Workspaces.Count - 1)];
        }
        ScheduleSave();
    }

    /// <summary>"aghouse", then "aghouse (2)" for a second workspace in the same folder, so names stay unique for select-workspace.</summary>
    private string UniqueName(string name)
    {
        var taken = Workspaces.Select(w => w.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        for (var n = 2; ; n++)
        {
            if (!taken.Contains($"{name} ({n})")) return $"{name} ({n})";
        }
    }

    public void RenameWorkspace(Workspace ws, string name)
    {
        if (!string.IsNullOrWhiteSpace(name)) ws.Name = name.Trim();
        ws.IsRenaming = false;
        ScheduleSave();
    }

    public void SelectRelative(int delta)
    {
        if (Workspaces.Count == 0) return;
        var i = _selected is null ? 0 : Workspaces.IndexOf(_selected);
        Selected = Workspaces[((i + delta) % Workspaces.Count + Workspaces.Count) % Workspaces.Count];
    }

    // ───────────────────────────── Panes ─────────────────────────────

    private PaneSlot CreateSlot(Workspace ws)
    {
        var slot = new PaneSlot(NewId("s"), TerminalThemes.BackgroundOf(_light));
        slot.FocusRequested += s => { if (s.ActiveTab is { } t) FocusPane(t.Id); };
        slot.TabActivated += p => FocusPane(p.Id);
        slot.CloseRequested += p => RequestClosePane(p.Id);
        slot.CloseOthersRequested += p => CloseOtherTabs(p.Id);
        slot.NewTabRequested += s => { if (s.ActiveTab is { } t) NewTab(t.Id); };
        slot.Renamed += _ => { UpdateLabels(ws); ScheduleSave(); };
        slot.UnzoomRequested += _ => { if (ws.ZoomedSlotId is not null) ToggleZoom(ws); };
        ws.Slots[slot.Id] = slot;
        return slot;
    }

    private PaneSession CreatePane(Workspace ws, PaneSlot slot, ShellProfile profile, string? cwd, int? index = null)
    {
        var id = NewId("p");
        var env = new Dictionary<string, string?>
        {
            [AgEnv.Pipe] = AgEnv.DefaultPipeName,
            [AgEnv.PaneId] = id,
            [AgEnv.WorkspaceId] = ws.Id,
            ["TERM_PROGRAM"] = "aghouse",
            // aghouse.exe (the CLI) lives next to the app.
            ["PATH"] = AppContext.BaseDirectory.TrimEnd('\\') + ";" + Environment.GetEnvironmentVariable("PATH"),
        };
        foreach (var (k, v) in profile.Env) env[k] = v;

        var pane = new PaneSession(id, ws.Id, profile, cwd,
            _light ? TerminalThemes.Light : TerminalThemes.Dark, TerminalThemes.BackgroundOf(_light),
            _fontFamily, _settings.FontSize, env);

        pane.TerminalEvent += OnTerminalEvent;
        pane.AttentionHandled += (p, message, askedAt) => RememberHandled(ws, p, message, askedAt);
        pane.Exited += p => { Log.Write($"exited {p.Id}"); ClosePane(p.Id); };
        pane.PropertyChanged += (_, e) => OnPanePropertyChanged(ws, pane, e);
        ws.Panes[id] = pane;
        slot.Add(pane, index);
        return pane;
    }

    public PaneSession? Split(Direction side, string? paneId = null, string? profileId = null, string? cwd = null)
    {
        var (ws, source) = paneId is null ? (_selected, _selected?.ActivePane) : FindPane(paneId);
        if (ws is null || source is null || ws.SlotOf(source) is not { } sourceSlot) return null;

        var profile = profileId is null ? source.Profile : Profiles.Resolve(profileId);
        var slot = CreateSlot(ws);
        var pane = CreatePane(ws, slot, profile, cwd ?? source.Cwd);
        Log.Write($"split {source.Id} {side} -> {pane.Id}");
        ws.Tree.Split(sourceSlot.Id, side, slot.Id);
        ws.ZoomedSlotId = null; // a new pane should be seen next to the one it came from
        Render(ws);
        FocusPane(pane.Id);
        ScheduleSave();
        return pane;
    }

    /// <summary>A new terminal as a tab in the same pane as <paramref name="paneId"/> (default: the focused one), right after it.</summary>
    public PaneSession? NewTab(string? paneId = null, string? profileId = null, string? cwd = null)
    {
        var (ws, source) = paneId is null ? (_selected, _selected?.ActivePane) : FindPane(paneId);
        if (ws is null || source is null || ws.SlotOf(source) is not { } slot) return null;

        var profile = profileId is null ? source.Profile : Profiles.Resolve(profileId);
        // A new tab belongs next to the one it came from.
        var pane = CreatePane(ws, slot, profile, cwd ?? source.Cwd, index: slot.Tabs.ToList().IndexOf(source) + 1);
        Log.Write($"tab {source.Id} -> {pane.Id}");
        UpdateLabels(ws);
        FocusPane(pane.Id);
        ScheduleSave();
        return pane;
    }

    /// <summary>
    /// Ctrl+PageDown / Ctrl+PageUp: the next or previous tab in the focused pane. False when the pane has
    /// no other tab, so the keys reach the terminal (less, vim and others use them).
    /// </summary>
    public bool SelectTabRelative(int delta)
    {
        if (_selected?.ActivePane is not { } pane || _selected.SlotOf(pane) is not { Tabs.Count: > 1 } slot) return false;
        if (slot.Relative(delta) is { } target && !ReferenceEquals(target, pane)) FocusPane(target.Id);
        return true;
    }

    /// <summary>"Close other tabs": each one that's running something asks first, like ✕ does.</summary>
    public void CloseOtherTabs(string paneId)
    {
        var (ws, pane) = FindPane(paneId);
        if (ws?.SlotOf(pane!) is not { } slot) return;
        foreach (var other in slot.Tabs.Where(t => t.Id != paneId).ToList()) RequestClosePane(other.Id);
        if (FindPane(paneId).Pane is not null) FocusPane(paneId);
    }

    /// <summary>
    /// A person asked to close a pane (✕ or Ctrl+Shift+W). If something is running in it, ask first;
    /// an idle prompt closes straight away. Scripted closes (pipe API) and shell exits skip this.
    /// </summary>
    public void RequestClosePane(string paneId)
    {
        var (ws, pane) = FindPane(paneId);
        if (pane is null) return;
        var what = ws?.SlotOf(pane) is { Tabs.Count: > 1 } ? "tab" : "pane";
        if (pane.DescribeRunning() is { } running && !Confirm(
                $"{running} is still running in “{pane.Label}”.\n\nClose the {what} and end it?"))
        {
            // The dialog is modal but the pipe API isn't: the pane may have gone while it was open.
            if (FindPane(paneId).Pane is { } still) still.FocusTerminal();
            return;
        }
        ClosePane(paneId); // no-op if it was already closed meanwhile
    }

    /// <summary>A person asked to close a whole workspace (context menu). Same rule as panes.</summary>
    public void RequestCloseWorkspace(Workspace ws)
    {
        var busy = ws.Panes.Values.Select(p => p.DescribeRunning()).OfType<string>().ToList();
        if (busy.Count > 0 && !Confirm(busy.Count == 1
                ? $"{busy[0]} is still running in “{ws.Name}”.\n\nClose the workspace and end it?"
                : $"{busy.Count} programs are still running in “{ws.Name}” ({string.Join(", ", busy)}).\n\nClose the workspace and end them?"))
        {
            if (Workspaces.Contains(ws)) ws.ActivePane?.FocusTerminal();
            return;
        }
        if (Workspaces.Contains(ws)) CloseWorkspace(ws);
    }

    /// <summary>
    /// The window is closing (✕, Alt+F4, taskbar). Same rule as panes: if anything is still running,
    /// ask first. Returns false to keep the window open. Session restore brings back layout and
    /// folders, never the processes, so this is the last chance to keep a running agent.
    /// </summary>
    public bool ConfirmQuit()
    {
        var busy = Workspaces
            .SelectMany(w => w.Panes.Values.Select(p => (Running: p.DescribeRunning(), Workspace: w.Name)))
            .Where(x => x.Running is not null)
            .Select(x => $"{x.Running} in “{x.Workspace}”")
            .ToList();
        if (busy.Count == 0) return true;

        // A dialog owned by a minimized window would open hidden. Not BringToFront(): Show() throws while closing.
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        const int shown = 5;
        var message = busy.Count == 1
            ? $"{busy[0]} is still running.\n\nQuit aghouse and end it?"
            : $"{busy.Count} programs are still running:\n\n"
              + string.Join("\n", busy.Take(shown).Select(b => "•  " + b))
              + (busy.Count > shown ? $"\n•  and {busy.Count - shown} more" : "")
              + "\n\nQuit aghouse and end them?";
        if (Confirm(message)) return true;
        _selected?.ActivePane?.FocusTerminal();
        return false;
    }

    /// <summary>Warning dialog whose default button is the safe one, so a stray Enter never ends a process.</summary>
    private bool Confirm(string message) =>
        MessageBox.Show(_window, message, "aghouse", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
        == MessageBoxResult.Yes;

    public void ClosePane(string paneId)
    {
        var (ws, pane) = FindPane(paneId);
        if (ws is null || pane is null) return;
        Log.Write($"close {paneId}");

        ws.Panes.Remove(paneId);
        ws.ForgetPane(paneId);
        var cwd = pane.Cwd;
        var slot = ws.SlotOf(pane);
        // The tab to its right takes its place; the slot itself goes only with its last tab.
        var replacement = slot?.Remove(pane);
        DisposePane(pane);

        if (replacement is null && slot is not null)
        {
            ws.Slots.Remove(slot.Id);
            WorkspaceView.Detach(slot.Frame);
            var nextSlot = ws.Tree.Remove(slot.Id);
            if (nextSlot is null)
            {
                RemoveWorkspace(ws, cwd);
                return;
            }
            if (ws.ActivePaneId == paneId) ws.ActivePaneId = ws.Slots[nextSlot].ActiveTab?.Id;
            Render(ws);
        }
        else
        {
            if (ws.ActivePaneId == paneId) ws.ActivePaneId = replacement?.Id;
            UpdateLabels(ws);
        }

        SetActive(ws, ws.ActivePaneId!);
        if (ReferenceEquals(ws, _selected)) ws.ActivePane?.FocusTerminal();
        ScheduleSave();
    }

    private static void DisposePane(PaneSession pane)
    {
        pane.Dispose();
        ToastService.ClearFor(pane.Id);
    }

    public void FocusPane(string paneId)
    {
        var (ws, pane) = FindPane(paneId);
        if (ws is null || pane is null) return;
        if (!ReferenceEquals(ws, _selected)) Selected = ws;
        SetActive(ws, paneId);
        pane.FocusTerminal();
    }

    public void MoveFocus(Direction direction)
    {
        if (_selected?.ActivePane is not { } current || _selected.SlotOf(current) is not { } slot) return;
        if (_selected.Tree.Neighbor(slot.Id, direction) is { } target && _selected.Slots[target].ActiveTab is { } tab)
        {
            FocusPane(tab.Id);
        }
    }

    public void JumpToUnread()
    {
        var pane = Workspaces.SelectMany(w => w.Panes.Values)
            .Where(p => p.HasUnread)
            .OrderByDescending(p => p.UnreadAt)
            .FirstOrDefault();
        if (pane is not null) FocusPane(pane.Id);
    }

    private void SetActive(Workspace ws, string paneId)
    {
        ws.ActivePaneId = paneId;
        foreach (var p in ws.Panes.Values) p.IsActive = p.Id == paneId;
        var activeSlot = ws.Panes.TryGetValue(paneId, out var target) ? target.SlotId : null;
        // Going to a hidden pane (Ctrl+Shift+U, Alt+Arrow, a click in the sidebar) ends the zoom.
        if (ws.ZoomedSlotId is not null && activeSlot != ws.ZoomedSlotId)
        {
            ws.ZoomedSlotId = null;
            Render(ws);
        }
        foreach (var slot in ws.Slots.Values)
        {
            if (slot.Id == activeSlot && !ReferenceEquals(slot.ActiveTab, target)) slot.Show(target!);
            slot.IsFocused = slot.Id == activeSlot;
        }
        if (ReferenceEquals(ws, _selected) && UserIsLooking() && ws.ActivePane is { } active)
        {
            active.HasUnread = false;
        }
        ws.RefreshSummary();
    }

    private void Render(Workspace ws)
    {
        if (ws.ZoomedSlotId is { } z && (!ws.Slots.ContainsKey(z) || ws.Slots.Count < 2)) ws.ZoomedSlotId = null;
        ws.View.Render(ws.Tree, id => ws.Slots[id].Frame, ws.ZoomedSlotId);
        // Headers (with the close button) while split; a lone pane shows one only while it needs you or holds tabs.
        var split = ws.Slots.Count > 1;
        foreach (var slot in ws.Slots.Values) slot.IsSplit = split;
        UpdateLabels(ws);
        ws.RefreshSummary(); // the zoom note names panes, so labels come first
    }

    /// <summary>
    /// Ctrl+Shift+Enter: the focused pane fills the workspace, or everything comes back. The other
    /// terminals keep running; the header says how many are hidden and whether one needs you.
    /// </summary>
    public bool ToggleZoom(Workspace? ws = null, string? paneId = null)
    {
        ws ??= _selected;
        if (ws is null || ws.Slots.Count < 2) return false;
        var pane = paneId is not null && ws.Panes.TryGetValue(paneId, out var p) ? p : ws.ActivePane;
        if (pane is null) return false;
        ws.ZoomedSlotId = ws.ZoomedSlotId is null ? pane.SlotId : null;
        Log.Write($"zoom {ws.Id} -> {ws.ZoomedSlotId ?? "none"}");
        Render(ws);
        if (pane.Id != ws.ActivePaneId) SetActive(ws, pane.Id);
        if (ReferenceEquals(ws, _selected)) _window.Dispatcher.BeginInvoke(DispatcherPriority.Input, pane.FocusTerminal);
        return true;
    }

    /// <summary>Moves a workspace to a 0-based position in the sidebar (drag, Move up/down, `aghouse move-workspace`).</summary>
    public void MoveWorkspace(Workspace ws, int index)
    {
        var from = Workspaces.IndexOf(ws);
        if (from < 0) return;
        index = Math.Clamp(index, 0, Workspaces.Count - 1);
        if (index == from) return;
        Workspaces.Move(from, index);
        OnPropertyChanged(nameof(Selected)); // the list can drop its selection when the selected row moves
        ScheduleSave();
    }

    /// <summary>
    /// Names each pane so it can be told apart from its siblings: the program's own title when it set
    /// one (an agent's task, "vim"), else its folder, else its shell. A folder that only repeats the
    /// workspace's name says nothing, so the shell's name is used instead. Names that are still shared
    /// get "(2)", "(3)" in layout order: left to right, top to bottom.
    /// </summary>
    private static void UpdateLabels(Workspace ws)
    {
        if (ws.Tree?.Root is null) return; // panes are created before the tree that holds them
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pane in ws.PanesInOrder())
        {
            // A name the user gave a tab is kept exactly as typed.
            if (pane.CustomName is { } custom) { pane.Label = custom; continue; }
            var name = pane.ProgramTitle
                ?? (pane.FolderName is { } folder && !NamesFolder(ws.Name, folder) ? folder : pane.Profile.Name);
            var n = seen[name] = seen.GetValueOrDefault(name) + 1;
            pane.Label = n == 1 ? name : $"{name} ({n})";
        }
    }

    /// <summary>The workspace is named after this folder: "aghouse", or "aghouse (2)" from <see cref="UniqueName"/>.</summary>
    private static bool NamesFolder(string workspaceName, string folder) =>
        workspaceName.Equals(folder, StringComparison.OrdinalIgnoreCase)
        || (workspaceName.StartsWith(folder + " (", StringComparison.OrdinalIgnoreCase) && workspaceName.EndsWith(')')
            && int.TryParse(workspaceName.AsSpan(folder.Length + 2, workspaceName.Length - folder.Length - 3), out _));

    public (Workspace? Workspace, PaneSession? Pane) FindPane(string paneId)
    {
        foreach (var ws in Workspaces)
        {
            if (ws.Panes.TryGetValue(paneId, out var pane)) return (ws, pane);
        }
        return (null, null);
    }

    // ───────────────────────────── Attention ─────────────────────────────

    private void OnTerminalEvent(PaneSession pane, TerminalEvent e)
    {
        switch (e)
        {
            case NotifyEvent n:
                Notify(pane.Id, n.Title, n.Body);
                break;
            case BellEvent:
                // Soft signal: mark unread, but no toast (bells are noisy).
                if (!IsFocused(pane)) MarkUnread(pane, null);
                break;
        }
    }

    public void Notify(string? paneId, string? title, string? body)
    {
        PaneSession? pane = null;
        Workspace? ws;
        if (paneId is not null) (ws, pane) = FindPane(paneId);
        else ws = _selected;
        if (ws is null) return;
        pane ??= ws.ActivePane;

        // The sidebar message is derived from waiting panes (Workspace.RefreshSummary), so a
        // notification for the pane the user is already looking at leaves nothing stale behind.
        if (!_notifiedOnce)
        {
            _notifiedOnce = true; // hooks work: retire the setup hint for good
            UpdateFooter();
            ScheduleSave();
        }

        var text = string.IsNullOrWhiteSpace(body) ? title : body;
        // An agent that asks for you has stopped working until you answer.
        if (pane is not null) ws.ClearStatus(Workspace.AgentKey, pane.Id);
        if (pane is not null && !IsFocused(pane)) MarkUnread(pane, text);
        var looking = UserIsLooking();
        Log.Write($"notify pane={pane?.Id} looking={looking}");

        if (!looking)
        {
            // A bare notification still says something: "aghouse" / "Needs you".
            if (_settings.Toasts) _toasts.Show(string.IsNullOrWhiteSpace(title) ? ws.Name : title,
                string.IsNullOrWhiteSpace(body) ? "Needs you" : body, ws.Id, pane?.Id);
            if (_settings.FlashTaskbar && WindowHandle is var hwnd && hwnd != IntPtr.Zero) Win32.FlashTaskbar(hwnd);
        }
    }

    // ───────────────────────────── Settings ─────────────────────────────

    /// <summary>The live settings. The Settings window edits a copy and hands it to <see cref="ApplySettings"/>.</summary>
    public AppSettings Settings => _settings;

    /// <summary>Why settings.json couldn't be read at launch, if it couldn't (Settings is read-only then).</summary>
    public SettingsProblem? SettingsProblem => _settingsProblem;

    /// <summary>
    /// Applies changed settings now, without a restart: the theme (chrome and terminals), the font,
    /// Ctrl+D, and the rest, which are read when they're used.
    /// </summary>
    public void ApplySettings(AppSettings changed)
    {
        var appearance = changed.Theme != _settings.Theme || changed.FontFamily != _settings.FontFamily
                         || changed.FontSize != _settings.FontSize;
        _settings.DefaultProfile = changed.DefaultProfile;
        _settings.FontFamily = changed.FontFamily;
        _settings.FontSize = Math.Clamp(changed.FontSize, PaneSession.MinFontSize, PaneSession.MaxFontSize);
        _settings.RestoreSession = changed.RestoreSession;
        _settings.Toasts = changed.Toasts;
        _settings.FlashTaskbar = changed.FlashTaskbar;
        _settings.Theme = changed.Theme;
        _settings.CtrlDSplits = changed.CtrlDSplits;

        _shortcuts.CtrlDSplits = _settings.CtrlDSplits;
        Application.Current.Resources["SplitRightGesture"] = _settings.CtrlDSplits ? "Ctrl+D" : "Alt+Shift+=";

        if (!appearance) return;
        Application.Current.ThemeMode = _settings.Theme switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        _fontFamily = ResolveFont(_settings.FontFamily);
        ApplyTerminalAppearance();
    }

    /// <summary>
    /// With the theme set to follow Windows, a light/dark switch in Windows recolours the terminals
    /// too (the chrome follows by itself through ThemeMode.System).
    /// </summary>
    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.General || _settings.Theme != "system") return;
        _window.Dispatcher.BeginInvoke(() =>
        {
            if (!_shuttingDown && TerminalThemes.SystemUsesLightTheme() != _light) ApplyTerminalAppearance();
        });
    }

    private void ApplyTerminalAppearance()
    {
        _light = TerminalThemes.IsLight(_settings.Theme);
        var theme = _light ? TerminalThemes.Light : TerminalThemes.Dark;
        var background = TerminalThemes.BackgroundOf(_light);
        foreach (var ws in Workspaces)
        {
            foreach (var slot in ws.Slots.Values) slot.Background = background;
            foreach (var pane in ws.Panes.Values) pane.SetAppearance(theme, background, _fontFamily, _settings.FontSize);
        }
    }

    /// <summary>The installed fonts a terminal can use well: the monospaced ones.</summary>
    public static IReadOnlyList<string> MonospaceFonts()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in System.Windows.Media.Fonts.SystemFontFamilies)
        {
            var name = family.Source;
            if (name.Contains("Mono", StringComparison.OrdinalIgnoreCase) || name.Contains("Code", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Consol", StringComparison.OrdinalIgnoreCase) || name.Contains("Courier", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Lucida Console", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }
        return [.. names];
    }

    private IntPtr WindowHandle => new System.Windows.Interop.WindowInteropHelper(_window).Handle;

    /// <summary>
    /// Our window is the foreground window and not minimized. (WPF's IsActive can stay true
    /// for a minimized window that kept activation, so it isn't enough on its own.)
    /// </summary>
    private bool UserIsLooking()
    {
        var hwnd = WindowHandle;
        return hwnd != IntPtr.Zero && Win32.GetForegroundWindow() == hwnd && !Win32.IsIconic(hwnd);
    }

    private bool IsFocused(PaneSession pane) =>
        UserIsLooking() && _selected is { } ws && ws.ActivePaneId == pane.Id && ws.Panes.ContainsKey(pane.Id);

    private void MarkUnread(PaneSession pane, string? message)
    {
        var before = (pane.HasUnread, pane.AttentionMessage);
        pane.RaiseAttention(message);
        var (ws, _) = FindPane(pane.Id);
        ws?.RefreshSummary();
        // Say it to screen readers too, but only when something changed: a pane that keeps
        // ringing its bell while already waiting shouldn't repeat itself.
        if (ws is not null && before != (pane.HasUnread, pane.AttentionMessage)) Announce(ws, pane);
    }

    private void Announce(Workspace ws, PaneSession pane)
    {
        var where = ws.Panes.Count > 1 ? $"{ws.Name}, {pane.Label}" : ws.Name;
        var text = pane.AttentionMessage is { } m ? $"{where} needs you: {m}" : $"{where} needs you";
        var peer = UIElementAutomationPeer.FromElement(_window) ?? UIElementAutomationPeer.CreatePeerForElement(_window);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other,
            AutomationNotificationProcessing.ImportantMostRecent, text, "aghouse.attention");
    }

    /// <summary>
    /// Requests that were handled, newest first, at most 20: the waiting list's "Earlier" section.
    /// Runtime only, like the rest of what agents report.
    /// </summary>
    public ObservableCollection<HandledRequest> Earlier { get; } = [];

    private void RememberHandled(Workspace ws, PaneSession pane, string? message, DateTime askedAt)
    {
        if (_shuttingDown) return;
        Earlier.Insert(0, new HandledRequest(ws.Name, pane.Id, pane.Label, message, askedAt, DateTime.UtcNow));
        while (Earlier.Count > 20) Earlier.RemoveAt(Earlier.Count - 1);
    }

    /// <summary>Every pane that needs you now, newest first.</summary>
    public IReadOnlyList<(Workspace Workspace, PaneSession Pane)> WaitingPanes() =>
        Workspaces.SelectMany(w => w.Panes.Values.Where(p => p.HasUnread).Select(p => (w, p)))
            .OrderByDescending(x => x.p.UnreadAt)
            .ToList();

    private void ClearFocusedUnread()
    {
        if (_selected?.ActivePane is { HasUnread: true } pane)
        {
            pane.HasUnread = false;
            _selected.RefreshSummary();
        }
    }

    private void OnToastActivated(string? workspaceId, string? paneId)
    {
        BringToFront();
        if (paneId is not null && FindPane(paneId).Pane is not null) FocusPane(paneId);
        else if (Workspaces.FirstOrDefault(w => w.Id == workspaceId) is { } ws) Selected = ws;
    }

    public void BringToFront()
    {
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Show();
        _window.Activate();
        Win32.SetForegroundWindow(WindowHandle);
    }

    private void OnPanePropertyChanged(Workspace ws, PaneSession pane, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaneSession.Cwd))
        {
            if (pane.Id == ws.ActivePaneId) ws.RefreshBranch();
            UpdateLabels(ws);
            ScheduleSave();
        }
        else if (e.PropertyName == nameof(PaneSession.Title))
        {
            UpdateLabels(ws);
        }
    }

    // ───────────────────────────── Input ─────────────────────────────

    private bool OnShortcut(Shortcut s) => OnShortcut(s, fromPalette: false);

    private bool OnShortcut(Shortcut s, bool fromPalette)
    {
        // Only while our window has focus; never steal keys from other windows on this thread.
        if (!fromPalette && !_window.IsActive) return false;
        // Let a rename box (workspace or tab) keep its keys.
        if ((Workspaces.Any(w => w.IsRenaming) || System.Windows.Input.Keyboard.FocusedElement is TextBox)
            && s.Action is AppAction.Copy or AppAction.Paste) return false;

        switch (s.Action)
        {
            case AppAction.NewWorkspace: NewWorkspace(); return true;
            case AppAction.NewTab: NewTab(); return true;
            case AppAction.NextTab: return SelectTabRelative(1);
            case AppAction.PreviousTab: return SelectTabRelative(-1);
            case AppAction.ClosePane: if (_selected?.ActivePaneId is { } id) RequestClosePane(id); return true;
            case AppAction.SplitRight: Split(Direction.Right); return true;
            case AppAction.SplitDown: Split(Direction.Down); return true;
            case AppAction.Focus: MoveFocus(s.Direction); return true;
            case AppAction.SelectWorkspace:
                if (Workspaces.Count > 0) Selected = s.Index == 8 ? Workspaces[^1] : Workspaces.ElementAtOrDefault(s.Index) ?? _selected;
                return true;
            case AppAction.NextWorkspace: SelectRelative(1); return true;
            case AppAction.PreviousWorkspace: SelectRelative(-1); return true;
            case AppAction.JumpToUnread: JumpToUnread(); return true;
            case AppAction.RenameWorkspace: if (_selected is not null) _selected.IsRenaming = true; return true;
            case AppAction.Copy: return CopySelection(_selected?.ActivePane);
            case AppAction.Paste: return Paste(_selected?.ActivePane);
            case AppAction.CloseWorkspace: if (_selected is not null) RequestCloseWorkspace(_selected); return true;
            case AppAction.ZoomPane: ToggleZoom(); return true;
            case AppAction.Find: OpenFind(); return true;
            case AppAction.FontBigger: _selected?.ActivePane?.Zoom(1); return true;
            case AppAction.FontSmaller: _selected?.ActivePane?.Zoom(-1); return true;
            case AppAction.FontReset: _selected?.ActivePane?.Zoom(0); return true;
            case AppAction.CommandPalette or AppAction.WaitingList or AppAction.Settings:
                SurfaceRequested?.Invoke(s.Action);
                return true;
        }
        return false;
    }

    /// <summary>The palette, the waiting list or Settings was asked for. MainWindow owns those windows.</summary>
    public event Action<AppAction>? SurfaceRequested;

    /// <summary>Runs an action as if its shortcut was pressed (the command palette uses this).</summary>
    public void Run(AppAction action, int index = 0, Direction direction = Direction.Left) =>
        OnShortcut(new Shortcut(action, index, direction), fromPalette: true);

    /// <summary>Ctrl+Shift+F: find in the focused pane's output. Again while open: back to the box.</summary>
    public void OpenFind()
    {
        if (_selected?.ActivePane is { } pane && _selected.SlotOf(pane) is { } slot) slot.OpenFind();
    }

    private void OnMouseDown(IntPtr hwnd, int message)
    {
        foreach (var ws in Workspaces)
        {
            foreach (var pane in ws.Panes.Values)
            {
                if (!pane.OwnsWindow(hwnd)) continue;
                if (!ReferenceEquals(ws, _selected)) Selected = ws;
                if (ws.ActivePaneId != pane.Id) SetActive(ws, pane.Id);
                return;
            }
        }
    }

    /// <summary>Right click in a terminal: copy if there's a selection, otherwise paste (like conhost).</summary>
    private bool OnRightClick(IntPtr hwnd)
    {
        var pane = Workspaces.SelectMany(w => w.Panes.Values).FirstOrDefault(p => p.OwnsWindow(hwnd));
        if (pane is null) return false;
        return CopySelection(pane) || Paste(pane);
    }

    private static bool CopySelection(PaneSession? pane)
    {
        var text = pane?.GetSelectedText();
        if (string.IsNullOrEmpty(text)) return false;
        try { Clipboard.SetText(text); } catch { return false; }
        return true;
    }

    private static bool Paste(PaneSession? pane)
    {
        if (pane is null) return false;
        string text;
        try { text = Clipboard.GetText(); } catch { return false; }
        if (string.IsNullOrEmpty(text)) return true;
        pane.SendText(text.Replace("\r\n", "\r").Replace('\n', '\r'));
        return true;
    }

    // ───────────────────────────── Ports ─────────────────────────────

    /// <summary>
    /// Finds the TCP ports that programs under each workspace's shells listen on. Can't see inside WSL,
    /// for the same reason <see cref="PaneSession.RunningPrograms"/> can't.
    /// </summary>
    private async Task RefreshPortsAsync()
    {
        if (_scanningPorts || _shuttingDown) return;
        _scanningPorts = true;
        try
        {
            var shells = Workspaces.Select(w => (Workspace: w, Pids: w.Panes.Values.Select(p => p.ShellPid).OfType<int>().ToList())).ToList();
            var ports = await Task.Run(() =>
            {
                var listening = Interop.ListeningPorts.Snapshot();
                var trees = Interop.ProcessTree.Descendants(shells.SelectMany(s => s.Pids));
                return shells.Select(s =>
                {
                    var pids = s.Pids.SelectMany(pid => trees.GetValueOrDefault(pid) ?? []).ToHashSet();
                    pids.ExceptWith(s.Pids); // a shell itself listening is plumbing, not a server the user started
                    return (IReadOnlyList<int>)listening.Where(l => pids.Contains(l.Pid)).Select(l => l.Port)
                        .Distinct().Order().ToList();
                }).ToList();
            });
            for (var i = 0; i < shells.Count; i++)
            {
                if (Workspaces.Contains(shells[i].Workspace)) shells[i].Workspace.SetPorts(ports[i]);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"port scan failed: {ex.Message}");
        }
        finally
        {
            _scanningPorts = false;
        }
    }

    // ───────────────────────────── Session ─────────────────────────────

    /// <summary>First launch (no session.json yet): MainWindow shows the welcome screen instead of a terminal.</summary>
    public event Action? WelcomeRequested;

    public void RestoreOrCreate()
    {
        if (!AppStorage.SessionExists && WelcomeRequested is not null)
        {
            WelcomeRequested();
            return;
        }
        var saved = AppStorage.LoadSession();
        _notifiedOnce = saved?.NotifiedOnce ?? false;
        UpdateFooter();
        var session = _settings.RestoreSession ? saved : null;
        if (session is { Workspaces.Count: > 0 })
        {
            foreach (var dto in session.Workspaces)
            {
                NewWorkspace(name: dto.Name, layout: dto.Layout, select: false);
            }
            Selected = Workspaces[Math.Clamp(session.SelectedIndex, 0, Workspaces.Count - 1)];
        }
        else
        {
            NewWorkspace();
        }
    }

    /// <summary>The window's placement, for session.json (set by MainWindow).</summary>
    public Func<WindowDto?>? CapturePlacement { get; set; }

    /// <summary>The window moved or resized, or the sidebar did.</summary>
    public void PlacementChanged() => ScheduleSave();

    private void ScheduleSave()
    {
        if (_shuttingDown) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveSession()
    {
        var session = new SessionDto
        {
            SelectedIndex = _selected is null ? 0 : Math.Max(0, Workspaces.IndexOf(_selected)),
            NotifiedOnce = _notifiedOnce,
            Window = CapturePlacement?.Invoke(),
            Workspaces = Workspaces.Where(w => w.Tree.Root is not null).Select(w => new WorkspaceDto
            {
                Name = w.Name,
                Layout = PaneLayoutSerializer.ToDto(w.Tree.Root!, slotId => SlotDto(w.Slots[slotId])),
            }).ToList(),
        };
        AppStorage.SaveSession(session);
    }

    /// <summary>
    /// A layout leaf. One tab keeps the original shape (cwd, profile); two or more add the tab list,
    /// with cwd/profile mirroring the active tab so an older aghouse still restores something sensible.
    /// </summary>
    private static PaneLayoutDto SlotDto(PaneSlot slot)
    {
        var active = slot.ActiveTab ?? slot.Tabs[0];
        var dto = new PaneLayoutDto { Type = "leaf", Cwd = active.Cwd, Profile = active.Profile.Id };
        if (slot.Tabs.Count > 1 || active.CustomName is not null)
        {
            dto.Tabs = slot.Tabs.Select(t => new TabDto { Cwd = t.Cwd, Profile = t.Profile.Id, Name = t.CustomName }).ToList();
            dto.ActiveTab = slot.Tabs.ToList().IndexOf(active);
        }
        return dto;
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private string NewId(string prefix) => $"{prefix}{++_nextId}";

    private string DefaultCwd() => _selected?.ActivePane?.Cwd ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string ResolveFont(string requested)
    {
        var installed = System.Windows.Media.Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in new[] { requested, "Cascadia Mono", "Cascadia Code", "Consolas" })
        {
            if (installed.Contains(candidate)) return candidate;
        }
        return "Consolas";
    }

    public void Dispose()
    {
        if (_shuttingDown) return; // Closing and a Windows sign-out can both get here
        _shuttingDown = true;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _saveTimer.Stop();
        _portTimer.Stop();
        _prTimer.Stop();
        SaveSession();
        _shortcuts.Dispose();
        foreach (var pane in Workspaces.SelectMany(w => w.Panes.Values).ToList()) pane.Dispose();
    }
}
