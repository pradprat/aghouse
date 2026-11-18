using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AgHouse.Core;
using AgHouse.Core.Ipc;

namespace AgHouse.Cli;

/// <summary>One CLI command: how it's spelled, what it does, and the code that does it.</summary>
internal sealed record Command(string Name, string Group, string Synopsis, string Summary, Func<Ctx, Task<int>> Run)
{
    public string[] Aliases { get; init; } = [];
    public string? Example { get; init; }
    public string? Details { get; init; }
}

/// <summary>The parsed command line for one command.</summary>
internal sealed class Ctx(List<string> positional, Dictionary<string, string> options, string? currentPane)
{
    public List<string> Positional { get; } = positional;
    public string? CurrentPane { get; } = currentPane;
    public string? Pos(int i) => i < Positional.Count ? Positional[i] : null;
    public string? Opt(string key) => options.TryGetValue(key, out var v) ? v : null;
    public bool Has(string key) => options.ContainsKey(key);
    public string Rest(int from = 0) => string.Join(' ', Positional.Skip(from));

    /// <summary>--pane, else the pane this runs in ($AGHOUSE_PANE_ID), else null (the app uses the focused pane).</summary>
    public string? Pane => Opt("pane") ?? CurrentPane;
}

internal static class Program
{
    private const int ExitOk = 0, ExitError = 1, ExitNotRunning = 2, ExitTimeout = 3;

    private static readonly string[] Groups =
    [
        "Workspaces", "Panes and tabs", "Shell input and output", "Sidebar", "Notifications", "Settings", "App", "Integrations",
    ];

