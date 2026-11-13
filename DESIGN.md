---
name: aghouse
description: A Windows-native terminal workspace for running coding agents side by side.
colors:
  system-accent: "#4CC2FF"
  mica-dark: "#202020"
  mica-light: "#F3F3F3"
  text-primary-dark: "#FFFFFF"
  text-secondary-dark: "rgba(255, 255, 255, 0.773)"
  text-tertiary-dark: "rgba(255, 255, 255, 0.529)"
  subtle-fill-dark: "rgba(255, 255, 255, 0.06)"
  control-stroke-dark: "rgba(255, 255, 255, 0.07)"
  control-fill-dark: "rgba(255, 255, 255, 0.06)"
  terminal-night: "#1C1C1C"
  terminal-night-ink: "#CCCCCC"
  terminal-night-selection: "#264F78"
  terminal-paper: "#FBFBFB"
  terminal-paper-ink: "#383A42"
  terminal-paper-selection: "#BFCEFF"
  logo-sky: "#4CC2FF"
  logo-deep-blue: "#0F6CBD"
  logo-amber: "#FFB900"
typography:
  title:
    fontFamily: "Segoe UI Variable, Segoe UI, sans-serif"
    fontSize: "14px"
    fontWeight: 600
  section-label:
    fontFamily: "Segoe UI Variable, Segoe UI, sans-serif"
    fontSize: "13px"
    fontWeight: 600
  body:
    fontFamily: "Segoe UI Variable, Segoe UI, sans-serif"
    fontSize: "14px"
    fontWeight: 400
  meta:
    fontFamily: "Segoe UI Variable, Segoe UI, sans-serif"
    fontSize: "12px"
    fontWeight: 400
  caption:
    fontFamily: "Segoe UI Variable, Segoe UI, sans-serif"
    fontSize: "11px"
    fontWeight: 400
  terminal:
    fontFamily: "Cascadia Mono, Consolas, monospace"
    fontSize: "11pt"
    fontWeight: 400
  icon:
    fontFamily: "Segoe Fluent Icons, Segoe MDL2 Assets"
    fontSize: "12px"
rounded:
  control: "4px"
  inset: "3px"
  pill: "1.5px"
spacing:
  hairline: "1px"
  xxs: "2px"
  xs: "4px"
  sm: "6px"
  md: "8px"
  lg: "10px"
components:
  workspace-item:
    backgroundColor: "transparent"
    textColor: "{colors.text-primary-dark}"
    typography: "{typography.title}"
    rounded: "{rounded.control}"
    padding: "7px 8px 7px 10px"
  workspace-item-hover:
    backgroundColor: "{colors.subtle-fill-dark}"
    rounded: "{rounded.control}"
  workspace-item-selected:
    backgroundColor: "{colors.subtle-fill-dark}"
    rounded: "{rounded.control}"
  selection-pill:
    backgroundColor: "{colors.system-accent}"
    rounded: "{rounded.pill}"
    width: "3px"
    height: "16px"
  attention-dot:
    backgroundColor: "{colors.system-accent}"
    size: "10px"
  pane-frame:
    backgroundColor: "{colors.terminal-night}"
    rounded: "{rounded.control}"
    padding: "2px 2px 2px 4px"
  pane-header:
    backgroundColor: "transparent"
    textColor: "{colors.text-secondary-dark}"
    typography: "{typography.meta}"
    height: "26px"
  pane-header-focused:
    textColor: "{colors.text-primary-dark}"
  pane-header-needs-you:
    backgroundColor: "{colors.system-accent}"
    textColor: "{colors.text-primary-dark}"
    rounded: "{rounded.inset}"
  icon-button:
    backgroundColor: "transparent"
    typography: "{typography.icon}"
    padding: "4px 8px"
  status-chip:
    backgroundColor: "{colors.control-fill-dark}"
    textColor: "{colors.text-secondary-dark}"
    typography: "{typography.caption}"
    rounded: "{rounded.inset}"
    padding: "0px 5px 1px 5px"
  progress-line:
    backgroundColor: "{colors.control-stroke-dark}"
    textColor: "{colors.text-secondary-dark}"
    rounded: "1px"
    height: "2px"
  pane-tab:
    backgroundColor: "transparent"
    textColor: "{colors.text-secondary-dark}"
    typography: "{typography.meta}"
    rounded: "{rounded.inset}"
    padding: "0px 8px"
    height: "22px"
  pane-tab-active:
    backgroundColor: "{colors.subtle-fill-dark}"
    textColor: "{colors.text-primary-dark}"
  pane-tab-needs-you:
    backgroundColor: "{colors.system-accent}"
    textColor: "{colors.text-primary-dark}"
    rounded: "{rounded.inset}"
  flyout:
    backgroundColor: "FlyoutBackground"
    rounded: "Windows 11 round corners (DWMWCP_ROUND)"
    padding: "8px"
  quiet-list-item:
    backgroundColor: "transparent"
    textColor: "{colors.text-primary-dark}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "5px 10px"
  quiet-list-item-selected:
    backgroundColor: "{colors.subtle-fill-dark}"
  settings-row:
    backgroundColor: "transparent"
    padding: "12px 0"
