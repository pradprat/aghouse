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

public enum FooterState { Hint, Waiting, Problem }

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
    private readonly bool _light;
    private readonly string _fontFamily;
    private Workspace? _selected;
    private int _nextId;
    private int _workspaceCounter;
    private bool _shuttingDown;
    private FooterState _footerKind;
    private string _footerText = "";
    private string? _footerDetail;

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
            RightClickHandler = OnRightClick,
            IsTerminalWindow = hwnd => Workspaces.SelectMany(w => w.Panes.Values).Any(p => p.OwnsWindow(hwnd)),
        };
        _shortcuts.MouseDown += OnMouseDown;
        _shortcuts.AttachTo(_window);

        _window.Activated += (_, _) => ClearFocusedUnread();
        _toasts.Activated += (ws, pane) => _window.Dispatcher.BeginInvoke(() => OnToastActivated(ws, pane));
        UpdateFooter();
    }

    public ObservableCollection<Workspace> Workspaces { get; } = [];

    // ───────────────────────────── Sidebar footer ─────────────────────────────

    /// <summary>
    /// The footer line under the workspace list. In priority order: a settings.json problem
    /// (until restart), how many panes are waiting, or the jump-to-unread shortcut hint.
    /// </summary>
    public FooterState FooterKind { get => _footerKind; private set => Set(ref _footerKind, value); }

    public string FooterText { get => _footerText; private set => Set(ref _footerText, value); }

    public string? FooterDetail { get => _footerDetail; private set => Set(ref _footerDetail, value); }

    private void UpdateFooter()
    {
        var waiting = Workspaces.Sum(w => w.WaitingCount);
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
            FooterDetail = "Jump to the pane that asked most recently (Ctrl+Shift+U)";
        }
        else
        {
            FooterKind = FooterState.Hint;
            FooterText = "Ctrl+Shift+U  jump to unread";
            FooterDetail = null;
        }
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
        var ws = new Workspace(NewId("w"), string.IsNullOrWhiteSpace(name) ? $"Workspace {++_workspaceCounter}" : name);
        ws.View.RatiosChanged += ScheduleSave;
        ws.View.Visibility = Visibility.Hidden;
        ws.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Workspace.WaitingCount)) UpdateFooter(); };

        if (layout is not null)
        {
            var root = PaneLayoutSerializer.FromDto(layout, leaf => CreatePane(ws, Profiles.Resolve(leaf.Profile), leaf.Cwd).Id);
            ws.Tree = new PaneTree(root);
        }
        else
        {
            var pane = CreatePane(ws, Profiles.Resolve(profileId), cwd ?? DefaultCwd());
            ws.Tree = new PaneTree(pane.Id);
        }

        ws.ActivePaneId = ws.Tree.Leaves()[0].PaneId;
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

    private PaneSession CreatePane(Workspace ws, ShellProfile profile, string? cwd)
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
        pane.Exited += p => { Log.Write($"exited {p.Id}"); ClosePane(p.Id); };
        pane.CloseRequested += p => RequestClosePane(p.Id);
        pane.FocusRequested += p => FocusPane(p.Id);
        pane.PropertyChanged += (_, e) => OnPanePropertyChanged(ws, pane, e);
        ws.Panes[id] = pane;
        return pane;
    }

    public PaneSession? Split(Direction side, string? paneId = null, string? profileId = null, string? cwd = null)
    {
        var (ws, source) = paneId is null ? (_selected, _selected?.ActivePane) : FindPane(paneId);
        if (ws is null || source is null) return null;

        var profile = profileId is null ? source.Profile : Profiles.Resolve(profileId);
        var pane = CreatePane(ws, profile, cwd ?? source.Cwd);
        Log.Write($"split {source.Id} {side} -> {pane.Id}");
        ws.Tree.Split(source.Id, side, pane.Id);
        Render(ws);
        FocusPane(pane.Id);
        ScheduleSave();
        return pane;
    }

    /// <summary>
    /// A person asked to close a pane (✕ or Ctrl+Shift+W). If something is running in it, ask first;
    /// an idle prompt closes straight away. Scripted closes (pipe API) and shell exits skip this.
    /// </summary>
    public void RequestClosePane(string paneId)
    {
        var (_, pane) = FindPane(paneId);
        if (pane is null) return;
        if (pane.DescribeRunning() is { } running && !Confirm(
                $"{running} is still running in “{pane.Label}”.\n\nClose the pane and end it?"))
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

    /// <summary>Warning dialog whose default button is the safe one, so a stray Enter never ends a process.</summary>
    private bool Confirm(string message) =>
        MessageBox.Show(_window, message, "aghouse", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
        == MessageBoxResult.Yes;

    public void ClosePane(string paneId)
    {
        var (ws, pane) = FindPane(paneId);
        if (ws is null || pane is null) return;
        Log.Write($"close {paneId}");

        var next = ws.Tree.Remove(paneId);
        ws.Panes.Remove(paneId);
        var cwd = pane.Cwd;
        DisposePane(pane);

        if (next is null)
        {
            RemoveWorkspace(ws, cwd);
            return;
        }

        if (ws.ActivePaneId == paneId) ws.ActivePaneId = next;
        Render(ws);
        SetActive(ws, ws.ActivePaneId!);
        if (ReferenceEquals(ws, _selected)) ws.ActivePane?.FocusTerminal();
        ScheduleSave();
    }

    private static void DisposePane(PaneSession pane)
    {
        WorkspaceView.Detach(pane.Frame);
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
        if (_selected?.ActivePaneId is not { } current) return;
        if (_selected.Tree.Neighbor(current, direction) is { } target) FocusPane(target);
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
        if (ReferenceEquals(ws, _selected) && UserIsLooking() && ws.ActivePane is { } active)
        {
            active.HasUnread = false;
        }
        ws.RefreshSummary();
    }

    private void Render(Workspace ws)
    {
        ws.View.Render(ws.Tree, id => ws.Panes[id].Frame);
        // Headers (with the close button) only while split; a lone pane stays chrome-free.
        var split = ws.Panes.Count > 1;
        foreach (var pane in ws.Panes.Values) pane.ShowHeader = split;
    }

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
        var text = string.IsNullOrWhiteSpace(body) ? title : body;
        if (pane is not null && !IsFocused(pane)) MarkUnread(pane, text);
        var looking = UserIsLooking();
        Log.Write($"notify pane={pane?.Id} looking={looking}");

        if (!looking)
        {
            if (_settings.Toasts) _toasts.Show(string.IsNullOrWhiteSpace(title) ? ws.Name : title, body ?? "", ws.Id, pane?.Id);
            if (WindowHandle is var hwnd && hwnd != IntPtr.Zero) Win32.FlashTaskbar(hwnd);
        }
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
            ScheduleSave();
        }
    }

    // ───────────────────────────── Input ─────────────────────────────

    private bool OnShortcut(Shortcut s)
    {
        // Only while our window has focus; never steal keys from other windows on this thread.
        if (!_window.IsActive) return false;
        // Let the rename box keep its keys.
        if (Workspaces.Any(w => w.IsRenaming) && s.Action is AppAction.Copy or AppAction.Paste) return false;

        switch (s.Action)
        {
            case AppAction.NewWorkspace: NewWorkspace(); return true;
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
        }
        return false;
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

    // ───────────────────────────── Session ─────────────────────────────

    public void RestoreOrCreate()
    {
        var session = _settings.RestoreSession ? AppStorage.LoadSession() : null;
        if (session is { Workspaces.Count: > 0 })
        {
            foreach (var dto in session.Workspaces)
            {
                NewWorkspace(name: dto.Name, layout: dto.Layout, select: false);
            }
            _workspaceCounter = Workspaces.Count;
            Selected = Workspaces[Math.Clamp(session.SelectedIndex, 0, Workspaces.Count - 1)];
        }
        else
        {
            NewWorkspace();
        }
    }

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
            Workspaces = Workspaces.Where(w => w.Tree.Root is not null).Select(w => new WorkspaceDto
            {
                Name = w.Name,
                Layout = PaneLayoutSerializer.ToDto(w.Tree.Root!, id => (w.Panes[id].Cwd, w.Panes[id].Profile.Id)),
            }).ToList(),
        };
        AppStorage.SaveSession(session);
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
        _shuttingDown = true;
        _saveTimer.Stop();
        SaveSession();
        _shortcuts.Dispose();
        foreach (var pane in Workspaces.SelectMany(w => w.Panes.Values).ToList()) pane.Dispose();
    }
}