    private static readonly Command[] Commands =
    [
        // ───────────── Workspaces ─────────────
        new("list", "Workspaces", "list", "Workspaces and their panes and tabs (JSON)",
            _ => CallAsync(Methods.List, new(), pretty: true)),

        new("new-workspace", "Workspaces", "new-workspace [--cwd DIR] [--profile ID] [--name NAME]", "Open a workspace (default: this folder)",
            c => CallAsync(Methods.NewWorkspace, new()
            {
                ["cwd"] = c.Opt("cwd") is { } cwd ? Path.GetFullPath(cwd) : Environment.CurrentDirectory,
                ["profile"] = c.Opt("profile"),
                ["name"] = c.Opt("name"),
            })) { Example = "aghouse new-workspace --cwd C:\\src\\api --profile pwsh --name api" },

        new("select-workspace", "Workspaces", "select-workspace <id|name|index>", "Show a workspace",
            c => CallAsync(Methods.SelectWorkspace, new() { ["workspace"] = c.Pos(0) ?? c.Opt("workspace") })),

        new("rename-workspace", "Workspaces", "rename-workspace <name> [--workspace W]", "Rename this workspace (or W)",
            c => CallAsync(Methods.RenameWorkspace, new()
            {
                ["name"] = c.Positional.Count > 0 ? c.Rest() : c.Opt("name"),
                ["workspace"] = c.Opt("workspace"),
                ["pane"] = c.CurrentPane,
            })),

        new("move-workspace", "Workspaces", "move-workspace <position> [--workspace W]", "Reorder the sidebar; 1 is the top",
            c => c.Pos(0) is null
                ? Task.FromResult(Fail("Usage: aghouse move-workspace <position> [--workspace W]"))
                : CallAsync(Methods.MoveWorkspace, new() { ["index"] = c.Pos(0), ["workspace"] = c.Opt("workspace"), ["pane"] = c.CurrentPane })),

        new("close-workspace", "Workspaces", "close-workspace [W]", "Close a workspace; ends what runs there without asking",
            c => CallAsync(Methods.CloseWorkspace, new() { ["workspace"] = c.Pos(0) ?? c.Opt("workspace"), ["pane"] = c.CurrentPane })),

        // ───────────── Panes and tabs ─────────────
        new("new-split", "Panes and tabs", "new-split [right|down|left|up] [--pane ID] [--profile ID] [--cwd DIR]",
            "Split a pane; prints the new pane's id",
            c => CallAsync(Methods.NewSplit, new()
            {
                ["direction"] = c.Pos(0) ?? c.Opt("direction") ?? "right",
                ["pane"] = c.Pane,
                ["profile"] = c.Opt("profile"),
                ["cwd"] = c.Opt("cwd") is { } cwd ? Path.GetFullPath(cwd) : null,
            })) { Example = "aghouse new-split down --profile wsl-Ubuntu" },

        new("new-tab", "Panes and tabs", "new-tab [--pane ID] [--profile ID] [--cwd DIR]", "A tab in the same pane, right after it",
            c => CallAsync(Methods.NewTab, new()
            {
                ["pane"] = c.Pane,
                ["profile"] = c.Opt("profile"),
                ["cwd"] = c.Opt("cwd") is { } cwd ? Path.GetFullPath(cwd) : null,
            })),

        new("focus-pane", "Panes and tabs", "focus-pane <id>", "Show a pane or tab and focus it",
            c => CallAsync(Methods.FocusPane, new() { ["pane"] = c.Pos(0) ?? c.Opt("pane") })) { Aliases = ["select-tab"] },

        new("close-pane", "Panes and tabs", "close-pane [id]", "Close a tab; a pane goes with its last tab. Never asks",
            c => CallAsync(Methods.ClosePane, new() { ["pane"] = c.Pos(0) ?? c.Pane })) { Aliases = ["close-tab"] },

        new("zoom-pane", "Panes and tabs", "zoom-pane [id]", "Fill the workspace with one pane, or show all again",
            c => CallAsync(Methods.ZoomPane, new() { ["pane"] = c.Pos(0) ?? c.Pane })),

        new("move-pane", "Panes and tabs", "move-pane <left|right|up|down|swap> --to ID [--pane ID]",
            "Move a pane to one side of another pane, or swap the two (like dragging its header)",
            c => c.Opt("to") is null
                ? Task.FromResult(Fail("Usage: aghouse move-pane <left|right|up|down|swap> --to ID [--pane ID]"))
                : CallAsync(Methods.MovePane, new()
                {
                    ["side"] = c.Pos(0) ?? c.Opt("side"),
                    ["pane"] = c.Pane,
                    ["to"] = c.Opt("to"),
                }))
            {
                Example = "aghouse move-pane left --to p3",
                Details = "Ids can be any tab of a pane: the whole pane moves, with all its tabs. 'left' puts this pane on the left of the --to pane.",
            },

        new("rename-pane", "Panes and tabs", "rename-pane <name> [--pane ID] | --clear", "Name a pane or tab; --clear goes back to the automatic name",
            c => c.Positional.Count == 0 && !c.Has("clear")
                ? Task.FromResult(Fail("Usage: aghouse rename-pane <name> [--pane ID], or --clear"))
                : CallAsync(Methods.RenamePane, new() { ["pane"] = c.Pane, ["name"] = c.Has("clear") ? "" : c.Rest() }))
            { Aliases = ["rename-tab"] },

        new("restart-pane", "Panes and tabs", "restart-pane [id] [--profile ID] [--cwd DIR]",
            "End the shell and start a fresh one in its place (another shell with --profile). Prints the new pane id",
            c => CallAsync(Methods.RestartPane, new()
            {
                ["pane"] = c.Pos(0) ?? c.Pane,
                ["profile"] = c.Opt("profile"),
                ["cwd"] = c.Opt("cwd") is { } cwd ? Path.GetFullPath(cwd) : null,
            }))
            {
                Aliases = ["restart-tab"],
                Example = "aghouse restart-pane --profile cmd",
                Details = "The new terminal has a new pane id. Restarting the pane this runs in ends this command's own shell.",
            },

        new("profiles", "Panes and tabs", "profiles [--json]", "Shells you can open (ids for --profile)", ProfilesAsync),

        // ───────────── Shell input and output ─────────────
        new("send-text", "Shell input and output", "send-text <text> [--pane ID] [--enter] [--wait-for REGEX [--timeout SECONDS]]",
            "Type text into a pane (stdin if no text); with --wait-for, wait for output matching REGEX", SendTextAsync)
            {
                Example = "aghouse send-text \"npm test\" --enter --pane p3 --wait-for \"passing|failing\" --timeout 300",
                Details = "--wait-for watches only what comes after the text went in, ignores the shell's echo of the command, " +
                          "prints the matching line and exits 3 on timeout (default 30 s, 0 = no limit). --json prints the " +
                          "pane id and the cursor from just before the text went in (for read-pane --since).",
            },

        new("send-keys", "Shell input and output", "send-keys <key>... [--pane ID]", "Press keys: ctrl+c, enter, esc, tab, up, f5, alt+x, y ...",
            c => c.Positional.Count == 0
                ? Task.FromResult(Fail("Usage: aghouse send-keys <key>... [--pane ID]  (e.g. ctrl+c, or up enter)"))
                : CallAsync(Methods.SendKeys, new() { ["pane"] = c.Pane, ["keys"] = c.Rest() }))
            {
                Example = "aghouse send-keys ctrl+c --pane p3",
                Details = "Keys: a single character, ctrl+<letter>, alt+<key>, enter, tab, shift+tab, esc, backspace, space, " +
                          "up, down, left, right, home, end, insert, delete, pgup, pgdn, f1-f12. Cursor, editing and function " +
                          "keys also take ctrl+, alt+ and shift+ (ctrl+left).",
            },

        new("read-pane", "Shell input and output", "read-pane [--pane ID] [--lines N] [--since CURSOR] [--json]",
            "What a pane printed: the last N lines (50), or everything after a cursor", ReadPaneAsync)
            {
                Example = "aghouse read-pane --pane p3 --lines 20",
                Details = "Plain text with colours and cursor moves removed; the line still being written (a prompt) comes last. " +
                          "--json adds 'next', a cursor to pass to --since to read only what came after, and 'partial', the " +
                          "unfinished line. Full-screen programs that redraw (vim, Claude Code) read back as fragments.",
            },

        new("wait-for", "Shell input and output", "wait-for <regex> [--pane ID] [--timeout SECONDS] [--since CURSOR] [--ignore-case]",
            "Wait until a pane prints a line matching regex; prints it. Exit 3 on timeout (default 30 s, 0 = no limit)", WaitForAsync)
            {
                Example = "$c = (aghouse read-pane --lines 0 --json | ConvertFrom-Json).next; aghouse send-text \"npm test\" --enter; aghouse wait-for \"passing|failing\" --since $c --timeout 300",
                Details = "To run a command and wait in one step, use send-text --wait-for. " +
                          "Only output after the cursor counts (default: from when wait-for starts). Take the cursor before " +
                          "sending a command so fast output isn't missed. The command you typed is echoed too, so match what " +
                          "only the output prints, or anchor the pattern ('^done$').",
            },

        // ───────────── Sidebar ─────────────
        new("set-status", "Sidebar", "set-status <key> <text> [--workspace W]", "A chip on the workspace's sidebar row",
            c => c.Positional.Count < 2
                ? Task.FromResult(Fail("Usage: aghouse set-status <key> <text>"))
                : CallAsync(Methods.SetStatus, new() { ["key"] = c.Pos(0), ["text"] = c.Rest(1), ["workspace"] = c.Opt("workspace"), ["pane"] = c.Pane }))
            { Example = "aghouse set-status tests \"Running tests\"" },

        new("clear-status", "Sidebar", "clear-status <key> [--workspace W]", "Remove a chip",
            c => c.Pos(0) is null
                ? Task.FromResult(Fail("Usage: aghouse clear-status <key>"))
                : CallAsync(Methods.ClearStatus, new() { ["key"] = c.Pos(0), ["workspace"] = c.Opt("workspace"), ["pane"] = c.Pane })),

        new("set-progress", "Sidebar", "set-progress <0..1|40%|clear> [--label L] [--workspace W]", "A progress line on the row",
            c => c.Pos(0) is null
                ? Task.FromResult(Fail("Usage: aghouse set-progress <0..1|40%|clear> [--label L]"))
                : CallAsync(Methods.SetProgress, new() { ["value"] = c.Pos(0), ["label"] = c.Opt("label"), ["workspace"] = c.Opt("workspace"), ["pane"] = c.Pane })),

        new("log", "Sidebar", "log <text> [--level info|warn|error] [--workspace W]", "A log line on the row",
            c => c.Positional.Count == 0
                ? Task.FromResult(Fail("Usage: aghouse log <text> [--level info|warn|error]"))
                : CallAsync(Methods.Log, new() { ["text"] = c.Rest(), ["level"] = c.Opt("level"), ["workspace"] = c.Opt("workspace"), ["pane"] = c.Pane })),

        // ───────────── Notifications ─────────────
        new("notify", "Notifications", "notify [--title T] [--body B] [--pane ID] [--finished]",
            "Mark a pane \"Needs you\"; a toast too if aghouse isn't in front. --finished: the work is done, so it waits behind panes that are asking something",
            c => CallAsync(Methods.Notify, new()
            {
                ["title"] = c.Opt("title"),
                ["body"] = c.Opt("body") ?? (c.Positional.Count > 0 ? c.Rest() : null),
                ["pane"] = c.Pane,
                ["finished"] = c.Has("finished") ? "true" : null,
            })) { Example = "aghouse notify --title Build --body \"Done, 2 warnings\" --finished" },

        new("waiting", "Notifications", "waiting", "Panes that need you, then handled requests (JSON)",
            _ => CallAsync(Methods.Waiting, new(), pretty: true)),

        // ───────────── Settings ─────────────
        new("settings", "Settings", "settings [get [KEY]] | set <KEY> <VALUE> | reset <KEY> | path  [--json]",
            "Read or change settings.json; changes apply at once", SettingsAsync)
            {
                Example = "aghouse settings set theme light",
                Details = "Keys: defaultProfile, fontFamily, fontSize, theme, darkTheme, lightTheme, restoreSession, toasts, flashTaskbar, " +
                          "ctrlDSplits, groupWorkspaces, warnMultilinePaste, updates " +
                          "(font-size and other spellings work). 'settings get KEY' prints just the value.",
            },

        new("themes", "Settings", "themes [list] | import  [--json]",
            "List colour themes, or import Windows Terminal's schemes", ThemesAsync)
            {
                Example = "aghouse settings set darkTheme \"Tokyo Night\"",
                Details = "theme picks the mode (system, dark, light); darkTheme and lightTheme pick what each mode shows. " +
                          "Windows keeps Mica and your accent. Add your own schemes to the \"schemes\" array in settings.json, " +
                          "in Windows Terminal's format, with an optional \"accent\". 'themes import' copies Windows Terminal's.",
            },

        // ───────────── App ─────────────
        new("actions", "App", "actions [--json]", "Every palette and shortcut action 'run' can start", ActionsAsync),

        new("run", "App", "run <action> [--pane ID]", "Do an action as if its shortcut was pressed (focuses --pane first)",
            c => c.Pos(0) is null
                ? Task.FromResult(Fail("Usage: aghouse run <action> [--pane ID]. 'aghouse actions' lists them."))
                : CallAsync(Methods.RunAction, new() { ["action"] = c.Pos(0), ["pane"] = c.Opt("pane") }))
            {
                Example = "aghouse run command-palette",
                Details = "Actions that close something running ask first, as their shortcuts do.",
            },

        new("activate", "App", "activate", "Bring the aghouse window to the front",
            _ => CallAsync(Methods.Activate, new())),

        new("quit", "App", "quit [--force]", "Close aghouse; asks first if programs are running, unless --force",
            c => CallAsync(Methods.Quit, new() { ["force"] = c.Has("force") ? "true" : null })),

        new("update", "App", "update [--json]", "Is there a newer aghouse? Asks GitHub Releases (aghouse needn't be running)", UpdateAsync)
            {
                Example = "aghouse update",
                Details = "Only checks. An installed aghouse installs new versions itself when it quits (setting 'updates': auto), " +
                          "or now with 'aghouse run restart-to-update', which asks first if programs are running. Exit 1 if GitHub can't be reached.",
            },

        new("ping", "App", "ping", "Is aghouse running? Prints its version",
            _ => CallAsync(Methods.Ping, new(), pretty: true)),

        new("commands", "App", "commands [--json]", "Every command, for scripts and agents", CommandsAsync),

        new("help", "App", "help [command]", "This help, or one command's", c => Task.FromResult(Help(c.Pos(0)))),

        // ───────────── Integrations ─────────────
        new("agent-guide", "Integrations", "agent-guide", "How to use aghouse, as Markdown for AGENTS.md or CLAUDE.md",
            _ => Task.FromResult(Print(AgentGuide.Markdown())))
            {
                Example = "aghouse agent-guide >> AGENTS.md",
                Details = "Claude Code gets the same guidance by itself when a session starts in a pane, once it's connected " +
                          "(aghouse hooks install claude). Other agents (Codex, Gemini CLI, Cursor ...) read it from AGENTS.md.",
            },

        new("claude-hook", "Integrations", "claude-hook", "For Claude Code hooks: reads hook JSON on stdin",
            c => ClaudeHookAsync(c.CurrentPane)),

        new("hooks", "Integrations", "hooks install|status|print claude [--yes]",
            "Connect Claude Code (~/.claude/settings.json; install asks first)",
            c => Task.FromResult((c.Pos(0), c.Pos(1)) switch
            {
                ("print", "claude") => Print(ClaudeHooksSnippet.Replace("\"aghouse claude-hook\"", JsonSerializer.Serialize(AgHouse.Core.ClaudeHooks.Command, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))),
                ("install", "claude") => InstallClaudeHooks(c.Has("yes")),
                ("status", "claude") => ClaudeHooksStatus(),
                _ => Fail("Usage: aghouse hooks print|install|status claude"),
            })),
    ];

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help") return Help(null);

