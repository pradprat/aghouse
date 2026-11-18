using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using AgHouse.App.Persistence;
using AgHouse.App.Terminal;
using AgHouse.App.Updates;
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

        LoadControls();
        VersionLabel.Text = VersionText();

        if (controller.SettingsProblem is { } problem)
        {
            // Read-only: saving now would overwrite the file the user is about to fix.
            ProblemBar.Visibility = Visibility.Visible;
            ProblemTitle.Text = problem.Summary;
            ProblemDetail.Text = $"{problem.Detail}\naghouse is running on default settings. Fix the file and restart aghouse to change settings here.";
            foreach (var control in new Control[] { DefaultShell, CtrlDSplits, RestoreSession, WarnMultilinePaste, ThemeChoice, FontChoice, SizeChoice, Toasts, FlashTaskbar, UpdatesChoice })
            {
                control.IsEnabled = false;
            }
        }

        RefreshClaude();
        Nav.SelectedIndex = (int)page;
        _loading = false;

        // A script may change settings while this is open (aghouse settings set): show what's in effect.
        controller.SettingsChanged += OnSettingsChanged;
        controller.Updates.Changed += RefreshUpdates;
        Closed += (_, _) =>
        {
            controller.SettingsChanged -= OnSettingsChanged;
            controller.Updates.Changed -= RefreshUpdates;
        };
        RefreshUpdates();
    }

    private bool _applying;

    private void OnSettingsChanged()
    {
        RefreshUpdates(); // its words depend on the updates setting
        if (_applying) return; // our own change: the controls already show it
        _loading = true;
        try { LoadControls(); }
        finally { _loading = false; }
    }

    /// <summary>Puts the current settings into the controls.</summary>
    private void LoadControls()
    {
        var s = _controller.Settings;
        DefaultShell.ItemsSource = _controller.Profiles.All;
        DefaultShell.SelectedItem = _controller.Profiles.Default;
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
        WarnMultilinePaste.IsChecked = s.WarnMultilinePaste;
        UpdatesChoice.SelectedItem = UpdatesChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.Updates) ?? UpdatesChoice.Items[0];
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
        s.WarnMultilinePaste = WarnMultilinePaste.IsChecked == true;
        if (UpdatesChoice.SelectedItem is ComboBoxItem { Tag: string updates }) s.Updates = updates;

        _applying = true;
        try { _controller.ApplySettings(s); }
        finally { _applying = false; }
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
        var path = Unbroken(ClaudeHooks.DefaultPath);
        ClaudeStatus.Text = status switch
        {
            { State: HookState.Connected } =>
                "Connected. A Claude Code session in any pane shows “Working” while it's busy and “Needs you” when it asks or finishes.",
            // Every hook runs aghouse, just another copy of it: that works, so don't call it "partly connected".
            { OtherCopyPath: { } other } =>
                $"Connected through another copy of aghouse ({Unbroken(other)}). Use this copy to point the hooks at this one instead.",
            { OtherCopy: not null } =>
                "Connected through whichever aghouse is on PATH (an older hook). Use this copy to point the hooks at this one by its full path.",
            { State: HookState.Partial } =>
                $"Partly connected: {string.Join(", ", status.Missing)} {(status.Missing.Count == 1 ? "doesn't" : "don't")} run aghouse yet. Connecting adds {(status.Missing.Count == 1 ? "it" : "them")} to {path}.",
            { State: HookState.NotConnected } =>
                $"Not connected. Connecting adds aghouse's hook to {path} so Claude Code can say when it needs you. Your other settings stay as they are.",
            _ => $"{path} isn't valid JSON, so aghouse won't change it. {status.Problem}",
        };
        ConnectClaude.Content = status.OtherCopy is not null ? "Use this copy" : "Connect Claude Code";
        ConnectClaude.Visibility = status.State is HookState.NotConnected or HookState.Partial ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A path with "~" for the profile folder, kept in one piece (word joiners) where WPF would break it at a backslash.</summary>
    private static string Unbroken(string path) =>
        path.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~", StringComparison.OrdinalIgnoreCase)
            .Replace("\\", "⁠\\⁠").Replace(".", "⁠.⁠");

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
        var message = status.OtherCopy is { } other
            ? $"aghouse will change these Claude Code hooks in {ClaudeHooks.DefaultPath} from “{other}” to “{ClaudeHooks.Command}”:\n\n"
              + string.Join("\n", status.Missing.Select(m => "•  " + m))
              + "\n\nYour other settings and hooks stay as they are, and the file is copied to settings.json.aghouse-backup first. "
              + "Claude Code sessions started after this run this copy of aghouse.\n\nChange the hooks?"
            : $"aghouse will add “{ClaudeHooks.Command}” to these Claude Code hooks in {ClaudeHooks.DefaultPath}:\n\n"
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

    /// <summary>The Updates row: what the last check found and the one button that does the next thing.</summary>
    private void RefreshUpdates()
    {
        var u = _controller.Updates;
        var latest = u.Latest;
        var name = latest is null ? "" : $"aghouse {latest.Version}";
        var canInstall = u.IsInstalled && latest is { CanInstall: true };
        var auto = _controller.Settings.Updates == "auto";

        (UpdateTitle.Text, UpdateStatus.Text) = u.State switch
        {
            UpdateState.Checking => ("Checking for updates…", "Asking GitHub for the newest release."),
            UpdateState.UpToDate => ("aghouse is up to date",
                $"Version {u.Current} is the newest. Checked at {u.LastChecked:t}."),
            UpdateState.Downloading => ($"Downloading {name}…", $"{u.Progress}%"),
            UpdateState.Ready => ($"{name} is ready",
                auto ? "Downloaded and checked. It installs when you quit aghouse, or restart to update now."
                     : "Downloaded and checked. Restart to install it."),
            UpdateState.Available when canInstall => ($"{name} is available",
                auto ? "It will download in the background." : "Download and restart installs it."),
            UpdateState.Available => ($"{name} is available",
                "This copy of aghouse wasn't installed with the installer, so it can't update itself. Get it from the release page."),
            UpdateState.Failed => (latest is null ? "Couldn't check for updates" : $"Couldn't download {name}", u.Error ?? ""),
            _ => ("Updates", _controller.Settings.Updates == "off"
                ? "aghouse doesn't check by itself. Check now, or see the Releases page."
                : "aghouse checks GitHub for a new version every few hours."),
        };

        UpdateNow.Visibility = Visibility.Collapsed;
        if (latest is not null && u.State is UpdateState.Ready or UpdateState.Available or UpdateState.Failed)
        {
            UpdateNow.Content = u.State == UpdateState.Ready ? "Restart to update"
                : canInstall ? "Download and restart"
                : "Open the release page";
            UpdateNow.Visibility = Visibility.Visible;
        }
        CheckUpdates.IsEnabled = u.State is not (UpdateState.Checking or UpdateState.Downloading or UpdateState.Ready);

        WhatsNew.Visibility = latest is null ? Visibility.Collapsed : Visibility.Visible;
        if (latest is not null) WhatsNewLink.NavigateUri = new Uri(latest.PageUrl);
    }

    private void CheckUpdates_Click(object sender, RoutedEventArgs e) => _ = _controller.Updates.CheckAsync();

    private void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        var u = _controller.Updates;
        if (u.IsInstalled && u.Latest is { CanInstall: true })
        {
            (Owner as MainWindow)?.RestartToUpdate();
        }
        else if (u.Latest is { } latest)
        {
            try { Process.Start(new ProcessStartInfo(latest.PageUrl) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { /* no default browser */ }
        }
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
