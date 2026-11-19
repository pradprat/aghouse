# Changelog

Every release is on [GitHub Releases](https://github.com/pradprat/aghouse/releases), with the installer, the portable zip and `SHA256SUMS.txt`. Installed copies (0.2.0 and later) update themselves when you quit.

## 1.0.0 (unreleased)

The first stable release. From 1.0 on, the `aghouse` CLI (commands, flags, JSON output and exit codes), the `settings.json` keys and the Claude Code / OpenCode integrations stay compatible until 2.0. New commands, flags, keys and JSON fields may still be added.

**Updating from 0.2.x:** install 1.0.0 by hand from the release page. Every 0.2 version hangs while installing its update on quit (see below), so they can't update themselves to 1.0. From 1.0 on, updates install on quit as intended.

### Fixes
- **Updates install again.** Quitting with an update ready, or clicking Restart to update, left a windowless aghouse running that never exited and never ran the installer. Checking the installer's hash blocked the UI thread on work that needed it.
- `new-workspace`, `new-split`, `new-tab` and `restart-pane` say so when `--cwd` doesn't exist or `--profile` isn't a profile, instead of quietly opening in your home folder or the default shell.

### CLI changes (breaking, made before the 1.0 promise)
- An option a command doesn't take is an error (exit 1). A misspelt `--pane` used to be ignored, so the command acted on the wrong pane.
- `aghouse update` is now `aghouse check-update`.
- `read-pane --json`: `next` is now `cursor`, the same name `send-text --json` uses.
- `waiting`: `workspace` and `pane` are now `workspaceName` and `paneName` (`pane` is an id everywhere else), and `since` is now `askedAt`. `earlier` entries gain `workspaceId`; `asked` and `handled` are now `askedAt` and `handledAt`.
- `list`: `unread` is now `needsYou`. `status` lists `{key, text, paneId}` entries instead of display text, and ports are in their own `ports` array.
- `ping` reports the version as `1.0.0`, not `1.0.0.0`.
- `-h` after a command shows its help. `claude-hook` is marked internal in `help`.

### Docs
- A screenshot, install steps, an FAQ, what stays stable in 1.0, CONTRIBUTING.md, issue templates and this changelog.

## 0.2.3

Accessibility and multi-monitor fixes.

- aghouse's text follows Windows' Accessibility › Text size, live.
- Screen readers hear the palette's selected row, the find count, tabs as tabs, and Settings help lines.
- The palette and the waiting list open on the monitor aghouse is on.
- "Make the sidebar wider / narrower" in the palette, or `aghouse run sidebar-wider` / `sidebar-narrower`.
- Focus rings in the waiting list, 4.5:1 contrast for workspace tiles, readable pane headers under Windows contrast themes, and larger header buttons.

## 0.2.2

- A pane's `PATH` comes from the machine and user `PATH` Windows holds now, so tools installed while aghouse is open (nvm, uv, pnpm) work in the next pane.
- Fixes `node`, `npm` and `npx` not being found in panes with nvm for Windows.

## 0.2.1

- The sidebar list is called **Workspaces** everywhere. Each workspace keeps its own tile colour.
- `main` / `master` no longer show on a row. Agents rows lead with the task or request.
- Motion for Needs you (a header swell and a dot ripple), following Windows' Animation effects setting.

## 0.2.0

A new sidebar, colour themes, OpenCode support, and self-updating.

- **Sidebar:** one line per workspace with a coloured tile, the branch and "Needs you". An **Agents** list shows every coding agent under Needs you, Working and Idle. Group by project, and Ctrl+Shift+B folds the sidebar to a rail.
- **OpenCode** reports Working and Needs you, like Claude Code (`aghouse hooks install opencode`).
- An agent that is asking comes before one that finished. Scripts can say `aghouse notify --finished`.
- **Drag a pane by its header** to move or swap it (`aghouse move-pane`). Every pane has a header, and tabs have Duplicate tab.
- **Themes:** a dark and a light theme, 15 built-in schemes, your own, or imported from Windows Terminal.
- **Updates** from GitHub Releases, installed when you quit.
- Closing a pane no longer crashes or freezes aghouse. Agents that animate their title cost about a tenth of the CPU.

## 0.1.2

A security release.

- Pane names are cleaned before they reach Claude's SessionStart context.
- A pane can't type into another pane that runs an agent (`allowInputToAgentPanes` to allow it).
- Safer paste: control characters are removed, and aghouse asks before a multi-line paste would run commands.
- Toasts name the workspace and pane that raised them. Pipe requests are size- and connection-limited.
- The Claude hook runs `aghouse.exe` by its full path. Reconnect Claude Code once after updating.

## 0.1.1

- Ctrl+V pastes an image into Claude Code.
- A Fluent close dialog that lists the programs still running.
- CLI commands for agents and scripts: `send-keys`, `read-pane`, `wait-for`, `send-text --wait-for`, `settings`, `actions`, `run`, `rename-pane`, `restart-pane`, `profiles`, `activate`, `quit`, `help`, `commands --json`.
- The SessionStart hook tells Claude Code it is in aghouse. `aghouse agent-guide` prints the same for AGENTS.md.

## 0.1.0

First release: workspaces with split panes and tabs on Windows Terminal's renderer, agent attention (OSC 9/99/777, bell, `aghouse notify`, toasts and taskbar flash), the command palette, the waiting list, find, Settings with one-click Claude Code hooks, and the `aghouse` CLI.
