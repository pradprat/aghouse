<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/logo-wordmark-dark.svg">
    <img src="assets/logo-wordmark.svg" alt="aghouse" width="420">
  </picture>
</p>

A Windows-native terminal workspace app for running coding agents side by side, in the spirit of cmux.

- **Native UI:** WPF on .NET 10 with the built-in Fluent theme. You get Mica on Windows 11, a system title bar, and Windows toast notifications.
- **Native terminals:** each pane is rendered by Windows Terminal's own DirectX engine over ConPTY, through [EasyWindowsTerminalControl](https://github.com/mitchcapper/EasyWindowsTerminalControl). Panes are not web views.
- **Workspaces, splits and tabs:** workspaces live in a vertical sidebar. Each workspace is a tree of split panes, and each pane can hold several terminals as tabs.
- **A sidebar that tells you what each agent is doing:** **Workspaces** lists your workspaces one line each, with a coloured letter tile (from the theme's own palette, a different hue for each until all six are in use) and the git branch when it isn't main or master, plus "Needs you" and the agent's request while one waits. **Agents** below it lists every coding agent running in a pane (Claude Code, Codex, OpenCode and other known CLIs, or anything that reports in through `aghouse`), under Needs you, Working and Idle, with its task (or, while it waits, its request) and how long it has been that way; click one to go to its pane. Hover a workspace for its folder, pull request (through `gh`), anything a script sets with `aghouse set-status`, its dev-server ports and its last log line; its right-click menu opens the ports. Ctrl+Shift+B folds the sidebar to a rail of tiles.
- **Agent attention:** a pane can ask for attention with an escape code (OSC 9 / 99 / 777, or a bell) or with `aghouse notify`. That pane gets an accent ring and the workspace gets a dot. If the window isn't in front, you also get a toast and the taskbar button flashes.
- **Everything a keystroke away:** a command palette (Ctrl+Shift+P) for every action, workspace and pane, and a waiting list (Ctrl+Shift+I) of the panes that need you, plus requests you already handled.
- **Terminal basics:** find in a pane's output, per-pane text size, and pane zoom.
- **Themes:** pick a dark theme and a light theme, and aghouse follows Windows between them. "Windows" keeps Mica and your accent colour. The 15 built-in schemes (Dracula, Tokyo Night, Gruvbox, Catppuccin, Solarized, Nord and more), your own schemes, and schemes imported from Windows Terminal colour the whole app, terminals included.
- **Settings:** a Settings window where changes apply at once. Settings › Integrations connects Claude Code in one click.
- **Automation:** the `aghouse` CLI talks to the app over a per-user named pipe.

## Build & run

Requires the .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`) and Windows 10 1809+ (Windows 11 for Mica).

```powershell
dotnet build
dotnet run --project src/AgHouse.App
dotnet test
```

The build output folder contains `AgHouseApp.exe` (the GUI) and `aghouse.exe` (the CLI). Every pane gets that folder on its `PATH`, so `aghouse` works inside panes without any setup.

## Release build & installer

```powershell
winget install JRSoftware.InnoSetup   # once; only needed for the installer
powershell -ExecutionPolicy Bypass -File packaging/publish.ps1
```

This writes three things to `artifacts/`. All of them bundle the .NET runtime, so the target PC needs nothing installed.

- `publish/`: the app folder.
- `aghouse-<version>-win-x64.zip`: a portable build. Unzip it anywhere and run `AgHouseApp.exe`. Keep the folder together, because the GUI needs `aghouse.exe`, `conpty.dll` and `ShellIntegration\` beside it.
- `aghouse-<version>-setup-x64.exe`: the installer. By default it installs for the current user only, into `%LOCALAPPDATA%\Programs\aghouse`, with no admin prompt. It adds a Start Menu shortcut and can optionally put `aghouse` on `PATH` and add a desktop icon. Uninstalling removes the program and the PATH entry, but keeps `%APPDATA%\aghouse`.

An installed aghouse updates itself from GitHub Releases: it checks every few hours, downloads the new installer in the background, verifies it against the release's `SHA256SUMS.txt`, and installs it when you quit (or right away with **Restart to update** in Settings › About). Set `"updates": "notify"` to only be told, or `"off"` to never check; `aghouse update` checks from the command line. The portable zip can't update itself and just says when a new version is out. A release must attach `aghouse-<version>-setup-x64.exe` and `SHA256SUMS.txt` (publish.ps1 makes both) for installed copies to pick it up.

Pass `-NoInstaller` or `-NoZip` to skip either one. The version number comes from `Directory.Build.props`. The installer is not code-signed, so on other PCs Windows SmartScreen will show "Windows protected your PC". Click **More info → Run anyway**.

## Keyboard

| Shortcut | Action |
|---|---|
| Ctrl+Shift+N | New workspace |
| Ctrl+Shift+T | New tab in the focused pane |
| Ctrl+PageDown / Ctrl+PageUp (or Ctrl+Shift+] / [) | Next / previous tab |
| Ctrl+D / Alt+Shift+= | Split right |
| Ctrl+Shift+D / Alt+Shift+- / Ctrl+Shift+E | Split down |
| Alt+Arrow | Move focus between panes |
| Ctrl+Shift+W | Close tab (the pane closes with its last tab) |
| Ctrl+1…8, Ctrl+9 | Go to workspace N / last workspace |
| Ctrl+Tab, Ctrl+Shift+Tab | Next / previous workspace |
| Ctrl+Shift+U | Jump to the pane (and tab) at the top of the waiting list: an agent asking something comes before one that finished |
| Ctrl+Shift+I | Waiting list: every pane that needs you, then earlier requests |
| Ctrl+Shift+P | Command palette: any action, workspace or pane (also the search button in the sidebar) |
| Ctrl+Shift+B | Fold the sidebar to a rail of workspace tiles, or open it again |
| Ctrl+Shift+Enter | Zoom the focused pane to fill the workspace (again to show all) |
| Ctrl+Shift+F | Find in the pane's output |
| Ctrl+= / Ctrl+- / Ctrl+0 (or Ctrl+wheel) | Bigger / smaller / reset text in this pane |
| Ctrl+Shift+R (or double-click) | Rename workspace |
| Ctrl+Alt+W | Close workspace |
| Ctrl+, | Settings |
| Ctrl+Shift+C / Ctrl+Shift+V | Copy / paste |
| Ctrl+V | Paste, as in Windows Terminal: text, copied files as their paths, or an image into Claude Code (sent as Alt+V, its image key) |
| Right click | Copy the selection, or paste if nothing is selected |

New workspaces are named after their folder ("aghouse", then "aghouse (2)"); double-click a name to rename it, and drag a row to reorder (or use Move up / Move down in its menu). The window remembers its size, position, sidebar width and whether the sidebar is folded. Every pane has a slim header that names it by what it is running (an agent's current task, `vim`), else its folder, else its shell, and numbers names that would otherwise repeat. When a workspace is split, the header also has a **✕** button that closes the pane. Typing `exit` in a pane also closes it.

**Rearrange panes** by dragging a pane's header onto another pane (the six-dot grip in the middle of the header shows when a pane can move). Drop near an edge to put it on that side, or in the middle to swap the two. A preview shows where it will go, and Esc calls the drag off. The terminals keep running; only their places change. (`aghouse move-pane` does the same from a script.)

**Group by project:** **Group by project** in the **⋯** menu next to "Workspaces" (or `groupWorkspaces` in Settings) groups the sidebar by project (`aghouse run toggle-groups` does the same). Each group is a git repo, with its worktrees, or else a folder. A project with only one workspace shows just its row, with no header; dragging that row moves it between groups. Click a group's header to collapse it. A collapsed group still shows the dot and "2 waiting" when something in it needs you. Ctrl+1..9, Ctrl+Tab and a jump to a waiting pane open the group they land in. A row drags only within its own group, since its folder decides which group it is in. Drag a group's header to move the whole group. A workspace that `cd`s into another repo moves to that repo's group.

A pane with two or more tabs shows them in that same header, with a **+** for another. Click a tab to show it, middle-click to close it, double-click to rename it, or right-click for Duplicate tab (a new tab right after it, in the same folder and shell) and Close other tabs. A pane with one tab has Duplicate tab on its header's right-click menu. A tab that needs you while it's in the background says "Needs you" in the strip, and its pane gets the accent ring. Tabs and their folders are restored with the session.

**Pane zoom** (Ctrl+Shift+Enter) fills the workspace with one pane while the others keep running out of sight. The header says how many are hidden, and if one of them needs you it says so ("Needs you in GitHub (2) · Ctrl+Shift+U"); going to a hidden pane ends the zoom. **Find** (Ctrl+Shift+F) opens in the pane's header and lists the matching lines of its output, newest first; Enter copies the selected line. **Text size** changes for one pane at a time and shows as "110%" in its header.

The footer's "2 waiting" line is a button: it opens the waiting list, which lists every pane that needs you (agents asking something first, then agents that finished, each newest first) and the last 20 requests you already handled. Click a row to go there.

If something is still running in a pane (an agent, a build), **✕** and Ctrl+Shift+W ask before ending it. A pane sitting at its prompt closes immediately, and so does `aghouse close-pane`, which never asks. WSL processes run outside Windows' process list, so WSL panes don't get this warning. Closing the last pane doesn't quit aghouse: a fresh workspace opens in the same folder. Use the window's close button to quit. If programs are still running anywhere, aghouse lists them and asks first, because a restored session brings back layout and folders, not running programs.

## CLI

```text
aghouse list
aghouse new-workspace [--cwd DIR] [--profile pwsh|powershell|cmd|gitbash|wsl-<distro>] [--name NAME]
aghouse select-workspace <id|name|index>
aghouse rename-workspace <name>
aghouse move-workspace <position>               # 1 is the top of the sidebar
aghouse close-workspace [W]                     # never asks, like close-pane
aghouse new-split [right|down|left|up] [--pane ID]
aghouse new-tab [--pane ID] [--profile ID] [--cwd DIR]
aghouse focus-pane <id> | close-pane [id]      # also select-tab / close-tab
aghouse zoom-pane [id]                          # fill the workspace, or show all again
aghouse move-pane <left|right|up|down|swap> --to ID [--pane ID]   # like dragging a pane's header
aghouse rename-pane "build box" | --clear       # also rename-tab
aghouse restart-pane [id] [--profile cmd] [--cwd DIR]   # a fresh shell in its place; prints the new pane id
aghouse profiles                                # shells you can open (ids for --profile)
aghouse waiting                                 # panes that need you, then handled requests (JSON)
aghouse notify --title "Build" --body "Done" [--finished]   # --finished: done, not asking; waits behind panes that ask

# Shell input and output
aghouse send-text "npm test" --enter [--pane ID] [--wait-for "passing|failing" --timeout 300]
aghouse send-keys ctrl+c                        # or: up enter, esc, tab, shift+tab, f5, alt+x, y ...
aghouse read-pane [--lines 50] [--since CURSOR] [--json]
aghouse wait-for "passing|failing" [--timeout 30] [--since CURSOR]   # exit 3 on timeout

# The workspace's sidebar row (--workspace W to target another)
aghouse set-status tests "Running tests"       # a line in the row's tooltip; set the same key again to change it
aghouse clear-status tests
aghouse set-progress 0.4 --label Tests          # or 40%, or clear
aghouse log "Lint found 3 warnings" --level warn

# Settings (apply at once, written to settings.json)
aghouse settings                                # every key, its value and what it takes
aghouse settings get theme | set theme light | reset theme | path
aghouse settings set darkTheme "Tokyo Night"    # what dark mode shows (lightTheme for light)
aghouse themes                                  # every theme; themes import copies Windows Terminal's

# The rest of the app
aghouse actions                                 # every palette/shortcut action
aghouse run split-right [--pane ID]             # do one as if its shortcut was pressed
aghouse activate                                # bring the window to the front
aghouse quit [--force]                          # asks about running programs unless --force
aghouse help <command> | commands --json        # help for one command | all of them, for agents
```

Exit codes: 0 ok, 1 error, 2 aghouse isn't running, 3 timed out (`wait-for`, `send-text --wait-for`).

### Driving a pane from an agent

`read-pane` gives back what a pane printed as plain text (colours and cursor moves stripped, the last 10,000 lines). To run a command and wait for its result, give `send-text` a `--wait-for` pattern. It watches only what comes after the text went in, ignores the shell's echo of the command, and prints the line that matched:

```powershell
$p = (aghouse new-split right | ConvertFrom-Json).paneId          # a shell of the agent's own
aghouse send-text "npm test" --enter --pane $p --wait-for "passing|failing" --timeout 300
aghouse read-pane --pane $p --lines 40                            # the rest of what it printed
aghouse send-keys ctrl+c --pane $p                                # stop something
```

For finer control, `send-text --json` and `read-pane --json` return a cursor (`cursor`, `next`) to pass to `read-pane --since` or `wait-for --since`. Full-screen programs that redraw (vim, Claude Code) read back as fragments. `aghouse commands --json` describes every command, so an agent can find out what it can do in one call.

### Teaching agents about aghouse

- **Claude Code** learns it by itself once it's connected (below): when a session starts in a pane, aghouse's `SessionStart` hook tells Claude which pane and workspace it's in, the commands it will need most, and a few rules (never type into its own pane, leave panes it didn't open alone). Outside aghouse the hook says nothing.
- **Other agents** (Codex, Gemini CLI, Cursor and others read `AGENTS.md`): `aghouse agent-guide >> AGENTS.md` adds the same guidance as Markdown. Agents only use it when `AGHOUSE_PANE_ID` is set, so it does no harm outside aghouse.

Inside a pane, `--pane` defaults to that pane (`$env:AGHOUSE_PANE_ID`). Every tab is its own terminal with its own pane id, so `--pane` and `AGHOUSE_PANE_ID` work the same for tabs. `aghouse list` shows each terminal with its `slot` (the pane it's a tab of), `tab` position and whether it's `visible`, plus each workspace's `pullRequest`, `status` chips, `progress` and `lastLog`.

Listening ports are found by looking at the processes under each pane's shell, every 3 seconds. Processes inside WSL aren't visible to Windows, so their ports don't show. The PR comes from `gh pr view` in the active pane's folder; without `gh`, or with no PR for the branch, the row just doesn't show one.

## Claude Code integration

Open Settings › Integrations and click **Connect Claude Code**, or run `aghouse hooks install claude` in any pane. Either one says exactly what it will add to `~/.claude/settings.json` and asks first. It only appends aghouse's entry to each hook list, keeps your other settings and hooks, and copies the file to `settings.json.aghouse-backup` before writing. It never touches a file that isn't valid JSON. `aghouse hooks status claude` tells you whether it's connected (or connected through another copy of aghouse, such as a dev build or the portable zip, when every hook runs that copy; **Use this copy** in Settings points them at the one you're running), and `aghouse hooks print claude` prints the snippet if you'd rather merge it by hand. Until Claude Code is connected or the first notification arrives, the foot of the sidebar offers to connect it. It routes Claude Code's `Notification`, `Stop`, `UserPromptSubmit`, `PostToolUse` and `SessionStart` events to `aghouse claude-hook`. While Claude is busy, it shows under **Working** in the sidebar's Agents list. When Claude asks for permission or finishes, it moves to **Needs you** and the pane that runs it gets marked "Needs you"; a pane that is asking comes before one that finished in the waiting list and for Ctrl+Shift+U. At `SessionStart`, Claude is told it's in aghouse and how to use the CLI (see [Teaching agents about aghouse](#teaching-agents-about-aghouse)). The hook does nothing outside aghouse. If you connected before `SessionStart` was added, Settings › Integrations shows "Partly connected"; click **Connect Claude Code** (or run `aghouse hooks install claude`) to add it.

## OpenCode integration

Open Settings › Integrations and click **Connect OpenCode**, or run `aghouse hooks install opencode`. Either one asks first, then writes a plugin folder of aghouse's own, `~/.config/opencode/plugins/aghouse` (a `tui.js` and a `package.json`). `opencode.json` and your other plugins stay as they are, and deleting the folder removes it. OpenCode 2 loads the plugin into each terminal it opens, so it runs inside the aghouse pane and reports only the sessions that terminal shows. While OpenCode works, the pane shows under **Working** in the Agents list. When it asks for a permission or an answer, or finishes, the pane is marked "Needs you", just like Claude Code. The plugin calls this copy's `aghouse.exe` by its full path and does nothing outside aghouse. `aghouse hooks status opencode` says whether it's connected, and `aghouse hooks print opencode` shows the plugin it writes. Without the plugin, a running OpenCode still shows in the Agents list, but only as Idle.

Other tools can raise attention by printing an escape code:

```powershell
Write-Host ("{0}]777;notify;Codex;Waiting for approval{1}" -f [char]27, [char]7)
```

## Shell integration

PowerShell profiles dot-source `ShellIntegration/aghouse.ps1` after your `$PROFILE`. The script wraps your prompt so it emits OSC 7 (cwd) and OSC 133 (prompt marks). The cmd profile sets `PROMPT` and the Git Bash profile sets `PROMPT_COMMAND` to report the cwd.

## Files

- Settings: `%APPDATA%\aghouse\settings.json`. The Settings window (the gear, or Ctrl+,) and `aghouse settings set` edit it, and every change applies at once. It holds the font, font size, default profile, Mode (`theme`: `system`/`dark`/`light`), `darkTheme` and `lightTheme` (a theme name; `aghouse themes` lists them), `schemes` (your own colour schemes, in Windows Terminal's format with an optional `"accent"`), `restoreSession`, `toasts`, `flashTaskbar`, `ctrlDSplits` and `groupWorkspaces`. The window only writes those keys, so anything else you put in the file stays. Ctrl+D splits right by default, so it no longer reaches the terminal, where it means end of input or exit (bash, WSL, PowerShell, Claude Code). Turn it off in Settings (`"ctrlDSplits": false`) to give it back; Alt+Shift+= still splits right. If the file has a syntax error, aghouse leaves it as it is, runs on default settings, shows the line with the error at the foot of the sidebar, and keeps Settings read-only until the file is fixed and aghouse restarts.
- Session: `%APPDATA%\aghouse\session.json`. Workspaces, split layout, each pane's cwd, the window's position and size, and the sidebar width are restored on launch. Scrollback is not restored. With no session.json (the very first launch), aghouse opens on a welcome screen instead of a terminal.
- Diagnostics: `%LOCALAPPDATA%\aghouse\aghouse.log`

## Layout

```text
src/AgHouse.Core   escape-code parser, output mirror (find), split-tree model, git branch lookup,
                   Claude Code hook installer, pipe protocol (no UI; unit-tested)
src/AgHouse.App    WPF app: panes (ConPTY + terminal control), workspaces, shortcuts, toasts, pipe server;
                   Surfaces/ holds the palette, waiting list, Settings and the welcome screen
src/AgHouse.Cli    aghouse.exe
tests/             xUnit tests for Core
assets/            logo sources (SVG); build-icons.ps1 regenerates the PNGs and src/AgHouse.App/Assets/aghouse.ico
```

## Known limitations

- The terminal control is a native child window, so WPF can't draw on top of it. Attention rings are drawn around panes, never over them.
- The terminal control has no search of its own. Find searches a plain-text copy of the pane's output, made since the pane opened. It lists matching lines but can't highlight them in the terminal or scroll to them. A screen that ConPTY repaints (after a resize, say) can show up twice; find folds identical lines into one with a count.
- The terminal packages come from Windows Terminal's CI builds, not an officially supported Microsoft package.
