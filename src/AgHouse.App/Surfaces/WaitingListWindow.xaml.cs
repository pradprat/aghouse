using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AgHouse.App.Surfaces;

/// <summary>
/// One row of the waiting list. A waiting pane says "Needs you · message"; an earlier one is
/// greyed and keeps only the message, so the list never suggests it still needs you.
/// </summary>
public sealed class WaitingRow
{
    public required string Where { get; init; }
    public string? Message { get; init; }
    public required bool Waiting { get; init; }
    public required DateTime At { get; init; }
    public required string PaneId { get; init; }

    public string Prefix => Waiting ? "Needs you" : "";
    public string MessageText => Waiting
        ? (string.IsNullOrWhiteSpace(Message) ? "" : " · " + Message)
        : (string.IsNullOrWhiteSpace(Message) ? "Needed you" : Message);
    public Brush WhereBrush =>
        Application.Current.TryFindResource(Waiting ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush") as Brush
        ?? SystemColors.WindowTextBrush;
    public string Age => FormatAge(DateTime.UtcNow - At);
    public string AgeDetail => (Waiting ? "Asked at " : "Handled at ") + At.ToLocalTime().ToString("t");

    /// <summary>"now", "4 min", "2 h", "3 d": short enough for the row's right edge.</summary>
    public static string FormatAge(TimeSpan age) => age.TotalMinutes switch
    {
        < 1 => "now",
        < 60 => $"{(int)age.TotalMinutes} min",
        < 60 * 24 => $"{(int)age.TotalHours} h",
        _ => $"{(int)age.TotalDays} d",
    };
}

/// <summary>
/// The footer's "N waiting" opens this (also Ctrl+Shift+I): every pane that needs you, newest first,
/// then the last requests that were handled. Click or Enter goes to the pane.
/// </summary>
public partial class WaitingListWindow : FlyoutWindow
{
    private readonly Action<string> _goToPane;

    public WaitingListWindow(Window owner, FrameworkElement anchor, IReadOnlyList<WaitingRow> waiting,
        IReadOnlyList<WaitingRow> earlier, Action<string> goToPane, Action backToTerminal)
    {
        InitializeComponent();
        Owner = owner;
        AfterClose = backToTerminal;
        _goToPane = goToPane;

        WaitingItems.ItemsSource = waiting;
        WaitingItems.Visibility = waiting.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NothingWaiting.Visibility = waiting.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        JumpHint.Visibility = waiting.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EarlierItems.ItemsSource = earlier;
        EarlierSection.Visibility = earlier.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Opens upward from the footer line it grew out of, flush with the sidebar's left edge.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000;
        SizeChanged += (_, _) => PlaceAbove(anchor);
        ContentRendered += (_, _) =>
        {
            var list = waiting.Count > 0 ? WaitingItems : earlier.Count > 0 ? EarlierItems : null;
            if (list is null) { Focus(); return; }
            list.SelectedIndex = 0;
            (list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
        };
    }

    private void PlaceAbove(FrameworkElement anchor)
    {
        if (PresentationSource.FromVisual(anchor) is not { CompositionTarget: { } target }) return;
        var topLeft = target.TransformFromDevice.Transform(anchor.PointToScreen(new Point(0, 0)));
        Left = topLeft.X - 4;
        Top = Math.Max(SystemParameters.WorkArea.Top, topLeft.Y - ActualHeight - 6);
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is ListBox { SelectedItem: WaitingRow row })
        {
            CloseThen(() => _goToPane(row.PaneId));
            e.Handled = true;
        }
    }

    private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox list
            && ItemsControl.ContainerFromElement(list, (DependencyObject)e.OriginalSource) is ListBoxItem { DataContext: WaitingRow row })
        {
            CloseThen(() => _goToPane(row.PaneId));
        }
    }
}
