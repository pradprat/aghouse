using System.Diagnostics;
using System.IO;
using System.Windows;
using AgHouse.App.Input;
using AgHouse.App.Persistence;
using AgHouse.App.Surfaces;
using AgHouse.Core.Layout;

namespace AgHouse.App;

/// <summary>The command palette, the waiting list and Settings: windows of their own, opened from here.</summary>
public partial class MainWindow
{
    private FlyoutWindow? _flyout;

    private void OnSurfaceRequested(AppAction action)
    {
        switch (action)
        {
            case AppAction.CommandPalette: ShowPalette(); break;
            case AppAction.WaitingList: ShowWaitingList(); break;
            case AppAction.Settings: OpenSettings(); break;
        }
    }

    private void BackToTerminal() => Controller.Selected?.ActivePane?.FocusTerminal();

    private bool Opening(FlyoutWindow flyout)
    {
        if (_flyout is { IsVisible: true }) return false;
        _flyout = flyout;
        flyout.Closed += (_, _) => { if (ReferenceEquals(_flyout, flyout)) _flyout = null; };
        return true;
    }

    // ───────────────────────────── Waiting list ─────────────────────────────

    public void ShowWaitingList()
    {
        if (_flyout is { IsVisible: true }) return;
        var waiting = Controller.WaitingPanes().Select(x => new WaitingRow
        {
            Where = Where(x.Workspace.Name, x.Pane.Label, x.Workspace.Panes.Count),
            Message = x.Pane.AttentionMessage,
            Waiting = true,
            At = x.Pane.UnreadAt,
            PaneId = x.Pane.Id,
        }).ToList();
        var earlier = Controller.Earlier.Select(h => new WaitingRow
        {
            Where = Where(h.WorkspaceName, h.PaneLabel, 2),
            Message = h.Message,
            Waiting = false,
            At = h.HandledAt,
            PaneId = h.PaneId,
        }).ToList();
        var window = new WaitingListWindow(this, FooterButton, waiting, earlier, Controller.FocusPane, BackToTerminal);
        if (Opening(window)) window.Show();
    }

    /// <summary>"claude agent › GitHub (2)", or just the workspace when it has one pane.</summary>
    private static string Where(string workspace, string pane, int paneCount) =>
        paneCount > 1 ? $"{workspace} › {pane}" : workspace;

    private void Footer_Click(object sender, RoutedEventArgs e)
    {
        switch (Controller.FooterKind)
        {
            case Workspaces.FooterState.Problem: OpenSettingsFile(); break;
            case Workspaces.FooterState.Setup: OpenSettings(SettingsPage.Integrations); break;
            default: ShowWaitingList(); break;
        }
    }

    // ───────────────────────────── Command palette ─────────────────────────────

    public void ShowPalette()
    {
        if (_flyout is { IsVisible: true }) return;
        var window = new PaletteWindow(this, PaletteItems(), BackToTerminal);
        if (Opening(window)) window.Show();
    }

