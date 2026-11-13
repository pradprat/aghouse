using System.Text.Json;
using System.Text.Json.Nodes;
using AgHouse.Core.Ipc;

namespace AgHouse.Cli;

internal static class Program
{
    private const string Usage = """
        aghouse — control the aghouse terminal app

        Usage: aghouse <command> [args] [--option value]

        Commands
          list                                  Workspaces and panes (JSON)
          new-workspace [--cwd DIR] [--profile ID] [--name NAME]
          select-workspace <id|name|index>
          rename-workspace <name> [--workspace W]
          move-workspace <position> [--workspace W]   1 is the top of the sidebar
          close-workspace [W]                   Ends what runs there without asking
          new-split [right|down|left|up] [--pane ID] [--profile ID] [--cwd DIR]
          new-tab [--pane ID] [--profile ID] [--cwd DIR]   A tab in the same pane, right after it
          focus-pane <id>                       Also select-tab <id>: shows the tab and focuses it
          close-pane [id]                       Also close-tab [id]: the pane goes with its last tab
          zoom-pane [id]                        Fill the workspace with one pane, or show all again
          waiting                               Panes that need you, then handled requests (JSON)
          send-text <text> [--pane ID] [--enter]
          notify [--title T] [--body B] [--pane ID]

        Sidebar (shown on this workspace's row; --workspace W to target another)
          set-status <key> <text>               A chip, e.g. set-status tests "Running tests"
          clear-status <key>
          set-progress <0..1|40%|clear> [--label L]
          log <text> [--level info|warn|error]

          claude-hook                          For Claude Code hooks: reads hook JSON on stdin
          hooks install claude [--yes]          Add aghouse's hooks to ~/.claude/settings.json (asks first)
          hooks status claude                   connected, partial or not connected
          hooks print claude                    Print the settings.json snippet to merge by hand
          ping

        Inside an aghouse pane, --pane defaults to the current pane ($AGHOUSE_PANE_ID).
        """;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var (positional, options) = Parse(args[1..]);
        var command = args[0];
        var currentPane = Environment.GetEnvironmentVariable(AgEnv.PaneId);
        string? Opt(string key) => options.TryGetValue(key, out var v) ? v : null;
        string? Pos(int i) => i < positional.Count ? positional[i] : null;

