using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace AgHouse.App.Terminal;

/// <summary>
/// One tab in a pane's tab strip. A plain Grid has no automation peer, so screen readers saw only loose
/// text; this one is a TabItem with its name (set through AutomationProperties.Name), whether it is the
/// tab showing, and Select to show it.
/// </summary>
public sealed class TabChip : Grid
{
    public Func<bool> IsActive { get; init; } = () => false;
    public Action Activate { get; init; } = () => { };

    protected override AutomationPeer OnCreateAutomationPeer() => new TabChipPeer(this);

    /// <summary>The tab became (or stopped being) the one showing: tell a screen reader that's listening.</summary>
    public void RaiseSelectionChanged(bool selected)
    {
        if (UIElementAutomationPeer.FromElement(this) is not { } peer) return;
        peer.RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, !selected, selected);
        if (selected && AutomationPeer.ListenerExists(AutomationEvents.SelectionItemPatternOnElementSelected))
            peer.RaiseAutomationEvent(AutomationEvents.SelectionItemPatternOnElementSelected);
    }

    private sealed class TabChipPeer(TabChip owner) : FrameworkElementAutomationPeer(owner), ISelectionItemProvider
    {
        private TabChip Chip => (TabChip)Owner;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TabItem;
        protected override string GetClassNameCore() => "TabItem";
        // A pane with one tab keeps its strip collapsed: then there are no tabs to speak of.
        protected override bool IsControlElementCore() => Owner.IsVisible;
        protected override bool IsContentElementCore() => Owner.IsVisible;

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.SelectionItem ? this : base.GetPattern(patternInterface);

        public bool IsSelected => Chip.IsActive();

        public IRawElementProviderSimple? SelectionContainer =>
            Chip.Parent is UIElement strip && UIElementAutomationPeer.CreatePeerForElement(strip) is { } peer
                ? ProviderFromPeer(peer)
                : null;

        public void Select() => Chip.Activate();
        public void AddToSelection() => Chip.Activate();
        public void RemoveFromSelection() => throw new InvalidOperationException("One tab is always showing.");
    }
}

/// <summary>The tab strip as a Tab control, whose selection is the tab showing.</summary>
internal sealed class TabStripPeer(TabStripPanel owner) : FrameworkElementAutomationPeer(owner), ISelectionProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tab;
    protected override string GetClassNameCore() => "TabStrip";
    protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : "Tabs";
    protected override bool IsControlElementCore() => Owner.IsVisible;
    protected override bool IsContentElementCore() => Owner.IsVisible;

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Selection ? this : base.GetPattern(patternInterface);

    public bool CanSelectMultiple => false;
    public bool IsSelectionRequired => true;

    public IRawElementProviderSimple[] GetSelection() =>
    [
        .. ((Panel)Owner).Children.OfType<TabChip>()
            .Where(chip => chip.IsActive())
            .Select(chip => UIElementAutomationPeer.CreatePeerForElement(chip))
            .Where(peer => peer is not null)
            .Select(peer => ProviderFromPeer(peer!)),
    ];
}
