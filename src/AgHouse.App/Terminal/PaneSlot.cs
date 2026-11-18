using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AgHouse.App.Terminal;

/// <summary>
/// One cell of a workspace's split layout: the frame, a 26px header, and one or more terminals (tabs),
/// of which only the active one is visible. The header sits above the terminals, never on top of them
/// (each terminal is a native HWND).
///
/// The header always shows. With one tab it is the pane header: name, "Needs you · message", and a ✕
/// while the workspace is split (a lone pane's ✕ would close the whole workspace). With two or more
/// tabs it becomes the tab strip, so tabs add no new band of chrome.
/// </summary>
public sealed class PaneSlot
{
    private readonly Grid _header;
    private readonly Border _headerTint;
    private readonly AttentionFirstRow _singleRow;
    private readonly TextBlock _headerText;
    private readonly TextBlock _attentionText;
    private readonly Run _attentionMessage;
    private readonly Button _closeButton;
    private readonly TabStripPanel _tabStrip;
    private readonly Button _newTabButton;
    private readonly TextBlock _fontZoomText;
    private readonly TextBlock _zoomNote;
    private readonly FrameworkElement _grip;
    private readonly FindBar _findBar;
    private readonly Grid _body;
    private readonly List<PaneSession> _tabs = [];
    private readonly Dictionary<PaneSession, TabView> _tabViews = [];
    private bool _isSplit;
    private bool _isFocused;
    private int _hiddenPanes;
    private string? _hiddenWaiting;

