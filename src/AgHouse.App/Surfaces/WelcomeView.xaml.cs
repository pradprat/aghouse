using System.Windows;
using System.Windows.Controls;
using AgHouse.App.Terminal;
using AgHouse.Core;

namespace AgHouse.App.Surfaces;

/// <summary>
/// The first launch, before any workspace exists: what aghouse is, a way to open the first terminal,
/// connecting Claude Code, and the keys worth knowing. It goes away for good once a workspace opens.
/// </summary>
public partial class WelcomeView : UserControl
{
    private readonly Action<string> _openIn;
    private readonly string _home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public WelcomeView(Action<string> openIn, string splitRightGesture)
    {
        InitializeComponent();
        _openIn = openIn;
        OpenHere.Content = $"Open a terminal in {PaneSession.FolderNameOf(_home)}";
        Keys.ItemsSource = new (string, string)[]
        {
            ("Ctrl+Shift+P", "Find any action, workspace or pane"),
            (splitRightGesture, "Split right (Ctrl+Shift+D splits down)"),
            ("Ctrl+Shift+T", "New tab in the pane"),
            ("Ctrl+Shift+U", "Jump to the pane that needs you"),
            ("Ctrl+Shift+I", "See everything that's waiting"),
            ("Ctrl+,", "Settings"),
        }.Select(k => Tuple.Create(k.Item1, k.Item2)).ToList();
        RefreshClaude();
        Loaded += (_, _) => OpenHere.Focus();
    }

    private void OpenHere_Click(object sender, RoutedEventArgs e) => _openIn(_home);

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Open a terminal in…", InitialDirectory = _home };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) _openIn(dialog.FolderName);
    }

    private void ConnectClaude_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } owner) SettingsWindow.ConnectClaudeCode(owner);
        RefreshClaude();
    }

    private void RefreshClaude()
    {
        var status = ClaudeHooks.Inspect();
        ClaudeStatus.Text = status.State switch
        {
            HookState.Connected => "Connected. Its panes will say when it's working and when it needs you.",
            HookState.Unreadable => "~/.claude/settings.json isn't valid JSON, so aghouse won't change it.",
            _ => "Connect it so a pane says “Needs you” when Claude Code asks for permission or finishes. aghouse asks before it changes anything.",
        };
        ConnectClaude.Visibility = status.State is HookState.NotConnected or HookState.Partial ? Visibility.Visible : Visibility.Collapsed;
    }
}
