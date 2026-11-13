using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AgHouse.App.Interop;
using AgHouse.Core.Layout;

namespace AgHouse.App.Input;

public enum AppAction
{
    NewWorkspace,
    NewTab,
    NextTab,
    PreviousTab,
    ClosePane,
    SplitRight,
    SplitDown,
    Focus,
    SelectWorkspace,
    NextWorkspace,
    PreviousWorkspace,
    JumpToUnread,
    RenameWorkspace,
    Copy,
    Paste,
    CloseWorkspace,
    ZoomPane,
    Find,
    FontBigger,
    FontSmaller,
    FontReset,
    CommandPalette,
    WaitingList,
    Settings,
}

public readonly record struct Shortcut(AppAction Action, int Index = 0, Direction Direction = Direction.Left);

/// <summary>
/// App-wide shortcuts. Keystrokes go straight to the terminal's native HWND, bypassing WPF
/// input bindings, so we look at every message the UI thread pumps before it is dispatched.
/// </summary>
public sealed class ShortcutRouter : IDisposable
{
    public ShortcutRouter()
    {
        ComponentDispatcher.ThreadPreprocessMessage += OnMessage;
    }

    /// <summary>Return true to swallow the key.</summary>
    public Func<Shortcut, bool>? Handler { get; set; }

    /// <summary>A mouse button went down on some window of this thread (used to track the active pane).</summary>
    public event Action<IntPtr, int>? MouseDown;

    /// <summary>Right click on a window: return true to swallow (terminal copy/paste).</summary>
    public Func<IntPtr, bool>? RightClickHandler { get; set; }

    /// <summary>Ctrl+wheel on a window (+1 up, -1 down): return true when it was a terminal and got zoomed.</summary>
    public Func<IntPtr, int, bool>? CtrlWheelHandler { get; set; }

    /// <summary>True if the window handle belongs to a terminal pane.</summary>
    public Func<IntPtr, bool>? IsTerminalWindow { get; set; }

    private Window? _window;

    /// <summary>
    /// WPF treats Tab as focus navigation: it raises KeyDown on the HwndHost, KeyboardNavigation
    /// moves focus and marks the message handled, so the terminal never sees it (no shell
    /// completion). Catch Tab while it tunnels and deliver it to the terminal ourselves.
    /// </summary>
    public void AttachTo(Window window)
    {
        _window = window;
        window.PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab) return;
        var msg = ComponentDispatcher.CurrentKeyboardMessage;
        if (msg.message != Win32.WM_KEYDOWN || IsTerminalWindow?.Invoke(msg.hwnd) != true) return;

