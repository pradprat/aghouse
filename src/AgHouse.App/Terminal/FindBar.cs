using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AgHouse.Core.Terminal;

namespace AgHouse.App.Terminal;

/// <summary>
/// Find in a pane's output (Ctrl+Shift+F). It takes over the pane's header row, because nothing can
/// be drawn over the terminal. The terminal control has no search, so this searches the pane's
/// <see cref="OutputMirror"/>. Matching lines are listed in a dropdown under the header (a popup
/// is its own window, like a menu) instead of being highlighted in the terminal. Enter copies the
/// selected line.
/// </summary>
internal sealed class FindBar : DockPanel
{
    private const int MaxResults = 200;

    private readonly PaneSlot _slot;
    private readonly TextBox _box;
    private readonly TextBlock _count;
    private readonly Popup _popup;
    private readonly ListBox _list;
    private readonly DispatcherTimer _debounce;

    public FindBar(PaneSlot slot)
    {
        _slot = slot;
        Margin = new Thickness(2, 0, 0, 0);

        var close = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = (FontFamily)Application.Current.Resources["IconFont"], FontSize = 10 },
            Padding = new Thickness(7, 3, 7, 3),
            MinWidth = 0,
            MinHeight = 0,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Close find (Esc)",
        };
        AutomationProperties.SetName(close, "Close find");
        close.Click += (_, _) => Close();
        SetDock(close, Dock.Right);
        Children.Add(close);

        _count = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
        _count.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        SetDock(_count, Dock.Right);
        Children.Add(_count);

        _box = new TextBox
        {
            FontSize = 12,
            Padding = new Thickness(6, 1, 6, 1),
            MinHeight = 0,
            Height = 22,
            MaxWidth = 320,
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 180,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(_box, "Find in this pane's output");
        _box.TextChanged += (_, _) => { _debounce!.Stop(); _debounce.Start(); };
        _box.PreviewKeyDown += OnKey;
        Children.Add(_box);

        _list = new ListBox
        {
            MaxHeight = 280,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Focusable = false,
            ItemTemplate = ResultTemplate(),
            ItemContainerStyle = (Style)Application.Current.Resources["QuietListItem"],
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_list, "Matching lines");
        _list.PreviewMouseLeftButtonUp += (_, _) => CopySelected();

        var surface = new Border
        {
            Child = _list,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
        };
        surface.SetResourceReference(Border.BackgroundProperty, "FlyoutBackground");
        surface.SetResourceReference(Border.BorderBrushProperty, "SurfaceStrokeColorFlyoutBrush");

        _popup = new Popup
        {
            Child = surface,
            Placement = PlacementMode.Bottom,
            AllowsTransparency = true,
            StaysOpen = true,
            PopupAnimation = PopupAnimation.None,
            VerticalOffset = 2,
        };

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Search(); };
        _box.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            // Clicking a result moves focus into the popup's list for a moment; anywhere else closes it.
            if (!_box.IsKeyboardFocusWithin && !_list.IsKeyboardFocusWithin && !_list.IsMouseOver) _popup.IsOpen = false;
        }, DispatcherPriority.Input);
    }

    public void Open()
    {
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() =>
        {
            Keyboard.Focus(_box);
            _box.SelectAll();
            Search();
        }, DispatcherPriority.Input);
    }

    public void Close()
    {
        _debounce.Stop();
        _popup.IsOpen = false;
        Visibility = Visibility.Collapsed;
        _slot.FindClosed();
    }

    private void Search()
    {
        var query = _box.Text;
        var pane = _slot.ActiveTab;
        if (pane is null || string.IsNullOrWhiteSpace(query))
        {
            _count.Text = "";
            _list.ItemsSource = null;
            _popup.IsOpen = false;
            return;
        }
        // Counts distinct lines: ConPTY repaints can print the same screen twice, so raw hits overstate.
        var matches = pane.Mirror.Find(query, MaxResults);
        _count.Text = matches.Count switch
        {
            0 => "No matches",
            1 => "1 line",
            >= MaxResults => $"{MaxResults}+ lines",
            var n => $"{n} lines",
        };
        _list.ItemsSource = matches;
        if (matches.Count > 0)
        {
            _list.SelectedIndex = 0;
            _list.ScrollIntoView(matches[0]);
            _popup.PlacementTarget = _slot.Header;
            _popup.Width = Math.Max(240, _slot.Header.ActualWidth);
            _popup.IsOpen = true;
        }
        else _popup.IsOpen = false;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Enter:
                CopySelected();
                e.Handled = true;
                break;
            case Key.Down or Key.Up when _list.Items.Count > 0:
                var i = Math.Clamp(_list.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, _list.Items.Count - 1);
                _list.SelectedIndex = i;
                _list.ScrollIntoView(_list.SelectedItem);
                e.Handled = true;
                break;
        }
    }

    private void CopySelected()
    {
        if (_list.SelectedItem is not FindMatch match) return;
        try { Clipboard.SetText(match.Line); _count.Text = "Copied"; }
        catch { _count.Text = "Couldn't copy"; }
        Keyboard.Focus(_box);
    }

    /// <summary>The line in the terminal font (it is terminal output), then "×3" when it appeared more than once.</summary>
    private static DataTemplate ResultTemplate()
    {
        var line = new FrameworkElementFactory(typeof(TextBlock));
        line.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(FindMatch.Line)));
        line.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Cascadia Mono, Consolas"));
        line.SetValue(TextBlock.FontSizeProperty, 12.0);
        line.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        line.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

        var count = new FrameworkElementFactory(typeof(TextBlock));
        count.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(FindMatch.Count)) { StringFormat = "×{0}" });
        count.SetValue(TextBlock.FontSizeProperty, 11.0);
        count.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 0, 0));
        count.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        count.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        count.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(FindMatch.Count)) { Converter = new MoreThanOneConverter() });
        count.SetValue(DockPanel.DockProperty, Dock.Right);

        var row = new FrameworkElementFactory(typeof(DockPanel));
        row.AppendChild(count);
        row.AppendChild(line);
        row.SetValue(ToolTipProperty, new System.Windows.Data.Binding(nameof(FindMatch.Line)));
        return new DataTemplate { VisualTree = row };
    }

    private sealed class MoreThanOneConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c) =>
            value is int n && n > 1 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
    }
}