        try
        {
            switch (command)
            {
                case "hooks":
                    if (Pos(0) == "print" && Pos(1) == "claude") { Console.WriteLine(ClaudeHooksSnippet); return 0; }
                    if (Pos(0) == "install" && Pos(1) == "claude") return InstallClaudeHooks(options.ContainsKey("yes"));
                    if (Pos(0) == "status" && Pos(1) == "claude") return ClaudeHooksStatus();
                    return Fail("Usage: aghouse hooks print|install|status claude");

                case "claude-hook":
                    return await ClaudeHookAsync(currentPane);

                case "list":
                    return await CallAsync(Methods.List, new(), pretty: true);

                case "ping":
                    return await CallAsync(Methods.Ping, new(), pretty: true);

                case "new-workspace":
                    return await CallAsync(Methods.NewWorkspace, new()
                    {
                        ["cwd"] = Opt("cwd") is { } cwd ? Path.GetFullPath(cwd) : Environment.CurrentDirectory,
                        ["profile"] = Opt("profile"),
                        ["name"] = Opt("name"),
                    });

                case "select-workspace":
                    return await CallAsync(Methods.SelectWorkspace, new() { ["workspace"] = Pos(0) ?? Opt("workspace") });

                case "rename-workspace":
                    return await CallAsync(Methods.RenameWorkspace, new()
                    {
                        ["name"] = Pos(0) ?? Opt("name"),
                        ["workspace"] = Opt("workspace"),
                        ["pane"] = currentPane,
                    });

                case "move-workspace":
                    if (Pos(0) is null) return Fail("Usage: aghouse move-workspace <position> [--workspace W]");
                    return await CallAsync(Methods.MoveWorkspace, new()
                    {
                        ["index"] = Pos(0),
                        ["workspace"] = Opt("workspace"),
                        ["pane"] = currentPane,
                    });

                case "close-workspace":
                    return await CallAsync(Methods.CloseWorkspace, new()
                    {
                        ["workspace"] = Pos(0) ?? Opt("workspace"),
                        ["pane"] = currentPane,
                    });

                case "zoom-pane":
                    return await CallAsync(Methods.ZoomPane, new() { ["pane"] = Pos(0) ?? Opt("pane") ?? currentPane });

                case "waiting":
                    return await CallAsync(Methods.Waiting, new(), pretty: true);

                case "new-split":
                    return await CallAsync(Methods.NewSplit, new()
                    {
                        ["direction"] = Pos(0) ?? Opt("direction") ?? "right",
                        ["pane"] = Opt("pane") ?? currentPane,
                        ["profile"] = Opt("profile"),
                        ["cwd"] = Opt("cwd") is { } splitCwd ? Path.GetFullPath(splitCwd) : null,
                    });

                case "new-tab":
                    return await CallAsync(Methods.NewTab, new()
                    {
                        ["pane"] = Opt("pane") ?? currentPane,
                        ["profile"] = Opt("profile"),
                        ["cwd"] = Opt("cwd") is { } tabCwd ? Path.GetFullPath(tabCwd) : null,
                    });

                case "focus-pane" or "select-tab":
                    return await CallAsync(Methods.FocusPane, new() { ["pane"] = Pos(0) ?? Opt("pane") });

                case "close-pane" or "close-tab":
                    return await CallAsync(Methods.ClosePane, new() { ["pane"] = Pos(0) ?? Opt("pane") ?? currentPane });

                case "send-text":
                {
                    var text = Pos(0) ?? Opt("text") ?? (Console.IsInputRedirected ? Console.In.ReadToEnd() : null);
                    if (text is null) return Fail("Usage: aghouse send-text <text> [--pane ID] [--enter]");
                    if (options.ContainsKey("enter")) text += "\r";
                    return await CallAsync(Methods.SendText, new() { ["pane"] = Opt("pane") ?? currentPane, ["text"] = text });
                }

                case "notify":
                    return await CallAsync(Methods.Notify, new()
                    {
                        ["title"] = Opt("title"),
                        ["body"] = Opt("body") ?? (positional.Count > 0 ? string.Join(' ', positional) : null),
                        ["pane"] = Opt("pane") ?? currentPane,
                    });

                case "set-status":
                    if (Pos(0) is null || positional.Count < 2) return Fail("Usage: aghouse set-status <key> <text>");
                    return await CallAsync(Methods.SetStatus, new()
                    {
                        ["key"] = Pos(0),
                        ["text"] = string.Join(' ', positional.Skip(1)),
                        ["workspace"] = Opt("workspace"),
                        ["pane"] = Opt("pane") ?? currentPane,
                    });

                case "clear-status":
                    if (Pos(0) is null) return Fail("Usage: aghouse clear-status <key>");
                    return await CallAsync(Methods.ClearStatus, new()
                    {
                        ["key"] = Pos(0),
                        ["workspace"] = Opt("workspace"),
                        ["pane"] = Opt("pane") ?? currentPane,
                    });

                case "set-progress":
                    if (Pos(0) is null) return Fail("Usage: aghouse set-progress <0..1|40%|clear> [--label L]");
                    return await CallAsync(Methods.SetProgress, new()
                    {
                        ["value"] = Pos(0),
                        ["label"] = Opt("label"),
                        ["workspace"] = Opt("workspace"),
                        ["pane"] = Opt("pane") ?? currentPane,
                    });

                case "log":
                    if (positional.Count == 0) return Fail("Usage: aghouse log <text> [--level info|warn|error]");
                    return await CallAsync(Methods.Log, new()
                    {
                        ["text"] = string.Join(' ', positional),
                        ["level"] = Opt("level"),
                        ["workspace"] = Opt("workspace"),
                        ["pane"] = Opt("pane") ?? currentPane,
                    });

                default:
                    return Fail($"Unknown command '{command}'. Run 'aghouse --help'.");
            }
        }
        catch (TimeoutException)
        {
            return Fail("aghouse is not running.", code: 2);
        }
        catch (IOException ex)
        {
            return Fail($"Could not talk to aghouse: {ex.Message}", code: 2);
        }
    }

    private static async Task<int> CallAsync(string method, Dictionary<string, string?> parameters, bool pretty = false)
    {
        await using var client = await RpcClient.ConnectAsync();
        var response = await client.CallAsync(method, parameters);
        if (!response.Ok) return Fail(response.Error ?? "Request failed.");
        if (response.Result is not null)
        {
            Console.WriteLine(response.Result.ToJsonString(new JsonSerializerOptions { WriteIndented = pretty }));
        }
        return 0;
    }

    /// <summary>
    /// Claude Code hook: forwards Notification/Stop events as an aghouse notification for this pane.
    /// Never fails the hook: outside aghouse (or if the app is closed) it exits 0 silently.
    /// </summary>
    private static async Task<int> ClaudeHookAsync(string? pane)
    {
        string? eventName = null, message = null;
        if (Console.IsInputRedirected)
        {
            try
            {
                // Windows PowerShell pipes with a UTF-8 BOM when $OutputEncoding is UTF-8.
                var input = (await Console.In.ReadToEndAsync()).TrimStart('﻿').Trim();
                var json = JsonNode.Parse(input);
                eventName = json?["hook_event_name"]?.GetValue<string>();
                message = json?["message"]?.GetValue<string>();
            }
            catch (JsonException) { }
        }
        if (pane is null) return 0;

        // The agent is busy: a prompt went in, or a tool just ran (including one the user approved).
        // It shows as a "Working" chip on the sidebar row until Claude stops or asks for you.
        if (eventName is "UserPromptSubmit" or "PostToolUse")
        {
            try
            {
                await using var client = await RpcClient.ConnectAsync(timeoutMs: 1000);
                await client.CallAsync(Methods.SetStatus, new() { ["key"] = "agent", ["text"] = "Working", ["pane"] = pane });
            }
            catch (Exception ex) when (ex is TimeoutException or IOException) { }
            return 0;
        }

        var body = eventName switch
        {
            "Stop" => "Finished responding",
            "SubagentStop" => null, // too chatty
            "Notification" => message ?? "", // no text: the pane says "Needs you · Claude Code", not a stock phrase
            _ => message,
        };
        if (body is null) return 0;

        try
        {
            await using var client = await RpcClient.ConnectAsync(timeoutMs: 1000);
            await client.CallAsync(Methods.Notify, new() { ["title"] = "Claude Code", ["body"] = body, ["pane"] = pane });
        }
        catch (Exception ex) when (ex is TimeoutException or IOException) { }
        return 0;
    }

    /// <summary>Adds aghouse's hooks to ~/.claude/settings.json after saying what will change and asking.</summary>
    private static int InstallClaudeHooks(bool yes)
    {
        var path = AgHouse.Core.ClaudeHooks.DefaultPath;
        var status = AgHouse.Core.ClaudeHooks.Inspect(path);
        switch (status.State)
        {
            case AgHouse.Core.HookState.Connected:
                Console.WriteLine($"Claude Code is already connected ({path}).");
                return 0;
            case AgHouse.Core.HookState.Unreadable:
                return Fail($"{path} isn't valid JSON, so aghouse won't change it: {status.Problem}");
        }

        Console.WriteLine($"aghouse will add \"{AgHouse.Core.ClaudeHooks.Command}\" to these Claude Code hooks in {path}:");
        foreach (var e in status.Missing) Console.WriteLine($"  {e}");
        Console.WriteLine("Your other settings and hooks stay as they are. The file is copied to settings.json.aghouse-backup first.");
        if (!yes)
        {
            if (Console.IsInputRedirected) return Fail("Add --yes to make the change without asking.");
            Console.Write("Add them? [y/N] ");
            if (Console.ReadLine()?.Trim().ToLowerInvariant() is not ("y" or "yes")) { Console.WriteLine("Nothing changed."); return 1; }
        }
        try
        {
            var (added, backup) = AgHouse.Core.ClaudeHooks.Install(path);
            Console.WriteLine($"Connected: {string.Join(", ", added)}." + (backup is null ? "" : $" Backup: {backup}"));
            Console.WriteLine("Claude Code sessions started from now on will report to aghouse.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            return Fail($"Couldn't change {path}: {ex.Message}");
        }
    }

    private static int ClaudeHooksStatus()
    {
        var status = AgHouse.Core.ClaudeHooks.Inspect();
        Console.WriteLine(status.State switch
        {
            AgHouse.Core.HookState.Connected => "connected",
            AgHouse.Core.HookState.Partial => $"partial (missing: {string.Join(", ", status.Missing)})",
            AgHouse.Core.HookState.NotConnected => "not connected",
            _ => $"unreadable: {status.Problem}",
        });
        return status.State == AgHouse.Core.HookState.Connected ? 0 : 1;
    }

    private const string ClaudeHooksSnippet = """
        Add this to ~/.claude/settings.json (merge with any existing "hooks"):

        {
          "hooks": {
            "Notification": [
              { "hooks": [ { "type": "command", "command": "aghouse claude-hook" } ] }
            ],
            "Stop": [
              { "hooks": [ { "type": "command", "command": "aghouse claude-hook" } ] }
            ],
            "UserPromptSubmit": [
              { "hooks": [ { "type": "command", "command": "aghouse claude-hook" } ] }
            ],
            "PostToolUse": [
              { "hooks": [ { "type": "command", "command": "aghouse claude-hook" } ] }
            ]
          }
        }

        Notification and Stop mark the pane "Needs you". UserPromptSubmit and PostToolUse
        show "Working" on the workspace's sidebar row while Claude is busy.
        The hook is a no-op outside aghouse panes.
        """;

    private static (List<string> Positional, Dictionary<string, string> Options) Parse(string[] args)
    {
        var positional = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal) && args[i].Length > 2)
            {
                var key = args[i][2..];
                var eq = key.IndexOf('=');
                if (eq > 0) options[key[..eq]] = key[(eq + 1)..];
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) options[key] = args[++i];
                else options[key] = "";
            }
            else positional.Add(args[i]);
        }
        return (positional, options);
    }

    private static int Fail(string message, int code = 1)
    {
        Console.Error.WriteLine($"aghouse: {message}");
        return code;
    }
}
