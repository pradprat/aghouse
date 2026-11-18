namespace AgHouse.Core.Ipc;

/// <summary>An app action <c>aghouse run</c> can start, by the name scripts use.</summary>
public sealed record ActionInfo(string Name, string Description, string? Gesture = null);

/// <summary>
/// Everything the command palette and the keyboard shortcuts can do, by name. <c>aghouse run &lt;name&gt;</c>
/// does it as if the shortcut was pressed (actions that ask before ending a program still ask).
/// </summary>
public static class ActionNames
{
    public static IReadOnlyList<ActionInfo> All { get; } =
    [
        new("new-workspace", "Open a new workspace", "Ctrl+Shift+N"),
        new("new-tab", "Open a new tab in the focused pane", "Ctrl+Shift+T"),
        new("next-tab", "Show the next tab in the focused pane", "Ctrl+PageDown"),
        new("previous-tab", "Show the previous tab in the focused pane", "Ctrl+PageUp"),
        new("split-right", "Split the focused pane to the right", "Ctrl+D"),
        new("split-down", "Split the focused pane downwards", "Ctrl+Shift+D"),
        new("close-pane", "Close the focused tab or pane (asks if something is running)", "Ctrl+Shift+W"),
        new("zoom-pane", "Fill the workspace with the focused pane, or show all panes again", "Ctrl+Shift+Enter"),
        new("focus-left", "Focus the pane on the left", "Alt+Left"),
        new("focus-right", "Focus the pane on the right", "Alt+Right"),
        new("focus-up", "Focus the pane above", "Alt+Up"),
        new("focus-down", "Focus the pane below", "Alt+Down"),
        .. Enumerable.Range(1, 9).Select(i => new ActionInfo(
            $"select-workspace-{i}", i == 9 ? "Select the last workspace" : $"Select workspace {i}", $"Ctrl+{i}")),
        new("next-workspace", "Select the next workspace", "Ctrl+Tab"),
        new("previous-workspace", "Select the previous workspace", "Ctrl+Shift+Tab"),
        new("jump-to-unread", "Focus the pane at the top of the waiting list: one asking something before one that finished, newest first", "Ctrl+Shift+U"),
        new("rename-workspace", "Start renaming the selected workspace in the sidebar", "Ctrl+Shift+R"),
        new("close-workspace", "Close the selected workspace (asks if something is running)", "Ctrl+Alt+W"),
        new("toggle-groups", "Group the sidebar's workspaces by project, or show them as one list again"),
        new("copy", "Copy the focused pane's selection", "Ctrl+Shift+C"),
        new("paste", "Paste the clipboard into the focused pane", "Ctrl+Shift+V"),
        new("find", "Find in the focused pane's output", "Ctrl+Shift+F"),
        new("font-bigger", "Bigger text in the focused pane", "Ctrl+="),
        new("font-smaller", "Smaller text in the focused pane", "Ctrl+-"),
        new("font-reset", "Reset the focused pane's text size", "Ctrl+0"),
        new("command-palette", "Open the command palette", "Ctrl+Shift+P"),
        new("waiting-list", "Open the waiting list", "Ctrl+Shift+I"),
        new("settings", "Open Settings", "Ctrl+,"),
        new("connect-claude-code", "Open Settings at Integrations"),
        new("about", "Open Settings at About"),
        new("edit-settings-file", "Open settings.json in the default editor"),
        new("open-logs", "Open the logs folder"),
        new("check-for-updates", "Ask GitHub for a newer aghouse and show the answer in Settings at About"),
        new("restart-to-update", "Install the new version now and restart (downloads it first; asks if something is running)"),
    ];

    public static ActionInfo? Find(string? name) =>
        All.FirstOrDefault(a => string.Equals(a.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
}