    public PaneSlot(string id, Color background)
    {
        Id = id;
        Background = background;

        _headerText = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0),
        };

        // "Needs you · <message>": attention said in words, so the accent ring can't be read as focus.
        var needsYou = new Run("Needs you") { FontWeight = FontWeights.SemiBold };
        needsYou.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
        _attentionMessage = new Run();
        _attentionMessage.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorSecondaryBrush");
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
        _headerTint = Tint();

        _closeButton = GhostButton("", 10, "Close pane (Ctrl+Shift+W)", "Close pane");
        _closeButton.Click += (_, _) => { if (ActiveTab is { } t) CloseRequested?.Invoke(t); };

        _singleRow = new AttentionFirstRow { Margin = new Thickness(2, 0, 0, 0), Children = { _headerText, _attentionText } };
        // A lone tab has no tab strip, so its menu lives on the title row.
        var duplicate = new MenuItem { Header = "Duplicate tab" };
        duplicate.Click += (_, _) => { if (ActiveTab is { } t) DuplicateRequested?.Invoke(t); };
        _singleRow.Background = Brushes.Transparent; // the whole row answers a right-click, not just the text
        _singleRow.ContextMenu = new ContextMenu { Items = { duplicate } };

        _newTabButton = GhostButton("", 10, "New tab (Ctrl+Shift+T)", "New tab");
        _newTabButton.Margin = new Thickness(2, 0, 0, 0);
        _newTabButton.Click += (_, _) => NewTabRequested?.Invoke(this);
        _tabStrip = new TabStripPanel { Visibility = Visibility.Collapsed, Children = { _newTabButton } };

        // "110%" while this pane's font is zoomed (Ctrl+= / Ctrl+-). Neutral: it's a fact, not a signal.
        _fontZoomText = new TextBlock
        {
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0),
            Visibility = Visibility.Collapsed,
            ToolTip = "Text size for this pane. Ctrl+0 resets it.",
        };
        _fontZoomText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

        // Pane zoom (Ctrl+Shift+Enter): says how many panes are hidden, and says so in words when one
        // of them needs you, so zooming in can never hide a request.
        _zoomNote = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0, 4, 0),
            Visibility = Visibility.Collapsed,
            Cursor = Cursors.Hand,
            ToolTip = "Show all panes (Ctrl+Shift+Enter)",
        };
        _zoomNote.MouseLeftButtonUp += (_, e) => { UnzoomRequested?.Invoke(this); e.Handled = true; };
        _zoomNote.MouseLeftButtonDown += (_, e) => e.Handled = true; // not the header's "focus the pane"

        _findBar = new FindBar(this) { Visibility = Visibility.Collapsed };

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(_fontZoomText);
        right.Children.Add(_zoomNote);
        right.Children.Add(_closeButton);
        _grip = Grip();
        var row = new PaneHeaderRow();
        row.Children.Add(new Grid { Children = { _singleRow, _tabStrip, _findBar } });
        row.Children.Add(_grip);
        row.Children.Add(right);

        _header = new Grid { Height = 26, Background = Brushes.Transparent };
        _header.Children.Add(_headerTint);
        _header.Children.Add(row);
        _header.MouseLeftButtonDown += (_, e) =>
        {
            FocusRequested?.Invoke(this);
            if (!AllowDrag) return;
            // Captured, so a quick flick that leaves the header (onto a terminal HWND) is still seen.
            _dragStart = e.GetPosition(_header);
            _header.CaptureMouse();
        };
        _header.MouseMove += (_, e) =>
        {
            if (_dragStart is not { } start) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndDragWatch(); return; }
            var delta = e.GetPosition(_header) - start;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance * 2
                && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
            _dragStart = null; // the capture stays: the drag takes it over
            HeaderDragStarted?.Invoke(this);
        };
        _header.MouseLeftButtonUp += (_, _) => EndDragWatch();

        _body = new Grid();
        var content = new DockPanel();
        DockPanel.SetDock(_header, Dock.Top);
        content.Children.Add(_header);
        content.Children.Add(_body);

        Frame = new Border
        {
            Child = content,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(background),
            CornerRadius = new CornerRadius(4),
            SnapsToDevicePixels = true,
        };
        Refresh();
    }

    public string Id { get; }
    public Border Frame { get; }

    /// <summary>The terminal palette's background, which the frame matches.</summary>
    public Color Background
    {
        get;
        set { field = value; if (Frame is not null) Frame.Background = new SolidColorBrush(value); }
    }

    /// <summary>The header row, for anchoring the find results under it and for dragging the pane.</summary>
    internal FrameworkElement Header => _header;

    private Point? _dragStart;

    /// <summary>The header can be dragged to move the pane: the workspace is split and not zoomed.</summary>
    public bool AllowDrag
    {
        get => _allowDrag;
        set { if (_allowDrag == value) return; _allowDrag = value; Refresh(); }
    }
    private bool _allowDrag;

    /// <summary>The header was dragged past the threshold. The header holds the mouse capture.</summary>
    public event Action<PaneSlot>? HeaderDragStarted;

    private void EndDragWatch()
    {
        if (_dragStart is null) return;
        _dragStart = null;
        if (_header.IsMouseCaptured) _header.ReleaseMouseCapture();
    }

    /// <summary>The "Zoomed" note was clicked.</summary>
    public event Action<PaneSlot>? UnzoomRequested;

    /// <summary>
    /// This slot fills the workspace (pane zoom) and <paramref name="hidden"/> panes are out of sight.
    /// <paramref name="waitingLabel"/> names a hidden pane that needs you, if any. 0 hidden = not zoomed.
    /// </summary>
    public void SetZoom(int hidden, string? waitingLabel)
    {
        if (_hiddenPanes == hidden && _hiddenWaiting == waitingLabel) return;
        (_hiddenPanes, _hiddenWaiting) = (hidden, waitingLabel);
        _zoomNote.Inlines.Clear();
        if (waitingLabel is not null)
        {
            var needsYou = new Run("Needs you") { FontWeight = FontWeights.SemiBold };
            needsYou.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
            var where = new Run($" in {waitingLabel} · Ctrl+Shift+U");
            where.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorSecondaryBrush");
            _zoomNote.Inlines.Add(needsYou);
            _zoomNote.Inlines.Add(where);
        }
        else if (hidden > 0)
        {
            var text = new Run(hidden == 1 ? "Zoomed · 1 hidden pane" : $"Zoomed · {hidden} hidden panes");
            text.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorSecondaryBrush");
            _zoomNote.Inlines.Add(text);
        }
        AutomationProperties.SetName(_zoomNote, waitingLabel is not null ? $"Needs you in {waitingLabel}" : $"Zoomed, {hidden} hidden");
        Refresh();
    }

    /// <summary>Ctrl+Shift+F: the find bar takes the header row; Esc gives it back.</summary>
    public void OpenFind() { _findBar.Open(); Refresh(); }

    public bool IsFinding => _findBar.Visibility == Visibility.Visible;

    internal void FindClosed()
    {
        Refresh();
        ActiveTab?.FocusTerminal();
    }

    public IReadOnlyList<PaneSession> Tabs => _tabs;
    public PaneSession? ActiveTab { get; private set; }

    /// <summary>The header (outside any tab) was clicked.</summary>
    public event Action<PaneSlot>? FocusRequested;
    /// <summary>A tab was clicked.</summary>
    public event Action<PaneSession>? TabActivated;
    /// <summary>✕ on the pane or a tab, middle-click on a tab, or "Close tab" in its menu.</summary>
    public event Action<PaneSession>? CloseRequested;
    public event Action<PaneSession>? CloseOthersRequested;
    public event Action<PaneSlot>? NewTabRequested;
    /// <summary>"Duplicate tab": a new tab right after this one, in its folder, running its shell.</summary>
    public event Action<PaneSession>? DuplicateRequested;
    /// <summary>The user renamed a tab (its <see cref="PaneSession.CustomName"/> changed).</summary>
    public event Action<PaneSession>? Renamed;

    /// <summary>The workspace has other slots: the header always shows (name and ✕) and the frame can show focus.</summary>
    public bool IsSplit
    {
        get => _isSplit;
        set { _isSplit = value; Refresh(); }
    }

    /// <summary>The workspace's active terminal is one of this slot's tabs.</summary>
    public bool IsFocused
    {
        get => _isFocused;
        set { _isFocused = value; Refresh(); }
    }

    public void Add(PaneSession pane, int? index = null)
    {
        pane.SlotId = Id;
        _tabs.Insert(Math.Clamp(index ?? _tabs.Count, 0, _tabs.Count), pane);
        // Hidden, not Collapsed: a background tab keeps its size, so switching tabs doesn't reflow it.
        pane.Control.Visibility = Visibility.Hidden;
        _body.Children.Add(pane.Control);
        pane.PropertyChanged += OnTabChanged;

        var view = new TabView(this, pane);
        _tabViews[pane] = view;
        _tabStrip.Children.Insert(_tabs.IndexOf(pane), view.Root);

        if (ActiveTab is null) Show(pane); else Refresh();
    }

    /// <summary>Takes a tab out. Returns the tab shown in its place, or null when the slot is now empty.</summary>
    public PaneSession? Remove(PaneSession pane)
    {
        var index = _tabs.IndexOf(pane);
        if (index < 0) return ActiveTab;
        _tabs.RemoveAt(index);
        pane.PropertyChanged -= OnTabChanged;
        _body.Children.Remove(pane.Control);
        if (_tabViews.Remove(pane, out var view)) _tabStrip.Children.Remove(view.Root);

        if (ReferenceEquals(ActiveTab, pane))
        {
            ActiveTab = null;
            // The tab to the right takes its place, like a browser; the last tab falls back to the left.
            if (_tabs.Count > 0) Show(_tabs[Math.Min(index, _tabs.Count - 1)]);
        }
        Refresh();
        return ActiveTab;
    }

    public void Show(PaneSession pane)
    {
        if (!_tabs.Contains(pane)) return;
        ActiveTab = pane;
        foreach (var tab in _tabs) tab.Control.Visibility = ReferenceEquals(tab, pane) ? Visibility.Visible : Visibility.Hidden;
        Refresh();
    }

    /// <summary>The tab <paramref name="delta"/> places from the active one, wrapping around.</summary>
    public PaneSession? Relative(int delta)
    {
        if (ActiveTab is null || _tabs.Count == 0) return null;
        var i = _tabs.IndexOf(ActiveTab);
        return _tabs[((i + delta) % _tabs.Count + _tabs.Count) % _tabs.Count];
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PaneSession.HasUnread) or nameof(PaneSession.AttentionMessage)
            or nameof(PaneSession.Label) or nameof(PaneSession.Cwd) or nameof(PaneSession.ZoomPercent))
        {
            Refresh();
        }
    }

    /// <summary>
    /// Three frame states, kept apart by shape and words, not colour alone:
    /// needs you = 2px accent ring + "Needs you" in the header; focused (split only) = 1px primary-text border
    /// + primary, semibold header text; at rest = 1px control stroke + secondary header text.
    /// A background tab that needs you rings the frame too, and its tab says so.
    /// </summary>
    private void Refresh()
    {
        var multi = _tabs.Count > 1;
        var waiting = _tabs.Any(t => t.HasUnread);
        var focused = _isFocused && _isSplit;
        var single = _tabs.Count == 1 ? _tabs[0] : null;

        // Every pane says what it's running, split or not. A lone pane gets no ✕ (it would close the
        // whole workspace; Ctrl+Shift+W still does) and no focus state (there's nothing to tell it from).
        // Find takes the whole row while it's open; the zoom note stays, so a hidden request is never lost.
        var finding = IsFinding;
        var zoomed = _hiddenPanes > 0;
        _singleRow.Visibility = multi || finding ? Visibility.Collapsed : Visibility.Visible;
        _tabStrip.Visibility = multi && !finding ? Visibility.Visible : Visibility.Collapsed;
        _closeButton.Visibility = _isSplit && !multi && !finding ? Visibility.Visible : Visibility.Collapsed;
        _zoomNote.Visibility = zoomed ? Visibility.Visible : Visibility.Collapsed;
        _grip.Visibility = _allowDrag && !finding ? Visibility.Visible : Visibility.Collapsed;
        var zoom = ActiveTab?.ZoomPercent ?? 100;
        _fontZoomText.Text = $"{zoom}%";
        _fontZoomText.Visibility = zoom != 100 && !finding ? Visibility.Visible : Visibility.Collapsed;

        if (single is not null)
        {
            _headerText.Text = single.Label;
            _headerText.ToolTip = single.Cwd;
            _attentionMessage.Text = single.AttentionMessage is { } m ? " · " + m : "";
            _attentionText.ToolTip = single.AttentionMessage;
        }
        _headerTint.Visibility = !multi && waiting ? Visibility.Visible : Visibility.Collapsed;
        _attentionText.Visibility = !multi && waiting ? Visibility.Visible : Visibility.Collapsed;
        // At rest: Secondary, not Tertiary, which falls under 4.5:1 on the light terminal background.
        SetBrush(_headerText, TextBlock.ForegroundProperty, ref _headerBrushKey, focused ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        _headerText.FontWeight = focused ? FontWeights.SemiBold : FontWeights.Normal;

        var tabsChanged = false;
        foreach (var view in _tabViews.Values) tabsChanged |= view.Update(ReferenceEquals(view.Pane, ActiveTab));
        // A tab whose name grew is re-measured at its old (trimmed) width, so the strip wouldn't hear
        // that it wants more room. Lay the whole strip out again from natural widths, when a tab changed.
        if (tabsChanged || _tabStrip.Children.Count != _laidOutTabs)
        {
            _laidOutTabs = _tabStrip.Children.Count;
            _tabStrip.InvalidateMeasure();
        }

        // Focus is the strong neutral stroke, not Primary Text: the 2px accent ring of a pane that needs you
        // must be the loudest edge in the window, and a white (or black) frame outshouted it.
        var (frameKey, thickness) = waiting ? ("AccentFillColorDefaultBrush", 2)
            : focused ? ("ControlStrongStrokeColorDefaultBrush", 1)
            : ("ControlStrokeColorDefaultBrush", 1);
        SetBrush(Frame, Border.BorderBrushProperty, ref _frameBrushKey, frameKey);
        Frame.BorderThickness = new Thickness(thickness);
        // Keep the terminal from shifting when the border gets thicker.
        var th = Frame.BorderThickness.Left;
        Frame.Padding = new Thickness(5 - th, 3 - th, 3 - th, 3 - th);
    }

    private string? _headerBrushKey;
    private string? _frameBrushKey;
    private int _laidOutTabs;

    /// <summary>
    /// SetResourceReference only when the key changes. Each call installs a new resource expression,
    /// which looks the brush up again and invalidates the element even when the key is the same, and
    /// Refresh runs on every label or attention change.
    /// </summary>
    private static void SetBrush(DependencyObject target, DependencyProperty property, ref string? current, string key)
    {
        if (current == key) return;
        current = key;
        switch (target)
        {
            case FrameworkElement fe: fe.SetResourceReference(property, key); break;
            case FrameworkContentElement fce: fce.SetResourceReference(property, key); break;
        }
    }

    private static Border Tint()
    {
        var tint = new Border { CornerRadius = new CornerRadius(3), Opacity = 0.16, Visibility = Visibility.Collapsed };
        tint.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        return tint;
    }

    /// <summary>
    /// Six dots (3×2) in the middle of the header: this pane can be dragged by its header. Only a
    /// hint and a bigger cursor target; the press goes on to the header, which does the drag.
    /// </summary>
    private static FrameworkElement Grip()
    {
        var dots = new GeometryGroup();
        foreach (var y in new[] { 1.0, 5.0 })
            foreach (var x in new[] { 1.0, 5.0, 9.0 })
                dots.Children.Add(new EllipseGeometry(new Point(x, y), 1.1, 1.1));
        dots.Freeze();
        var path = new Path
        {
            Data = dots,
            Width = 10,
            Height = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
        };
        path.SetResourceReference(Shape.FillProperty, "TextFillColorSecondaryBrush");
        var grip = new Border
        {
            Child = path,
            Width = 28,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Drag to move this pane",
        };
        // Subtle fill on hover, like the ghost buttons beside it.
        grip.MouseEnter += (_, _) => grip.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
        grip.MouseLeave += (_, _) => grip.Background = Brushes.Transparent;
        AutomationProperties.SetName(grip, "Drag to move this pane");
        return grip;
    }

    private static Button GhostButton(string glyph, double size, string tip, string name)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = (FontFamily)Application.Current.Resources["IconFont"], FontSize = size },
            Padding = new Thickness(7, 3, 7, 3),
            MinWidth = 0,
            MinHeight = 0,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false, // never steal focus from the terminal
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = tip,
        };
        AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>
    /// One tab in the strip. Active: subtle fill, primary semibold text (like Fluent's TabView; never the
    /// accent). Needs you: accent tint and "Needs you · name". ✕ shows on the active tab.
    /// Click selects, middle-click closes, double-click renames.
    /// </summary>
    private sealed class TabView
    {
        private readonly PaneSlot _slot;
        private readonly Border _bg;
        private readonly Border _tint;
        private readonly TextBlock _text;
        private readonly Run _needsYou;
        private readonly Run _name;
        private readonly TextBox _renameBox;
        private readonly Button _close;
        private readonly DockPanel _row;
        private bool _active;

        public TabView(PaneSlot slot, PaneSession pane)
        {
            _slot = slot;
            Pane = pane;

            _needsYou = new Run("Needs you · ") { FontWeight = FontWeights.SemiBold };
            _needsYou.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
            _name = new Run();
            _text = new TextBlock
            {
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Inlines = { _needsYou, _name },
            };

            _renameBox = new TextBox { FontSize = 12, Padding = new Thickness(2, 0, 2, 0), MinHeight = 0, Visibility = Visibility.Collapsed };
            AutomationProperties.SetName(_renameBox, "Tab name");
            _renameBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { CommitRename(); e.Handled = true; }
                else if (e.Key == Key.Escape) { EndRename(); e.Handled = true; }
            };
            _renameBox.LostKeyboardFocus += (_, _) => { if (_renameBox.IsVisible) CommitRename(); };

            _close = GhostButton("", 8,"Close tab (Ctrl+Shift+W)", "Close tab");
            _close.Padding = new Thickness(5, 3, 5, 3);
            _close.Click += (_, _) => _slot.CloseRequested?.Invoke(Pane);

            var row = _row = new DockPanel { Margin = new Thickness(8, 0, 1, 0) };
            DockPanel.SetDock(_close, Dock.Right);
            row.Children.Add(_close);
            row.Children.Add(new Grid { Children = { _text, _renameBox } });

            _tint = Tint();
            _bg = new Border { CornerRadius = new CornerRadius(3), Background = Brushes.Transparent };
            Root = new Grid
            {
                Margin = new Thickness(0, 2, 2, 2),
                Background = Brushes.Transparent,
                Children = { _bg, _tint, row },
            };
            AutomationProperties.SetItemType(Root, "Tab");

            Root.MouseEnter += (_, _) => Update(_active);
            Root.MouseLeave += (_, _) => Update(_active);
            Root.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) BeginRename();
                else _slot.TabActivated?.Invoke(Pane);
                e.Handled = true; // not the header's "focus the pane"
            };
            Root.MouseUp += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle) { _slot.CloseRequested?.Invoke(Pane); e.Handled = true; }
            };

            var duplicate = new MenuItem { Header = "Duplicate tab" };
            duplicate.Click += (_, _) => _slot.DuplicateRequested?.Invoke(Pane);
            var rename = new MenuItem { Header = "Rename tab" };
            rename.Click += (_, _) => BeginRename();
            var close = new MenuItem { Header = "Close tab", InputGestureText = "Ctrl+Shift+W" };
            close.Click += (_, _) => _slot.CloseRequested?.Invoke(Pane);
            var closeOthers = new MenuItem { Header = "Close other tabs" };
            closeOthers.Click += (_, _) => _slot.CloseOthersRequested?.Invoke(Pane);
            Root.ContextMenu = new ContextMenu { Items = { duplicate, rename, new Separator(), close, closeOthers } };
        }

        public PaneSession Pane { get; }
        public Grid Root { get; }

        private (string Label, bool Waiting, bool Active)? _measured;
        private string? _bgKey;
        private string? _nameKey;

        /// <summary>Shows the tab's current state. True when something that changes its width changed.</summary>
        public bool Update(bool active)
        {
            _active = active;
            var waiting = Pane.HasUnread;
            _name.Text = Pane.Label;
            _needsYou.Text = waiting ? "Needs you · " : "";
            Root.ToolTip = waiting && Pane.AttentionMessage is { } m ? $"Needs you: {m}\n{Pane.Cwd}" : Pane.Cwd;
            AutomationProperties.SetName(Root, waiting ? $"{Pane.Label}, needs you" : Pane.Label);

            _tint.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
            TabStripPanel.SetKeepWide(Root, waiting);
            var bg = active ? "SubtleFillColorSecondaryBrush" : Root.IsMouseOver ? "SubtleFillColorTertiaryBrush" : null;
            if (bg is null) { _bg.Background = Brushes.Transparent; _bgKey = null; }
            else SetBrush(_bg, Border.BackgroundProperty, ref _bgKey, bg);
            SetBrush(_name, TextElement.ForegroundProperty, ref _nameKey, active ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
            _name.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            // ✕ only on the active tab: other tabs close with a middle-click or their menu. Showing it on hover
            // would either shift the strip under the pointer or reserve room that crowded tabs need for names.
            _close.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            _row.Margin = new Thickness(8, 0, active ? 1 : 8, 0); // the ✕ brings its own padding

            var state = (Pane.Label, waiting, active);
            if (_measured == state) return false;
            _measured = state;
            return true;
        }

        private void BeginRename()
        {
            _renameBox.Text = Pane.CustomName ?? Pane.Label;
            _text.Visibility = Visibility.Hidden;
            _renameBox.Visibility = Visibility.Visible;
            _renameBox.Dispatcher.BeginInvoke(() => { _renameBox.Focus(); _renameBox.SelectAll(); },
                System.Windows.Threading.DispatcherPriority.Input);
        }

        private void CommitRename()
        {
            // An empty name goes back to the derived one (program title, folder, shell).
            Pane.CustomName = _renameBox.Text;
            EndRename();
            _slot.Renamed?.Invoke(Pane);
        }

        private void EndRename()
        {
            _renameBox.Visibility = Visibility.Collapsed;
            _text.Visibility = Visibility.Visible;
            Pane.FocusTerminal();
        }
    }
}
