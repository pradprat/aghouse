using System.Text.Json.Nodes;
using AgHouse.App.Input;
using AgHouse.App.Persistence;
using AgHouse.App.Terminal;
using AgHouse.Core.Ipc;
using AgHouse.Core.Layout;
using AgHouse.Core.Settings;
using AgHouse.Core.Terminal;

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
            catch (ArgumentException ex) // a key name or setting value the Core parsers rejected
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
                var pane = FindPane(id).Pane!;
                // Where the output stood just before the text went in: wait-for from here can't miss the reply.
                var cursor = pane.Mirror.Read(last: 0).Next;
                pane.SendText(r.Get("text") ?? "");
                return new JsonObject { ["paneId"] = id, ["cursor"] = cursor };
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

            case Methods.ReadPane:
            {
                var id = RequirePane(r.Get("pane"));
                long? since = null;
                if (r.Get("since") is { Length: > 0 } s)
                {
                    since = long.TryParse(s, out var c) && c >= 0 ? c : throw new RpcException($"--since takes a cursor from an earlier read, not '{s}'.");
                }
                var last = 50;
                if (r.Get("lines") is { Length: > 0 } l && (!int.TryParse(l, out last) || last < 0))
                    throw new RpcException($"--lines takes a number, not '{l}'.");
                var read = FindPane(id).Pane!.Mirror.Read(since, last);
                return new JsonObject
                {
                    ["paneId"] = id,
                    ["lines"] = new JsonArray(read.Lines.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                    ["partial"] = read.Partial,
                    ["next"] = read.Next,
                    ["truncated"] = read.Truncated,
                };
            }

            case Methods.SendKeys:
            {
                var id = RequirePane(r.Get("pane"));
                var keys = (r.Get("keys") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (keys.Length == 0) throw new RpcException("Usage: send-keys <key>... (e.g. ctrl+c, up enter).");
                FindPane(id).Pane!.SendText(KeyChords.ToSequence(keys));
                return null;
            }

            case Methods.RenamePane:
            {
                var id = RequirePane(r.Get("pane"));
                var (ws, pane) = FindPane(id);
                pane!.CustomName = r.Get("name"); // empty goes back to the name aghouse picks
                UpdateLabels(ws!);
                ScheduleSave();
                return new JsonObject { ["paneId"] = id, ["name"] = pane.Label };
            }

            case Methods.RestartPane:
            {
                var id = RequirePane(r.Get("pane"));
                var profile = r.Get("profile");
                if (profile is not null) RequireProfile(profile);
                var pane = RestartPane(id, profile, r.Get("cwd")) ?? throw new RpcException($"Pane '{id}' not found.");
                return new JsonObject { ["oldPaneId"] = id, ["paneId"] = pane.Id, ["profile"] = pane.Profile.Id };
            }

            case Methods.Profiles:
            {
                var list = new JsonArray();
                foreach (var p in Profiles.All)
                {
                    list.Add(new JsonObject
                    {
                        ["id"] = p.Id,
                        ["name"] = p.Name,
                        ["commandLine"] = p.CommandLine,
                        ["default"] = ReferenceEquals(p, Profiles.Default),
                    });
                }
                return new JsonObject { ["profiles"] = list };
            }

            case Methods.GetSettings:
                return SettingsJson(r.Get("key"));

            case Methods.SetSetting:
            {
                var key = SettingKeys.Require(r.Get("key"));
                var value = SettingKeys.Parse(key, r.Get("value"));
                if (key.Name == "defaultProfile") value = RequireProfile(r.Get("value")!).Id;
                if (key.Name == "fontFamily")
                {
                    var name = value!.GetValue<string>();
                    var font = System.Windows.Media.Fonts.SystemFontFamilies
                        .FirstOrDefault(f => string.Equals(f.Source, name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new RpcException($"The font '{name}' isn't installed. Monospaced fonts here: {string.Join(", ", MonospaceFonts())}.");
                    value = font.Source;
                }
                return ChangeSetting(key, value);
            }

            case Methods.ResetSetting:
            {
                var key = SettingKeys.Require(r.Get("key"));
                return ChangeSetting(key, key.Default?.DeepClone());
            }

            case Methods.Actions:
            {
                var list = new JsonArray();
                foreach (var a in ActionNames.All)
                {
                    list.Add(new JsonObject { ["name"] = a.Name, ["description"] = a.Description, ["gesture"] = a.Gesture });
                }
                return new JsonObject { ["actions"] = list };
            }

            case Methods.RunAction:
            {
                var info = ActionNames.Find(r.Get("action"))
                           ?? throw new RpcException($"Unknown action '{r.Get("action")}'. Run 'aghouse actions' for the list.");
                if (r.Get("pane") is { } target) FocusPane(RequirePane(target));
                RunNamedAction(info.Name);
                return new JsonObject { ["action"] = info.Name };
            }

            case Methods.Quit:
            {
                var force = r.Get("force") is "true" or "1" or "yes";
                // Answer first: the pipe goes away with the window.
                _ = Task.Delay(150).ContinueWith(_ => _window.Dispatcher.BeginInvoke(() => RequestQuit(force)));
                return new JsonObject { ["quitting"] = true, ["force"] = force };
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

    /// <summary>Every setting a script may change (or just <paramref name="key"/>), with its value and what it takes.</summary>
    private JsonObject SettingsJson(string? key)
    {
        var current = AppStorage.ToJson(_settings);
        JsonObject Entry(SettingKey k) => new()
        {
            ["key"] = k.Name,
            ["value"] = current[k.Name]?.DeepClone(),
            ["type"] = k.Type.ToString().ToLowerInvariant(),
            ["values"] = k.ValueHint,
            ["default"] = k.Default?.DeepClone(),
            ["description"] = k.Description,
        };
        if (key is not null) return Entry(SettingKeys.Require(key));
        return new JsonObject
        {
            ["path"] = AppStorage.SettingsPath,
            ["readOnly"] = _settingsProblem is not null,
            ["problem"] = _settingsProblem is { } p ? $"{p.Summary}: {p.Detail}" : null,
            ["settings"] = new JsonArray(SettingKeys.All.Select(k => (JsonNode?)Entry(k)).ToArray()),
        };
    }

    /// <summary>The same path as a change in the Settings window: apply now, then write settings.json.</summary>
    private JsonObject ChangeSetting(SettingKey key, JsonNode? value)
    {
        if (_settingsProblem is { } problem)
            throw new RpcException($"{problem.Summary}, so settings are read-only until it's fixed and aghouse restarts ({AppStorage.SettingsPath}).");
        var json = AppStorage.ToJson(_settings);
        json[key.Name] = value;
        ApplySettings(AppStorage.FromJson(json));
        var saved = AppStorage.SaveSettings(_settings);
        return new JsonObject
        {
            ["key"] = key.Name,
            ["value"] = AppStorage.ToJson(_settings)[key.Name]?.DeepClone(),
            ["saved"] = saved,
            ["note"] = saved ? null : "Applied until aghouse closes, but settings.json couldn't be written. The log has the details.",
        };
    }

    private ShellProfile RequireProfile(string id) =>
        Profiles.All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(p.Name, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new RpcException($"Unknown profile '{id}'. Profiles: {string.Join(", ", Profiles.All.Select(p => p.Id))}.");

    /// <summary>Things only the main window can do (open a Settings page, a file, a folder), by action name.</summary>
    public event Action<string>? UiRequested;

    /// <summary>An action from <see cref="ActionNames"/>, run as if its shortcut was pressed.</summary>
    private void RunNamedAction(string name)
    {
        if (name.StartsWith("select-workspace-", StringComparison.Ordinal))
        {
            Run(AppAction.SelectWorkspace, index: int.Parse(name["select-workspace-".Length..]) - 1);
            return;
        }
        AppAction? action = name switch
        {
            "new-workspace" => AppAction.NewWorkspace,
            "new-tab" => AppAction.NewTab,
            "next-tab" => AppAction.NextTab,
            "previous-tab" => AppAction.PreviousTab,
            "split-right" => AppAction.SplitRight,
            "split-down" => AppAction.SplitDown,
            "close-pane" => AppAction.ClosePane,
            "zoom-pane" => AppAction.ZoomPane,
            "next-workspace" => AppAction.NextWorkspace,
            "previous-workspace" => AppAction.PreviousWorkspace,
            "jump-to-unread" => AppAction.JumpToUnread,
            "rename-workspace" => AppAction.RenameWorkspace,
            "close-workspace" => AppAction.CloseWorkspace,
            "copy" => AppAction.Copy,
            "paste" => AppAction.Paste,
            "find" => AppAction.Find,
            "font-bigger" => AppAction.FontBigger,
            "font-smaller" => AppAction.FontSmaller,
            "font-reset" => AppAction.FontReset,
            "command-palette" => AppAction.CommandPalette,
            "waiting-list" => AppAction.WaitingList,
            "settings" => AppAction.Settings,
            _ => null,
        };
        Direction? direction = name switch
        {
            "focus-left" => Direction.Left,
            "focus-right" => Direction.Right,
            "focus-up" => Direction.Up,
            "focus-down" => Direction.Down,
            _ => null,
        };

        // Something the user is meant to see or type into opens in front, not behind another app.
        if (action is AppAction.CommandPalette or AppAction.WaitingList or AppAction.Settings or AppAction.RenameWorkspace
                or AppAction.Find or AppAction.ClosePane or AppAction.CloseWorkspace
            || name is "connect-claude-code" or "about")
        {
            BringToFront();
        }

        if (action is { } a) Run(a);
        else if (direction is { } d) Run(AppAction.Focus, direction: d);
        else if (UiRequested is { } ui) ui(name);
        else throw new RpcException($"'{name}' can't run here.");
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
