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
    private IProcess? _process;
    private bool _disposed;
    private bool _attached;
    private bool _focusWhenAttached;

    private string? _cwd;
    private string? _title;
    private bool _hasUnread;
    private bool _isActive;
    private TerminalTheme _theme;
    private Color _background;
    private string _fontFamily;
    private int _baseFontSize;
    private int _fontSize;

    public const int MinFontSize = 6;
    public const int MaxFontSize = 48;

    public PaneSession(string id, string workspaceId, ShellProfile profile, string? cwd, TerminalTheme theme, Color background,
        string fontFamily, int fontSize, IReadOnlyDictionary<string, string?> env)
    {
        _theme = theme;
        _background = background;
        _fontFamily = fontFamily;
        _baseFontSize = _fontSize = fontSize;
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

        _label = profile.Name; // until the controller names it (WorkspaceController.UpdateLabels)

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

    /// <summary>The slot (layout cell) this terminal is a tab of.</summary>
    public string SlotId { get; set; } = "";
    public EasyTerminalControl Control { get; }
    public TermPTY Pty { get; }
    public DateTime UnreadAt { get; private set; }

    /// <summary>A plain-text copy of the output, for find (the native control has no search).</summary>
    public OutputMirror Mirror { get; } = new();

    /// <summary>
    /// Raised when the pane stops needing the user, with what it had asked (null for a bare bell) and
    /// when it asked. The waiting list keeps these as "Earlier".
    /// </summary>
    public event Action<PaneSession, string?, DateTime>? AttentionHandled;

    /// <summary>The terminal font size in points; <see cref="ZoomPercent"/> compares it with the setting.</summary>
    public int FontSize
    {
        get => _fontSize;
        private set { if (Set(ref _fontSize, value)) OnPropertyChanged(nameof(ZoomPercent)); }
    }

    /// <summary>100 at the font size from settings; Ctrl+= / Ctrl+- change it for this pane only.</summary>
    public int ZoomPercent => (int)Math.Round(100.0 * _fontSize / _baseFontSize);

    /// <summary>Ctrl+= / Ctrl+- (and Ctrl+wheel): one point bigger or smaller. 0 goes back to the setting.</summary>
    public void Zoom(int delta) =>
        ApplyFont(delta == 0 ? _baseFontSize : Math.Clamp(_fontSize + delta, MinFontSize, MaxFontSize));

    /// <summary>
    /// New appearance from Settings: colours, font and size. A zoomed pane keeps its offset from the
    /// new size.
    /// </summary>
    public void SetAppearance(TerminalTheme theme, Color background, string fontFamily, int baseFontSize)
    {
        var offset = _fontSize - _baseFontSize;
        (_theme, _background, _fontFamily, _baseFontSize) = (theme, background, fontFamily, baseFontSize);
        Control.Theme = theme;
        Control.FontFamilyWhenSettingTheme = new FontFamily(fontFamily);
        OnPropertyChanged(nameof(ZoomPercent));
        ApplyFont(Math.Clamp(baseFontSize + offset, MinFontSize, MaxFontSize), force: true);
    }

    private void ApplyFont(int size, bool force = false)
    {
        if (size == _fontSize && !force) return;
        FontSize = size;
        Control.FontSizeWhenSettingTheme = size;
        if (!_attached) return; // the control applies it with the theme when it loads
        try { Control.Terminal.SetTheme(_theme, _fontFamily, (short)size, _background); }
        catch (Exception ex) { Log.Write($"font {Id}: {ex.Message}"); }
    }

    /// <summary>Raised on the UI thread for notifications and bells.</summary>
    public event Action<PaneSession, TerminalEvent>? TerminalEvent;

    /// <summary>Raised on the UI thread when the shell process exits or fails to start.</summary>
    public event Action<PaneSession>? Exited;

    public string? Cwd
    {
        get => _cwd;
        private set => Set(ref _cwd, value);
    }

    /// <summary>The raw terminal title. Use <see cref="ProgramTitle"/> for display.</summary>
    public string? Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    /// <summary>
    /// The title the running program set ("vim", an agent's current task), or null while the shell
    /// is only showing its own path ("C:\WINDOWS\system32\cmd.exe"). cmd appends " - &lt;command&gt;"
    /// while a command runs; that command is kept.
    /// </summary>
    public string? ProgramTitle
    {
        get
        {
            var t = _title?.Trim();
            if (string.IsNullOrEmpty(t) || t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            var dash = t.IndexOf(".exe - ", StringComparison.OrdinalIgnoreCase);
            if (dash > 0 && t[..dash].Contains('\\'))
            {
                // cmd echoes the line as typed, double spaces and all ("ping  -t 127.0.0.1").
                var cmd = string.Join(' ', t[(dash + 7)..].Split(' ', StringSplitOptions.RemoveEmptyEntries));
                return cmd.Length > 0 ? cmd : null;
            }
            return t;
        }
    }

    /// <summary>The current folder's name: "aghouse", "Home" for the user's profile folder, "C:" for a drive root.</summary>
    public string? FolderName => FolderNameOf(_cwd);

    public static string? FolderNameOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var trimmed = path.TrimEnd('\\', '/');
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
        if (string.Equals(trimmed, home, StringComparison.OrdinalIgnoreCase)) return "Home";
        return Path.GetFileName(trimmed) is { Length: > 0 } leaf ? leaf : trimmed;
    }

    /// <summary>
    /// Programs running under this pane's shell (empty when it's sitting at a prompt). Can't see
    /// inside WSL: Linux processes aren't in the Windows process table.
    /// </summary>
    public IReadOnlyList<string> RunningPrograms() =>
        _process is ProcessFactory.WrappedProcess { HasExited: false } p ? ProcessTree.RunningUnder(p.Pid) : [];

    /// <summary>The shell's process id while it runs (for finding the ports its programs listen on).</summary>
    public int? ShellPid => _process is ProcessFactory.WrappedProcess { HasExited: false } p ? p.Pid : null;

    /// <summary>A short name for what's running, for confirmations: the program's own title if it set one, else its exe.</summary>
    public string? DescribeRunning()
    {
        var running = RunningPrograms();
        if (running.Count == 0) return null;
        if (ProgramTitle is { } t) return t;
        return running.Count == 1 ? running[0] : $"{running[0]} and {running.Count - 1} more";
    }

    /// <summary>
    /// What the header (and confirmations, and screen readers) call this pane. Set by the controller,
    /// because a name that tells panes apart depends on the workspace and the siblings.
    /// </summary>
    public string Label
    {
        get => _label;
        set => Set(ref _label, value);
    }

    private string _label;
    private string? _customName;
    private string? _attention;

    /// <summary>A name the user gave this tab (double-click a tab). Wins over the derived name; null to go back to it.</summary>
    public string? CustomName
    {
        get => _customName;
        set => Set(ref _customName, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    /// <summary>What the agent said when it asked for attention (shown in the header while unread).</summary>
    public string? AttentionMessage
    {
        get => _attention;
        private set => Set(ref _attention, value);
    }

    /// <summary>Mark this pane as needing the user, optionally with the agent's message.</summary>
    public void RaiseAttention(string? message)
    {
        // A bare bell keeps a message the agent is still waiting on; a new message replaces it.
        if (!string.IsNullOrWhiteSpace(message)) AttentionMessage = message.Trim();
        else if (!HasUnread) AttentionMessage = null;
        HasUnread = true;
        UnreadAt = DateTime.UtcNow; // a repeat request on a waiting pane counts as the newest
    }

    public bool HasUnread
    {
        get => _hasUnread;
        set
        {
            var (was, message, at) = (_hasUnread, _attention, UnreadAt);
            if (value) UnreadAt = DateTime.UtcNow;
            else AttentionMessage = null;
            Set(ref _hasUnread, value);
            if (was && !value) AttentionHandled?.Invoke(this, message, at);
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
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
                // Say which shell, what Windows reported, and what to try next.
                MessageBox.Show(
                    $"aghouse couldn't start {Profile.Name}.\n\n"
                    + $"{ex.Message.Trim()}\nCommand: {Profile.CommandLine}\n\n"
                    + "Check that it's installed, or open a workspace with another shell from the menu next to the + button.",
                    "aghouse", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        Mirror.Feed(str);
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
