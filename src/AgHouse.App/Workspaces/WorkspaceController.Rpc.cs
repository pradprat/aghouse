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
                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body)) body = "Needs your attention";
                Notify(pane, title, body);
                return null;
            }

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
            var panes = new JsonArray();
            foreach (var leaf in ws.Tree.Leaves())
            {
                var p = ws.Panes[leaf.PaneId];
                panes.Add(new JsonObject
                {
                    ["id"] = p.Id,
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
                ["panes"] = panes,
            });
        }
        return new JsonObject { ["workspaces"] = workspaces };
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

    /// <summary>Explicit workspace param, else the caller's pane's workspace, else the selected one.</summary>
    private Workspace WorkspaceFor(RpcRequest r)
    {
        if (r.Get("workspace") is { } key) return RequireWorkspace(key);
        if (r.Get("pane") is { } pane && FindPane(pane).Workspace is { } ws) return ws;
        return _selected ?? throw new RpcException("No workspace.");
    }
}
