<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/logo-wordmark-dark.svg">
    <img src="assets/logo-wordmark.svg" alt="aghouse" width="420">
  </picture>
</p>

A Windows-native terminal workspace app for running coding agents side by side, in the spirit of cmux.

- **Native UI:** WPF on .NET 10 with the built-in Fluent theme. You get Mica on Windows 11, a system title bar, and Windows toast notifications.
- **Native terminals:** each pane is rendered by Windows Terminal's own DirectX engine over ConPTY, through [EasyWindowsTerminalControl](https://github.com/mitchcapper/EasyWindowsTerminalControl). Panes are not web views.
- **Workspaces + splits:** workspaces live in a vertical sidebar. Each workspace is a tree of split panes, and the sidebar shows the active pane's cwd and git branch.
- **Agent attention:** a pane can ask for attention with an escape code (OSC 9 / 99 / 777, or a bell) or with `aghouse notify`. That pane gets an accent ring and the workspace gets a dot. If the window isn't in front, you also get a toast and the taskbar button flashes.
- **Automation:** the `aghouse` CLI talks to the app over a per-user named pipe.

## Build & run

Requires the .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`) and Windows 10 1809+ (Windows 11 for Mica).

```powershell
dotnet build
dotnet run --project src/AgHouse.App
dotnet test
```

The build output folder contains `AgHouseApp.exe` (the GUI) and `aghouse.exe` (the CLI). Every pane gets that folder on its `PATH`, so `aghouse` works inside panes without any setup.

## Keyboard

| Shortcut | Action |
|---|---|
| Ctrl+Shift+N / Ctrl+Shift+T | New workspace |
| Alt+Shift+= / Ctrl+Shift+D | Split right |
| Alt+Shift+- / Ctrl+Shift+E | Split down |
| Alt+Arrow | Move focus between panes |
| Ctrl+Shift+W | Close pane |
| Ctrl+1…8, Ctrl+9 | Go to workspace N / last workspace |
| Ctrl+Tab, Ctrl+Shift+Tab | Next / previous workspace |
| Ctrl+Shift+U | Jump to the most recent unread pane |
| Ctrl+Shift+R (or double-click) | Rename workspace |
| Ctrl+Shift+C / Ctrl+Shift+V | Copy / paste |
| Right click | Copy the selection, or paste if nothing is selected |

When a workspace is split, each pane gets a slim header with its folder (or program title) and a **✕** button that closes the pane. Typing `exit` in a pane also closes it.

If something is still running in a pane (an agent, a build), **✕** and Ctrl+Shift+W ask before ending it. A pane sitting at its prompt closes immediately, and so does `aghouse close-pane`, which never asks. WSL processes run outside Windows' process list, so WSL panes don't get this warning. Closing the last pane doesn't quit aghouse: a fresh workspace opens in the same folder. Use the window's close button to quit.

## CLI

```text
aghouse list
aghouse new-workspace [--cwd DIR] [--profile pwsh|powershell|cmd|gitbash|wsl-<distro>] [--name NAME]
aghouse select-workspace <id|name|index>
aghouse rename-workspace <name>
aghouse new-split [right|down|left|up] [--pane ID]
aghouse focus-pane <id> | close-pane [id]
aghouse send-text "npm test" --enter [--pane ID]
aghouse notify --title "Build" --body "Done"
```

Inside a pane, `--pane` defaults to that pane (`$env:AGHOUSE_PANE_ID`).

## Claude Code integration

Run `aghouse hooks print claude` and merge the snippet it prints into `~/.claude/settings.json`. It routes Claude Code's `Notification` and `Stop` events to `aghouse claude-hook`. When Claude asks for permission or finishes, the pane that runs it gets marked. The hook does nothing outside aghouse.

Other tools can raise attention by printing an escape code:

```powershell
Write-Host ("{0}]777;notify;Codex;Waiting for approval{1}" -f [char]27, [char]7)
```

## Shell integration

PowerShell profiles dot-source `ShellIntegration/aghouse.ps1` after your `$PROFILE`. The script wraps your prompt so it emits OSC 7 (cwd) and OSC 133 (prompt marks). The cmd profile sets `PROMPT` and the Git Bash profile sets `PROMPT_COMMAND` to report the cwd.

## Files

- Settings: `%APPDATA%\aghouse\settings.json`, which you can open with the gear button in the sidebar. It holds the font, font size, default profile, theme (`system`/`dark`/`light`) and the toast toggle. Changes apply when aghouse restarts. If the file has a syntax error, aghouse leaves it as it is, runs on default settings, and shows the line with the error at the foot of the sidebar.
- Session: `%APPDATA%\aghouse\session.json`. Workspaces, split layout and each pane's cwd are restored on launch. Scrollback is not restored.
- Diagnostics: `%LOCALAPPDATA%\aghouse\aghouse.log`

## Layout

```text
src/AgHouse.Core   escape-code parser, split-tree model, git branch lookup, pipe protocol (no UI; unit-tested)
src/AgHouse.App    WPF app: panes (ConPTY + terminal control), workspaces, shortcuts, toasts, pipe server
src/AgHouse.Cli    aghouse.exe
tests/             xUnit tests for Core
assets/            logo sources (SVG); build-icons.ps1 regenerates the PNGs and src/AgHouse.App/Assets/aghouse.ico
```

## Known limitations

- The terminal control is a native child window, so WPF can't draw on top of it. Attention rings are drawn around panes, never over them.
- The terminal colour scheme is chosen at launch. Switching the Windows theme while the app is running doesn't recolour existing panes.
- The terminal packages come from Windows Terminal's CI builds, not an officially supported Microsoft package.
