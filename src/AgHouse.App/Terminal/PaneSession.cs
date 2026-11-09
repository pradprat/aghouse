using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AgHouse.App.Interop;
using AgHouse.Core.Terminal;
using EasyWindowsTerminalControl;
using EasyWindowsTerminalControl.Internals;
using Microsoft.Terminal.Wpf;

namespace AgHouse.App.Terminal;

/// <summary>
/// One terminal pane: a ConPTY session (<see cref="TermPTY"/>) rendered by Windows Terminal's
/// control, plus the escape-sequence tap that feeds notifications/cwd to the rest of the app.
/// </summary>
public sealed class PaneSession : ObservableObject, IDisposable
{
    private readonly Dispatcher _ui;
    private readonly OscParser _parser = new();
    private readonly List<TerminalEvent> _scratch = [];
    private readonly Grid _header;
    private readonly Border _headerTint;
    private readonly TextBlock _headerText;
    private readonly TextBlock _attentionText;
    private readonly System.Windows.Documents.Run _attentionMessage;
    private IProcess? _process;
    private bool _disposed;
    private bool _attached;
    private bool _focusWhenAttached;

    private string? _cwd;
    private string? _title;
    private bool _hasUnread;
    private bool _isActive;

    public PaneSession(string id, string workspaceId, ShellProfile profile, string? cwd, TerminalTheme theme,
        Color background, string fontFamily, int fontSize, IReadOnlyDictionary<string, string?> env)
    {
        _ui = Dispatcher.CurrentDispatcher;
        Id = id;
        WorkspaceId = workspaceId;
        Profile = profile;
        _cwd = ValidDirectory(cwd) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Control = new EasyTerminalControl
        {
            StartupCommandLine = profile.CommandLine,
            Theme = theme,
            FontFamilyWhenSettingTheme = new FontFamily(fontFamily),
            FontSizeWhenSettingTheme = fontSize,
            Win32InputMode = true,
        };
        // The control creates and auto-starts its own TermPTY when it loads. We start ours
        // (with our env + factory) instead, so detach the default before it can start.
        Control.ConPTYTerm = null;

        // Header strip (only shown while the workspace is split): location, attention message, close button.
        // It sits above the terminal, never on top of it (the terminal is a native HWND).
        _headerText = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 8, 0),
        };

        // "Needs you · <message>": attention said in words, so the accent ring can't be read as focus.
        var needsYou = new System.Windows.Documents.Run("Needs you") { FontWeight = FontWeights.SemiBold };
        needsYou.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
        _attentionMessage = new System.Windows.Documents.Run();
        _attentionMessage.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextFillColorSecondaryBrush");
        _attentionText = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = Visibility.Collapsed,
        };
        _attentionText.Inlines.Add(needsYou);
        _attentionText.Inlines.Add(_attentionMessage);

        // Faint accent wash behind the header of a pane that needs you (a separate layer so the text keeps full opacity).
        _headerTint = new Border { CornerRadius = new CornerRadius(3), Opacity = 0.16, Visibility = Visibility.Collapsed };
        _headerTint.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");

        var closeButton = new Button
        {
            Content = new TextBlock
            {
                Text = "",
                FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
                FontSize = 10,
            },
            Padding = new Thickness(7, 3, 7, 3),
            MinWidth = 0,
            MinHeight = 0,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Close pane (Ctrl+Shift+W)",
        };
        System.Windows.Automation.AutomationProperties.SetName(closeButton, "Close pane");
        closeButton.Click += (_, _) => CloseRequested?.Invoke(this);

        var headerRow = new DockPanel();
        DockPanel.SetDock(closeButton, Dock.Right);
        DockPanel.SetDock(_headerText, Dock.Left);
        headerRow.Children.Add(closeButton);
        headerRow.Children.Add(_headerText);
        headerRow.Children.Add(_attentionText);

        _header = new Grid { Height = 26, Background = Brushes.Transparent, Visibility = Visibility.Collapsed };
        _header.Children.Add(_headerTint);
        _header.Children.Add(headerRow);
        _header.MouseLeftButtonDown += (_, _) => FocusRequested?.Invoke(this);

        var body = new DockPanel();
        DockPanel.SetDock(_header, Dock.Top);
        body.Children.Add(_header);
        body.Children.Add(Control);
        UpdateHeaderText();

        Frame = new Border
        {
            Child = body,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 2, 2),
            Background = new SolidColorBrush(background),
            CornerRadius = new CornerRadius(4),
            SnapsToDevicePixels = true,
        };
        UpdateFrame();

        Pty = new TermPTY();
        Pty.InterceptOutputToUITerminal = OnOutput;

        var factory = new EnvProcessFactory(env);
        factory.ProcessStarted += OnProcessStarted;

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Pty.TermReady += (_, _) => ready.TrySetResult();

        var startDir = _cwd;
        Task.Factory.StartNew(() =>
        {
            try
            {
                Pty.Start(profile.CommandLine, 120, 30, false, factory, startDir);
            }
            catch (Exception ex)
            {
                ready.TrySetException(ex);
            }
        }, TaskCreationOptions.LongRunning);

        _ = AttachWhenReadyAsync(ready.Task);
    }

    public string Id { get; }
    public string WorkspaceId { get; }
    public ShellProfile Profile { get; }
    public Border Frame { get; }
    public EasyTerminalControl Control { get; }
    public TermPTY Pty { get; }
    public DateTime UnreadAt { get; private set; }

    /// <summary>Raised on the UI thread for notifications and bells.</summary>
    public event Action<PaneSession, TerminalEvent>? TerminalEvent;

    /// <summary>Raised on the UI thread when the shell process exits or fails to start.</summary>
    public event Action<PaneSession>? Exited;

    /// <summary>The header's close button was clicked.</summary>
    public event Action<PaneSession>? CloseRequested;

    /// <summary>The header was clicked.</summary>
    public event Action<PaneSession>? FocusRequested;

    public string? Cwd
    {
        get => _cwd;
        private set { if (Set(ref _cwd, value)) UpdateHeaderText(); }
    }

    public string? Title
    {
        get => _title;
        private set { if (Set(ref _title, value)) UpdateHeaderText(); }
    }

    /// <summary>Show the header strip (with the close button). The controller turns it on while split.</summary>
    public bool ShowHeader
    {
        get => _header.Visibility == Visibility.Visible;
        set
        {
            _header.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            UpdateFrame(); // a lone pane drops its focus border
        }
    }

    /// <summary>
    /// Programs running under this pane's shell (empty when it's sitting at a prompt). Can't see
    /// inside WSL: Linux processes aren't in the Windows process table.
    /// </summary>
    public IReadOnlyList<string> RunningPrograms() =>
        _process is ProcessFactory.WrappedProcess { HasExited: false } p ? ProcessTree.RunningUnder(p.Pid) : [];

    /// <summary>A short name for what's running, for confirmations: the program's own title if it set one, else its exe.</summary>
    public string? DescribeRunning()
    {
        var running = RunningPrograms();
        if (running.Count == 0) return null;
        if (_title is { Length: > 0 } t && !t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return t.Trim();
        return running.Count == 1 ? running[0] : $"{running[0]} and {running.Count - 1} more";
    }

    /// <summary>The pane's label (folder or program title), as the header shows it.</summary>
    public string Label => _headerText.Text;

    /// <summary>What the agent said when it asked for attention (shown in the header while unread).</summary>
    public string? AttentionMessage { get; private set; }

    /// <summary>Mark this pane as needing the user, optionally with the agent's message.</summary>
    public void RaiseAttention(string? message)
    {
        // A bare bell keeps a message the agent is still waiting on; a new message replaces it.
        if (!string.IsNullOrWhiteSpace(message)) AttentionMessage = message.Trim();
        else if (!HasUnread) AttentionMessage = null;
        _attentionMessage.Text = AttentionMessage is null ? "" : " · " + AttentionMessage;
        _attentionText.ToolTip = AttentionMessage;
        HasUnread = true;
        UnreadAt = DateTime.UtcNow; // a repeat request on a waiting pane counts as the newest
        UpdateFrame();
    }

    private void UpdateHeaderText()
    {
        // Prefer a program-set title (e.g. "vim"), but not the shell's own exe path.
        var title = _title is { Length: > 0 } t && !t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? t : null;
        var cwd = _cwd is null ? null : Path.GetFileName(_cwd.TrimEnd('\\')) is { Length: > 0 } leaf ? leaf : _cwd;
        _headerText.Text = title ?? cwd ?? Profile.Name;
        _headerText.ToolTip = _cwd;
    }

    public bool HasUnread
    {
        get => _hasUnread;
        set
        {
            if (!Set(ref _hasUnread, value)) return;
            if (value) UnreadAt = DateTime.UtcNow;
            else AttentionMessage = null;
            UpdateFrame();
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set { if (Set(ref _isActive, value)) UpdateFrame(); }
    }

    private async Task AttachWhenReadyAsync(Task ready)
    {
        try
        {
            await ready;
        }
        catch (Exception ex)
        {
            await _ui.InvokeAsync(() =>
            {
                MessageBox.Show($"Could not start '{Profile.Name}':\n{ex.Message}", "aghouse",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Exited?.Invoke(this);
            });
            return;
        }

        await _ui.InvokeAsync(async () =>
        {
            if (_disposed) return;
            var term = Control.Terminal;
            if (!term.IsLoaded)
            {
                var loaded = new TaskCompletionSource();
                void OnLoaded(object s, RoutedEventArgs e) { term.Loaded -= OnLoaded; loaded.TrySetResult(); }
                term.Loaded += OnLoaded;
                await loaded.Task;
            }
            // Let the control's own Loaded handler finish first.
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            if (_disposed) return;

            Control.ConPTYTerm = Pty;
            _attached = true;
            if (_focusWhenAttached) FocusTerminal();
        });
    }

    private void OnProcessStarted(IProcess process)
    {
        _process = process;
        Task.Factory.StartNew(() =>
        {
            try { process.WaitForExit(); } catch { /* already gone */ }
            _ui.BeginInvoke(() => { if (!_disposed) Exited?.Invoke(this); });
        }, TaskCreationOptions.LongRunning);
    }

    /// <summary>Runs on the ConPTY reader thread for every output chunk. Observes only.</summary>
    private void OnOutput(ref Span<char> str)
    {
        _parser.Feed(str, _scratch);
        if (_scratch.Count == 0) return;
        var events = _scratch.ToArray();
        _scratch.Clear();
        _ui.BeginInvoke(() =>
        {
            if (_disposed) return;
            foreach (var e in events)
            {
                switch (e)
                {
                    case CwdEvent c: Cwd = c.Path; break;
                    case TitleEvent t: Title = t.Title; break;
                    default: TerminalEvent?.Invoke(this, e); break;
                }
            }
        });
    }

    public void SendText(string text) => Pty.WriteToTerm(text);

    public string? GetSelectedText()
    {
        try { return Control.Terminal.GetSelectedText(); }
        catch { return null; }
    }

    public void FocusTerminal()
    {
        if (!_attached) { _focusWhenAttached = true; return; }
        var host = FindHost();
        if (host is not null && host.Handle != IntPtr.Zero)
        {
            host.Focus();
            Win32.SetFocus(host.Handle);
        }
        else
        {
            Control.Terminal.Focus();
        }
    }

    /// <summary>True if <paramref name="hwnd"/> is this pane's terminal window (or a child of it).</summary>
    public bool OwnsWindow(IntPtr hwnd)
    {
        var host = FindHost();
        if (host is null || host.Handle == IntPtr.Zero || hwnd == IntPtr.Zero) return false;
        return host.Handle == hwnd || Win32.IsChild(host.Handle, hwnd);
    }

    private HwndHost? FindHost() => FindChild<HwndHost>(Control);

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>
    /// Three pane states, kept apart by shape and words, not colour alone:
    /// needs you = 2px accent ring + tinted "Needs you" header; focused (split only) = 1px primary-text border
    /// + bright header text; at rest = 1px control stroke + dim header text.
    /// </summary>
    private void UpdateFrame()
    {
        if (Frame is null) return; // ShowHeader can run before the frame exists
        var focused = IsActive && ShowHeader;

        if (HasUnread)
        {
            Frame.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
            Frame.BorderThickness = new Thickness(2);
        }
        else if (focused)
        {
            Frame.SetResourceReference(Border.BorderBrushProperty, "TextFillColorPrimaryBrush");
            Frame.BorderThickness = new Thickness(1);
        }
        else
        {
            Frame.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            Frame.BorderThickness = new Thickness(1);
        }

        _headerTint.Visibility = HasUnread ? Visibility.Visible : Visibility.Collapsed;
        _attentionText.Visibility = HasUnread ? Visibility.Visible : Visibility.Collapsed;
        _headerText.SetResourceReference(TextBlock.ForegroundProperty,
            focused ? "TextFillColorPrimaryBrush" : HasUnread ? "TextFillColorSecondaryBrush" : "TextFillColorTertiaryBrush");
        _headerText.FontWeight = focused ? FontWeights.SemiBold : FontWeights.Normal;
        // Keep the terminal from shifting when the border gets thicker.
        var t = Frame.BorderThickness.Left;
        Frame.Padding = new Thickness(5 - t, 3 - t, 3 - t, 3 - t);
    }

    private static string? ValidDirectory(string? dir) =>
        !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) ? dir : null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Pty.CloseStdinToApp(); } catch { }
        try { Pty.StopExternalTermOnly(); } catch { }
        try { if (_process is { HasExited: false }) _process.Kill(true); } catch { }
    }
}