---

# Design System: aghouse

## Overview

**Creative North Star: "The Native Workbench"**

aghouse is a set of plain Windows tools with nothing dressed up. The built-in WPF Fluent theme does the work: Mica behind the window, the system title bar, the user's own accent colour, Segoe UI text and Segoe Fluent Icons glyphs. The terminals are the content. The window chrome is a narrow sidebar and thin frames around panes. Nothing else competes for space or attention.

The system is quiet on purpose. It is almost entirely made of theme resources rather than fixed colours, so it follows the user's light/dark setting and accent and looks like it shipped with Windows. Fixed colours appear in only two places: the terminal palettes, because the terminal is a native control that can't read WPF brushes, and the logo. Density is that of a desktop power tool: compact rows, 12px metadata, and a terminal font at 11pt by default.

aghouse never looks like a web app in a frame, never like a neon "hacker" terminal, never like busy IDE chrome, and never paints brand colour across the UI.

**Key Characteristics:**
- Fluent theme resources everywhere (`DynamicResource` brushes), so light, dark and accent changes follow Windows.
- Flat: Mica, subtle fills and 1–2px strokes. No shadows.
- One accent colour (the user's Windows accent) for both "selected" and "needs you", told apart by shape and words. Focus is always neutral.
- Terminals fill the window. Chrome is a resizable sidebar plus pane frames.
- Small Segoe Fluent Icons glyphs for every control. No text buttons in the chrome.

## Colors

The palette is the Windows theme itself, plus two terminal palettes chosen to sit quietly next to Mica.

### Primary
- **System Accent** (`AccentFillColorDefaultBrush`; reference value `system-accent`, the dark-theme fill for Windows' default blue): the user's own Windows accent. It is the only chromatic colour in the chrome. It marks the selected workspace (a 3×16 pill), workspaces with a pane that needs you (a filled dot with a faint halo), and panes that need you (a 2px frame). Never hard-code it. Always bind the brush so the user's accent wins.

### Neutral
- **Mica Backdrop** (`mica-dark` / `mica-light`): the window background, provided by Windows 11's Mica material. The app never paints its own window background.
- **Primary Text** (`TextFillColorPrimaryBrush`; `text-primary-dark`): workspace names (inherited by default), the focused pane's 1px border and header title, and "Needs you".
- **Secondary Text** (`TextFillColorSecondaryBrush`; `text-secondary-dark`): the "Workspaces" label, git branch, cwd, the agent's waiting message (in a Needs-you pane header and under the workspace name), header names of panes at rest, and the footer's setup and idle hints.
- **Tertiary Text** (`TextFillColorTertiaryBrush`; `text-tertiary-dark`): not used for text anyone has to read. It is about 3.4:1 on the light terminal background and on light Mica, under the 4.5:1 floor, so pane headers at rest and the footer hint moved to Secondary.
- **Subtle Fill** (`SubtleFillColorSecondaryBrush`; `subtle-fill-dark`): background for hovered and selected sidebar items.
- **Control Stroke** (`ControlStrokeColorDefaultBrush`; `control-stroke-dark`): the 1px frame around inactive panes, the 1px edge of a status chip, and the track of the progress line.
- **Control Fill** (`ControlFillColorDefaultBrush`; `control-fill-dark`): the status chip's fill, so a chip reads as a resting Fluent control on any row background (hover, selected or none). A port chip steps to `ControlFillColorSecondaryBrush` on hover.
- **Flyout surface** (`FlyoutBackground`, edge `SurfaceStrokeColorFlyoutBrush`): the command palette, the waiting list and the find results, Fluent's own menu surface.
- **Divider** (`DividerStrokeColorDefaultBrush`): the hairline under each setting and between the welcome screen's sections.
- **Caution background** (`SystemFillColorCautionBackgroundBrush`): only the Settings InfoBar for a broken settings.json, with the caution glyph. It is the same semantic exception as the footer's.

The `-dark` values above are reference values for the dark theme. The brushes are the source of truth.

### Terminal palettes
These are fixed because the terminal is a native child window with its own renderer.
- **Terminal Night** (`terminal-night` on `terminal-night-ink`, selection `terminal-night-selection`): the dark terminal, using Windows Terminal's Campbell 16-colour table. The pane frame's background matches it so the frame and terminal read as one surface.
- **Terminal Paper** (`terminal-paper` on `terminal-paper-ink`, selection `terminal-paper-selection`): the light terminal, using the One Half Light 16-colour table.
- The terminal palette follows the `theme` setting (`system` / `dark` / `light`) live. Changing it in Settings, or switching Windows between light and dark while it's `system`, recolours every pane and its frame without a restart.

### Logo colours
- **Sky** (`logo-sky`), **Deep Blue** (`logo-deep-blue`), **Amber** (`logo-amber`): used only in the logo and icon assets (`assets/`). The amber dot is the logo's picture of attention. Inside the app, attention uses the system accent.

### Named Rules
**The Borrowed Colour Rule.** The chrome has no colours of its own. Every surface, text and stroke colour in the UI is a Fluent theme brush, and the only chromatic colour is the user's accent. A hex value in UI code is a defect, except in the terminal palettes. The single exception is semantic: the warning glyph for a broken settings.json uses Fluent's `SystemFillColorCautionBrush`, exactly as a Windows InfoBar would.

**The One Accent Rule.** The accent marks only selection and attention. Never use it for decoration, headings, icons at rest, or hover states. That includes the cmux-style metadata: status chips, the progress line, PR state and the active tab are all neutral, so the accent still means only "selected workspace" or "Needs you" at a glance. The same goes for the newer surfaces. The selected row in the palette, the waiting list and the find results takes the subtle fill, not Fluent's default accent selection. Links in About are neutral and underlined. The only accent outside the main window is the selected page's pill in Settings' nav, which is selection. Stock control states (a checked CheckBox, a focused TextBox's underline) stay as Fluent draws them.

## Typography

**UI Font:** Segoe UI Variable (with Segoe UI), inherited from the Fluent theme and not set anywhere in code
**Terminal Font:** Cascadia Mono, 11pt by default, configurable in `settings.json`
**Icon Font:** Segoe Fluent Icons (with Segoe MDL2 Assets), the `IconFont` resource

**Character:** This is the operating system's own voice. Segoe in the chrome and Cascadia in the panes are exactly what a Windows Terminal user already reads all day.

### Hierarchy
- **Title** (600, 14px): workspace names in the sidebar, trimmed with an ellipsis.
- **Section Label** (600, 13px, secondary text): the "Workspaces" heading. It is the only heading in the chrome.
- **Body** (400, 14px): Fluent default for menus, tooltips and the rename text box.
- **Meta** (400, 12px, secondary text): git branch, PR, cwd, the waiting message, pane header text and tab names.
- **Caption** (400, 11px): the footer status line at the foot of the sidebar (secondary text for the setup and idle hints, primary for the waiting count and a settings problem), status chips, the progress label and the log line.
- **Icon** (Segoe Fluent Icons, 10–13px): add (`E710`), chevron (`E70D`), settings (`E713`), close (`E711`). The git branch icon is a hand-drawn 11px stroke path at 1.4 thickness.

### Named Rules
**The System Voice Rule.** Never load a web font or a custom display face. Hierarchy comes from weight (400 vs 600) and from the three text brushes, not from size jumps. The whole UI spans 11–14px.

## Layout

The window has three columns: the sidebar (248px by default, resizable from 170 to 480px), a 4px invisible splitter, and the terminal host, which fills the rest. The window defaults to 1280×800, with a minimum of 640×400. Outer margins are 8px on the window edges and 4px where the two regions meet (sidebar `8,4,4,8`, host `2,4,8,8`).

The sidebar docks three bands: a header row (the "Workspaces" label plus add and profile-menu buttons), a scrolling list of workspaces, and a footer (a one-line status plus settings gear). Each workspace row is a stack, top to bottom, and every line after the name appears only when it has something to say:

1. The name, with the attention dot in a right-hand column.
2. **Needs you** · the newest waiting message, only while a pane needs you. It sits straight under the name so status lines can never push attention down.
3. One meta line: the git branch (trimmed at 96px), the PR ("#123 open") and the cwd.
4. Status chips, wrapping onto a second line at most: agent state ("Working"), `aghouse set-status` entries, then listening ports. Past five chips the rest fold into "+N".
5. Progress: a Caption label ("Tests 40%") over a 2px line the width of the row.
6. The newest `aghouse log` line.

An idle row with none of that is still two lines high, git or not, exactly as before. A busy row reaches about six lines.

Each workspace is a tree of split panes built from nested Grids. There is a 6px invisible splitter between panes, and each pane has a minimum size of 60px wide or 40px tall. A pane can hold several terminals as tabs; only the active one is visible, the rest keep running behind it. A pane header strip (26px high) appears when the workspace is split, on any pane with two or more tabs (where it is the tab strip), and on a lone single-tab pane only while it needs you; that pane's terminal gives up the row until the user reaches it.

The spacing rhythm is small steps of 1, 2, 4, 6, 8 and 10px. Row padding is `10,7,8,7`, and there is 1px between rows.

### Named Rules
**The Airspace Rule.** The terminal is a native HWND, and WPF can never draw on top of it. Every indicator, header or overlay sits beside or around a pane, never over it. Attention is a frame around the pane, not a badge on it. Something that has to float above the terminals is its own window, the way a menu or tooltip is: the palette and the waiting list are owned flyout windows, and the find results are a popup. Such surfaces are transient, take focus while open, and hand it back to the terminal when they close. Nothing persistent ever floats over a pane.

## Elevation & Depth

aghouse is flat. There are no drop shadows anywhere in the app. Depth comes from the Windows 11 Mica backdrop behind the window, the slightly different shade of the terminal surface (`terminal-night` against Mica), subtle fills on hovered and selected rows, and 1–2px strokes around panes. Menus, tooltips and the context menu use the Fluent theme's own popup styling unchanged.

### Named Rules
**The Mica-Only Rule.** The window's material belongs to Windows. Don't add shadows, glows, gradients or acrylic panels to the chrome. A new layer of depth must come from a Fluent brush, not from a custom effect.

## Shapes

Gently rounded corners on everything interactive: 4px on sidebar row backgrounds and pane frames, and Fluent's own radii on buttons and menus. The selection pill is a 3×16 capsule with 1.5px radius. The attention dot is a 10px circle with a 1px halo ring 3px outside it at 45% opacity. The Needs-you header tint uses the inset radius (3px), one step smaller than the frame's, so it nests inside the 4px frame. Pane frames are 1px at rest and when focused, and 2px when they need you. The padding adjusts by the difference so the terminal never shifts.

## Components

### Buttons
Stock Fluent and quiet.
- **Shape:** Fluent default radius.
- **Header buttons** (new workspace, profile menu): stock Fluent buttons holding one Segoe Fluent Icons glyph, with padding `8,4` and `6,4`.
- **Ghost buttons** (settings gear, pane close ✕): transparent background, no border, Fluent hover fill. The pane close button can't take focus, so it never steals focus from the terminal.
- **Every icon button has a tooltip naming the action and its shortcut** ("New workspace (Ctrl+Shift+N)").

### Navigation: workspace list
Styled like a WinUI NavigationView item.
- **Rest:** transparent. Workspace name in Title type, metadata lines in Meta type.
- **Hover:** subtle-fill background on a 4px-rounded rectangle.
- **Selected:** the same subtle fill, plus a 3×16 accent pill at the left edge, level with the name line (9px from the top). A busy row is six lines tall, and the pill marks the name, not the middle of the row.
- **Focus:** keyboard focus gets Fluent's two-stroke focus ring (`FocusStrokeColorOuterBrush` 2px over `FocusStrokeColorInnerBrush` 1px) on the row's 4px shape. It's neutral, never the accent, and only appears for keyboard focus (Ctrl+Arrow moves it without changing the selection).
- **Accessibility:** each row's automation name is the workspace name, its help text is the location followed by the branch, PR, chips, progress and log line, and its item status is "Needs you: <message>" while a pane waits. Icon-only buttons carry an automation name ("New workspace", "Open settings", "Close pane"), not just a tooltip. When a pane starts needing you, the window raises a UIA notification ("aghouse, Windows PowerShell (2) needs you: Waiting for approval"; just the workspace name when it has one pane) so screen readers hear what sighted users see.
- **Name:** a new workspace is named after its folder ("aghouse"; "Home" for the user's profile folder), with "(2)" when that name is taken, so `select-workspace` by name stays unambiguous.
- **Waiting line:** the second line, **Needs you** (600, Primary Text) then "· <newest message>" in Secondary Text, in Meta type. It is derived from the waiting panes, so it disappears as soon as nothing is waiting; the sidebar never shows a request that was already handled. A pane that rang its bell without a message shows just "Needs you". The tooltip lists every waiting message, newest first.
- **PR:** "#123 open", "#123 draft", "#123 merged" or "#123 closed", from `gh pr view` for the active pane's branch, after the branch on the meta line. State is said in words, never in GitHub's green or purple. No `gh`, signed out, or no PR: nothing shows.

### Status chips
Neutral, small, and never the accent.
- **Shape:** Control Fill with a 1px Control Stroke edge, 3px radius, Caption type in Secondary Text, padding `5,0,5,1`, 4px apart, trimmed at 150px.
- **Order:** agent state first, grouped by what it says ("Working", or "2 working" for two panes, matching "2 waiting"), then `aghouse set-status` entries in the order they were set, then listening ports (":5173").
- **Ports:** TCP ports in LISTEN state owned by any process under the workspace's shells (not the shell itself), checked every 3 seconds. A port chip opens `http://localhost:<port>` in the default browser and is the only chip that reacts to hover. WSL processes aren't visible to Windows, so their ports don't show.
- **Overflow:** five chips, then "+N" with the rest in its tooltip.

### Progress line and log line
- **Progress:** `aghouse set-progress` draws a Caption label ("Build 65%", or just "65%") over a 2px line: Control Stroke track, Secondary Text fill. It is a stock WPF ProgressBar with a quiet template, so screen readers get the value. `clear` removes it.
- **Log:** the newest `aghouse log` line in Caption, Secondary Text, one line. A `warn` or `error` line gets the caution glyph (`E7BA`, `SystemFillColorCautionBrush`), the same treatment as the settings problem in the footer. The tooltip holds the last 20 lines, newest first.
- Status, progress and log are runtime state and are never saved: a restored session has no agents running yet.
- **Rename:** the name swaps in place for a compact Fluent text box (padding `4,1`). Enter commits, Esc cancels.
- **Context menu:** Rename, Split right, Split down, separator, Move up, Move down, separator, Close workspace. Each item that has a shortcut shows it as input-gesture text.

### Pane frame (signature component)
The frame around each terminal. It's the main place attention shows up. Its three states are told apart by shape and words, not by colour alone, because an accent outline means "focus" everywhere else in Windows.
- **At rest:** 1px Control Stroke border, 4px radius, background matching the terminal. Header text in Secondary Text.
- **Focused** (only when the workspace is split): 1px Primary Text border, header text in Primary Text at 600 weight. A lone pane never gets a focus border, because there's nothing to tell it apart from.
- **Needs you:** 2px accent border, plus the header gets a faint accent tint (the accent brush at 16% opacity, on its own layer so the text stays at full opacity) and says **Needs you** (600, Primary Text) followed by "· <agent's message>" in Secondary Text. The full message is in a tooltip. Needs-you wins over focused, and both clear together when the user reaches the pane.
- **Header** (when split): 26px strip with the pane's name in Meta type, the attention text when present, and, on the right, the text size readout and zoom note when they apply, then a ghost ✕. Clicking the header focuses the pane. When the strip is too narrow for both, the name trims first (down to about eight characters), because the request is what the user came to read.
- **Lone pane header** (only while it needs you): the same strip with just "Needs you · <message>": no name, since the sidebar row names the workspace, and no ✕. It disappears when the pane is handled. This keeps the Say-It Rule on lone panes at the cost of one terminal row while the pane waits.
- **Tab strip** (a pane with two or more tabs): the 26px header holds the tabs and a ghost "+" right after the last one. Tabs keep their natural width up to 180px; when crowded they share the width and trim, except a tab that needs you, which keeps its full width (up to 60% of the strip) so "Needs you" is never cut to "Ne…".
  - **Tab at rest:** Meta type, Secondary Text, transparent; hover gets the tertiary subtle fill.
  - **Active tab:** subtle fill, Primary Text at 600, and the only ✕ in the strip, like Fluent's TabView. Never the accent.
  - **Tab that needs you:** the 16% accent tint and "**Needs you** · <name>", and the pane frame takes the 2px accent ring even while that tab is in the background. Showing the tab clears both.
  - **Mouse:** click shows a tab, middle-click closes it, double-click renames it in place (Enter commits, Esc cancels, an empty name goes back to the derived one). Its context menu: Rename tab, Close tab, Close other tabs.
- **Pane name:** a name the user gave the tab, else the title the running program set (an agent's current task, "vim", a cmd command), else the folder, else the shell ("Windows PowerShell"). A folder that only repeats the workspace's name is skipped for the shell's name, and names still shared by siblings get "(2)", "(3)" in layout order. The same name is used in confirmations and screen-reader announcements, so three panes in one repo never all read "aghouse".

### Confirmations
The stock Windows message box, never a custom dialog. It's used only before something irreversible happens to running work, such as closing a pane or workspace, or quitting aghouse, while a program is still running. Quitting lists every running program and where it runs, up to five. An idle prompt closes without asking.
- **Wording:** names the program and the place, then asks the question: "PING.EXE is still running in “aghouse”. Close the pane and end it?"
- **Buttons:** Yes / No with the warning icon. **No** is the default button, so a stray Enter never ends a process.

### Sidebar footer status
One Caption line beside the settings gear. It is a ghost button (transparent, Fluent hover fill) that opens what its state is about. It shows the most important of four states:
- **Settings problem:** settings.json couldn't be parsed. A caution glyph (`E7BA`) and "settings.json has an error on line N" in Primary Text. The tooltip has the parser's message and says the app is running on defaults until the file is fixed and aghouse restarts. The broken file is never overwritten. A click opens the file.
- **Waiting:** "2 waiting · Ctrl+Shift+U" in Primary Text, counting panes that need you across all workspaces. A click opens the waiting list.
- **Setup** (until Claude Code is connected or the first notification arrives): "Connect Claude Code so it can say when it needs you" in Secondary Text, allowed to wrap. A click opens Settings › Integrations. The first notification of any kind retires it for good (`notifiedOnce` in session.json).
- **Idle:** "Ctrl+Shift+U  jump to a waiting pane" in Secondary Text. A click opens the waiting list, for its Earlier section.

The gear's tooltip says "Settings (Ctrl+,)".

### Command palette (Ctrl+Shift+P)
An owned flyout window, 600px wide, centred 44px below the top of the main window, like Windows Terminal's.
- **Search box:** Body type, with the hint "Search actions, workspaces and panes" in Secondary Text while empty.
- **With nothing typed:** grouped lists under small section labels (12/600, Secondary Text). **Waiting** comes first and only appears when something waits. Each of its rows reads "**Needs you** · workspace › pane" with the request in Meta type. Then **Workspaces** (with branch and location, and Ctrl+1…9 as gesture text), **Panes** (only in workspaces with more than one), and **Actions**, each with its shortcut right-aligned in Caption type like a menu's input gesture text.
- **Typing:** one ranked list. A match at the start of a title beats one at the start of a word, which beats one anywhere, which beats a match in the details. A waiting pane wins a tie, and a bare number picks that workspace.
- **Keys:** Up/Down (wrapping), PageUp/PageDown, Enter runs the item, Esc closes. Losing focus closes it too. Focus goes back to the terminal it came from.

### Waiting list (Ctrl+Shift+I, or the footer)
An owned flyout window, 380px wide, opening upward from the footer line it grew out of.
- **Waiting:** every pane that needs you, newest first. Each row has "workspace › pane" in Body-small Primary Text, "**Needs you** · request" under it in Meta type, and its age ("now", "4 min", "2 h") in Caption at the right. A one-line hint, "Ctrl+Shift+U jumps to the newest", sits beside the heading while something waits. Empty: "Nothing waiting. A pane that needs you shows up here."
- **Earlier:** the last 20 handled requests, all in Secondary Text, with the request but never "Needs you". This section is runtime only and hidden while empty.
- Click or Enter goes to the pane. The selected row takes the subtle fill.

### Find bar (Ctrl+Shift+F)
It takes over the pane's 26px header row while open. The tab strip and ✕ step aside, but a pane-zoom note stays. It holds a compact Fluent TextBox (max 320px), a Caption count ("3 lines", "No matches", "200+ lines") and a ghost ✕. Matching lines drop down in a flyout popup under the header. Each line is in the terminal font, because it is terminal output, with "×3" in Caption when the same line appeared more than once. Up/Down move through the lines, Enter copies the selected one (the count says "Copied"), and Esc closes and returns focus. It searches aghouse's plain-text mirror of the output, because the terminal control has no search of its own.

### Pane zoom note and text size
- **Pane zoom** (Ctrl+Shift+Enter): the zoomed pane fills the workspace. On the right of its header, "Zoomed · 2 hidden panes" in Meta, Secondary Text. When a hidden pane needs you it becomes "**Needs you** in GitHub (2) · Ctrl+Shift+U". Clicking it shows all panes again. There is no accent ring on the zoomed pane for a hidden pane's request, because the ring means *this* pane.
- **Text size** (Ctrl+= / Ctrl+- / Ctrl+0, Ctrl+wheel over a pane): per pane, runtime only. While it isn't 100%, the header shows "118%" in Caption, Secondary Text (only where the pane has a header).

### Settings window (Ctrl+,)
A normal Fluent window with Mica and the system title bar, 780×600 and resizable, owned by the main window.
- **Nav:** a 200px list of General, Appearance, Notifications, Integrations and About, each with a Segoe Fluent Icons glyph. The rows look like the sidebar's: subtle fill, accent pill on the selected page, two-stroke focus ring.
- **Pages:** a 14/600 page title, then rows. Each row has a 14px label with Meta-type help under it in Secondary Text, the stock control on the right, and a hairline Divider under it. No cards.
- **Behaviour:** every change applies at once and is written to settings.json, keeping keys aghouse doesn't know. A broken settings.json shows a Fluent InfoBar (caution background and glyph, "Edit settings.json") and makes every control read-only.
- **Integrations:** Claude Code's state in words ("Connected…", "Partly connected: Stop doesn't…", "Not connected…", "isn't valid JSON…"), and **Connect Claude Code**, which shows a stock MessageBox listing exactly what will be added and asks first (Yes / No, No by default).
- **About:** the 48px mark, "aghouse", "Version 0.1.0 · 03d5770", the one-line description, neutral underlined links (GitHub, Releases), "aghouse doesn't check for updates…", MIT License, and buttons for the logs folder and settings.json.

### Welcome screen (first launch only)
Shown in the terminal area when there is no session.json, before any terminal exists, so it is ordinary WPF. It is a 520px column centred in the area: the 40px mark and "aghouse", then "Run coding agents side by side" (14/600) with one Secondary sentence, then **Open a terminal in Home** (default button) and **Choose a folder…**. Below a Divider comes Claude Code's state with **Connect Claude Code**, and below another, "Keys worth knowing": six shortcut rows with the keys in Caption, Secondary Text. It goes away for good when the first workspace opens.

### Reordering workspaces
Drag a row. A 2px rounded line in Primary Text (never the accent) marks the gap where it will land. The row's context menu also has **Move up** and **Move down**, and its **Close workspace** shows Ctrl+Alt+W.

### Attention dot
A 10px accent circle with a faint 1px halo ring. It sits at the right edge of the sidebar row, level with the workspace name, so it can't be mistaken for the left-edge selection pill. It appears only while a pane in the workspace needs you. Together with the pane's Needs-you state, it is the whole attention vocabulary inside the app. Toasts and the taskbar flash carry it outside the window.

### Named Rules
**The Say-It Rule.** Attention is never shown by colour alone. Wherever the accent marks a pane that needs you, the words "Needs you" appear with it. Focus uses neutral brushes, so accent never means "where your keystrokes go".

**The One-Word Rule.** Attention has one vocabulary. A pane's state is "Needs you"; a count is "waiting" ("2 waiting", "jump to a waiting pane"). "Unread", "alert" and "attention" never appear in the UI; "unread" survives only as a field name in the CLI's JSON, where renaming would break scripts. An agent that is busy is "Working" ("2 working"): a neutral chip, never the accent, and it clears the moment the agent asks for you, so "Working" and "Needs you" never show for the same pane.

## Do's and Don'ts

### Do:
- **Do** bind every UI colour to a Fluent theme brush (`DynamicResource ...Brush`) so light, dark and accent follow Windows.
- **Do** use stock Fluent controls as they are. Build a custom template only when WPF lacks the control, as the workspace list does with its NavigationView-style item.
- **Do** use one Segoe Fluent Icons glyph (10–13px) for chrome actions, with a tooltip that names the shortcut.
- **Do** put attention around panes (the 2px accent frame plus the "Needs you" header) and beside workspaces (the accent dot at the row's right edge), never over the terminal.
- **Do** show focus with neutral brushes only (a 1px Primary Text border and bold header text).
- **Do** match a pane frame's background to its terminal palette so frame and terminal read as one surface.
- **Do** keep chrome text at 11–14px and build hierarchy from weight and the three text brushes.
- **Do** let agents and scripts fill the sidebar (chips, progress, log) through the CLI, and keep every such line absent until it has something to say.

### Don't:
- **Don't** make it look like a web app in a frame: no custom title bar, web fonts, CSS-style cards, or Electron-style chrome.
- **Don't** drift toward a hacker or neon terminal: no green-on-black, glow, scanlines or "cyber" styling.
- **Don't** add busy IDE chrome: no toolbars, status bars or extra panels that take space from the terminals. The only tab strip is a pane's own 26px header, and only while it holds two or more tabs.
- **Don't** colour status: no green "open", purple "merged", red error chips or accent progress bars. Words and the caution glyph carry meaning.
- **Don't** splash brand colour across the UI. Logo sky, deep blue and amber stay in the logo and icon.
- **Don't** use the accent for anything except selection and attention, and never use it to show focus.
- **Don't** show attention by colour alone. The accent ring always comes with the words "Needs you".
- **Don't** add drop shadows, glows, gradients or custom acrylic to the chrome.
- **Don't** hard-code hex colours in UI code. Only the terminal palettes may.
