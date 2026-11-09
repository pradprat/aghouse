using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        Loaded += (_, _) => Controller.RestoreOrCreate();
        Closing += (_, _) => Controller.Dispose();
    }

    public WorkspaceController Controller { get; }

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

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        AppStorage.EnsureSettingsFile(); // creates it if missing; an existing file is never rewritten
        var path = AppStorage.SettingsPath;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { Process.Start("notepad.exe", $"\"{path}\""); }
    }

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
