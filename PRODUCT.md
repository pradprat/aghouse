# Product

<!-- impeccable:product-schema 1 -->

## Platform

windows

Native Windows desktop app: WPF on .NET 10 with the built-in Fluent theme. It is not a web app and not a wrapper around one. Impeccable's enum (`web` / `ios` / `android` / `adaptive`) has no desktop value, so this records the real platform. The HTML/CSS detector and the `live` / `generate` browser tooling do not apply.

## Users

Developers on Windows who run several coding agents at once (Claude Code, Codex and similar CLIs), each in its own terminal. They watch several long-running agent sessions and step in only when one needs them: a permission prompt, a question, or a finished task. They want cmux-style multi-agent tooling without a Mac and without Electron.

## Product Purpose

aghouse is a terminal workspace for running coding agents side by side. Workspaces sit in a vertical sidebar, and each one is a tree of split panes. When an agent needs attention, the app shows which pane it is and lets the user jump straight to it. Success means a developer can keep many agents running and never miss one that is waiting on them, without hunting through windows or tabs.

## Positioning

aghouse is the Windows-native take on the cmux idea. The UI uses WPF Fluent, Mica and the system title bar. Each pane is drawn by Windows Terminal's own DirectX renderer over ConPTY, not by a web view. Agent attention is a first-class signal: escape codes (OSC 9 / 99 / 777, bell), `aghouse notify` and Claude Code hooks all feed one attention model. Electron clones and plain terminal tabs offer none of this.

## Operating Context

- The app runs all day next to an editor and a browser, often behind other windows. Attention has to reach the user when aghouse isn't in front, through toasts and a flashing taskbar button.
- Supported shells: pwsh, Windows PowerShell, cmd, Git Bash and WSL distros. Shell integration reports the cwd (OSC 7) and prompt marks (OSC 133).
- The app is driven from the keyboard first, with full shortcut coverage. Mouse paths exist for closing panes, renaming and the context menu.
- The `aghouse` CLI talks to the app over a per-user named pipe. Every pane has it on `PATH` and gets `AGHOUSE_PANE_ID`, so agents and scripts can control the app from inside a pane.
- Claude Code integration: Settings › Integrations or `aghouse hooks install claude` adds aghouse's hook to `~/.claude/settings.json`, after asking and with a backup. It routes Notification, Stop, UserPromptSubmit and PostToolUse events to the pane that ran them. `aghouse hooks print claude` still prints the snippet for a hand merge.
- File locations: settings at `%APPDATA%\aghouse\settings.json` (edited by the Settings window, changes apply at once), session at `%APPDATA%\aghouse\session.json` (layout, cwd and window placement are restored, scrollback is not), log at `%LOCALAPPDATA%\aghouse\aghouse.log`.

## Capabilities and Constraints

- Workspaces with split panes (right/down/left/up), and tabs inside a pane (each tab its own terminal). Focus moves between panes, tabs switch, panes and tabs close, workspaces and tabs can be renamed.
- cmux-style sidebar rows: the active pane's cwd, git branch and PR (via `gh`), plus status chips (an agent's "Working" from Claude Code hooks, `aghouse set-status` entries, listening ports), a progress line (`aghouse set-progress`) and the newest log line (`aghouse log`). This metadata is runtime-only and never saved.
- Attention: a pane needing attention gets an accent ring and its workspace gets a dot. Toasts and the taskbar flash fire when the window is in the background. Ctrl+Shift+U jumps to the pane that asked most recently. The waiting list (Ctrl+Shift+I, or the footer's "N waiting") shows every waiting pane, then the last 20 handled requests, runtime only.
- Command palette (Ctrl+Shift+P): waiting panes, workspaces, panes and every action with its shortcut. Find in a pane's output (Ctrl+Shift+F), per-pane text size (Ctrl+= / - / 0, Ctrl+wheel), pane zoom (Ctrl+Shift+Enter), drag to reorder workspaces, Ctrl+Alt+W closes a workspace.
- A Settings window (Ctrl+,) with General, Appearance, Notifications, Integrations and About. A first-run welcome screen appears when there is no session.json.
- When a workspace is split, each pane gets a slim header with a name that tells it apart from its siblings and a ✕ close button. A pane with two or more tabs uses that header as its tab strip. New workspaces are named after their folder.
- **Hard technical constraint:** the terminal control is a native child window (an airspace issue), so WPF can never draw over a pane. Attention indicators and overlays must sit around panes, never on top of them. Transient surfaces that must float (the palette, the waiting list, find results) are separate owned windows or popups, like menus.
- The terminal control has no search API. Find searches aghouse's own plain-text mirror of the output and lists matching lines; it can't highlight them in the terminal.
- No update check: aghouse has no published releases yet, so About links to the Releases page and claims nothing about being up to date.
- The terminal packages come from Windows Terminal CI builds, not an officially supported Microsoft package.
- Minimum OS is Windows 10 1809. Mica requires Windows 11.
- Executables: `AgHouseApp.exe` (GUI) and `aghouse.exe` (CLI). The names differ so they don't collide on Windows' case-insensitive file system.
- Not built yet: a browser pane (deferred on purpose from the MVP).

## Brand Commitments

- Name: **aghouse**, written in lowercase.
- Logo sources in `assets/`: `logo.svg`, `logo-small.svg`, `logo-wordmark.svg`, `logo-wordmark-dark.svg`, plus generated PNGs and `src/AgHouse.App/Assets/aghouse.ico` (built by `assets/build-icons.ps1`).
- Must feel native to Windows: Fluent theme, Mica, system title bar, standard Windows controls. It must never read as a web app inside a frame.

## Evidence on Hand

- README with features, keyboard map, CLI reference and known limitations.
- Logo and icon assets (listed above).
- Distribution: open source on GitHub.
- None of the following exist, and future work must not invent them: users or adoption numbers, testimonials, benchmarks, press, release builds or download counts, an installer or store listing.

## Product Principles

1. **Native to Windows.** Use the platform's own controls, materials and conventions before building anything custom. If something would only make sense in an Electron app, it doesn't belong here.
2. **The terminal is the real thing.** Panes are Windows Terminal's renderer and are never swapped for a web view. Design works with the airspace limit instead of hiding it.
3. **Attention is the product.** The most important thing aghouse does is show which agent needs you and get you there in one keystroke. Every surface should make that signal clearer and never bury it.
4. **Scriptable first.** Anything the UI can do, the `aghouse` CLI and named pipe can do too, so agents and scripts are first-class users.
5. **Keyboard first, mouse friendly.** Every action has a shortcut, and mouse paths exist for the actions people reach for by hand.
