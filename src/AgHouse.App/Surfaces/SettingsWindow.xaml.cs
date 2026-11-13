using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using AgHouse.App.Persistence;
using AgHouse.App.Terminal;
using AgHouse.App.Workspaces;
using AgHouse.Core;

namespace AgHouse.App.Surfaces;

public enum SettingsPage { General, Appearance, Notifications, Integrations, About }

/// <summary>
/// Settings (Ctrl+, or the gear). Each change applies at once and is written to settings.json,
/// keeping any keys aghouse doesn't know. When settings.json couldn't be read at launch, the page
/// is read-only and says so, because aghouse never overwrites a file it couldn't parse.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly int[] Sizes = [8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24];

    private readonly WorkspaceController _controller;
    private bool _loading = true;

    public SettingsWindow(Window owner, WorkspaceController controller, SettingsPage page)
    {
        InitializeComponent();
        Owner = owner;
        _controller = controller;

        var s = controller.Settings;
        DefaultShell.ItemsSource = controller.Profiles.All;
        DefaultShell.SelectedItem = controller.Profiles.Default;
        CtrlDSplits.IsChecked = s.CtrlDSplits;
        RestoreSession.IsChecked = s.RestoreSession;
        ThemeChoice.SelectedItem = ThemeChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.Theme) ?? ThemeChoice.Items[0];
        var fonts = WorkspaceController.MonospaceFonts().ToList();
        if (!fonts.Contains(s.FontFamily, StringComparer.OrdinalIgnoreCase)) fonts.Insert(0, s.FontFamily);
        FontChoice.ItemsSource = fonts;
        FontChoice.SelectedItem = fonts.First(f => string.Equals(f, s.FontFamily, StringComparison.OrdinalIgnoreCase));
        var sizes = Sizes.Contains(s.FontSize) ? Sizes : [.. Sizes.Append(s.FontSize).Order()];
        SizeChoice.ItemsSource = sizes;
        SizeChoice.SelectedItem = s.FontSize;
        Toasts.IsChecked = s.Toasts;
        FlashTaskbar.IsChecked = s.FlashTaskbar;
        VersionLabel.Text = VersionText();

        if (controller.SettingsProblem is { } problem)
        {
            // Read-only: saving now would overwrite the file the user is about to fix.
            ProblemBar.Visibility = Visibility.Visible;
            ProblemTitle.Text = problem.Summary;
            ProblemDetail.Text = $"{problem.Detail}\naghouse is running on default settings. Fix the file and restart aghouse to change settings here.";
            foreach (var control in new Control[] { DefaultShell, CtrlDSplits, RestoreSession, ThemeChoice, FontChoice, SizeChoice, Toasts, FlashTaskbar })
            {
                control.IsEnabled = false;
            }
        }

        RefreshClaude();
        Nav.SelectedIndex = (int)page;
        _loading = false;
    }

    public void ShowPage(SettingsPage page) => Nav.SelectedIndex = (int)page;

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var tag = (Nav.SelectedItem as ListBoxItem)?.Tag as string;
        foreach (var (name, panel) in new[]
                 {
                     ("General", (FrameworkElement)GeneralPage), ("Appearance", AppearancePage), ("Notifications", NotificationsPage),
                     ("Integrations", IntegrationsPage), ("About", AboutPage),
                 })
        {
            panel.Visibility = name == tag ? Visibility.Visible : Visibility.Collapsed;
        }
        if (tag == "Integrations") RefreshClaude(); // the user may have edited the file meanwhile
    }

    /// <summary>Any control changed: apply it now, then write settings.json.</summary>
    private void Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _controller.SettingsProblem is not null) return;
        var s = _controller.Settings.Clone();
        // Only once someone picks a shell: an unset default keeps following "the first shell found".
        if (ReferenceEquals(sender, DefaultShell) && DefaultShell.SelectedItem is ShellProfile profile) s.DefaultProfile = profile.Id;
        s.CtrlDSplits = CtrlDSplits.IsChecked == true;
        s.RestoreSession = RestoreSession.IsChecked == true;
        if (ThemeChoice.SelectedItem is ComboBoxItem { Tag: string theme }) s.Theme = theme;
        if (FontChoice.SelectedItem is string font) s.FontFamily = font;
        if (SizeChoice.SelectedItem is int size) s.FontSize = size;
        s.Toasts = Toasts.IsChecked == true;
        s.FlashTaskbar = FlashTaskbar.IsChecked == true;

        _controller.ApplySettings(s);
        if (!AppStorage.SaveSettings(_controller.Settings))
        {
            ProblemBar.Visibility = Visibility.Visible;
            ProblemTitle.Text = "Couldn't save settings.json";
            ProblemDetail.Text = "The change applies until aghouse closes. The log has the details.";
        }
    }

    // ───────────── Claude Code ─────────────

    private void RefreshClaude()
    {
        var status = ClaudeHooks.Inspect();
        // Word joiners keep the path in one piece when the line wraps (WPF would break it at a backslash).
        var path = ClaudeHooks.DefaultPath.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~")
            .Replace("\\", "⁠\\⁠").Replace(".", "⁠.⁠");
        ClaudeStatus.Text = status.State switch
        {
            HookState.Connected =>
                "Connected. A Claude Code session in any pane shows “Working” while it's busy and “Needs you” when it asks or finishes.",
            HookState.Partial =>
                $"Partly connected: {string.Join(", ", status.Missing)} {(status.Missing.Count == 1 ? "doesn't" : "don't")} run aghouse yet. Connecting adds {(status.Missing.Count == 1 ? "it" : "them")} to {path}.",
            HookState.NotConnected =>
                $"Not connected. Connecting adds aghouse's hook to {path} so Claude Code can say when it needs you. Your other settings stay as they are.",
            _ => $"{path} isn't valid JSON, so aghouse won't change it. {status.Problem}",
        };
        ConnectClaude.Visibility = status.State is HookState.NotConnected or HookState.Partial ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ConnectClaude_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectClaudeCode(this)) RefreshClaude();
    }

    /// <summary>
    /// Says exactly what will change in ~/.claude/settings.json, asks, then adds the hooks.
    /// Shared with the welcome screen. True when Claude Code is connected afterwards.
    /// </summary>
    public static bool ConnectClaudeCode(Window owner)
    {
        var status = ClaudeHooks.Inspect();
        if (status.State == HookState.Connected) return true;
        if (status.State == HookState.Unreadable)
        {
            MessageBox.Show(owner, $"{ClaudeHooks.DefaultPath} isn't valid JSON, so aghouse won't change it.\n\n{status.Problem}",
                "aghouse", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        var message = $"aghouse will add “{ClaudeHooks.Command}” to these Claude Code hooks in {ClaudeHooks.DefaultPath}:\n\n"
                      + string.Join("\n", status.Missing.Select(m => "•  " + m))
                      + "\n\nYour other settings and hooks stay as they are, and the file is copied to settings.json.aghouse-backup first. "
                      + "Claude Code sessions started after this report to aghouse.\n\nAdd the hooks?";
        if (MessageBox.Show(owner, message, "aghouse", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            != MessageBoxResult.Yes) return false;
        try
        {
            ClaudeHooks.Install();
            return true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            Log.Write($"claude hooks: {ex.Message}");
            MessageBox.Show(owner, $"aghouse couldn't change {ClaudeHooks.DefaultPath}.\n\n{ex.Message}", "aghouse",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    // ───────────── About ─────────────

    /// <summary>"Version 0.1.0 · 03d5770": the commit comes from the build's informational version.</summary>
    private static string VersionText()
    {
        var info = typeof(SettingsWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = info.IndexOf('+');
        var version = plus > 0 ? info[..plus] : info;
        var commit = plus > 0 ? info[(plus + 1)..] : null;
        return commit is { Length: >= 7 } ? $"Version {version} · {commit[..7]}" : $"Version {version}";
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { /* no default browser */ }
        e.Handled = true;
    }

    private void Logs_Click(object sender, RoutedEventArgs e) => MainWindow.OpenLogsFolder();

    private void EditJson_Click(object sender, RoutedEventArgs e) => MainWindow.OpenSettingsFile();
}
