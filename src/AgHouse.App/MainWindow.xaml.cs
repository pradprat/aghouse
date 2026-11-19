using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AgHouse.App.Notifications;
using AgHouse.App.Persistence;
using AgHouse.App.Terminal;
using AgHouse.App.Workspaces;
using AgHouse.Core.Layout;

namespace AgHouse.App;

public partial class MainWindow : Window
{
    public MainWindow(AppSettings settings, SettingsProblem? settingsProblem, ShellProfiles profiles, ToastService toasts)
    {
        InitializeComponent();
        Controller = new WorkspaceController(this, WorkspaceHost, settings, settingsProblem, profiles, toasts);
        DataContext = Controller;
        RestorePlacement(AppStorage.LoadSession()?.Window);
        Controller.CapturePlacement = CapturePlacement;
        LocationChanged += (_, _) => Controller.PlacementChanged();
        SizeChanged += (_, _) => Controller.PlacementChanged();
        StateChanged += (_, _) => Controller.PlacementChanged();
        Controller.SurfaceRequested += OnSurfaceRequested;
        Controller.UiRequested += OnUiRequested;
        Controller.WelcomeRequested += ShowWelcome;
        Controller.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(WorkspaceController.GroupWorkspaces)) ApplyGrouping(); };
        Controller.SidebarGroupsChanged += QueueRegroup;
        ApplyGrouping();
        Loaded += (_, _) => Controller.RestoreOrCreate();
        Closing += (_, e) =>
        {
            if (!Controller.QuitForced && !Controller.ConfirmQuit()) { e.Cancel = true; return; }
            Controller.Dispose();
        };
    }

    public WorkspaceController Controller { get; }

    // ───────────────────────────── Placement ─────────────────────────────

    /// <summary>
    /// Puts the window where it was last time, unless that place is now off every screen (a monitor
    /// was unplugged); then it opens centred, as on first run.
    /// </summary>
    private void RestorePlacement(WindowDto? p)
    {
        if (p is null) return;
        if (p.SidebarWidth >= SidebarColumn.MinWidth)
        {
            SidebarColumn.Width = new GridLength(Math.Min(p.SidebarWidth, SidebarColumn.MaxWidth));
        }
        if (p.SidebarCollapsed) SetSidebarCollapsed(true);
        if (p.Width < MinWidth || p.Height < MinHeight) return;
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        // The title bar must be reachable: at least 120×40 of its top edge on screen.
        var titleBar = new Rect(p.Left, p.Top, p.Width, 40);
        titleBar.Intersect(screen);
        if (titleBar.IsEmpty || titleBar.Width < 120 || titleBar.Height < 40) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        (Left, Top, Width, Height) = (p.Left, p.Top, p.Width, p.Height);
        if (p.Maximized) WindowState = WindowState.Maximized;
    }

    private WindowDto? CapturePlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (bounds.IsEmpty || bounds.Width <= 0) return null;
        return new WindowDto
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
            SidebarWidth = IsSidebarCollapsed ? _openSidebarWidth : SidebarColumn.ActualWidth,
            SidebarCollapsed = IsSidebarCollapsed,
        };
    }

    private void SidebarSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        Controller.PlacementChanged();

    // ───────────────────────────── Sidebar rail ─────────────────────────────

    public static readonly DependencyProperty IsSidebarCollapsedProperty =
        DependencyProperty.Register(nameof(IsSidebarCollapsed), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    /// <summary>The sidebar is folded to a rail of tiles (Ctrl+Shift+B, the fold button). The sidebar's templates bind to it.</summary>
    public bool IsSidebarCollapsed
    {
        get => (bool)GetValue(IsSidebarCollapsedProperty);
        private set => SetValue(IsSidebarCollapsedProperty, value);
    }

    /// <summary>The rail: a 20px tile with 10px either side, inside the sidebar's 6px and 2px margins.</summary>
    private const double RailWidth = 48;
    private double _openSidebarWidth = 248;

    public void ToggleSidebar()
    {
        SetSidebarCollapsed(!IsSidebarCollapsed);
        Controller.PlacementChanged();
    }

    private void SetSidebarCollapsed(bool collapsed)
    {
        if (collapsed == IsSidebarCollapsed) return;
        if (collapsed)
        {
            _openSidebarWidth = SidebarColumn.ActualWidth > 0 ? SidebarColumn.ActualWidth : SidebarColumn.Width.Value;
            SidebarColumn.MinWidth = 0;
            SidebarColumn.Width = new GridLength(RailWidth);
            SidebarColumn.MaxWidth = RailWidth;
            Sidebar.Margin = new Thickness(6, 4, 2, 8);
        }
        else
        {
            SidebarColumn.MaxWidth = 480;
            SidebarColumn.MinWidth = 170;
            SidebarColumn.Width = new GridLength(Math.Clamp(_openSidebarWidth, 170, 480));
            Sidebar.Margin = new Thickness(8, 4, 4, 8);
        }
        IsSidebarCollapsed = collapsed;
        SidebarSplitter.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        // On the rail every control sits centred over the tiles.
        HeaderBar.Margin = collapsed ? new Thickness(0, 2, 0, 2) : new Thickness(6, 2, 0, 6);
        HeaderBar.HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        RailNewButton.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.HorizontalContentAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        // One glyph both ways: the hamburger (GlobalNavButton) is the sidebar toggle Windows users know from NavigationView
        // and Settings; the tooltip says which way it goes. ClosePane/OpenPane (E89F/E8A0) read as generic panels.
        FoldGlyph.Text = "";
        FoldButton.ToolTip = collapsed ? "Open the sidebar (Ctrl+Shift+B)" : "Fold the sidebar (Ctrl+Shift+B)";
        AutomationProperties.SetName(FoldButton, collapsed ? "Open the sidebar" : "Fold the sidebar");
        // The rail hides Agents and the footer's status line and adds the + row: refit once that layout has settled.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, FitWorkspaceList);
    }

    /// <summary>One step of the keyboard resize: the palette's "Make the sidebar wider / narrower".</summary>
    private const double SidebarStep = 32;

    /// <summary>Widens or narrows the open sidebar within its 170–480px range. Widening the rail opens it first.</summary>
    public void ResizeSidebar(double delta)
    {
        if (IsSidebarCollapsed)
        {
            if (delta < 0) return;
            SetSidebarCollapsed(false);
        }
        else
        {
            var width = SidebarColumn.ActualWidth > 0 ? SidebarColumn.ActualWidth : SidebarColumn.Width.Value;
            SidebarColumn.Width = new GridLength(Math.Clamp(width + delta, 170, 480));
        }
        Controller.PlacementChanged();
    }

    private void FoldSidebar_Click(object sender, RoutedEventArgs e) => ToggleSidebar();

    private void Search_Click(object sender, RoutedEventArgs e) => ShowPalette();

    /// <summary>The Agents list takes what it needs, up to 45% of the sidebar; the workspace list may use the rest.</summary>
    private void Sidebar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        AgentsScroll.MaxHeight = Math.Max(80, e.NewSize.Height * 0.45);
        FitWorkspaceList();
    }

    /// <summary>Agents or the footer changed height (an agent started, the status line wrapped): refit the list.</summary>
    private void SidebarBand_SizeChanged(object sender, SizeChangedEventArgs e) => FitWorkspaceList();

    /// <summary>
    /// The workspace list is as tall as its rows, so Agents sits right under it, but no taller than what the header,
    /// Agents and the footer leave: past that it scrolls, as before. The slack row under Agents takes what's over.
    /// </summary>
    private void FitWorkspaceList()
    {
        var rows = Sidebar.RowDefinitions;
        var others = rows[0].ActualHeight + rows[1].ActualHeight + rows[3].ActualHeight + rows[5].ActualHeight;
        WorkspaceList.MaxHeight = Math.Max(48, Sidebar.ActualHeight - others);
    }

    private void AgentsHistory_Click(object sender, RoutedEventArgs e) => ShowWaitingList();

    private void AgentRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AgentRow row) Controller.GoToAgent(row);
    }

    /// <summary>The ⋯ next to "Workspaces": a new workspace with a chosen shell, and how the list is grouped.</summary>
    private void SpacesMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SpacesMenuButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var with = new MenuItem { Header = "New workspace with" };
        foreach (var profile in Controller.Profiles.All)
        {
            var item = new MenuItem { Header = profile.Name };
            item.Click += (_, _) => Controller.NewWorkspace(profileId: profile.Id);
            with.Items.Add(item);
        }
        menu.Items.Add(with);
        menu.Items.Add(new Separator());
        var grouped = Controller.GroupWorkspaces;
        var group = new MenuItem { Header = "Group by project", IsCheckable = true, IsChecked = grouped };
        group.Click += (_, _) => Controller.SetGroupWorkspaces(!grouped);
        menu.Items.Add(group);
        if (grouped)
        {
            var collapse = new MenuItem { Header = "Collapse all groups" };
            collapse.Click += (_, _) => Controller.SetAllGroupsExpanded(false);
            var expand = new MenuItem { Header = "Expand all groups" };
            expand.Click += (_, _) => Controller.SetAllGroupsExpanded(true);
            menu.Items.Add(collapse);
            menu.Items.Add(expand);
        }
        menu.IsOpen = true;
    }

    /// <summary>A row's menu opens the dev servers its programs listen on (ports used to be chips on the row).</summary>
    private void RowMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        foreach (var added in menu.Items.OfType<FrameworkElement>().Where(i => Equals(i.Tag, PortItemTag)).ToList()) menu.Items.Remove(added);
        if (menu.DataContext is not Workspace { Ports.Count: > 0 } ws) return;
        var at = 0;
        foreach (var port in ws.Ports)
        {
            var item = new MenuItem { Header = $"Open localhost:{port}", Tag = PortItemTag };
            item.Click += (_, _) => OpenPort(port);
            menu.Items.Insert(at++, item);
        }
        menu.Items.Insert(at, new Separator { Tag = PortItemTag });
    }

    private const string PortItemTag = "port";

    private static void OpenPort(int port)
    {
        try { Process.Start(new ProcessStartInfo($"http://localhost:{port}") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { /* no default browser */ }
    }

    // ───────────────────────────── Reordering workspaces ─────────────────────────────

    // A row drags within the list (grouped: within its own group, since its folder decides the group).
    // A group header drags the whole group between the others.

    private Point? _dragStart;
    private object? _dragged; // a Workspace or a SidebarGroup
    private InsertionAdorner? _insertion;

    private void WorkspaceList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Not from the rename box or a port chip: those keep their own clicks.
        var source = e.OriginalSource as DependencyObject;
        _dragged = FindAncestor<TextBox>(source) is not null ? null
            // A lone row (a project with one workspace, so no header) carries its group: it moves between groups.
            : FindAncestor<ListBoxItem>(source) is { DataContext: Workspace ws } ? (Controller.GroupWorkspaces && ws.Group is { HasHeader: false } lone ? lone : (object)ws)
            : FindAncestor<GroupItem>(source) is { DataContext: CollectionViewGroup { Name: SidebarGroup g } } ? g
            : null;
        _dragStart = _dragged is null ? null : e.GetPosition(WorkspaceList);
    }

    private void WorkspaceList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || _dragged is not { } dragged || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(WorkspaceList) - start;
        if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance * 2
            && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance * 2) return;
        _dragStart = null;
        try { DragDrop.DoDragDrop(WorkspaceList, new DataObject(dragged.GetType(), dragged), DragDropEffects.Move); }
        finally { RemoveInsertion(); _dragged = null; }
    }

    private void WorkspaceList_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DropTarget(e) is not { LineY: var lineY })
        {
            e.Effects = DragDropEffects.None;
            RemoveInsertion();
            return;
        }
        e.Effects = DragDropEffects.Move;
        if (_insertion is null && AdornerLayer.GetAdornerLayer(WorkspaceList) is { } layer)
        {
            _insertion = new InsertionAdorner(WorkspaceList);
            layer.Add(_insertion);
        }
        _insertion?.MoveTo(lineY);
    }

    private void WorkspaceList_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when crossing into a child; only drop the line when the pointer left the list.
        var p = e.GetPosition(WorkspaceList);
        if (p.X < 0 || p.Y < 0 || p.X > WorkspaceList.ActualWidth || p.Y > WorkspaceList.ActualHeight) RemoveInsertion();
    }

    private void WorkspaceList_Drop(object sender, DragEventArgs e)
    {
        RemoveInsertion();
        if (DropTarget(e) is not { Gap: var gap }) return;
        e.Handled = true;
        if (e.Data.GetData(typeof(SidebarGroup)) is SidebarGroup group)
        {
            Controller.MoveGroup(group, gap);
            return;
        }
        if (e.Data.GetData(typeof(Workspace)) is not Workspace ws || Controller.Workspaces.IndexOf(ws) is var from && from < 0) return;
        // The gap is among the rows it may land between: before one of them, or after the last.
        var rows = RowsFor(ws);
        if (rows.Count == 0) return;
        if (gap < rows.Count)
        {
            var to = Controller.Workspaces.IndexOf(rows[gap].Workspace);
            Controller.MoveWorkspace(ws, to > from ? to - 1 : to);
        }
        else
        {
            var to = Controller.Workspaces.IndexOf(rows[^1].Workspace);
            Controller.MoveWorkspace(ws, to >= from ? to : to + 1);
        }
    }

    /// <summary>
    /// Where a drop would land: the gap index among the rows (or groups) it may go between, and the y of
    /// the insertion line in list coordinates. Null where it can't land (a row outside its own group).
    /// </summary>
    private (int Gap, double LineY)? DropTarget(DragEventArgs e)
    {
        var p = e.GetPosition(WorkspaceList);
        if (e.Data.GetData(typeof(SidebarGroup)) is SidebarGroup)
            return Gap([.. GroupItems().Select(g => (FrameworkElement)g.Item)], p);
        if (e.Data.GetData(typeof(Workspace)) is not Workspace ws) return null;

        var rows = RowsFor(ws);
        if (rows.Count == 0) return null;
        if (Controller.GroupWorkspaces && FindAncestor<GroupItem>(rows[0].Item) is { } groupItem)
        {
            // Only over its own group (header to last row): its folder, not the drag, decides the group.
            var top = groupItem.TranslatePoint(new Point(0, 0), WorkspaceList).Y;
            if (p.Y < top || p.Y > top + groupItem.ActualHeight) return null;
        }
        return Gap([.. rows.Select(r => (FrameworkElement)r.Item)], p);
    }

    /// <summary>The rows a dragged row may land between, in sidebar order: all of them, or (grouped) its group's.</summary>
    private List<(Workspace Workspace, ListBoxItem Item)> RowsFor(Workspace dragged)
    {
        var rows = new List<(Workspace, ListBoxItem)>();
        foreach (var ws in Controller.SidebarOrder())
        {
            if (Controller.GroupWorkspaces && !ReferenceEquals(ws.Group, dragged.Group)) continue;
            if (WorkspaceList.ItemContainerGenerator.ContainerFromItem(ws) is ListBoxItem { IsVisible: true } item) rows.Add((ws, item));
        }
        return rows;
    }

    /// <summary>The group containers on screen, in sidebar order (the same order as <see cref="WorkspaceController.GroupsInOrder"/>).</summary>
    private List<(SidebarGroup Group, GroupItem Item)> GroupItems()
    {
        var items = new List<(SidebarGroup, GroupItem)>();
        if (WorkspaceList.Items.Groups is not { } groups) return items;
        foreach (var g in groups.OfType<CollectionViewGroup>())
        {
            if (g.Name is SidebarGroup group && WorkspaceList.ItemContainerGenerator.ContainerFromItem(g) is GroupItem { IsVisible: true } item)
                items.Add((group, item));
        }
        return items;
    }

    /// <summary>The gap nearest <paramref name="p"/> among <paramref name="elements"/> (0..Count), and where its line goes.</summary>
    private (int Gap, double LineY) Gap(IReadOnlyList<FrameworkElement> elements, Point p)
    {
        double lastBottom = 0;
        for (var i = 0; i < elements.Count; i++)
        {
            var top = elements[i].TranslatePoint(new Point(0, 0), WorkspaceList).Y;
            if (p.Y < top + elements[i].ActualHeight / 2) return (i, top - 1);
            lastBottom = top + elements[i].ActualHeight;
        }
        return (elements.Count, lastBottom + 1);
    }

    // ───────────────────────────── Grouping ─────────────────────────────

    /// <summary>
    /// Turns the list's grouping on or off with the setting. Set on the ListBox's own Items, not on the
    /// collection's default view: that one can be collected and come back without grouping.
    /// </summary>
    private void ApplyGrouping()
    {
        var items = WorkspaceList.Items;
        var grouped = Controller.GroupWorkspaces;
        if (grouped == items.GroupDescriptions.Count > 0) return;
        if (grouped) items.GroupDescriptions.Add(ByProject());
        else items.GroupDescriptions.Clear();
        Reselect();
    }

    private static PropertyGroupDescription ByProject() =>
        new(nameof(Workspace.Group)) { CustomSort = GroupOrder.Instance };

    private bool _regroupQueued;

    /// <summary>
    /// A workspace changed group (its project was found, or it cd'd into another repo) or the groups
    /// changed order: group and sort again, once, after the burst (a restored session finds every
    /// workspace's project at about the same time). Not live grouping: that adds new groups in arrival
    /// order, ignoring the sort, and drops the selection on a later dispatcher pass.
    /// </summary>
    private void QueueRegroup()
    {
        if (_regroupQueued || WorkspaceList.Items.GroupDescriptions.Count == 0) return;
        _regroupQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            _regroupQueued = false;
            var descriptions = WorkspaceList.Items.GroupDescriptions;
            if (descriptions.Count == 0) return;
            // A new description regroups from scratch. Items.Refresh() didn't: the rows stayed in the groups
            // they had when the view first grouped them (before any workspace knew its project).
            using (WorkspaceList.Items.DeferRefresh())
            {
                descriptions.Clear();
                descriptions.Add(ByProject());
            }
            Reselect();
        });
    }

    /// <summary>Regrouping drops the list's selection: select the controller's workspace again and keep it in view.</summary>
    private void Reselect()
    {
        if (Controller.Selected is not { } ws) return;
        WorkspaceList.SelectedItem = ws;
        WorkspaceList.ScrollIntoView(ws);
    }

    /// <summary>Groups sit where their first workspace is (<see cref="SidebarGroup.Order"/>); rows not yet in a group go last.</summary>
    private sealed class GroupOrder : System.Collections.IComparer
    {
        public static readonly GroupOrder Instance = new();

        public int Compare(object? x, object? y) => Key(x).CompareTo(Key(y));

        private static int Key(object? o) => o switch
        {
            CollectionViewGroup { Name: SidebarGroup g } => g.Order,
            SidebarGroup g => g.Order,
            _ => int.MaxValue,
        };
    }

    private void CollapseAllGroups_Click(object sender, RoutedEventArgs e) => Controller.SetAllGroupsExpanded(false);

    private void ExpandAllGroups_Click(object sender, RoutedEventArgs e) => Controller.SetAllGroupsExpanded(true);

    private void Ungroup_Click(object sender, RoutedEventArgs e) => Controller.SetGroupWorkspaces(false);

    private void RemoveInsertion()
    {
        if (_insertion is null) return;
        AdornerLayer.GetAdornerLayer(WorkspaceList)?.Remove(_insertion);
        _insertion = null;
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null and not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    /// <summary>A 2px neutral line between rows where a dragged workspace will land. Never the accent.</summary>
    private sealed class InsertionAdorner(UIElement list) : Adorner(list)
    {
        private double _y = -10;

        public void MoveTo(double y)
        {
            if (Math.Abs(y - _y) < 0.5) return;
            _y = y;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var brush = (Brush?)TryFindResource("TextFillColorPrimaryBrush") ?? Brushes.Gray;
            var width = ((FrameworkElement)AdornedElement).ActualWidth;
            dc.DrawRoundedRectangle(brush, null, new Rect(4, _y - 1, Math.Max(0, width - 8), 2), 1, 1);
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceOf(sender) is { } ws) Controller.MoveWorkspaceBy(ws, -1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceOf(sender) is { } ws) Controller.MoveWorkspaceBy(ws, 1);
    }

    private void NewWorkspace_Click(object sender, RoutedEventArgs e) => Controller.NewWorkspace();

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private static Workspace? WorkspaceOf(object sender) => (sender as FrameworkElement)?.DataContext as Workspace;

    private void WorkspaceItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && WorkspaceOf(sender) is { } ws)
        {
            ws.IsRenaming = true;
            e.Handled = true;
        }
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceOf(sender) is { } ws) ws.IsRenaming = true;
    }

    private void SplitRight_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceOf(sender) is { ActivePaneId: { } id }) Controller.Split(Direction.Right, id);
    }

    private void SplitDown_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceOf(sender) is { ActivePaneId: { } id }) Controller.Split(Direction.Down, id);
    }

    private void CloseWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceOf(sender) is { } ws) Controller.RequestCloseWorkspace(ws);
    }

    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box)
        {
            box.Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || WorkspaceOf(box) is not { } ws) return;
        if (e.Key == Key.Enter)
        {
            Controller.RenameWorkspace(ws, box.Text);
            ws.ActivePane?.FocusTerminal();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ws.IsRenaming = false;
            ws.ActivePane?.FocusTerminal();
            e.Handled = true;
        }
    }

    private void RenameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box && WorkspaceOf(box) is { IsRenaming: true } ws) Controller.RenameWorkspace(ws, box.Text);
    }
}