        var command = Find(args[0]);
        if (command is null) return Fail($"Unknown command '{args[0]}'. Run 'aghouse help'.");

        var (positional, options) = Parse(args[1..]);
        if (options.ContainsKey("help") || options.ContainsKey("h")) return Help(command.Name);
        var ctx = new Ctx(positional, options, Environment.GetEnvironmentVariable(AgEnv.PaneId));

        try
        {
            return await command.Run(ctx);
        }
        catch (TimeoutException)
        {
            return Fail("aghouse is not running.", ExitNotRunning);
        }
        catch (IOException ex)
        {
            return Fail($"Could not talk to aghouse: {ex.Message}", ExitNotRunning);
        }
    }

    /// <summary>For tests: the guide must only name commands that exist.</summary>
    internal static bool IsCommand(string name) => Find(name) is not null;

    private static Command? Find(string name) =>
        Commands.FirstOrDefault(c => c.Name == name || c.Aliases.Contains(name));

    // ───────────────────────────── Help ─────────────────────────────

    private static int Help(string? name)
    {
        if (name is not null)
        {
            var c = Find(name);
            if (c is null) return Fail($"Unknown command '{name}'. Run 'aghouse help'.");
            var sb = new StringBuilder();
            sb.AppendLine($"aghouse {c.Synopsis}");
            if (c.Aliases.Length > 0) sb.AppendLine($"Also: {string.Join(", ", c.Aliases)}");
            sb.AppendLine().AppendLine(c.Summary + ".");
            if (c.Details is not null) sb.AppendLine().AppendLine(c.Details);
            if (c.Example is not null) sb.AppendLine().AppendLine("Example:").AppendLine("  " + c.Example);
            Console.Write(sb.ToString());
            return ExitOk;
        }

        var usage = new StringBuilder();
        usage.AppendLine("aghouse: control the aghouse terminal app").AppendLine();
        usage.AppendLine("Usage: aghouse <command> [args] [--option value]");
        const int width = 46;
        foreach (var group in Groups)
        {
            usage.AppendLine().AppendLine(group);
            foreach (var c in Commands.Where(c => c.Group == group))
            {
                var line = "  " + c.Synopsis;
                usage.AppendLine(line.Length < width ? line.PadRight(width) + c.Summary : line + "\n" + new string(' ', width) + c.Summary);
            }
        }
        usage.AppendLine();
        usage.AppendLine("Inside an aghouse pane, --pane defaults to that pane ($AGHOUSE_PANE_ID); elsewhere, to the focused one.");
        usage.AppendLine("'aghouse help <command>' explains one command; 'aghouse commands --json' lists them all for scripts.");
        usage.AppendLine("Exit codes: 0 ok, 1 error, 2 aghouse is not running, 3 timed out (wait-for).");
        Console.Write(usage.ToString());
        return ExitOk;
    }

    private static Task<int> CommandsAsync(Ctx c)
    {
        if (!c.Has("json"))
        {
            foreach (var cmd in Commands) Console.WriteLine($"{cmd.Name,-18} {cmd.Summary}");
            return Task.FromResult(ExitOk);
        }
        var list = new JsonArray();
        foreach (var cmd in Commands)
        {
            list.Add(new JsonObject
            {
                ["name"] = cmd.Name,
                ["aliases"] = new JsonArray(cmd.Aliases.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
                ["group"] = cmd.Group,
                ["usage"] = "aghouse " + cmd.Synopsis,
                ["summary"] = cmd.Summary,
                ["details"] = cmd.Details,
                ["example"] = cmd.Example,
            });
        }
        var root = new JsonObject
        {
            ["commands"] = list,
            ["paneDefault"] = "Inside a pane, --pane defaults to $AGHOUSE_PANE_ID.",
            ["exitCodes"] = new JsonObject { ["0"] = "ok", ["1"] = "error", ["2"] = "aghouse is not running", ["3"] = "timed out" },
        };
        Console.WriteLine(root.ToJsonString(Pretty));
        return Task.FromResult(ExitOk);
    }

    // ───────────────────────────── Shell output ─────────────────────────────

    private static async Task<int> ReadPaneAsync(Ctx c)
    {
        await using var client = await RpcClient.ConnectAsync();
        var response = await client.CallAsync(Methods.ReadPane, new()
        {
            ["pane"] = c.Pos(0) ?? c.Pane,
            ["lines"] = c.Opt("lines"),
            ["since"] = c.Opt("since"),
        });
        if (!response.Ok) return Fail(response.Error ?? "Request failed.");
        var result = response.Result!;
        if (c.Has("json"))
        {
            Console.WriteLine(result.ToJsonString(Pretty));
            return ExitOk;
        }
        if (result["truncated"]?.GetValue<bool>() == true)
            Console.Error.WriteLine("aghouse: some lines after that cursor were already dropped (a pane keeps its last 10,000).");
        foreach (var line in result["lines"]!.AsArray()) Console.WriteLine(line?.GetValue<string>());
        if (result["partial"]?.GetValue<string>() is { } partial) Console.WriteLine(partial);
        return ExitOk;
    }

    /// <summary>The regex and --timeout of wait-for and send-text --wait-for, or a usage error.</summary>
    private static (Regex? Regex, double Timeout, int? Error) WaitOptions(Ctx c, string pattern)
    {
        Regex regex;
        try
        {
            regex = new Regex(pattern, c.Has("ignore-case") ? RegexOptions.IgnoreCase : RegexOptions.None);
        }
        catch (ArgumentException ex)
        {
            return (null, 0, Fail($"'{pattern}' isn't a valid regex: {ex.Message}"));
        }
        var timeout = 30.0;
        if (c.Opt("timeout") is { Length: > 0 } t
            && !double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out timeout))
        {
            return (null, 0, Fail($"--timeout takes seconds, not '{t}'."));
        }
        return (regex, timeout, null);
    }

    private static async Task<int> WaitForAsync(Ctx c)
    {
        if (c.Pos(0) is not { } pattern) return Fail("Usage: aghouse wait-for <regex> [--pane ID] [--timeout SECONDS] [--since CURSOR]");
        var (regex, timeout, error) = WaitOptions(c, pattern);
        if (error is { } code) return code;
        await using var client = await RpcClient.ConnectAsync();
        return await WaitAsync(client, c.Pane, c.Opt("since"), regex!, pattern, timeout, typed: []);
    }

    private static async Task<int> SendTextAsync(Ctx c)
    {
        var text = c.Positional.Count > 0 ? c.Rest() : c.Opt("text") ?? (Console.IsInputRedirected ? Console.In.ReadToEnd() : null);
        if (text is null) return Fail("Usage: aghouse send-text <text> [--pane ID] [--enter] [--wait-for REGEX [--timeout SECONDS]]");
        if (c.Has("enter")) text += "\r";

        Regex? regex = null;
        var timeout = 0.0;
        var pattern = c.Opt("wait-for");
        if (pattern is { Length: > 0 })
        {
            int? error;
            (regex, timeout, error) = WaitOptions(c, pattern);
            if (error is { } code) return code;
        }

        await using var client = await RpcClient.ConnectAsync();
        var response = await client.CallAsync(Methods.SendText, new() { ["pane"] = c.Pane, ["text"] = text });
        if (!response.Ok) return Fail(response.Error ?? "Request failed.");
        if (c.Has("json")) Console.WriteLine(response.Result?.ToJsonString(Compact));
        if (regex is null) return ExitOk;

        var typed = text.Split('\r', '\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        return await WaitAsync(client, response.Result?["paneId"]?.GetValue<string>(), response.Result?["cursor"]?.ToString(),
            regex, pattern!, timeout, typed);
    }

    /// <summary>
    /// Polls the pane's output from <paramref name="cursor"/> (or from now) until a line matches.
    /// Lines that are only the echo of what was just <paramref name="typed"/> don't count.
    /// </summary>
    private static async Task<int> WaitAsync(RpcClient client, string? pane, string? cursor, Regex regex, string pattern,
        double timeout, IReadOnlyList<string> typed)
    {
        bool Matches(string line) => regex.IsMatch(line) && !IsEcho(line, typed);
        var clock = Stopwatch.StartNew();
        // The unfinished line (a prompt) counts only once something new has happened: otherwise the
        // prompt that was already there would match a pattern meant for the next one.
        var first = true;
        var progressed = false;
        string? firstPartial = null;
        while (true)
        {
            var response = await client.CallAsync(Methods.ReadPane, new()
            {
                ["pane"] = pane,
                ["since"] = cursor,
                ["lines"] = cursor is null ? "0" : null, // the first read only takes the cursor: earlier output doesn't count
            });
            if (!response.Ok) return Fail(response.Error ?? "Request failed.");
            var result = response.Result!;
            pane ??= result["paneId"]?.GetValue<string>(); // keep watching this pane even if focus moves

            foreach (var line in result["lines"]!.AsArray())
            {
                if (line?.GetValue<string>() is { } text && Matches(text))
                {
                    Console.WriteLine(text);
                    return ExitOk;
                }
            }
            var partial = result["partial"]?.GetValue<string>();
            if (first) firstPartial = partial;
            first = false;
            progressed |= result["lines"]!.AsArray().Count > 0;
            if (partial is not null && (progressed || partial != firstPartial) && Matches(partial))
            {
                Console.WriteLine(partial);
                return ExitOk;
            }
            cursor = result["next"]!.ToString();

            if (timeout > 0 && clock.Elapsed.TotalSeconds >= timeout)
                return Fail($"Timed out after {timeout:0.#} s waiting for /{pattern}/ in pane {pane}.", ExitTimeout);
            await Task.Delay(250);
        }
    }

    /// <summary>
    /// The shell echoing a command that was typed: the whole command, or (as ConPTY repaints the line
    /// while it's typed) the prompt followed by the first part of it.
    /// </summary>
    internal static bool IsEcho(string line, IReadOnlyList<string> typed)
    {
        var trimmed = line.TrimEnd();
        foreach (var command in typed)
        {
            if (trimmed.Contains(command, StringComparison.Ordinal)) return true;
            for (var n = command.Length - 1; n >= 4; n--)
            {
                if (trimmed.EndsWith(command[..n], StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }

    // ───────────────────────────── Settings, profiles, actions ─────────────────────────────

    private static async Task<int> SettingsAsync(Ctx c)
    {
        var verb = c.Pos(0) ?? "get";
        switch (verb)
        {
            case "path":
                Console.WriteLine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "aghouse", "settings.json"));
                return ExitOk;

            case "get":
            {
                var key = c.Pos(1);
                var result = await RequestAsync(Methods.GetSettings, new() { ["key"] = key });
                if (result is null) return ExitError;
                if (c.Has("json")) { Console.WriteLine(result.ToJsonString(Pretty)); return ExitOk; }
                if (key is not null) { Console.WriteLine(Plain(result["value"])); return ExitOk; }

                var rows = result["settings"]!.AsArray().Select(s => s!).ToList();
                var keyWidth = rows.Max(r => r["key"]!.GetValue<string>().Length) + 2;
                var valueWidth = Math.Min(24, rows.Max(r => Plain(r["value"]).Length) + 2);
                foreach (var r in rows)
                {
                    Console.WriteLine($"{r["key"]!.GetValue<string>().PadRight(keyWidth)}{Plain(r["value"]).PadRight(valueWidth)}{r["values"]}");
                }
                Console.WriteLine();
                Console.WriteLine(result["path"]!.GetValue<string>());
                if (result["problem"]?.GetValue<string>() is { } problem) Console.WriteLine($"Read-only: {problem}");
                return ExitOk;
            }

            case "set":
            {
                if (c.Pos(1) is not { } key || c.Positional.Count < 3) return Fail("Usage: aghouse settings set <key> <value>");
                return PrintChange(await RequestAsync(Methods.SetSetting, new() { ["key"] = key, ["value"] = c.Rest(2) }), c);
            }

            case "reset":
            {
                if (c.Pos(1) is not { } key) return Fail("Usage: aghouse settings reset <key>");
                return PrintChange(await RequestAsync(Methods.ResetSetting, new() { ["key"] = key }), c);
            }

            default:
                return Fail("Usage: aghouse settings [get [key]] | set <key> <value> | reset <key> | path");
        }
    }

    private static int PrintChange(JsonNode? result, Ctx c)
    {
        if (result is null) return ExitError;
        if (c.Has("json")) Console.WriteLine(result.ToJsonString(Pretty));
        else Console.WriteLine($"{result["key"]} = {Plain(result["value"])}");
        if (result["note"]?.GetValue<string>() is { } note) Console.Error.WriteLine($"aghouse: {note}");
        return ExitOk;
    }

    private static async Task<int> ThemesAsync(Ctx c)
    {
        switch (c.Pos(0) ?? "list")
        {
            case "list":
            {
                var result = await RequestAsync(Methods.Themes, new());
                if (result is null) return ExitError;
                if (c.Has("json")) { Console.WriteLine(result.ToJsonString(Pretty)); return ExitOk; }
                var rows = result["themes"]!.AsArray().Select(t => t!).ToList();
                var width = rows.Max(t => t["name"]!.GetValue<string>().Length) + 2;
                foreach (var t in rows)
                {
                    var marks = new List<string>();
                    if (t["darkTheme"]!.GetValue<bool>()) marks.Add("darkTheme");
                    if (t["lightTheme"]!.GetValue<bool>()) marks.Add("lightTheme");
                    if (t["lowContrast"]!.GetValue<bool>()) marks.Add("low contrast");
                    var source = t["source"]!.GetValue<string>() == "custom" ? "custom" : "";
                    Console.WriteLine($"{t["name"]!.GetValue<string>().PadRight(width)}{t["kind"]!.GetValue<string>(),-12}{source,-8}" +
                                      (marks.Count > 0 ? $"({string.Join(", ", marks)})" : ""));
                }
                foreach (var p in result["problems"]!.AsArray()) Console.Error.WriteLine($"aghouse: skipped in settings.json: {p}");
                return ExitOk;
            }

            case "import":
            {
                var result = await RequestAsync(Methods.ImportThemes, new());
                if (result is null) return ExitError;
                if (c.Has("json")) Console.WriteLine(result.ToJsonString(Pretty));
                else Console.WriteLine(result["summary"]!.GetValue<string>());
                if (result["saved"]?.GetValue<bool>() == false)
                    Console.Error.WriteLine("aghouse: imported until aghouse closes, but settings.json couldn't be written. The log has the details.");
                return ExitOk;
            }

            default:
                return Fail("Usage: aghouse themes [list] | import");
        }
    }

    private static async Task<int> ProfilesAsync(Ctx c)
    {
        var result = await RequestAsync(Methods.Profiles, new());
        if (result is null) return ExitError;
        if (c.Has("json")) { Console.WriteLine(result.ToJsonString(Pretty)); return ExitOk; }
        foreach (var p in result["profiles"]!.AsArray())
        {
            var mark = p!["default"]!.GetValue<bool>() ? "  (default)" : "";
            Console.WriteLine($"{p["id"]!.GetValue<string>(),-20} {p["name"]}{mark}");
        }
        return ExitOk;
    }

    private static async Task<int> ActionsAsync(Ctx c)
    {
        var result = await RequestAsync(Methods.Actions, new());
        if (result is null) return ExitError;
        if (c.Has("json")) { Console.WriteLine(result.ToJsonString(Pretty)); return ExitOk; }
        foreach (var a in result["actions"]!.AsArray())
        {
            var gesture = a!["gesture"]?.GetValue<string>() is { } g ? $"  [{g}]" : "";
            Console.WriteLine($"{a["name"]!.GetValue<string>(),-22} {a["description"]}{gesture}");
        }
        return ExitOk;
    }

    /// <summary>A JSON value as plain text: strings without quotes, null as "(unset)".</summary>
    private static string Plain(JsonNode? value) => value switch
    {
        null => "(unset)",
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        _ => value.ToJsonString(),
    };

    // ───────────────────────────── Pipe ─────────────────────────────

    // Paths and prompts stay readable: a prompt ends in ">", not in an escaped character code.
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static async Task<int> CallAsync(string method, Dictionary<string, string?> parameters, bool pretty = false)
    {
        await using var client = await RpcClient.ConnectAsync();
        var response = await client.CallAsync(method, parameters);
        if (!response.Ok) return Fail(response.Error ?? "Request failed.");
        if (response.Result is not null)
        {
            Console.WriteLine(response.Result.ToJsonString(pretty ? Pretty : Compact));
        }
        return ExitOk;
    }

    /// <summary>One call whose result the command formats itself. Null (after saying why) if the app refused.</summary>
    private static async Task<JsonNode?> RequestAsync(string method, Dictionary<string, string?> parameters)
    {
        await using var client = await RpcClient.ConnectAsync();
        var response = await client.CallAsync(method, parameters);
        if (response.Ok) return response.Result ?? new JsonObject();
        Fail(response.Error ?? "Request failed.");
        return null;
    }

    // ───────────────────────────── Claude Code ─────────────────────────────

    /// <summary>
    /// Claude Code hook: forwards Notification/Stop events as an aghouse notification for this pane.
    /// Never fails the hook: outside aghouse (or if the app is closed) it exits 0 silently.
    /// </summary>
    private static async Task<int> ClaudeHookAsync(string? pane)
    {
        string? eventName = null, message = null, notificationType = null;
        if (Console.IsInputRedirected)
        {
            try
            {
                // Windows PowerShell pipes with a UTF-8 BOM when $OutputEncoding is UTF-8.
                var input = (await Console.In.ReadToEndAsync()).TrimStart('﻿').Trim();
                var json = JsonNode.Parse(input);
                eventName = json?["hook_event_name"]?.GetValue<string>();
                message = json?["message"]?.GetValue<string>();
                notificationType = json?["notification_type"]?.GetValue<string>();
            }
            catch (JsonException) { }
        }
        if (pane is null) return 0;

        // A session started (or was resumed, cleared or compacted) in a pane: tell Claude where it is
        // and how to use aghouse. Claude Code adds additionalContext to the conversation.
        if (eventName is "SessionStart")
        {
            var context = new JsonObject
            {
                ["hookSpecificOutput"] = new JsonObject
                {
                    ["hookEventName"] = "SessionStart",
                    ["additionalContext"] = AgentGuide.Primer(await WhereAsync(pane)),
                },
            };
            Console.WriteLine(context.ToJsonString(Compact));
            return 0;
        }

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

        // Done, not asking: Stop, and the reminder Claude sends after sitting idle at its prompt. A permission
        // prompt or a question stays "asking", so it comes first in the waiting list and for Ctrl+Shift+U.
        var finished = AgHouse.Core.ClaudeHooks.IsFinished(eventName, notificationType, message);

        try
        {
            await using var client = await RpcClient.ConnectAsync(timeoutMs: 1000);
            await client.CallAsync(Methods.Notify, new()
            {
                ["title"] = "Claude Code", ["body"] = body, ["pane"] = pane, ["finished"] = finished ? "true" : null,
            });
        }
        catch (Exception ex) when (ex is TimeoutException or IOException) { }
        return 0;
    }

    /// <summary>This pane's name, its workspace and its neighbours, for the primer. Null if aghouse doesn't answer.</summary>
    private static async Task<PaneContext?> WhereAsync(string pane)
    {
        try
        {
            await using var client = await RpcClient.ConnectAsync(timeoutMs: 1000);
            var response = await client.CallAsync(Methods.List);
            foreach (var ws in response.Result?["workspaces"]?.AsArray() ?? [])
            {
                var panes = ws?["panes"]?.AsArray().OfType<JsonObject>().ToList() ?? [];
                if (panes.FirstOrDefault(p => p["id"]?.GetValue<string>() == pane) is not { } me) continue;
                var others = panes.Where(p => !ReferenceEquals(p, me))
                    .Select(p => (p["id"]!.GetValue<string>(), p["name"]?.GetValue<string>() ?? ""))
                    .ToList();
                return new PaneContext(pane, me["name"]?.GetValue<string>(), ws!["name"]?.GetValue<string>(), others);
            }
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or JsonException or InvalidOperationException) { }
        return new PaneContext(pane, null, null, []);
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

        Console.WriteLine(status.OtherCopy is { } other
            ? $"aghouse will change these Claude Code hooks in {path} from \"{other}\" to \"{AgHouse.Core.ClaudeHooks.Command}\":"
            : $"aghouse will add \"{AgHouse.Core.ClaudeHooks.Command}\" to these Claude Code hooks in {path}:");
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
        Console.WriteLine(status switch
        {
            { State: AgHouse.Core.HookState.Connected } => "connected",
            // Exit code stays 1: 'hooks install' would still change them (to this copy).
            { OtherCopy: { } other } => $"connected through another copy of aghouse ({other}); 'aghouse hooks install claude' points them at this one",
            { State: AgHouse.Core.HookState.Partial } => $"partial (missing: {string.Join(", ", status.Missing)})",
            { State: AgHouse.Core.HookState.NotConnected } => "not connected",
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
            ],
            "SessionStart": [
              { "hooks": [ { "type": "command", "command": "aghouse claude-hook" } ] }
            ]
          }
        }

        Notification and Stop mark the pane "Needs you". UserPromptSubmit and PostToolUse
        show "Working" on the workspace's sidebar row while Claude is busy. SessionStart
        tells Claude it's in aghouse and how to use the aghouse CLI (see aghouse agent-guide).
        The hook is a no-op outside aghouse panes.
        """;

    // ───────────────────────────── Plumbing ─────────────────────────────

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
                else if (IsFlag(key)) options[key] = "";
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) options[key] = args[++i];
                else options[key] = "";
            }
            else positional.Add(args[i]);
        }
        return (positional, options);
    }

    /// <summary>Options that never take a value, so "--enter hello" keeps "hello" as an argument.</summary>
    private static bool IsFlag(string key) =>
        key is "enter" or "json" or "force" or "clear" or "yes" or "ignore-case" or "help" or "finished";

    private static async Task<int> UpdateAsync(Ctx c)
    {
        var current = AgHouse.Core.Updates.ReleaseFeed.CurrentVersion(typeof(Program).Assembly);
        AgHouse.Core.Updates.ReleaseInfo? latest;
        try
        {
            using var http = AgHouse.Core.Updates.ReleaseFeed.CreateClient(current);
            latest = await AgHouse.Core.Updates.ReleaseFeed.FetchLatestAsync(http, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Fail($"Couldn't reach GitHub: {ex.Message}");
        }
        var newer = latest is not null && latest.Version > current;
        if (c.Has("json"))
        {
            Console.WriteLine(new JsonObject
            {
                ["current"] = current.ToString(),
                ["latest"] = latest?.Version.ToString(),
                ["newer"] = newer,
                ["page"] = latest?.PageUrl ?? AgHouse.Core.Updates.ReleaseFeed.ReleasesPage,
            }.ToJsonString(Pretty));
            return ExitOk;
        }
        return Print(newer
            ? $"aghouse {latest!.Version} is out (this is {current}): {latest.PageUrl}\n" +
              "In aghouse: Settings › About, or 'aghouse run restart-to-update'."
            : $"aghouse {current} is up to date.");
    }

    private static int Print(string text)
    {
        Console.WriteLine(text);
        return ExitOk;
    }

    private static int Fail(string message, int code = ExitError)
    {
        Console.Error.WriteLine($"aghouse: {message}");
        return code;
    }
}
