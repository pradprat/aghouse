using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace AgHouse.App.Surfaces;

/// <summary>
/// Ctrl+Shift+P. With nothing typed it lists, in groups: panes that need you, workspaces, then
/// actions with their shortcuts. Typing ranks everything in one list. Enter runs the selected row;
/// Esc goes back to the terminal.
/// </summary>
public partial class PaletteWindow : FlyoutWindow
{
    private readonly IReadOnlyList<PaletteItem> _all;
    private readonly ListCollectionView _view;

    public PaletteWindow(Window owner, IReadOnlyList<PaletteItem> items, Action backToTerminal)
    {
        InitializeComponent();
        Owner = owner;
        AfterClose = backToTerminal;
        _all = items;
        _view = new ListCollectionView(items.ToList());
        Results.ItemsSource = _view;

        // Near the top of the window, centred, like Windows Terminal's palette.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Loaded += (_, _) =>
        {
            var o = owner.WindowState == WindowState.Maximized ? owner.RestoreBounds : new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight);
            if (owner.WindowState == WindowState.Maximized)
            {
                // RestoreBounds is the old size; a maximized window fills its screen's work area.
                o = SystemParameters.WorkArea;
            }
            Width = Math.Min(600, Math.Max(360, o.Width - 64));
            Left = o.Left + (o.Width - Width) / 2;
            Top = o.Top + 44;
        };
        ContentRendered += (_, _) => Query.Focus();
        Filter("");
    }

    private void Query_TextChanged(object sender, TextChangedEventArgs e)
    {
        Hint.Visibility = Query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Filter(Query.Text.Trim());
    }

    private void Filter(string query)
    {
        using (_view.DeferRefresh())
        {
            _view.GroupDescriptions.Clear();
            _view.SortDescriptions.Clear();
            if (query.Length == 0)
            {
                foreach (var item in _all) item.Score = 0;
                _view.Filter = null;
                _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PaletteItem.Group)));
                _view.SortDescriptions.Add(new SortDescription(nameof(PaletteItem.Order), ListSortDirection.Ascending));
            }
            else
            {
                foreach (var item in _all) item.Score = item.Match(query);
                _view.Filter = o => ((PaletteItem)o).Score >= 0;
                _view.SortDescriptions.Add(new SortDescription(nameof(PaletteItem.Score), ListSortDirection.Descending));
                _view.SortDescriptions.Add(new SortDescription(nameof(PaletteItem.Order), ListSortDirection.Ascending));
            }
        }
        NoMatches.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        Results.Visibility = _view.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (!_view.IsEmpty)
        {
            Results.SelectedIndex = 0;
            Results.ScrollIntoView(Results.SelectedItem);
        }
    }

    private void Query_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var count = Results.Items.Count;
        switch (e.Key)
        {
            case Key.Down or Key.Up when count > 0:
                Results.SelectedIndex = ((Results.SelectedIndex + (e.Key == Key.Down ? 1 : -1)) % count + count) % count;
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
                break;
            case Key.PageDown or Key.PageUp when count > 0:
                Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + (e.Key == Key.PageDown ? 8 : -8), 0, count - 1);
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                RunSelected();
                e.Handled = true;
                break;
        }
    }

    private void Results_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Results, (DependencyObject)e.OriginalSource) is ListBoxItem { DataContext: PaletteItem item })
        {
            Results.SelectedItem = item;
            RunSelected();
        }
    }

    private void RunSelected()
    {
        if (Results.SelectedItem is PaletteItem item) CloseThen(item.Run);
    }
}