        // Handling it here means OnMessage below won't see it, so check Ctrl+Tab and
        // Ctrl+Shift+Tab first. Otherwise hand the key to the terminal the way the message
        // loop would have (TranslateMessage also queues the matching WM_CHAR).
        e.Handled = true;
        if (Map(Win32.VK_TAB) is { } shortcut && Handler?.Invoke(shortcut) == true) return;
        Win32.TranslateMessage(ref msg);
        Win32.DispatchMessage(ref msg);
    }

    private void OnMessage(ref MSG msg, ref bool handled)
    {
        if (handled) return;
        switch (msg.message)
        {
            case Win32.WM_LBUTTONDOWN or Win32.WM_MBUTTONDOWN:
                MouseDown?.Invoke(msg.hwnd, msg.message);
                break;

            case Win32.WM_RBUTTONDOWN:
                MouseDown?.Invoke(msg.hwnd, msg.message);
                if (RightClickHandler?.Invoke(msg.hwnd) == true) handled = true;
                break;

            case Win32.WM_RBUTTONUP:
                // Swallow the matching up so the terminal doesn't act on half a click.
                if (RightClickHandler is not null && _swallowNextRightUp) { handled = true; _swallowNextRightUp = false; }
                break;

            case Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN:
                if (Map((int)msg.wParam) is { } shortcut && Handler?.Invoke(shortcut) == true) handled = true;
                break;

            case Win32.WM_MOUSEWHEEL when Win32.IsDown(Win32.VK_CONTROL) && !Win32.IsDown(Win32.VK_SHIFT) && !Win32.IsDown(Win32.VK_MENU):
                // Ctrl+wheel over a terminal: that pane's text size, as in Windows Terminal and browsers.
                var delta = (short)(((long)msg.wParam >> 16) & 0xFFFF);
                if (delta != 0 && CtrlWheelHandler?.Invoke(msg.hwnd, delta > 0 ? 1 : -1) == true) handled = true;
                break;
        }
        if (handled && msg.message == Win32.WM_RBUTTONDOWN) _swallowNextRightUp = true;
    }

    private bool _swallowNextRightUp;

    /// <summary>Ctrl+D splits right, as cmux's ⌘D does. Off gives Ctrl+D (EOF, exit) back to the terminal.</summary>
    public bool CtrlDSplits { get; set; } = true;

    private Shortcut? Map(int vk)
    {
        var ctrl = Win32.IsDown(Win32.VK_CONTROL);
        var shift = Win32.IsDown(Win32.VK_SHIFT);
        var alt = Win32.IsDown(Win32.VK_MENU);

        if (ctrl && shift && !alt)
        {
            return vk switch
            {
                'N' => new Shortcut(AppAction.NewWorkspace),
                'T' => new Shortcut(AppAction.NewTab), // as in Windows Terminal
                Win32.VK_OEM_6 => new Shortcut(AppAction.NextTab),      // Ctrl+Shift+] (cmux)
                Win32.VK_OEM_4 => new Shortcut(AppAction.PreviousTab),  // Ctrl+Shift+[
                'W' => new Shortcut(AppAction.ClosePane),
                // cmux pairing: Ctrl+D right, Ctrl+Shift+D down. Ctrl+Shift+E stays as a down alias.
                'D' or 'E' => new Shortcut(AppAction.SplitDown),
                'U' => new Shortcut(AppAction.JumpToUnread),
                'R' => new Shortcut(AppAction.RenameWorkspace),
                'C' => new Shortcut(AppAction.Copy),
                'V' => new Shortcut(AppAction.Paste),
                Win32.VK_TAB => new Shortcut(AppAction.PreviousWorkspace),
                // Windows Terminal's palette and find keys; Enter zooms the pane (togglePaneZoom).
                'P' => new Shortcut(AppAction.CommandPalette),
                'F' => new Shortcut(AppAction.Find),
                Win32.VK_RETURN => new Shortcut(AppAction.ZoomPane),
                'I' => new Shortcut(AppAction.WaitingList),
                _ => null,
            };
        }

        // Ctrl+Alt+W: close the whole workspace. Ctrl+Shift+W already closes a tab or pane.
        if (ctrl && alt && !shift && vk == 'W') return new Shortcut(AppAction.CloseWorkspace);

        if (alt && shift && !ctrl)
        {
            return vk switch
            {
                Win32.VK_OEM_PLUS => new Shortcut(AppAction.SplitRight),
                Win32.VK_OEM_MINUS => new Shortcut(AppAction.SplitDown),
                _ => null,
            };
        }

        if (alt && !shift && !ctrl)
        {
            return vk switch
            {
                Win32.VK_LEFT => new Shortcut(AppAction.Focus, Direction: Direction.Left),
                Win32.VK_RIGHT => new Shortcut(AppAction.Focus, Direction: Direction.Right),
                Win32.VK_UP => new Shortcut(AppAction.Focus, Direction: Direction.Up),
                Win32.VK_DOWN => new Shortcut(AppAction.Focus, Direction: Direction.Down),
                _ => null,
            };
        }

        if (ctrl && !shift && !alt)
        {
            if (vk is >= '1' and <= '9') return new Shortcut(AppAction.SelectWorkspace, vk - '1');
            if (vk == Win32.VK_TAB) return new Shortcut(AppAction.NextWorkspace);
            if (vk == 'D' && CtrlDSplits) return new Shortcut(AppAction.SplitRight);
            if (vk == Win32.VK_NEXT) return new Shortcut(AppAction.NextTab);      // Ctrl+PageDown
            if (vk == Win32.VK_PRIOR) return new Shortcut(AppAction.PreviousTab); // Ctrl+PageUp
            // Text size, as in Windows Terminal: Ctrl+= / Ctrl+- / Ctrl+0 (and the number pad's + / -).
            if (vk is Win32.VK_OEM_PLUS or Win32.VK_ADD) return new Shortcut(AppAction.FontBigger);
            if (vk is Win32.VK_OEM_MINUS or Win32.VK_SUBTRACT) return new Shortcut(AppAction.FontSmaller);
            if (vk is '0' or Win32.VK_NUMPAD0) return new Shortcut(AppAction.FontReset);
            if (vk == Win32.VK_OEM_COMMA) return new Shortcut(AppAction.Settings);
        }

        return null;
    }

    public void Dispose()
    {
        ComponentDispatcher.ThreadPreprocessMessage -= OnMessage;
        if (_window is not null) _window.PreviewKeyDown -= OnPreviewKeyDown;
    }
}
