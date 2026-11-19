using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AgHouse.App.Terminal;

namespace AgHouse.App.Workspaces;

/// <summary>Where a dragged pane lands: on one side of the target pane, or in its place (the two swap).</summary>
public enum PaneDropSide { Left, Right, Up, Down, Swap }

/// <summary>
/// Moves a pane by dragging its header onto another pane: an edge band puts it on that side, the middle
/// swaps the two.
///
/// The terminals are native HWNDs, so WPF drag-and-drop and adorners can't work over them. Instead the
/// header keeps the mouse capture (Win32 SetCapture, which sees moves over child HWNDs too), the target
/// is found from the frames' geometry, and the preview is a Popup, a window of its own that draws above
/// the terminals. Esc goes to the terminal's HWND, not to WPF, so it's read from the message pump.
/// </summary>
public sealed class PaneDrag
{
    /// <summary>The share of a pane's width or height, from each edge, that drops on that side.</summary>
    private const double EdgeBand = 0.3;

    private const int WM_KEYDOWN = 0x0100;
    private const int VK_ESCAPE = 0x1B;

    private readonly Func<Workspace, string, string, PaneDropSide, bool> _commit;
    private Workspace? _ws;
    private PaneSlot? _source;
    private (PaneSlot Slot, PaneDropSide Side)? _target;
    private Popup? _preview;
    private Border? _previewFrame;
    private TextBlock? _previewText;

    /// <param name="commit">Moves (workspace, source slot id, target slot id, side). False when nothing changed.</param>
    public PaneDrag(Func<Workspace, string, string, PaneDropSide, bool> commit) => _commit = commit;

    public bool IsDragging => _source is not null;

    /// <summary>Starts dragging <paramref name="source"/>; its header already holds the mouse capture.</summary>
    public void Start(Workspace ws, PaneSlot source)
    {
        End();
        var header = source.Header;
        if (!header.IsMouseCaptured && !header.CaptureMouse()) return;
        (_ws, _source) = (ws, source);
        header.MouseMove += OnMove;
        header.MouseLeftButtonUp += OnUp;
        header.LostMouseCapture += OnLostCapture;
        ComponentDispatcher.ThreadPreprocessMessage += OnMessage;
        Mouse.OverrideCursor = Cursors.SizeAll;
        Log.Write($"pane drag {source.Id}");
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_ws is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { End(); return; }
        _target = HitTest(e.GetPosition(_ws.View));
        ShowPreview();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        var (ws, source, target) = (_ws, _source, _target);
        End();
        e.Handled = true;
        if (ws is null || source is null || target is not { } t) return;
        _commit(ws, source.Id, t.Slot.Id, t.Side);
    }

    private void OnLostCapture(object sender, MouseEventArgs e) => End();

    private void OnMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message != WM_KEYDOWN || (int)msg.wParam != VK_ESCAPE || !IsDragging) return;
        handled = true; // the Esc was for the drag, not the program in the pane
        End();
    }

    /// <summary>The pane under <paramref name="p"/> (in the workspace view's coordinates), and which zone of it.</summary>
    private (PaneSlot, PaneDropSide)? HitTest(Point p)
    {
        if (_ws is null) return null;
        foreach (var slot in _ws.Slots.Values)
        {
            if (ReferenceEquals(slot, _source) || Bounds(slot) is not { } r || !r.Contains(p)) continue;
            var x = (p.X - r.X) / r.Width;
            var y = (p.Y - r.Y) / r.Height;
            var (edge, side) = new[]
            {
                (x, PaneDropSide.Left), (1 - x, PaneDropSide.Right), (y, PaneDropSide.Up), (1 - y, PaneDropSide.Down),
            }.MinBy(z => z.Item1);
            return (slot, edge < EdgeBand ? side : PaneDropSide.Swap);
        }
        return null;
    }

    /// <summary>A slot's frame in the workspace view's coordinates, or null when it isn't on screen.</summary>
    private Rect? Bounds(PaneSlot slot)
    {
        var frame = slot.Frame;
        if (_ws is null || !frame.IsVisible || frame.ActualWidth <= 0 || !frame.IsDescendantOf(_ws.View)) return null;
        return frame.TransformToAncestor(_ws.View).TransformBounds(new Rect(0, 0, frame.ActualWidth, frame.ActualHeight));
    }

    private void ShowPreview()
    {
        if (_ws is null) return;
        if (_target is not { } target || Bounds(target.Slot) is not { } r)
        {
            if (_preview is not null) _preview.IsOpen = false;
            return;
        }
        var (slot, side) = target;
        var area = side switch
        {
            PaneDropSide.Left => r with { Width = r.Width / 2 },
            PaneDropSide.Right => r with { X = r.X + r.Width / 2, Width = r.Width / 2 },
            PaneDropSide.Up => r with { Height = r.Height / 2 },
            PaneDropSide.Down => r with { Y = r.Y + r.Height / 2, Height = r.Height / 2 },
            _ => r,
        };
        area.Inflate(-3, -3);
        if (area.Width <= 0 || area.Height <= 0) return;

        EnsurePreview();
        _previewText!.Text = side == PaneDropSide.Swap ? $"Swap with {slot.ActiveTab?.Label ?? "this pane"}" : "";
        _previewText.Visibility = side == PaneDropSide.Swap ? Visibility.Visible : Visibility.Collapsed;
        _previewFrame!.Width = area.Width;
        _previewFrame.Height = area.Height;
        _preview!.PlacementTarget = _ws.View;
        _preview.HorizontalOffset = area.X;
        _preview.VerticalOffset = area.Y;
        _preview.IsOpen = true;
    }

    /// <summary>
    /// A neutral wash and outline, like the sidebar's insertion line. Never the accent: that means
    /// selection and "Needs you".
    /// </summary>
    private void EnsurePreview()
    {
        if (_preview is not null) return;
        var wash = new Border { CornerRadius = new CornerRadius(4), Opacity = 0.12 };
        wash.SetResourceReference(Border.BackgroundProperty, "TextFillColorPrimaryBrush");
        _previewText = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8),
        };
        _previewText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        _previewText.SetResourceReference(TextBlock.FontSizeProperty, "TypeMeta");
        var label = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = _previewText };
        label.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        _previewFrame = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(2),
            Child = new Grid { Children = { wash, label } },
        };
        _previewFrame.SetResourceReference(Border.BorderBrushProperty, "TextFillColorPrimaryBrush");
        // Bound to the label's text visibility, so the side drops show only the wash.
        label.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding(nameof(Visibility)) { Source = _previewText });
        _preview = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            IsHitTestVisible = false,
            Focusable = false,
            Child = _previewFrame,
        };
    }

    /// <summary>Ends the drag, dropped or not. Safe to call twice.</summary>
    public void End()
    {
        var source = _source;
        _source = null;
        _ws = null;
        _target = null;
        if (_preview is not null) _preview.IsOpen = false;
        if (source is null) return;
        ComponentDispatcher.ThreadPreprocessMessage -= OnMessage;
        Mouse.OverrideCursor = null;
        var header = source.Header;
        header.MouseMove -= OnMove;
        header.MouseLeftButtonUp -= OnUp;
        header.LostMouseCapture -= OnLostCapture;
        if (header.IsMouseCaptured) header.ReleaseMouseCapture();
    }
}