    private List<PaletteItem> PaletteItems()
    {
        var items = new List<PaletteItem>();
        var order = 0;

        foreach (var (ws, pane) in Controller.WaitingPanes())
        {
            var paneId = pane.Id;
            items.Add(new PaletteItem
            {
                Group = "Waiting",
                Title = Where(ws.Name, pane.Label, ws.Panes.Count),
                Detail = pane.AttentionMessage,
                NeedsYou = true,
                Keywords = "waiting needs you",
                Order = order++,
                Run = () => Controller.FocusPane(paneId),
            });
        }

        order = 1000;
        var workspaces = Controller.Workspaces.ToList();
        for (var i = 0; i < workspaces.Count; i++)
        {
            var ws = workspaces[i];
            var number = i + 1;
            items.Add(new PaletteItem
            {
                Group = "Workspaces",
                Title = ws.Name,
                Detail = ws.HasBranch ? $"{ws.Branch}  {ws.Location}" : ws.Location,
                Gesture = number <= 8 ? $"Ctrl+{number}" : i == workspaces.Count - 1 ? "Ctrl+9" : null,
                WorkspaceNumber = number,
                Keywords = "workspace",
                Order = order++,
                Run = () => { if (Controller.Workspaces.Contains(ws)) Controller.Selected = ws; },
            });
        }

        // Panes and tabs: only in workspaces with more than one, where a name tells them apart.
        order = 2000;
        foreach (var ws in workspaces.Where(w => w.Panes.Count > 1))
        {
            foreach (var pane in ws.PanesInOrder())
            {
                var paneId = pane.Id;
                items.Add(new PaletteItem
                {
                    Group = "Panes",
                    Title = pane.Label,
                    Detail = $"in {ws.Name}",
                    Keywords = "pane tab " + pane.Cwd,
                    Order = order++,
                    Run = () => Controller.FocusPane(paneId),
                });
            }
        }

        order = 5000;
        void Action(string title, string? gesture, Action run, string? keywords = null) =>
            items.Add(new PaletteItem { Group = "Actions", Title = title, Gesture = gesture, Run = run, Keywords = keywords, Order = order++ });
        void Shortcut(string title, string? gesture, AppAction action, string? keywords = null, Direction direction = Direction.Left) =>
            Action(title, gesture, () => Controller.Run(action, direction: direction), keywords);

        var splitRight = (string)(Application.Current.Resources["SplitRightGesture"] ?? "Ctrl+D");
        var zoomed = Controller.Selected?.ZoomedSlotId is not null;
        Shortcut("New workspace", "Ctrl+Shift+N", AppAction.NewWorkspace);
        foreach (var profile in Controller.Profiles.All)
        {
            var id = profile.Id;
            Action($"New workspace with {profile.Name}", null, () => Controller.NewWorkspace(profileId: id), "shell profile");
        }
        Shortcut("New tab", "Ctrl+Shift+T", AppAction.NewTab);
        Shortcut("Split right", splitRight, AppAction.SplitRight, "pane");
        Shortcut("Split down", "Ctrl+Shift+D", AppAction.SplitDown, "pane");
        Shortcut(zoomed ? "Show all panes" : "Zoom pane", "Ctrl+Shift+Enter", AppAction.ZoomPane, "maximize fill unzoom");
        Shortcut("Find in pane output", "Ctrl+Shift+F", AppAction.Find, "search");
        Shortcut("Jump to the newest waiting pane", "Ctrl+Shift+U", AppAction.JumpToUnread, "needs you attention");
        Action("Show waiting list", "Ctrl+Shift+I", ShowWaitingList, "needs you earlier history");
        Shortcut("Next tab", "Ctrl+PageDown", AppAction.NextTab);
        Shortcut("Previous tab", "Ctrl+PageUp", AppAction.PreviousTab);
        Shortcut("Next workspace", "Ctrl+Tab", AppAction.NextWorkspace);
        Shortcut("Previous workspace", "Ctrl+Shift+Tab", AppAction.PreviousWorkspace);
        Shortcut("Focus pane on the left", "Alt+Left", AppAction.Focus, "move", Direction.Left);
        Shortcut("Focus pane on the right", "Alt+Right", AppAction.Focus, "move", Direction.Right);
        Shortcut("Focus pane above", "Alt+Up", AppAction.Focus, "move", Direction.Up);
        Shortcut("Focus pane below", "Alt+Down", AppAction.Focus, "move", Direction.Down);
        Shortcut("Rename workspace", "Ctrl+Shift+R", AppAction.RenameWorkspace);
        if (Controller.Selected is { } selected)
        {
            Action("Move workspace up", null, () => Controller.MoveWorkspace(selected, Controller.Workspaces.IndexOf(selected) - 1), "reorder");
            Action("Move workspace down", null, () => Controller.MoveWorkspace(selected, Controller.Workspaces.IndexOf(selected) + 1), "reorder");
        }
        Shortcut("Close tab or pane", "Ctrl+Shift+W", AppAction.ClosePane);
        Shortcut("Close workspace", "Ctrl+Alt+W", AppAction.CloseWorkspace);
        Shortcut("Bigger text", "Ctrl+=", AppAction.FontBigger, "zoom font size");
        Shortcut("Smaller text", "Ctrl+-", AppAction.FontSmaller, "zoom font size");
        Shortcut("Reset text size", "Ctrl+0", AppAction.FontReset, "zoom font size");
        Action("Settings", "Ctrl+,", () => OpenSettings(), "preferences options theme font");
        Action("Connect Claude Code", null, () => OpenSettings(SettingsPage.Integrations), "hooks integration setup");
        Action("Edit settings.json", null, OpenSettingsFile, "json config");
        Action("Open the logs folder", null, OpenLogsFolder, "diagnostics log");
        Action("About aghouse", null, () => OpenSettings(SettingsPage.About), "version");
        return items;
    }

    // ───────────────────────────── Welcome ─────────────────────────────

    /// <summary>First launch: the welcome screen fills the terminal area until the first workspace opens.</summary>
    private void ShowWelcome()
    {
        var welcome = new WelcomeView(cwd => Controller.NewWorkspace(cwd: cwd),
            (string)(Application.Current.Resources["SplitRightGesture"] ?? "Ctrl+D"));
        WorkspaceHost.Children.Add(welcome);
        void OnChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (Controller.Workspaces.Count == 0) return;
            Controller.Workspaces.CollectionChanged -= OnChanged;
            WorkspaceHost.Children.Remove(welcome);
            Controller.RefreshFooter();
        }
        Controller.Workspaces.CollectionChanged += OnChanged;
    }

    // ───────────────────────────── Settings ─────────────────────────────

    private SettingsWindow? _settingsWindow;

    /// <summary>Opens Settings at <paramref name="page"/>, or brings the open one forward on that page.</summary>
    public void OpenSettings(SettingsPage page = SettingsPage.General)
    {
        if (_settingsWindow is { IsVisible: true } open)
        {
            open.ShowPage(page);
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this, Controller, page);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            Controller.RefreshFooter(); // Claude Code may have been connected
            BackToTerminal();
        };
        _settingsWindow.Show();
    }

    // ───────────────────────────── Files ─────────────────────────────

    internal static void OpenSettingsFile()
    {
        AppStorage.EnsureSettingsFile(); // creates it if missing; an existing file is never rewritten
        var path = AppStorage.SettingsPath;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { Process.Start("notepad.exe", $"\"{path}\""); }
    }

    internal static void OpenLogsFolder()
    {
        var dir = Path.GetDirectoryName(Log.FilePath)!;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }
}
