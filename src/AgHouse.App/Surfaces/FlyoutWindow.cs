using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace AgHouse.App.Surfaces;

/// <summary>
/// A transient window owned by the main window: the command palette and the waiting list. It is
/// its own HWND, like a menu, so it can sit above the terminals (WPF can't draw over them). It uses
/// Fluent's flyout surface and Windows' own rounded corners and shadow, closes on Esc or when it
/// loses focus, and hands focus back to the terminal it came from.
/// </summary>
public class FlyoutWindow : Window
{
    private bool _closing;

    public FlyoutWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.Height;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "FlyoutBackground");
        SourceInitialized += (_, _) => RoundCorners();
        Deactivated += (_, _) => CloseOnce();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { CloseOnce(); e.Handled = true; }
        };
        Closing += (_, _) => _closing = true;
    }

    /// <summary>Runs after the flyout has closed and focus is back on the main window.</summary>
    public Action? AfterClose { get; set; }

    protected void CloseOnce()
    {
        if (_closing) return;
        _closing = true;
        var owner = Owner;
        var after = AfterClose;
        Close();
        owner?.Activate();
        // Let the main window take activation before the action (or the terminal) takes focus.
        owner?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => after?.Invoke());
    }

    /// <summary>Closes the flyout and then runs <paramref name="action"/> on the main window.</summary>
    protected void CloseThen(Action action)
    {
        AfterClose = action;
        CloseOnce();
    }

    private void RoundCorners()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var preference = 2; // DWMWCP_ROUND, as Windows 11 draws its own flyouts
        _ = DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref preference, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
