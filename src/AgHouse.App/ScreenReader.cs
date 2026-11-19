using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;

namespace AgHouse.App;

/// <summary>Says something to a screen reader through a UIA notification, as Windows' own apps do.</summary>
public static class ScreenReader
{
    /// <summary>
    /// Raises <paramref name="text"/> from <paramref name="source"/>'s window. A newer message with the same
    /// <paramref name="activityId"/> replaces one not yet spoken, so moving fast through a list reads only where it stops.
    /// </summary>
    public static void Say(UIElement source, string text, string activityId)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        UIElement target = Window.GetWindow(source) ?? source;
        var peer = UIElementAutomationPeer.FromElement(target) ?? UIElementAutomationPeer.CreatePeerForElement(target);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other,
            AutomationNotificationProcessing.ImportantMostRecent, text, activityId);
    }
}

/// <summary>
/// A Grid that a screen reader hears as one line of text, its AutomationProperties.Name, instead of the
/// pieces inside it. A plain Grid has no automation peer, so a name set on it is never heard.
/// </summary>
public sealed class SpokenPanel : System.Windows.Controls.Grid
{
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(SpokenPanel owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetClassNameCore() => nameof(SpokenPanel);
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
        protected override List<AutomationPeer>? GetChildrenCore() => null;
    }
}
