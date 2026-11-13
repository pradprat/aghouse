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
- **A sidebar that tells you what each agent is doing:** every row shows the cwd, git branch and pull request (through `gh`), plus chips for what's going on: "Working" while Claude Code is busy, anything a script sets with `aghouse set-status`, and the ports its dev servers listen on (click one to open it). Scripts can also show a progress line and a log line.
- **Agent attention:** a pane can ask for attention with an escape code (OSC 9 / 99 / 777, or a bell) or with `aghouse notify`. That pane gets an accent ring and the workspace gets a dot. If the window isn't in front, you also get a toast and the taskbar button flashes.
- **Everything a keystroke away:** a command palette (Ctrl+Shift+P) for every action, workspace and pane, and a waiting list (Ctrl+Shift+I) of the panes that need you, plus requests you already handled.
- **Terminal basics:** find in a pane's output, per-pane text size, and pane zoom.
- **Settings:** a Settings window where changes apply at once, including the theme, which recolours the terminals too. Settings › Integrations connects Claude Code in one click.
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
| Ctrl+Shift+U | Jump to the pane (and tab) that asked for you most recently |
| Ctrl+Shift+I | Waiting list: every pane that needs you, then earlier requests |
| Ctrl+Shift+P | Command palette: any action, workspace or pane |
| Ctrl+Shift+Enter | Zoom the focused pane to fill the workspace (again to show all) |
| Ctrl+Shift+F | Find in the pane's output |
| Ctrl+= / Ctrl+- / Ctrl+0 (or Ctrl+wheel) | Bigger / smaller / reset text in this pane |
| Ctrl+Shift+R (or double-click) | Rename workspace |
| Ctrl+Alt+W | Close workspace |
| Ctrl+, | Settings |
| Ctrl+Shift+C / Ctrl+Shift+V | Copy / paste |
| Right click | Copy the selection, or paste if nothing is selected |

New workspaces are named after their folder ("aghouse", then "aghouse (2)"); double-click a name to rename it, and drag a row to reorder (or use Move up / Move down in its menu). The window remembers its size, position and sidebar width. When a workspace is split, each pane gets a slim header with a **✕** button that closes the pane. The header names the pane by what it is running (an agent's current task, `vim`), else its folder, else its shell, and numbers names that would otherwise repeat. A lone pane shows a header only while it needs you. Typing `exit` in a pane also closes it.

A pane with two or more tabs shows them in that same header, with a **+** for another. Click a tab to show it, middle-click to close it, double-click to rename it, or right-click for Close other tabs. A tab that needs you while it's in the background says "Needs you" in the strip, and its pane gets the accent ring. Tabs and their folders are restored with the session.

**Pane zoom** (Ctrl+Shift+Enter) fills the workspace with one pane while the others keep running out of sight. The header says how many are hidden, and if one of them needs you it says so ("Needs you in GitHub (2) · Ctrl+Shift+U"); going to a hidden pane ends the zoom. **Find** (Ctrl+Shift+F) opens in the pane's header and lists the matching lines of its output, newest first; Enter copies the selected line. **Text size** changes for one pane at a time and shows as "110%" in its header.

The footer's "2 waiting" line is a button: it opens the waiting list, which lists every pane that needs you, newest first, and the last 20 requests you already handled. Click a row to go there.

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
aghouse waiting                                 # panes that need you, then handled requests (JSON)
aghouse send-text "npm test" --enter [--pane ID]
aghouse notify --title "Build" --body "Done"

# The workspace's sidebar row (--workspace W to target another)
aghouse set-status tests "Running tests"       # a chip; set the same key again to change it
aghouse clear-status tests
aghouse set-progress 0.4 --label Tests          # or 40%, or clear
aghouse log "Lint found 3 warnings" --level warn
```

Inside a pane, `--pane` defaults to that pane (`$env:AGHOUSE_PANE_ID`). Every tab is its own terminal with its own pane id, so `--pane` and `AGHOUSE_PANE_ID` work the same for tabs. `aghouse list` shows each terminal with its `slot` (the pane it's a tab of), `tab` position and whether it's `visible`, plus each workspace's `pullRequest`, `status` chips, `progress` and `lastLog`.

Listening ports are found by looking at the processes under each pane's shell, every 3 seconds. Processes inside WSL aren't visible to Windows, so their ports don't show. The PR comes from `gh pr view` in the active pane's folder; without `gh`, or with no PR for the branch, the row just doesn't show one.

## Claude Code integration

Open Settings › Integrations and click **Connect Claude Code**, or run `aghouse hooks install claude` in any pane. Either one says exactly what it will add to `~/.claude/settings.json` and asks first. It only appends aghouse's entry to each hook list, keeps your other settings and hooks, and copies the file to `settings.json.aghouse-backup` before writing. It never touches a file that isn't valid JSON. `aghouse hooks status claude` tells you whether it's connected, and `aghouse hooks print claude` prints the snippet if you'd rather merge it by hand. Until Claude Code is connected or the first notification arrives, the foot of the sidebar offers to connect it. It routes Claude Code's `Notification`, `Stop`, `UserPromptSubmit` and `PostToolUse` events to `aghouse claude-hook`. While Claude is busy, its workspace row shows a **Working** chip. When Claude asks for permission or finishes, the chip goes and the pane that runs it gets marked "Needs you". The hook does nothing outside aghouse.

Other tools can raise attention by printing an escape code:

```powershell
Write-Host ("{0}]777;notify;Codex;Waiting for approval{1}" -f [char]27, [char]7)
```

## Shell integration

PowerShell profiles dot-source `ShellIntegration/aghouse.ps1` after your `$PROFILE`. The script wraps your prompt so it emits OSC 7 (cwd) and OSC 133 (prompt marks). The cmd profile sets `PROMPT` and the Git Bash profile sets `PROMPT_COMMAND` to report the cwd.

## Files

- Settings: `%APPDATA%\aghouse\settings.json`. The Settings window (the gear, or Ctrl+,) edits it, and every change applies at once. It holds the font, font size, default profile, theme (`system`/`dark`/`light`), `restoreSession`, `toasts`, `flashTaskbar` and `ctrlDSplits`. The window only writes those keys, so anything else you put in the file stays. Ctrl+D splits right by default, so it no longer reaches the terminal, where it means end of input or exit (bash, WSL, PowerShell, Claude Code). Turn it off in Settings (`"ctrlDSplits": false`) to give it back; Alt+Shift+= still splits right. If the file has a syntax error, aghouse leaves it as it is, runs on default settings, shows the line with the error at the foot of the sidebar, and keeps Settings read-only until the file is fixed and aghouse restarts.
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
