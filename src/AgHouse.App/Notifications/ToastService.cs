using CommunityToolkit.WinUI.Notifications;

namespace AgHouse.App.Notifications;

/// <summary>Windows toast notifications. Clicking one focuses the pane that raised it.</summary>
public sealed class ToastService
{
    /// <summary>(workspaceId, paneId) — raised on a background thread.</summary>
    public event Action<string?, string?>? Activated;

    public ToastService()
    {
        ToastNotificationManagerCompat.OnActivated += e =>
        {
            var args = ToastArguments.Parse(e.Argument);
            args.TryGetValue("ws", out var ws);
            args.TryGetValue("pane", out var pane);
            Activated?.Invoke(ws, string.IsNullOrEmpty(pane) ? null : pane);
        };
    }

    public void Show(string title, string body, string workspaceId, string? paneId)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddArgument("ws", workspaceId)
                .AddArgument("pane", paneId ?? "")
                .AddText(title);
            if (!string.IsNullOrWhiteSpace(body)) builder.AddText(body);
            builder.Show(t => t.Tag = paneId ?? workspaceId); // one toast per pane, newest replaces older
            Log.Write($"toast shown tag={paneId ?? workspaceId}");
        }
        catch (Exception ex)
        {
            Log.Write($"toast failed: {ex}");
        }
    }

    public static void ClearFor(string tag)
    {
        try { ToastNotificationManagerCompat.History.Remove(tag); }
        catch { /* history unavailable */ }
    }
}
