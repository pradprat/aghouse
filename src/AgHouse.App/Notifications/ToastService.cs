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

    /// <summary>
    /// Any program in a pane can raise a notification (OSC 9/99/777), so the title and body are its
    /// words, not aghouse's: <paramref name="source"/> ("workspace › pane") is always shown as the
    /// attribution line, and the texts are cut to a toast's length.
    /// </summary>
    public void Show(string title, string body, string workspaceId, string? paneId, string source)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddArgument("ws", workspaceId)
                .AddArgument("pane", paneId ?? "")
                .AddText(Cut(title, 120))
                .AddAttributionText("From " + Cut(source, 80));
            if (!string.IsNullOrWhiteSpace(body)) builder.AddText(Cut(body, 400));
            builder.Show(t => t.Tag = paneId ?? workspaceId); // one toast per pane, newest replaces older
            Log.Write($"toast shown tag={paneId ?? workspaceId}");
        }
        catch (Exception ex)
        {
            Log.Write($"toast failed: {ex}");
        }
    }

    private static string Cut(string text, int max)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)); // no control-character tricks
        flat = new string(flat.Where(c => !char.IsControl(c)).ToArray());
        return flat.Length <= max ? flat : flat[..(max - 1)] + "…";
    }

    public static void ClearFor(string tag)
    {
        try { ToastNotificationManagerCompat.History.Remove(tag); }
        catch { /* history unavailable */ }
    }
}
