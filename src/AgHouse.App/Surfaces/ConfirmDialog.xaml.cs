using System.Windows;
using System.Windows.Automation;

namespace AgHouse.App.Surfaces;

/// <summary>One running program the action would end.</summary>
/// <param name="Name">What is running: the program's title (status glyphs dropped) or its exe.</param>
/// <param name="Where">"workspace › pane", or just the pane when the workspace is already named.</param>
/// <param name="NeedsYou">The agent there is waiting on the user.</param>
/// <param name="State">The agent's own state ("Working"), when it reported one and isn't waiting.</param>
public sealed record ConfirmItem(string Name, string Where, bool NeedsYou, string? State)
{
    public bool HasState => !NeedsYou && !string.IsNullOrEmpty(State);

    /// <summary>The row as a screen reader says it.</summary>
    public string Spoken => $"{Name}, {Where}" + (NeedsYou ? ", needs you" : HasState ? $", {State}" : "");
}

/// <summary>
/// The confirmation before ending running work (closing a pane, a workspace, or quitting). A Fluent
/// window in ContentDialog's shape rather than the Win32 MessageBox, which stays light in dark mode
/// and can't draw an agent's title glyphs. Cancel is the default button, so a stray Enter never ends
/// a process.
/// </summary>
public partial class ConfirmDialog : Window
{
    /// <summary>The most rows shown before the rest fold into "and N more".</summary>
    public const int MaxItems = 5;

    private ConfirmDialog(Window owner, string heading, string body, string confirm, IReadOnlyList<ConfirmItem> items)
    {
        InitializeComponent();
        Owner = owner;
        Heading.Text = heading;
        Body.Text = body;
        ConfirmButton.Content = confirm;

        // Waiting agents first, then busy ones: those are the ones a quit would hurt most.
        var ordered = items.OrderByDescending(i => i.NeedsYou).ThenByDescending(i => i.HasState).ToList();
        if (ordered.Count > 0)
        {
            Items.ItemsSource = ordered.Take(MaxItems).ToList();
            if (ordered.Count > MaxItems)
            {
                More.Text = $"and {ordered.Count - MaxItems} more";
                More.ToolTip = string.Join("\n", ordered.Skip(MaxItems).Select(i => $"{i.Name} · {i.Where}"));
                More.Visibility = Visibility.Visible;
            }
        }
        else
        {
            Items.Visibility = Visibility.Collapsed;
        }

        // Screen readers hear the question and what it costs, not just "aghouse".
        AutomationProperties.SetName(this, heading);
        AutomationProperties.SetHelpText(this, body);
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>Shows the dialog and returns true only when the user chose the action.</summary>
    public static bool Ask(Window owner, string heading, string body, string confirm, IReadOnlyList<ConfirmItem>? items = null) =>
        new ConfirmDialog(owner, heading, body, confirm, items ?? []).ShowDialog() == true;
}
