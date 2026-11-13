using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
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
        Controller.WelcomeRequested += ShowWelcome;
        Loaded += (_, _) => Controller.RestoreOrCreate();
        Closing += (_, e) =>
        {
            if (!Controller.ConfirmQuit()) { e.Cancel = true; return; }
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
            SidebarWidth = SidebarColumn.ActualWidth,
        };
    }

    private void SidebarSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        Controller.PlacementChanged();

    // ───────────────────────────── Reordering workspaces ─────────────────────────────

    private Point? _dragStart;
    private Workspace? _dragged;
    private InsertionAdorner? _insertion;

    private void WorkspaceList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Not from the rename box or a port chip: those keep their own clicks.
        var source = e.OriginalSource as DependencyObject;
        _dragged = FindAncestor<TextBox>(source) is null && FindAncestor<ListBoxItem>(source) is { DataContext: Workspace ws } ? ws : null;
        _dragStart = _dragged is null ? null : e.GetPosition(WorkspaceList);
    }

    private void WorkspaceList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || _dragged is not { } ws || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(WorkspaceList) - start;
        if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance * 2
            && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance * 2) return;
        _dragStart = null;
        try { DragDrop.DoDragDrop(WorkspaceList, new DataObject(typeof(Workspace), ws), DragDropEffects.Move); }
        finally { RemoveInsertion(); _dragged = null; }
    }

    private void WorkspaceList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Workspace)) is not Workspace)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move;
        var (_, lineY) = DropTarget(e.GetPosition(WorkspaceList));
        if (_insertion is null && AdornerLayer.GetAdornerLayer(WorkspaceList) is { } layer)
        {
            _insertion = new InsertionAdorner(WorkspaceList);
            layer.Add(_insertion);
        }
        _insertion?.MoveTo(lineY);
        e.Handled = true;
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
        if (e.Data.GetData(typeof(Workspace)) is not Workspace ws) return;
        var (index, _) = DropTarget(e.GetPosition(WorkspaceList));
        var from = Controller.Workspaces.IndexOf(ws);
        if (from < 0) return;
        Controller.MoveWorkspace(ws, index > from ? index - 1 : index);
        e.Handled = true;
    }

    /// <summary>The gap the pointer is nearest: the insertion index (0..Count) and the y of the line in list coordinates.</summary>
    private (int Index, double LineY) DropTarget(Point p)
    {
        var count = Controller.Workspaces.Count;
        double lastBottom = 0;
        for (var i = 0; i < count; i++)
        {
            if (WorkspaceList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item) continue;
            var top = item.TranslatePoint(new Point(0, 0), WorkspaceList).Y;
            var bottom = top + item.ActualHeight;
            if (p.Y < top + item.ActualHeight / 2) return (i, top - 1);
            lastBottom = bottom;
        }
        return (count, lastBottom + 1);
    }

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
        if (WorkspaceOf(sender) is { } ws) Controller.MoveWorkspace(ws, Controller.Workspaces.IndexOf(ws) - 1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceOf(sender) is { } ws) Controller.MoveWorkspace(ws, Controller.Workspaces.IndexOf(ws) + 1);
    }

    private void NewWorkspace_Click(object sender, RoutedEventArgs e) => Controller.NewWorkspace();

    private void ProfileMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ProfileButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var profile in Controller.Profiles.All)
        {
            var item = new MenuItem { Header = profile.Name };
            item.Click += (_, _) => Controller.NewWorkspace(profileId: profile.Id);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

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

    /// <summary>A port chip opens the dev server in the default browser.</summary>
    private void Chip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SidebarChip { Port: { } port }) return;
        try { Process.Start(new ProcessStartInfo($"http://localhost:{port}") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { /* no default browser */ }
        e.Handled = true;
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
