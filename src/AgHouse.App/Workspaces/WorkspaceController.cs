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
using AgHouse.App.Surfaces;
using AgHouse.App.Terminal;
using AgHouse.App.Updates;
using AgHouse.Core.Ipc;
using AgHouse.Core.Layout;
using AgHouse.Core.Terminal;
using AgHouse.Core.Themes;
using AgHouse.App.Theming;

namespace AgHouse.App.Workspaces;

public enum FooterState { Hint, Setup, Update, Waiting, Problem }

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
    private readonly PaneDrag _paneDrag;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _portTimer;
    private readonly DispatcherTimer _prTimer;
    private bool _scanningPorts;
    private bool _light;
    private ColorScheme _scheme;
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
        Themes = new ThemeCatalog(settings.Schemes);
        _scheme = Themes.Resolve(_light ? settings.LightTheme : settings.DarkTheme, _light);
        AppTheme.Apply(_scheme);
        _fontFamily = ResolveFont(settings.FontFamily);
        _paneDrag = new PaneDrag(MovePane);

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
        Updates = new UpdateService(() => _settings.Updates);
        Updates.Changed += UpdateFooter;
        UpdateFooter();
        Workspaces.CollectionChanged += (_, _) => QueueAgentRefresh();
    }

    /// <summary>New versions from GitHub Releases (Settings › About, the footer, <c>aghouse run restart-to-update</c>).</summary>
    public UpdateService Updates { get; }

    /// <summary>The window is closing for good: the quit wasn't called off.</summary>
    public bool IsShuttingDown => _shuttingDown;

    public ObservableCollection<Workspace> Workspaces { get; } = [];

    // ───────────────────────────── Sidebar footer ─────────────────────────────

    /// <summary>
    /// The footer line under the workspace list. In priority order: a settings.json problem
    /// (until restart), how many panes are waiting, a new aghouse version, how to set up agent
    /// hooks (until the first notification ever), or the Ctrl+Shift+U shortcut hint.
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
            FooterDetail = "Show what's waiting (Ctrl+Shift+I). Ctrl+Shift+U jumps to the top one: a pane that's asking something comes before one that finished.";
        }
        else if (UpdateFooterText() is var (text, detail))
        {
            FooterKind = FooterState.Update;
            FooterText = text;
            FooterDetail = detail;
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

    /// <summary>
    /// A new version, once there's something to do or know: it's downloaded, or it's out and needs
    /// a click. A background download in progress stays quiet.
    /// </summary>
    private (string Text, string Detail)? UpdateFooterText()
    {
        if (Updates?.Latest is not { } latest) return null;
        var name = $"aghouse {latest.Version}";
        return Updates.State switch
        {
            UpdateState.Ready => ($"{name} installs when you quit",
                $"{name} is downloaded and checked. It installs when aghouse quits, or open About to restart and update now."),
            UpdateState.Available or UpdateState.Failed when Updates.IsInstalled && latest.CanInstall => ($"{name} is available",
                "Open About to download it and restart."),
            UpdateState.Available or UpdateState.Failed => ($"{name} is available",
                "This copy of aghouse wasn't installed with the installer, so it can't update itself. Open About for the Releases page."),
            _ => null,
        };
    }

    // Hooks that run another copy of aghouse still reach this one (same pipe): no setup hint for them.
    private bool _hooksConnected = AgHouse.Core.ClaudeHooks.Inspect().Reaches;

    /// <summary>Re-reads whether Claude Code is connected (after Settings or the welcome screen connected it).</summary>
    public void RefreshFooter()
    {
        _hooksConnected = AgHouse.Core.ClaudeHooks.Inspect().Reaches;
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
            // Ctrl+1..9, a toast, the palette or Ctrl+Shift+U never land on a row hidden in a collapsed group.
            if (_settings.GroupWorkspaces && value.Group is { IsExpanded: false } collapsed) collapsed.IsExpanded = true;
            foreach (var g in _groups.Values) g.ContainsSelected = ReferenceEquals(value.Group, g);
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
        PaneLayoutDto? layout = null, bool select = true, int? tile = null)
    {
        var ws = new Workspace(NewId("w"), name?.Trim() ?? "");
        ws.View.RatiosChanged += ScheduleSave;
        ws.View.Visibility = Visibility.Hidden;
        ws.AgentsChanged += QueueAgentRefresh;
        ws.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Workspace.WaitingCount)) { UpdateFooter(); UpdateGroups(); }
            else if (e.PropertyName == nameof(Workspace.WorkingCount)) UpdateGroups();
            else if (e.PropertyName == nameof(Workspace.Project)) AssignGroup(ws); // found, or a cd into another repo
            else if (e.PropertyName == nameof(Workspace.Name)) { UpdateLabels(ws); QueueAgentRefresh(); } // a folder that matches the name stops being a useful label
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
        // A saved colour stays; a new workspace (or one from an older session) takes a hue its neighbours lack.
        ws.TileIndex = tile is int t && t >= 0 && t < SpaceTiles.Count ? t : SpaceTiles.Pick(ws.Name, Workspaces.Select(w => w.TileIndex));
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
        UpdateGroups();

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
        var order = SidebarOrder(); // as the sidebar shows them, groups included
        var i = _selected is null ? 0 : Math.Max(0, order.IndexOf(_selected));
        Selected = order[((i + delta) % order.Count + order.Count) % order.Count];
    }

    // ───────────────────────────── Panes ─────────────────────────────

    private PaneSlot CreateSlot(Workspace ws)
    {
        var slot = new PaneSlot(NewId("s"), TerminalThemes.BackgroundOf(_scheme));
        slot.FocusRequested += s => { if (s.ActiveTab is { } t) FocusPane(t.Id); };
        slot.TabActivated += p => FocusPane(p.Id);
        slot.CloseRequested += p => RequestClosePane(p.Id);
        slot.CloseOthersRequested += p => CloseOtherTabs(p.Id);
        slot.NewTabRequested += s => { if (s.ActiveTab is { } t) NewTab(t.Id); };
        slot.DuplicateRequested += p => NewTab(p.Id);
        slot.HeaderDragStarted += s => _paneDrag.Start(ws, s);
        slot.Renamed += _ => { UpdateLabels(ws); ScheduleSave(); };
        slot.UnzoomRequested += _ => { if (ws.ZoomedSlotId is not null) ToggleZoom(ws); };
        ws.Slots[slot.Id] = slot;
        return slot;
    }

    private PaneSession CreatePane(Workspace ws, PaneSlot slot, ShellProfile profile, string? cwd, int? index = null)
    {
        var id = NewId("p");
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var env = new Dictionary<string, string?>
        {
            [AgEnv.Pipe] = AgEnv.DefaultPipeName,
            [AgEnv.PaneId] = id,
            [AgEnv.PaneToken] = token,
            [AgEnv.WorkspaceId] = ws.Id,
            ["TERM_PROGRAM"] = "aghouse",
            // aghouse.exe (the CLI) lives next to the app.
            ["PATH"] = AppContext.BaseDirectory.TrimEnd('\\') + ";" + Environment.GetEnvironmentVariable("PATH"),
        };
        // Launched from a Claude Code shell, the app carries that session's markers and NO_COLOR.
        foreach (var name in InheritedEnv.ToUnset(Environment.GetEnvironmentVariables())) env[name] = null;
        foreach (var (k, v) in profile.Env) env[k] = v;

        var pane = new PaneSession(id, ws.Id, profile, cwd,
            TerminalThemes.From(_scheme), TerminalThemes.BackgroundOf(_scheme),
            _fontFamily, _settings.FontSize, env) { Token = token };

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
    /// Ends the pane's shell and starts a new one in its place: the same tab position and name, the
    /// same folder and shell unless <paramref name="profileId"/> or <paramref name="cwd"/> say otherwise.
    /// The new terminal gets a new pane id. Null if there's no such pane.
    /// </summary>
    public PaneSession? RestartPane(string paneId, string? profileId = null, string? cwd = null)
    {
        var (ws, old) = FindPane(paneId);
        if (ws is null || old is null || ws.SlotOf(old) is not { } slot) return null;

        var profile = profileId is null ? old.Profile : Profiles.Resolve(profileId);
        var wasActive = ws.ActivePaneId == paneId;
        // Inserted just before the old tab, so when it goes the new one is where it was (and shown if it was).
        var pane = CreatePane(ws, slot, profile, cwd ?? old.Cwd, index: slot.Tabs.ToList().IndexOf(old));
        pane.CustomName = old.CustomName;
        Log.Write($"restart {paneId} -> {pane.Id}");

        // Not ClosePane: that would take the slot or even the workspace with a lone pane.
        ws.Panes.Remove(paneId);
        ws.ForgetPane(paneId);
        slot.Remove(old);
        DisposePane(old); // its Exited then finds no pane to close

        if (wasActive)
        {
            SetActive(ws, pane.Id);
            if (ReferenceEquals(ws, _selected)) pane.FocusTerminal();
        }
        UpdateLabels(ws);
        ws.RefreshSummary();
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
        if (pane.DescribeRunning() is { } running && !ConfirmDialog.Ask(_window,
                $"Close this {what}?",
                $"“{TitleText.WithoutStatusGlyphs(running)}” is still running here. Closing the {what} ends it."
                + (pane.HasUnread ? " It's waiting on you." : ""),
                "Close and _end it"))
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
        var running = RunningSnapshot(ws.Panes.Values);
        var busy = ws.Panes.Values.Select(p => RunningItem(ws, p, running, withWorkspace: false)).OfType<ConfirmItem>().ToList();
        if (busy.Count > 0 && !ConfirmDialog.Ask(_window,
                $"Close “{ws.Name}”?",
                busy.Count == 1 ? "Closing the workspace ends this program:" : $"Closing the workspace ends these {busy.Count} programs:",
                busy.Count == 1 ? "Close and _end it" : "Close and _end them",
                busy))
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
        var running = RunningSnapshot(Workspaces.SelectMany(w => w.Panes.Values));
        var busy = Workspaces
            .SelectMany(w => w.Panes.Values.Select(p => RunningItem(w, p, running, withWorkspace: true)))
            .OfType<ConfirmItem>()
            .ToList();
        if (busy.Count == 0) return true;

        // A dialog owned by a minimized window would open hidden. Not BringToFront(): Show() throws while closing.
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        // Session restore brings back layout and folders, never processes: say so, it's the real cost.
        var body = (busy.Count == 1 ? "Quitting ends this program." : $"Quitting ends these {busy.Count} programs.")
                   + " Next time, aghouse reopens your workspaces and folders, not the programs.";
        if (ConfirmDialog.Ask(_window, "Quit aghouse?", body, busy.Count == 1 ? "_Quit and end it" : "_Quit and end them", busy))
            return true;
        _selected?.ActivePane?.FocusTerminal();
        return false;
    }

    /// <summary>A pane's running program as a confirmation row, or null when it's sitting at a prompt.</summary>
    private static ConfirmItem? RunningItem(Workspace ws, PaneSession pane,
        IReadOnlyDictionary<int, IReadOnlyList<string>> snapshot, bool withWorkspace)
    {
        if (pane.DescribeRunning(snapshot) is not { } running) return null;
        return new ConfirmItem(
            TitleText.WithoutStatusGlyphs(running),
            withWorkspace ? $"{ws.Name} › {pane.Label}" : pane.Label,
            pane.HasUnread,
            ws.AgentStateOf(pane.Id));
    }

    /// <summary>What runs under each of these panes' shells, from one snapshot of the process table.</summary>
    private static Dictionary<int, IReadOnlyList<string>> RunningSnapshot(IEnumerable<PaneSession> panes) =>
        ProcessTree.RunningUnder(panes.Select(p => p.ShellPid).OfType<int>());

    /// <summary><c>aghouse quit --force</c>: the window closes without asking about running programs.</summary>
    public bool QuitForced { get; private set; }

    /// <summary>Close the window as its ✕ would. With <paramref name="force"/>, nothing asks first.</summary>
    public void RequestQuit(bool force)
    {
        if (_shuttingDown) return;
        QuitForced = force;
        _window.Close();
        QuitForced = false; // the dialog said No: a later ✕ asks again
    }

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
        // A WinRT call into the notification platform: off the UI thread, it has no reason to block a close.
        var id = pane.Id;
        Task.Run(() => ToastService.ClearFor(id));
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
        // The top of the waiting list: an agent that's asking before one that finished, newest first.
        if (WaitingPanes() is [var (_, pane), ..]) FocusPane(pane.Id);
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
        // Every pane has a header; only split panes get its ✕ and a focus state.
        var split = ws.Slots.Count > 1;
        foreach (var slot in ws.Slots.Values)
        {
            slot.IsSplit = split;
            slot.AllowDrag = split && ws.ZoomedSlotId is null; // a zoomed pane has nowhere to go
        }
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

    /// <summary>
    /// Moves a pane (slot) to one side of another, or swaps the two: dragging a pane by its header, or
    /// `aghouse move-pane`. The terminals keep running; only their frames move. False when nothing changed.
    /// </summary>
    public bool MovePane(Workspace ws, string slotId, string targetSlotId, PaneDropSide side)
    {
        if (!ws.Slots.ContainsKey(slotId) || !ws.Slots.ContainsKey(targetSlotId)) return false;
        var moved = side switch
        {
            PaneDropSide.Swap => ws.Tree.Swap(slotId, targetSlotId),
            PaneDropSide.Left => ws.Tree.Move(slotId, targetSlotId, Direction.Left),
            PaneDropSide.Right => ws.Tree.Move(slotId, targetSlotId, Direction.Right),
            PaneDropSide.Up => ws.Tree.Move(slotId, targetSlotId, Direction.Up),
            _ => ws.Tree.Move(slotId, targetSlotId, Direction.Down),
        };
        if (!moved) return false;
        Log.Write($"move pane {slotId} {side} {targetSlotId}");
        ws.ZoomedSlotId = null;
        Render(ws);
        // The pane that moved is the one being handled: it keeps (or takes) the focus.
        if (ws.Slots[slotId].ActiveTab is { } tab)
        {
            if (ReferenceEquals(ws, _selected)) FocusPane(tab.Id); else SetActive(ws, tab.Id);
        }
        ScheduleSave();
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
        UpdateGroups(); // a group sits where its first workspace is
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
            // Without the agent's status glyph: it's a live spinner, so keeping it would rename the pane
            // (and lay out its header again) on every frame, and UI fonts often draw it as a box.
            var name = (pane.ProgramTitle is { } title ? TitleText.WithoutStatusGlyphs(title) : null)
                ??(pane.FolderName is { } folder && !NamesFolder(ws.Name, folder) ? folder : pane.Profile.Name);
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

    /// <param name="finished">The agent is done rather than asking something: it waits behind asking panes.</param>
    public void Notify(string? paneId, string? title, string? body, bool finished = false)
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
        if (pane is not null && !IsFocused(pane)) MarkUnread(pane, text, finished);
        var looking = UserIsLooking();
        Log.Write($"notify pane={pane?.Id} looking={looking}");

        if (!looking)
        {
            // A bare notification still says something: "aghouse" / "Needs you".
            if (_settings.Toasts) _toasts.Show(string.IsNullOrWhiteSpace(title) ? ws.Name : title,
                string.IsNullOrWhiteSpace(body) ? "Needs you" : body, ws.Id, pane?.Id,
                pane is null ? ws.Name : $"{ws.Name} › {pane.Label}");
            if (_settings.FlashTaskbar && WindowHandle is var hwnd && hwnd != IntPtr.Zero) Win32.FlashTaskbar(hwnd);
        }
    }

    // ───────────────────────────── Settings ─────────────────────────────

    /// <summary>The live settings. The Settings window edits a copy and hands it to <see cref="ApplySettings"/>.</summary>
    public AppSettings Settings => _settings;

    /// <summary>Why settings.json couldn't be read at launch, if it couldn't (Settings is read-only then).</summary>
    public SettingsProblem? SettingsProblem => _settingsProblem;

    /// <summary>Settings changed (from the Settings window or <c>aghouse settings set</c>).</summary>
    public event Action? SettingsChanged;

    /// <summary>
    /// Applies changed settings now, without a restart: the theme (chrome and terminals), the font,
    /// Ctrl+D, and the rest, which are read when they're used.
    /// </summary>
    public void ApplySettings(AppSettings changed)
    {
        var schemesChanged = changed.Schemes.ToJsonString() != _settings.Schemes.ToJsonString();
        var appearance = changed.Theme != _settings.Theme || changed.FontFamily != _settings.FontFamily
                         || changed.FontSize != _settings.FontSize || schemesChanged
                         || !ThemeCatalog.SameName(changed.DarkTheme, _settings.DarkTheme)
                         || !ThemeCatalog.SameName(changed.LightTheme, _settings.LightTheme);
        _settings.DarkTheme = changed.DarkTheme;
        _settings.LightTheme = changed.LightTheme;
        if (schemesChanged)
        {
            _settings.Schemes = (System.Text.Json.Nodes.JsonArray)changed.Schemes.DeepClone();
            Themes = new ThemeCatalog(_settings.Schemes);
        }
        _settings.DefaultProfile = changed.DefaultProfile;
        _settings.FontFamily = changed.FontFamily;
        _settings.FontSize = Math.Clamp(changed.FontSize, PaneSession.MinFontSize, PaneSession.MaxFontSize);
        _settings.RestoreSession = changed.RestoreSession;
        _settings.Toasts = changed.Toasts;
        _settings.FlashTaskbar = changed.FlashTaskbar;
        _settings.Theme = changed.Theme;
        _settings.CtrlDSplits = changed.CtrlDSplits;
        _settings.WarnMultilinePaste = changed.WarnMultilinePaste;
        var groupingChanged = changed.GroupWorkspaces != _settings.GroupWorkspaces;
        _settings.GroupWorkspaces = changed.GroupWorkspaces;
        if (groupingChanged) GroupingSettingApplied();
        var updatesChanged = changed.Updates != _settings.Updates;
        _settings.Updates = changed.Updates;
        if (updatesChanged) Updates.SettingChanged();

        _shortcuts.CtrlDSplits = _settings.CtrlDSplits;
        Application.Current.Resources["SplitRightGesture"] = _settings.CtrlDSplits ? "Ctrl+D" : "Alt+Shift+=";

        if (appearance)
        {
            Application.Current.ThemeMode = _settings.Theme switch
            {
                "light" => ThemeMode.Light,
                "dark" => ThemeMode.Dark,
                _ => ThemeMode.System,
            };
            _fontFamily = ResolveFont(_settings.FontFamily);
            ApplyAppearance();
        }
        // Last, so listeners (the Settings window) see the theme that's now on screen.
        SettingsChanged?.Invoke();
    }

    /// <summary>Every theme that can be picked: "Windows", the built-ins and settings.json's custom schemes.</summary>
    public ThemeCatalog Themes { get; private set; }

    /// <summary>The theme on screen now.</summary>
    public ColorScheme CurrentScheme => _scheme;

    /// <summary>
    /// Windows changed something the theme depends on. With Mode following Windows, a light/dark
    /// switch swaps to the other theme of the pair. An accent change or a contrast theme turning on
    /// or off rebuilds the colours. Background priority lets WPF swap its own Fluent theme first,
    /// since the scheme's colours are built from it.
    /// </summary>
    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.Accessibility
            or Microsoft.Win32.UserPreferenceCategory.Color)) return;
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_shuttingDown) return;
            var modeChanged = _settings.Theme == "system" && TerminalThemes.SystemUsesLightTheme() != _light;
            if (modeChanged || e.Category != Microsoft.Win32.UserPreferenceCategory.General) ApplyAppearance();
        });
    }

    /// <summary>The theme on screen changed (a setting, or Windows switching light/dark).</summary>
    public event Action? AppearanceChanged;

    /// <summary>Shows the theme for the current mode in the window and every terminal.</summary>
    private void ApplyAppearance()
    {
        _light = TerminalThemes.IsLight(_settings.Theme);
        _scheme = Themes.Resolve(_light ? _settings.LightTheme : _settings.DarkTheme, _light);
        AppTheme.Apply(_scheme);
        var theme = TerminalThemes.From(_scheme);
        var background = TerminalThemes.BackgroundOf(_scheme);
        foreach (var ws in Workspaces)
        {
            foreach (var slot in ws.Slots.Values) slot.Background = background;
            foreach (var pane in ws.Panes.Values) pane.SetAppearance(theme, background, _fontFamily, _settings.FontSize);
        }
        AppearanceChanged?.Invoke();
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

    private void MarkUnread(PaneSession pane, string? message, bool finished = false)
    {
        var before = (pane.HasUnread, pane.AttentionMessage);
        pane.RaiseAttention(message, finished);
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

    /// <summary>Every pane that needs you now: asking first, then finished, each newest first (<see cref="PaneSession.ByUrgency"/>).</summary>
    public IReadOnlyList<(Workspace Workspace, PaneSession Pane)> WaitingPanes()
    {
        var owner = Workspaces.SelectMany(w => w.Panes.Values.Where(p => p.HasUnread).Select(p => (p, w))).ToDictionary(x => x.p, x => x.w);
        return PaneSession.ByUrgency(owner.Keys).Select(p => (owner[p], p)).ToList();
    }

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
            QueueAgentRefresh();
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
            && s.Action is AppAction.Copy or AppAction.Paste or AppAction.PasteAny) return false;

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
                if (Workspaces.Count > 0 && SidebarOrder() is var order) // the numbers follow the sidebar, groups included
                    Selected = s.Index == 8 ? order[^1] : order.ElementAtOrDefault(s.Index) ?? _selected;
                return true;
            case AppAction.NextWorkspace: SelectRelative(1); return true;
            case AppAction.PreviousWorkspace: SelectRelative(-1); return true;
            case AppAction.JumpToUnread: JumpToUnread(); return true;
            case AppAction.RenameWorkspace: if (_selected is not null) _selected.IsRenaming = true; return true;
            case AppAction.Copy: return CopySelection(_selected?.ActivePane);
            case AppAction.Paste: return Paste(_selected?.ActivePane);
            case AppAction.PasteAny: return PasteAny(_selected?.ActivePane);
            case AppAction.CloseWorkspace: if (_selected is not null) RequestCloseWorkspace(_selected); return true;
            case AppAction.ZoomPane: ToggleZoom(); return true;
            case AppAction.Find: OpenFind(); return true;
            case AppAction.FontBigger: _selected?.ActivePane?.Zoom(1); return true;
            case AppAction.FontSmaller: _selected?.ActivePane?.Zoom(-1); return true;
            case AppAction.FontReset: _selected?.ActivePane?.Zoom(0); return true;
            case AppAction.CommandPalette or AppAction.WaitingList or AppAction.Settings or AppAction.ToggleSidebar:
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

    private bool Paste(PaneSession? pane)
    {
        if (pane is null) return false;
        string text;
        try { text = Clipboard.GetText(); } catch { return false; }
        return Paste(pane, text);
    }

    private bool Paste(PaneSession pane, string text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        // Without bracketed paste every line break is an Enter, and copied text can hide one.
        if (!pane.BracketedPaste && _settings.WarnMultilinePaste && PasteText.RunsLines(text))
        {
            // Asked after the key or click is handled: a modal loop inside a terminal input hook re-enters it.
            _window.Dispatcher.BeginInvoke(() =>
            {
                var ok = ConfirmPaste(text);
                if (FindPane(pane.Id).Pane is not { } still) return; // closed while the dialog was open
                if (ok) still.Paste(text);
                still.FocusTerminal();
            });
            return true;
        }
        pane.Paste(text);
        return true;
    }

    private bool ConfirmPaste(string text)
    {
        const int MaxShown = 6, MaxWidth = 100;
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var count = lines.Length - (lines[^1].Length == 0 ? 1 : 0); // a trailing break doesn't add a line
        var shown = lines.Take(MaxShown).Select(l =>
        {
            var clean = PasteText.Prepare(l, bracketed: false);
            return clean.Length > MaxWidth ? clean[..MaxWidth] + "…" : clean;
        });
        var preview = string.Join("\n", shown) + (lines.Length > MaxShown ? "\n…" : "");
        return ConfirmDialog.Ask(_window,
            count == 1 ? "Paste and run this line?" : $"Paste and run {count} lines?",
            "The shell runs each line as it arrives. Check it's what you meant to copy:\n\n" + preview
            + "\n\nSettings › General turns this question off.",
            "_Paste");
    }

    /// <summary>
    /// Plain Ctrl+V, which Windows Terminal also binds to paste. Programs like Claude Code count on
    /// the terminal for it: Claude ignores a bare Ctrl+V on Windows. Text pastes as Ctrl+Shift+V
    /// does, copied files as their paths, and an image as Alt+V (ESC v), Claude's image-paste key
    /// on Windows; Claude then reads the image from the clipboard itself. With nothing on the
    /// clipboard, Ctrl+V reaches the terminal unchanged.
    /// </summary>
    private bool PasteAny(PaneSession? pane)
    {
        if (pane is null) return false;
        bool hasImage;
        string? text = null;
        List<string?>? files = null;
        try
        {
            hasImage = Clipboard.ContainsImage() || Clipboard.ContainsData(DataFormats.Dib);
            if (Clipboard.ContainsText()) text = Clipboard.GetText();
            if (Clipboard.ContainsFileDropList()) files = Clipboard.GetFileDropList().Cast<string?>().ToList();
        }
        catch (Exception ex)
        {
            // Another program has the clipboard open. A raw ^V would do nothing useful, so drop it.
            Log.Write($"Ctrl+V: clipboard unavailable: {ex.Message}");
            return true;
        }

        switch (PasteText.Choose(hasImage, files is { Count: > 0 }, text))
        {
            case ClipboardPaste.Image: pane.SendText("\u001bv"); return true;
            case ClipboardPaste.Text: return Paste(pane, text!);
            case ClipboardPaste.Files: return Paste(pane, PasteText.FilePaths(files!));
            default: return false;
        }
    }

    // ───────────────────────────── Ports ─────────────────────────────

    /// <summary>
    /// Finds the TCP ports that programs under each workspace's shells listen on. Can't see inside WSL,
    /// for the same reason <see cref="PaneSession.RunningPrograms"/> can't.
    /// </summary>
    private async Task RefreshPortsAsync()
    {
        // Nobody sees the sidebar of a minimized window: the next tick after it comes back catches up.
        if (_scanningPorts || _shuttingDown || _window.WindowState == WindowState.Minimized) return;
        _scanningPorts = true;
        try
        {
            var shells = Workspaces.Select(w => (Workspace: w, Pids: w.Panes.Values.Select(p => p.ShellPid).OfType<int>().ToList())).ToList();
            var (ports, running) = await Task.Run(() =>
            {
                var listening = Interop.ListeningPorts.Snapshot();
                // What runs under each shell, too: the Agents list spots agents by name, and drops one whose shell has nothing left.
                var (trees, programs) = Interop.ProcessTree.DescendantsAndRunning([.. shells.SelectMany(s => s.Pids)]);
                var ports = shells.Select(s =>
                {
                    var pids = s.Pids.SelectMany(pid => trees.GetValueOrDefault(pid) ?? []).ToHashSet();
                    pids.ExceptWith(s.Pids); // a shell itself listening is plumbing, not a server the user started
                    return (IReadOnlyList<int>)listening.Where(l => pids.Contains(l.Pid)).Select(l => l.Port)
                        .Distinct().Order().ToList();
                }).ToList();
                return (ports, programs);
            });
            for (var i = 0; i < shells.Count; i++)
            {
                if (Workspaces.Contains(shells[i].Workspace)) shells[i].Workspace.SetPorts(ports[i]);
            }
            _runningUnder = running;
            RefreshAgents(); // also moves every agent's age on
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
        _collapsedGroups.UnionWith(saved?.CollapsedGroups ?? []);
        UpdateFooter();
        var session = _settings.RestoreSession ? saved : null;
        if (session is { Workspaces.Count: > 0 })
        {
            foreach (var dto in session.Workspaces)
            {
                NewWorkspace(name: dto.Name, layout: dto.Layout, select: false, tile: dto.Tile);
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
            CollapsedGroups = SavedCollapsedGroups(),
            Workspaces =Workspaces.Where(w => w.Tree.Root is not null).Select(w => new WorkspaceDto
            {
                Name = w.Name,
                Tile = w.TileIndex,
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
        foreach (var candidate in new[] { requested, "Cascadia Mono", "Cascadia Code", "Consolas" })
        {
            if (IsInstalled(candidate)) return candidate;
        }
        return "Consolas";
    }

    /// <summary>
    /// Whether a font family is installed, by asking for that one family. This runs before the window
    /// first shows, and listing every installed family instead took ~130 ms.
    /// </summary>
    private static bool IsInstalled(string family)
    {
        // A comma list or a font URI would make WPF fall back to another family and answer yes.
        if (string.IsNullOrWhiteSpace(family) || family.IndexOfAny([',', '#', '/']) >= 0) return false;
        try
        {
            var typeface = new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily(family),
                FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            return typeface.TryGetGlyphTypeface(out _);
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException) { return false; }
    }

    public void Dispose()
    {
        if (_shuttingDown) return; // Closing and a Windows sign-out can both get here
        _shuttingDown = true;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _saveTimer.Stop();
        _portTimer.Stop();
        _prTimer.Stop();
        Updates.Dispose();
        SaveSession();
        _shortcuts.Dispose();
        var panes = Workspaces.SelectMany(w => w.Panes.Values).ToList();
        foreach (var pane in panes) pane.Dispose();
        // Panes end their programs in the background; don't let the process exit before they have.
        try { Task.WaitAll(panes.Select(p => p.Ended).ToArray(), TimeSpan.FromSeconds(3)); } catch { }
    }
}
