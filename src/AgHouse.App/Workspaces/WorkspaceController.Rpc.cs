using System.Text.Json.Nodes;
using AgHouse.Core.Ipc;
using AgHouse.Core.Layout;

namespace AgHouse.App.Workspaces;

public sealed partial class WorkspaceController
{
    /// <summary>Entry point for pipe requests; marshals onto the UI thread.</summary>
    public Task<RpcResponse> HandleRpcAsync(RpcRequest request) =>
        _window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                return RpcResponse.Success(request.Id, Dispatch(request));
            }
            catch (RpcException ex)
            {
                return RpcResponse.Failure(request.Id, ex.Message);
            }
        }).Task;

    private sealed class RpcException(string message) : Exception(message);

    private JsonNode? Dispatch(RpcRequest r)
    {
        switch (r.Method)
        {
            case Methods.Ping:
                return new JsonObject { ["app"] = "aghouse", ["version"] = typeof(WorkspaceController).Assembly.GetName().Version?.ToString() };

            case Methods.Activate:
                BringToFront();
                return null;

            case Methods.List:
                return ListJson();

            case Methods.NewWorkspace:
            {
                var ws = NewWorkspace(r.Get("cwd"), r.Get("profile"), r.Get("name"));
                return new JsonObject { ["workspaceId"] = ws.Id, ["paneId"] = ws.ActivePaneId };
            }

            case Methods.SelectWorkspace:
                Selected = RequireWorkspace(r.Get("workspace"));
                return null;

            case Methods.RenameWorkspace:
            {
                var name = r.Get("name");
                if (string.IsNullOrWhiteSpace(name)) throw new RpcException("Missing name.");
                RenameWorkspace(WorkspaceFor(r), name);
                return null;
            }

            case Methods.NewSplit:
            {
                var side = (r.Get("direction") ?? "right").ToLowerInvariant() switch
                {
                    "right" => Direction.Right,
                    "down" => Direction.Down,
                    "left" => Direction.Left,
                    "up" => Direction.Up,
                    var d => throw new RpcException($"Unknown direction '{d}' (use right|down|left|up)."),
                };
                var source = r.Get("pane");
                if (source is not null) RequirePane(source);
                var pane = Split(side, source, r.Get("profile"), r.Get("cwd"))
                           ?? throw new RpcException("No pane to split.");
                return new JsonObject { ["paneId"] = pane.Id };
            }

            case Methods.NewTab:
            {
                var source = r.Get("pane");
                if (source is not null) RequirePane(source);
                var pane = NewTab(source, r.Get("profile"), r.Get("cwd")) ?? throw new RpcException("No pane to add a tab to.");
                return new JsonObject { ["paneId"] = pane.Id };
            }

            case Methods.FocusPane:
                FocusPane(RequirePane(r.Get("pane")));
                return null;

            case Methods.ClosePane:
                ClosePane(RequirePane(r.Get("pane")));
                return null;

            case Methods.SendText:
            {
                var id = RequirePane(r.Get("pane"));
                FindPane(id).Pane!.SendText(r.Get("text") ?? "");
                return null;
            }

            case Methods.Notify:
            {
                var pane = r.Get("pane");
                if (pane is not null && FindPane(pane).Pane is null) pane = null; // stale id: fall back to current
                var title = r.Get("title");
                var body = r.Get("body");
                Notify(pane, title, body);
                return null;
            }

            case Methods.SetStatus:
            {
                var key = r.Get("key");
                var text = r.Get("text");
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(text)) throw new RpcException("Usage: set-status <key> <text>.");
                WorkspaceFor(r).SetStatus(key.Trim(), text, CallerPane(r));
                return null;
            }

            case Methods.ClearStatus:
            {
                var key = r.Get("key");
                if (string.IsNullOrWhiteSpace(key)) throw new RpcException("Usage: clear-status <key>.");
                WorkspaceFor(r).ClearStatus(key.Trim(), CallerPane(r));
                return null;
            }

            case Methods.SetProgress:
            {
                var value = r.Get("value")?.Trim();
                double? progress = value switch
                {
                    null or "" or "clear" => null,
                    _ when value.EndsWith('%') && double.TryParse(value[..^1], System.Globalization.CultureInfo.InvariantCulture, out var pct) => pct / 100,
                    _ when double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var v) => v > 1 ? v / 100 : v,
                    _ => throw new RpcException($"Progress '{value}' isn't a number (use 0..1, 40%, or clear)."),
                };
                WorkspaceFor(r).SetProgress(progress, r.Get("label"));
                return null;
            }

            case Methods.Log:
            {
                var text = r.Get("text");
                if (string.IsNullOrWhiteSpace(text)) throw new RpcException("Usage: log <text>.");
                var level = (r.Get("level") ?? "info").ToLowerInvariant();
                if (level is not ("info" or "warn" or "warning" or "error"))
                    throw new RpcException($"Unknown level '{level}' (use info|warn|error).");
                WorkspaceFor(r).AddLog(text, isProblem: level is not "info");
                return null;
            }

            case Methods.MoveWorkspace:
            {
                // 1-based like select-workspace's index: "move-workspace 1" puts it at the top.
                if (!int.TryParse(r.Get("index"), out var index) || index < 1)
                    throw new RpcException("Usage: move-workspace <position> (1 is the top).");
                var ws = WorkspaceFor(r);
                MoveWorkspace(ws, index - 1);
                return new JsonObject { ["workspaceId"] = ws.Id, ["index"] = Workspaces.IndexOf(ws) + 1 };
            }

            case Methods.CloseWorkspace:
                // Like close-pane: a script asked, so it doesn't ask back.
                CloseWorkspace(WorkspaceFor(r));
                return null;

            case Methods.ZoomPane:
            {
                var id = RequirePane(r.Get("pane"));
                var ws = FindPane(id).Workspace!;
                if (!ToggleZoom(ws, id)) throw new RpcException("Only a workspace with two or more panes can zoom one.");
                return new JsonObject { ["zoomed"] = ws.ZoomedSlotId is not null };
            }

            case Methods.Waiting:
                return WaitingJson();

            default:
                throw new RpcException($"Unknown method '{r.Method}'.");
        }
    }

    private JsonObject ListJson()
    {
        var workspaces = new JsonArray();
        for (var i = 0; i < Workspaces.Count; i++)
        {
            var ws = Workspaces[i];
            // One entry per terminal (tabs included), in layout order. "slot" groups the tabs of one pane;
            // "tab" is the 1-based position in its strip and "visible" says whether it's the tab on show.
            var panes = new JsonArray();
            foreach (var p in ws.PanesInOrder())
            {
                var slot = ws.SlotOf(p);
                panes.Add(new JsonObject
                {
                    ["id"] = p.Id,
                    ["slot"] = p.SlotId,
                    ["tab"] = (slot?.Tabs.ToList().IndexOf(p) ?? 0) + 1,
                    ["visible"] = ReferenceEquals(slot?.ActiveTab, p),
                    ["name"] = p.Label,
                    ["active"] = p.Id == ws.ActivePaneId,
                    ["unread"] = p.HasUnread,
                    ["profile"] = p.Profile.Id,
                    ["cwd"] = p.Cwd,
                    ["title"] = p.Title,
                });
            }
            workspaces.Add(new JsonObject
            {
                ["id"] = ws.Id,
                ["index"] = i + 1,
                ["name"] = ws.Name,
                ["selected"] = ws.IsSelected,
                ["unread"] = ws.HasUnread,
                ["branch"] = ws.Branch,
                ["lastMessage"] = ws.LastMessage,
                ["pullRequest"] = ws.PullRequest,
                ["status"] = new JsonArray(ws.Chips.Select(c => (JsonNode?)JsonValue.Create(c.Text)).ToArray()),
                ["progress"] = ws.Progress,
                ["lastLog"] = ws.LastLog,
                ["panes"] = panes,
            });
        }
        return new JsonObject { ["workspaces"] = workspaces };
    }

    /// <summary>What the waiting list shows: panes that need you now (newest first), then handled requests.</summary>
    private JsonObject WaitingJson()
    {
        var waiting = new JsonArray();
        foreach (var (ws, pane) in WaitingPanes())
        {
            waiting.Add(new JsonObject
            {
                ["workspaceId"] = ws.Id,
                ["workspace"] = ws.Name,
                ["paneId"] = pane.Id,
                ["pane"] = pane.Label,
                ["message"] = pane.AttentionMessage,
                ["since"] = pane.UnreadAt.ToString("O"),
            });
        }
        var earlier = new JsonArray();
        foreach (var h in Earlier)
        {
            earlier.Add(new JsonObject
            {
                ["workspace"] = h.WorkspaceName,
                ["paneId"] = h.PaneId,
                ["pane"] = h.PaneLabel,
                ["message"] = h.Message,
                ["asked"] = h.AskedAt.ToString("O"),
                ["handled"] = h.HandledAt.ToString("O"),
            });
        }
        return new JsonObject { ["waiting"] = waiting, ["earlier"] = earlier };
    }

    private string RequirePane(string? id)
    {
        id ??= _selected?.ActivePaneId;
        if (id is null || FindPane(id).Pane is null) throw new RpcException($"Pane '{id}' not found.");
        return id;
    }

    /// <summary>Workspace by id, exact name, or 1-based index.</summary>
    private Workspace RequireWorkspace(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new RpcException("Missing workspace.");
        var ws = Workspaces.FirstOrDefault(w => w.Id == key)
                 ?? Workspaces.FirstOrDefault(w => string.Equals(w.Name, key, StringComparison.OrdinalIgnoreCase))
                 ?? (int.TryParse(key, out var n) && n >= 1 && n <= Workspaces.Count ? Workspaces[n - 1] : null);
        return ws ?? throw new RpcException($"Workspace '{key}' not found.");
    }

    /// <summary>The pane the request came from, if it still exists (agent status is kept per pane).</summary>
    private string? CallerPane(RpcRequest r) => r.Get("pane") is { } id && FindPane(id).Pane is not null ? id : null;

    /// <summary>Explicit workspace param, else the caller's pane's workspace, else the selected one.</summary>
    private Workspace WorkspaceFor(RpcRequest r)
    {
        if (r.Get("workspace") is { } key) return RequireWorkspace(key);
        if (r.Get("pane") is { } pane && FindPane(pane).Workspace is { } ws) return ws;
        return _selected ?? throw new RpcException("No workspace.");
    }
}
