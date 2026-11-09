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
          new-split [right|down|left|up] [--pane ID] [--profile ID] [--cwd DIR]
          focus-pane <id>
          close-pane [id]
          send-text <text> [--pane ID] [--enter]
          notify [--title T] [--body B] [--pane ID]
          claude-hook                           For Claude Code hooks: reads hook JSON on stdin
          hooks print claude                    Print a Claude Code settings.json snippet
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
                    return Fail("Usage: aghouse hooks print claude");

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

                case "new-split":
                    return await CallAsync(Methods.NewSplit, new()
                    {
                        ["direction"] = Pos(0) ?? Opt("direction") ?? "right",
                        ["pane"] = Opt("pane") ?? currentPane,
                        ["profile"] = Opt("profile"),
                        ["cwd"] = Opt("cwd") is { } splitCwd ? Path.GetFullPath(splitCwd) : null,
                    });

                case "focus-pane":
                    return await CallAsync(Methods.FocusPane, new() { ["pane"] = Pos(0) ?? Opt("pane") });

                case "close-pane":
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

        var body = eventName switch
        {
            "Stop" => "Finished responding",
            "SubagentStop" => null, // too chatty
            "Notification" => message ?? "Needs your attention",
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

    private const string ClaudeHooksSnippet = """
        Add this to ~/.claude/settings.json (merge with any existing "hooks"):

        {
          "hooks": {
            "Notification": [
              { "hooks": [ { "type": "command", "command": "aghouse claude-hook" } ] }
            ],
            "Stop": [
              { "hooks": [ { "type": "command", "command": "aghouse claude-hook" } ] }
            ]
          }
        }

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
