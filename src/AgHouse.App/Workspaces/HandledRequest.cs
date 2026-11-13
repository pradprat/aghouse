namespace AgHouse.App.Workspaces;

/// <summary>A pane that needed you and was handled: shown under "Earlier" in the waiting list.</summary>
public sealed record HandledRequest(
    string WorkspaceName, string PaneId, string PaneLabel, string? Message, DateTime AskedAt, DateTime HandledAt);
