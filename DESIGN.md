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
  confirm-dialog:
    backgroundColor: "LayerFillColorAlt over Mica; footer band on Mica"
    typography: "20px / 600 heading, {typography.body} body"
    padding: "20px 24px 24px 24px"
    width: "460px"
---

# Design System: aghouse

## Overview

**Creative North Star: "The Native Workbench"**

aghouse is a set of plain Windows tools with nothing dressed up. The built-in WPF Fluent theme does the work: Mica behind the window, the system title bar, the user's own accent colour, Segoe UI text and Segoe Fluent Icons glyphs. The terminals are the content. The window chrome is a narrow sidebar and thin frames around panes. Nothing else competes for space or attention.

The system is quiet on purpose. It is almost entirely made of theme resources rather than fixed colours, so it follows the user's light/dark setting and accent and looks like it shipped with Windows. Fixed colours appear in only two places: the colour-scheme tables, because the terminal is a native control that can't read WPF brushes, and the logo. The default theme, "Windows", is exactly that native look. A user who picks a scheme theme (Dracula, Solarized, their own) opts into a window coloured by that scheme, but even then every colour comes from the scheme's table through fixed rules, never from hand-picked chrome hex. Density is that of a desktop power tool: compact rows, 12px metadata, and a terminal font at 11pt by default.

aghouse never looks like a web app in a frame, never like a neon "hacker" terminal, never like busy IDE chrome, and never paints brand colour across the UI.

**Key Characteristics:**
- Fluent theme resources everywhere (`DynamicResource` brushes), so light, dark and accent changes follow Windows.
- Flat: Mica, subtle fills and 1–2px strokes. No shadows.
- One accent colour (the user's Windows accent, or the scheme's under a scheme theme) for both "selected" and "needs you", told apart by shape and words. Focus is always neutral.
- Paired themes: a dark theme and a light theme, with Mode (follow Windows, Dark or Light) choosing which shows. "Windows" is the default for both.
- Terminals fill the window. Chrome is a resizable sidebar (Workspaces above, Agents right under them, foldable to a 48px rail) plus pane frames.
- Workspaces wear a letter tile coloured from the theme's own ANSI table: the one place hue appears in the chrome besides the accent, and never with meaning.
- Small Segoe Fluent Icons glyphs for every control. No text buttons in the chrome.

## Colors

Under the default "Windows" theme, the palette is the Windows theme itself plus two terminal palettes chosen to sit quietly next to Mica. Everything in Primary and Neutral below describes that theme. Scheme themes keep every one of these roles and brushes, but derive their values from the scheme (see Themes).

### Primary
- **System Accent** (`AccentFillColorDefaultBrush`; reference value `system-accent`, the dark-theme fill for Windows' default blue): the user's own Windows accent. It is the only chromatic colour in the chrome. It marks the selected workspace (a 3×16 pill), workspaces with a pane that needs you (a filled dot with a faint halo), and panes that need you (a 2px frame). Never hard-code it. Always bind the brush so the user's accent wins.

### Neutral
- **Mica Backdrop** (`mica-dark` / `mica-light`): the window background under the Windows theme, provided by Windows 11's Mica material. Under that theme the app never paints its own window background; scheme themes paint it solid.
- **Primary Text** (`TextFillColorPrimaryBrush`; `text-primary-dark`): workspace names (inherited by default), the focused pane's 1px border and header title, and "Needs you".
- **Secondary Text** (`TextFillColorSecondaryBrush`; `text-secondary-dark`): the "Workspaces" and "Agents" labels, sidebar icons at rest, the git branch, the workspace after an agent's task, agent ages, quieted agent marks, the agent's waiting message (in a Needs-you pane header and under the workspace name), header names of panes at rest, and the footer's setup and idle hints.
- **Tertiary Text** (`TextFillColorTertiaryBrush`; `text-tertiary-dark`): not used for text anyone has to read. It is about 3.4:1 on the light terminal background and on light Mica, under the 4.5:1 floor, so pane headers at rest and the footer hint moved to Secondary.
- **Subtle Fill** (`SubtleFillColorSecondaryBrush`; `subtle-fill-dark`): background for hovered and selected sidebar items.
- **Control Stroke** (`ControlStrokeColorDefaultBrush`; `control-stroke-dark`): the 1px frame around inactive panes and the track of the progress line.
- **Flyout surface** (`FlyoutBackground`, edge `SurfaceStrokeColorFlyoutBrush`): the command palette, the waiting list and the find results, Fluent's own menu surface.
- **Divider** (`DividerStrokeColorDefaultBrush`): the hairline under each setting and between the welcome screen's sections.
- **Caution background** (`SystemFillColorCautionBackgroundBrush`): only the Settings InfoBar for a broken settings.json, with the caution glyph. It is the same semantic exception as the footer's.

The `-dark` values above are reference values for the dark theme. The brushes are the source of truth.

### Terminal palettes
These are fixed because the terminal is a native child window with its own renderer.
- **Terminal Night** (`terminal-night` on `terminal-night-ink`, selection `terminal-night-selection`): the dark terminal of the "Windows" theme, using Windows Terminal's Campbell 16-colour table. The pane frame's background matches it so the frame and terminal read as one surface.
- **Terminal Paper** (`terminal-paper` on `terminal-paper-ink`, selection `terminal-paper-selection`): the light terminal of the "Windows" theme, using the One Half Light 16-colour table.
- Under a scheme theme, the terminal takes the scheme's own background, foreground, selection and 16 colours, and the pane frame still matches its background.

### Themes
Settings › Appearance has **Mode** (`theme`: `system` / `dark` / `light`), **Dark theme** (`darkTheme`) and **Light theme** (`lightTheme`). Mode decides which of the pair shows. Changing any of them, or switching Windows between light and dark while Mode follows Windows, recolours the window, every pane and its frame without a restart.
- **Windows** (default for both): the native look described above. Mica, Fluent's brushes, the user's accent, and Terminal Night / Terminal Paper.
- **Built-in schemes** (`AgHouse.Core/Themes/BuiltInSchemes.cs`). Dark: Campbell, One Half Dark, Solarized Dark, Tango Dark, Gruvbox Dark, Tokyo Night, Catppuccin Mocha, Dracula, Nord. Light: One Half Light, Solarized Light, Tango Light, Gruvbox Light, Catppuccin Latte, Tokyo Night Day. The tables are the published ones. Each declares an accent from its own palette, and tests hold it to 3:1 on the window and the terminal.
- **Custom schemes:** the `schemes` array in settings.json, in Windows Terminal's format plus an optional `accent`. **Import from Windows Terminal** (or `aghouse themes import`) copies Windows Terminal's own schemes in once; later changes there don't follow. A scheme that can't be used is skipped, and Appearance names it with the caution glyph. A picked name that no longer exists shows as Windows.
- **Derivation** (`ChromePalette`): Mica is turned off and windows are painted solid. The **window surface** is the terminal background moved 4% toward the foreground. **Flyouts** are dark schemes' background at 9%, or light schemes' terminal background. **Primary text** is the foreground, pushed to 4.5:1 on the surface if it falls short. **Secondary text** is the foreground at the lowest opacity that still reaches 4.5:1. Every other Fluent grey (fills, strokes, dividers, control states) is re-expressed at the same position on the surface-to-text line, alpha kept. The **accent** is the scheme's, else its cursor, bright blue or blue: the first that isn't grey and reaches 3:1 on both window and terminal. The system **title bar** takes the surface and text colours (Windows 11).
- **Contrast themes** (Windows high contrast) always win: schemes are ignored and the Windows theme shows. Pane frames then take the contrast theme's window colour, not the terminal's background, because the header text comes from the contrast theme and has to read on it.
- **Low contrast:** a scheme whose own terminal text is under 4.5:1 (Solarized Light is one) is still offered, marked "low contrast" in the picker and in `aghouse themes`.
- **Preview:** under the pickers, two small WPF drawings of aghouse, one per theme, side by side. Each shows the sidebar with the selected workspace's pill and a waiting workspace's dot, and a Needs-you pane running a few lines in the scheme's colours. The one on screen says "showing now". The picker rows show the scheme's background with six ANSI swatches and an accent dot.

### Tile colours
Each workspace's letter tile (and the generic glyph of an agent the Agents list can't name) takes one of six hues from the theme on screen's ANSI table: blue, green, red, purple, cyan, yellow (`SpaceTiles`, published by `AppTheme` as `SpaceTile0Brush`..`SpaceTile5Brush` and `SpaceTileInk0Brush`..). Under the Windows theme that is Campbell's or One Half Light's table. Dark themes prefer the bright half, light themes the normal half; the other half stands in for a grey (Solarized's brights) or a colour that can't carry a letter. The letter is the scheme's foreground or background, whichever reads better, pushed to 3:1. The hue is picked once, when the workspace is created (`SpaceTiles.Pick`): the one its name hashes to (FNV-1a) while no other workspace wears fewer, else the least-worn hue after it, so no two tiles match until all six are in use. It is saved in session.json (`tile`), so it stays put across launches and renames; a session from before this keeps its hashed hues where they don't clash. Tiles carry identity only: never state, never attention. A Windows contrast theme draws tiles in Window Text.

### Logo colours
- **Sky** (`logo-sky`), **Deep Blue** (`logo-deep-blue`), **Amber** (`logo-amber`): used only in the logo and icon assets (`assets/`). The logo is a prompt, `>_`, on a sky-to-deep-blue tile; its amber cursor is the logo's picture of attention, the agent waiting for you. Inside the app, attention uses the accent of the theme on screen.

### Named Rules
**The Borrowed Colour Rule.** The chrome has no colours of its own. Every surface, text and stroke colour in the UI is a Fluent theme brush, and the only chromatic colour is the accent. The Windows theme borrows those brushes from Windows. A scheme theme overrides the same brushes app-wide with values derived from the scheme's table, so UI code still binds only to Fluent brush keys and never knows which theme is on. A hex value in UI code is a defect. Hex appears only in scheme tables, and in the Settings preview and picker swatches, which draw scheme data. There are three exceptions. One is semantic: the warning glyph uses Fluent's `SystemFillColorCautionBrush`, exactly as a Windows InfoBar would. The other is the workspace tiles (see Tile colours), the only place ANSI colours reach the chrome. Those colours still come from the scheme's table by a fixed rule, never from a hand-picked hex, and they mark which workspace a row is, never its state. The third is the agent logos in the Agents list (see Agents list): Claude Code's keeps its terracotta (`#D97757`) and Amp's its red (`#F34E3F`), because that colour is part of the logo. They are fixed hex in the mark templates only, mark which agent a row is, never its state, and read on both light and dark backgrounds. When every listed agent is that same program the colour says nothing, so it steps down to Secondary Text (see Agent marks).

**The One Accent Rule.** The accent marks only selection and attention. Never use it for decoration, headings, icons at rest, or hover states. That includes the cmux-style metadata and the Agents list: the progress line, PR state, the Working and Idle marks and the active tab are all neutral, so the accent still means only "selected workspace" or "Needs you" at a glance. The same goes for the newer surfaces. The selected row in the palette, the waiting list and the find results takes the subtle fill, not Fluent's default accent selection. Links in About are neutral and underlined. The only accent outside the main window is the selected page's pill in Settings' nav, which is selection. Stock control states (a checked CheckBox, a focused TextBox's underline) stay as Fluent draws them.

## Typography

**UI Font:** Segoe UI Variable (with Segoe UI), inherited from the Fluent theme and not set anywhere in code
**Terminal Font:** Cascadia Mono, 11pt by default, configurable in `settings.json`
**Icon Font:** Segoe Fluent Icons (with Segoe MDL2 Assets), the `IconFont` resource

**Character:** This is the operating system's own voice. Segoe in the chrome and Cascadia in the panes are exactly what a Windows Terminal user already reads all day.

### Hierarchy
- **Row** (400, 13px, primary text): workspace names in the sidebar, trimmed with an ellipsis, and the "Settings" row.
- **Section Label** (400, 12px, secondary text): "Workspaces" and "Agents", and the Agents list's state headings ("Idle" then the count at 600).
- **Body** (400, 14px): Fluent default for menus, tooltips and the rename text box.
- **Meta** (400, 12px, secondary text): git branch, PR, cwd, the waiting message, pane header text and tab names.
- **Caption** (400, 11px): the footer status line at the foot of the sidebar (secondary text for the setup and idle hints, primary for the waiting count and a settings problem), agent ages.
- **Icon** (Segoe Fluent Icons, 10–14px): add (`E710`), more (`E712`), search (`E721`), fold / open the sidebar (`E700`, the GlobalNavButton hamburger Windows uses for its own sidebars, in both states; the tooltip says which way), history (`E81C`), agent, when it can't be named (`E99A`), chevron (`E70D`), settings (`E713`), close (`E711`).
- **Tile letter** (600, 11px): the capital on a workspace tile.

### Named Rules
**The System Voice Rule.** Never load a web font or a custom display face. Hierarchy comes from weight (400 vs 600) and from the three text brushes, not from size jumps. The whole UI spans 11–14px, except two 20px headings from WinUI's type ramp: the confirmation dialog's title and the Settings page title, which has to outrank the 14px row labels under it. These are the sizes at 100%: the ramp lives in App.xaml as `TypeCaption`, `TypeMeta`, `TypeRow`, `TypeBody` and `TypeTitle` (bound with DynamicResource, never a literal), and `Typography` scales it, and the stock Fluent controls' text, by Windows' Settings › Accessibility › Text size, live. Icons, tile letters, agent monograms and the Settings preview drawings keep their size, as Windows' own icons do.

## Layout

The window has three columns: the sidebar (248px by default, resizable from 170 to 480px), a 4px invisible splitter, and the terminal host, which fills the rest. The window defaults to 1280×800, with a minimum of 640×400. Outer margins are 8px on the window edges and 4px where the two regions meet (sidebar `8,4,4,8`, host `2,4,8,8`).

The sidebar stacks four bands, after Herdr. It has no mark or name of its own: the system title bar right above it already shows both, and a second copy only repeated it.

1. **Header:** "Workspaces" (Section Label; the UI has one name for them, the one the menus, tooltips and CLI use), then four ghost icons on the right: search (opens the command palette), **⋯** (New workspace with › a shell; Group by project; Collapse / Expand all groups), **+** (new workspace) and fold.
2. **Workspace list:** as tall as its rows, so Agents sits right under it, up to what the header, Agents and the footer leave (`MainWindow.FitWorkspaceList`); past that it scrolls. Whatever height is over collects above the footer, never between the two lists, so a short list doesn't strand Agents at the bottom behind an empty band.
3. **Agents:** a hairline Divider, "Agents" with a ghost history icon (the waiting list), then the agents under their state headings. It is as tall as its rows, up to 45% of the sidebar, and scrolls past that. With no agent running it says "No agents running. Start Claude Code, Codex or OpenCode in a pane and it shows up here."
4. **Footer:** a hairline Divider, the one-line status, then the **Settings** row (gear and word).

A workspace row is one line: the 20px tile, the name (Row type), and on the right the git branch (Meta, Secondary Text, at most 110px) and the attention dot. The name takes the room it needs first and the branch trims into what's left, because the name is the row's identity. "main" and "master" don't show at all: on every row they're noise (the tooltip still names the branch). Two more lines appear only when they have something to say:

1. **Needs you** · the first waiting message (asking before finished, then newest), under the name, only while a pane needs you. It may wrap onto a second line and trims after that, because the request is what the user came to read.
2. Progress: a 2px line under the name, the width of the row.

Everything else a row used to show sits in its tooltip, one fact per line: the name, the cwd, the branch with its PR ("main · PR #123 open"), `aghouse set-status` entries, listening ports, the progress label and the newest `aghouse log` line. Agent state ("Working") moved to the Agents list. Ports also open from the row's right-click menu ("Open localhost:5173").

**Rail** (Ctrl+Shift+B, the fold button, `aghouse run toggle-sidebar`): the sidebar folds to 48px. The rail holds the open button, **+**, a column of tiles (the selected one keeps its fill and pill, and a waiting one wears a 10px accent dot ringed in the window colour on its top-right corner), and the gear. Group headers, the Agents list and the footer status hide; every tile shows, even from a collapsed group. The waiting list then opens from the gear. The rail and the open width are saved in session.json.

**Grouped sidebar** (`groupWorkspaces`, **Group by project** in the Workspaces **⋯** menu, `aghouse run toggle-groups`): a header per project above its rows, and 4px above and below each project that has a header, so it reads as one block; lone rows space like any row. The group body is an Expander re-templated to this quiet header, because WPF's group automation peer then offers expand and collapse to screen readers. A project with one workspace has no header, because it would only repeat the row's name: the row stands alone, can't be collapsed, and drags as its whole group. The header appears once a second workspace joins. The header has a 9px chevron, the project name in Meta at 600 in Secondary Text, and the count in Secondary Text. It gets the subtle fill on hover. Collapsed, the header must still say what it hides, the same rule as the zoom note: on the right, "2 waiting" (600, Primary Text) or else "Working" (Secondary Text), then the 10px attention dot while a hidden pane needs you. While a group is collapsed and holds the selected workspace, its header carries the selection pill and the subtle fill. Expanded, the header shows only the name and the count, because the rows speak for themselves. Dragging, the 2px neutral insertion line goes between rows of the dragged row's own group, or between groups for a dragged header or lone row.

**Pane drag preview:** dragging a pane's header over another pane shows the drop as a Popup above the terminals. It covers the half the pane will take, or the whole target with "Swap with <name>" for a swap. The fill is Primary Text at 12% with a 2px Primary Text outline and a 4px radius, neutral like the insertion line. The cursor is the move cursor.

Each workspace is a tree of split panes built from nested Grids. There is a 6px invisible splitter between panes, and each pane has a minimum size of 60px wide or 40px tall. A pane can hold several terminals as tabs; only the active one is visible, the rest keep running behind it. Every pane has a header strip (26px high) above its terminal, split or not. On a pane with two or more tabs it is the tab strip.

The spacing rhythm is small steps of 1, 2, 4, 6, 8 and 10px. Workspace rows are padded `8,6,8,6` with 10px between tile and name, agent rows `6,4`, and there is 1px between rows.

### Named Rules
**The Airspace Rule.** The terminal is a native HWND, and WPF can never draw on top of it. Every indicator, header or overlay sits beside or around a pane, never over it. Attention is a frame around the pane, not a badge on it. Something that has to float above the terminals is its own window, the way a menu or tooltip is: the palette and the waiting list are owned flyout windows, and the find results are a popup. Such surfaces are transient, take focus while open, and hand it back to the terminal when they close. Nothing persistent ever floats over a pane.

## Elevation & Depth

aghouse is flat. There are no drop shadows anywhere in the app. Depth comes from the Windows 11 Mica backdrop behind the window, the slightly different shade of the terminal surface (`terminal-night` against Mica), subtle fills on hovered and selected rows, and 1–2px strokes around panes. Menus, tooltips and the context menu use the Fluent theme's own popup styling unchanged.

### Named Rules
**The Mica-Only Rule.** The window's material belongs to Windows. Don't add shadows, glows, gradients or acrylic panels to the chrome. A new layer of depth must come from a Fluent brush, not from a custom effect.

## Motion

Motion is rare and always says something. It lives in `Motion.cs` (`Motion.Entrance` attached property in XAML, `Motion.Breathe` and `Motion.Arrive` from code), uses WinUI's timings (167ms fast, 250ms normal) and its decelerate curve (`0,0,0,1`), and follows Windows' Animation effects setting (`SystemParameters.ClientAreaAnimation`): off, and everything simply appears in its final state.
- **Needs you (the one authored moment):** the 2px ring and the words arrive instantly. The header tint (pane or tab) swells to about 2.5× its 16% and settles over 700ms. In the sidebar the dot scales in from 40%, and its halo ripples out to 1.6× and fades back. Then every young halo ripples together every 3 seconds, on one shared timer, for 90 seconds after its request arrived (`Motion.PulseFor`, counted from the workspace's `WaitingSince`), and then rests: the dot, its still halo and the words stay until the pane is reached. A request the user is leaving on purpose (often a finished agent) stops tugging at the eye; a new request from the same pane ripples at once and starts another 90 seconds. The timer stops when no halo is young. Only the workspace row (and a collapsed group's header) pulses: the Agents list's Needs-you dot keeps its halo still, because the row above already ripples for the same request. The rail's dot has no resting halo; its ring appears only as it ripples.
- **Selection:** the accent pill grows from its centre (167ms), in the sidebar, its group headers and Settings' nav.
- **Flyouts:** the content arrives 8px from the edge it opened from (the palette drops in, the waiting list rises from the footer) and fades up, 167ms. The window itself appears at once, so typing never waits.
- **Never animated:** anything that resizes a terminal (folding the sidebar, splits, zoom, frame thickness), because each frame would resize ConPTY; the terminals themselves; hover fills. Nothing animates when a row or a container is merely being built, only when a live element changes state.

### Named Rules
**The Asking-Pulse Rule.** The only repeating motion in the app is the halo of something that needs you, and only for its first 90 seconds, in one place per request. Working agents, progress, a request that has had its minute and a half, and everything else stay still.

## Shapes

Gently rounded corners on everything interactive: 4px on sidebar row backgrounds and pane frames, and Fluent's own radii on buttons and menus. The selection pill is a 3×16 capsule with 1.5px radius. The attention dot is a 10px circle with a 1px halo ring 3px outside it at 45% opacity. The Needs-you header tint uses the inset radius (3px), one step smaller than the frame's, so it nests inside the 4px frame. Pane frames are 1px at rest and when focused, and 2px when they need you. The padding adjusts by the difference so the terminal never shifts.

## Components

### Buttons
Stock Fluent and quiet.
- **Shape:** Fluent default radius.
- **Ghost buttons** (every sidebar control: search, fold, ⋯, +, history, the footer status, Settings; and the pane close ✕): transparent background, no border, a 4px subtle fill on hover. Sidebar icons rest in Secondary Text at 12–14px. The pane header's buttons (✕, the tab strip's + and the active tab's ✕, find's ✕) use the same Ghost style with a 10px glyph in a target of at least 24×24 (the tab ✕ is as tall as its tab). They can't take focus, so they never steal focus from the terminal.
- **Every icon button has a tooltip naming the action and its shortcut** ("New workspace (Ctrl+Shift+N)").

### Navigation: Workspaces (the workspace list)
One line per workspace, after Herdr.
- **Rest:** transparent. The tile (20px, 5px radius, Tile letter), the name in Row type, the branch on the right in Meta.
- **Hover:** subtle-fill background on the row's 4px-rounded rectangle, the same shape the focus ring draws.
- **Selected:** the same subtle fill, plus a 3×16 accent pill at the left edge, level with the tile (8px from the top), so it still marks the name when "Needs you" makes the row taller.
- **Focus:** keyboard focus gets Fluent's two-stroke focus ring (`FocusStrokeColorOuterBrush` 2px over `FocusStrokeColorInnerBrush` 1px) on the row's 4px shape. It's neutral, never the accent, and only appears for keyboard focus (Ctrl+Arrow moves it without changing the selection).
- **Tooltip:** after 700ms, the row's facts one per line (see Layout). It replaces the meta line, chips and log line rows used to carry.
- **Accessibility:** each row's automation name is the workspace name, its help text is the location followed by the branch, PR, status entries, ports, progress and log line, and its item status is "Needs you: <message>" while a pane waits. Icon-only buttons carry an automation name ("New workspace", "Open settings", "Close pane"), not just a tooltip. When a pane starts needing you, the window raises a UIA notification ("aghouse, Windows PowerShell (2) needs you: Waiting for approval"; just the workspace name when it has one pane) so screen readers hear what sighted users see.
- **Name:** a new workspace is named after its folder ("aghouse"; "Home" for the user's profile folder), with "(2)" when that name is taken, so `select-workspace` by name stays unambiguous.
- **Waiting line:** the second line, **Needs you** (600, Primary Text) then "· <message>" in Secondary Text, in Meta type: the first waiting pane's, in the waiting order (asking before finished, then newest). It is derived from the waiting panes, so it disappears as soon as nothing is waiting; the sidebar never shows a request that was already handled. A pane that rang its bell without a message shows just "Needs you". The tooltip lists every waiting message in that order.
- **PR:** "#123 open", "#123 draft", "#123 merged" or "#123 closed", from `gh pr view` for the active pane's branch, after the branch in the row's tooltip. State is said in words, never in GitHub's green or purple. No `gh`, signed out, or no PR: nothing shows.
- **Ports:** TCP ports in LISTEN state owned by any process under the workspace's shells (not the shell itself), checked every 3 seconds, listed in the tooltip and opened from the row's menu ("Open localhost:5173", at the top). WSL processes aren't visible to Windows, so their ports don't show.
- **Progress:** `aghouse set-progress` draws a 2px line under the name: Control Stroke track, Secondary Text fill. Its label ("Build 65%") is in the tooltip. It is a stock WPF ProgressBar with a quiet template, so screen readers get the value. `clear` removes it.
- **Status and log:** `aghouse set-status` entries and the newest `aghouse log` line are tooltip lines. Status, progress and log are runtime state and are never saved: a restored session has no agents running yet.
- **Rename:** the name swaps in place for a compact Fluent text box (padding `4,1`). Enter commits, Esc cancels.
- **Context menu:** the workspace's ports, if any, then Rename, Split right, Split down, separator, Move up, Move down, separator, Close workspace. Each item that has a shortcut shows it as input-gesture text.

### Agents list
Every pane running a coding agent, found two ways. The 3-second process scan recognises a known agent program under the pane's shell by its image name (`AgentPrograms`: Claude Code, OpenCode, Codex, Gemini CLI, Copilot CLI, Cursor Agent, Aider and a few more), so an agent without hooks, or Claude Code before its first prompt, is listed within 3 seconds of starting. And an agent that reports in (a Claude Code hook, or `aghouse notify` / `set-status agent` from the pane) is listed even when it runs as a plain node script the scan can't name. Working means the agent said so, or its terminal title starts with a braille spinner frame (Claude Code animates its title while it works); otherwise it is Idle. A row with no title of its own is named after the program ("OpenCode"). An agent drops out once the scan finds nothing left under its shell, unless it is waiting on you. Rows group under state headings in this order, each only while it has rows:
- **Needs you:** in the waiting order (asking before finished, then newest), with the 10px accent dot and a still halo. The heading says it in words, so the Say-It Rule holds.
- **Working:** newest first, with a 7px filled dot in Secondary Text.
- **Idle:** newest first, with a 9px ring in Secondary Text at 1.2px.

A row is a ghost button: the agent's mark (14px; see below), then what tells the rows apart in Primary Text, then " · <workspace>" in Secondary, both 12px. What leads is the task (the title the agent set, without its status glyphs); for an agent that needs you it is the request instead, the same words its workspace row and pane header show, so one waiting pane never reads two ways. With neither, the row is just the workspace name. The text wraps onto a second line before it trims, like the workspace row's waiting line, because the task is what the row is for; the mark, the time in its state ("now", "4 min", "2 h", the waiting list's format, Caption) and the state mark stay level with the first line. A click goes to the pane. The tooltip says "workspace › pane", then "Needs you · <request>" and the task under it, "Working for 4 min" or "Idle for 2 h"; screen readers hear the row's own order, "Fix the build, aghouse, Working, 4 min". The history icon beside "Agents" opens the waiting list. Agent state never appears on the workspace row; the row keeps only "Needs you".

**Agent marks** (`AgentMark` in MainWindow.xaml, picked by the agent's program, `AgentPrograms`): each agent's own logo at 14px, as path markup (sources and licence in `assets/THIRD-PARTY-NOTICES.md`). Claude Code, OpenCode, Codex, Gemini CLI, Copilot CLI, Cursor Agent, Goose, Kiro, Qwen Code and Amp have one. A single-colour logo takes Primary Text, the way an SVG's `currentColor` follows the text, so it suits every theme; Claude Code and Amp keep their brand colour. While every listed agent is the same program (`AgentRow.MarkQuiet`), the mark tells no row apart, so Claude Code's and Amp's step down to Secondary Text instead of repeating their colour down the list; the colour comes back as soon as a second kind of agent joins. Every other known agent (Aider, Crush, Droid, Auggie) gets a 16px rounded square outlined in Secondary Text with a two-letter monogram at 8px/600 ("Ai" Aider, "Cr" Crush), so two agents never look alike. An agent known only because it reported in (a node script the scan can't name) keeps the generic agent glyph (`E99A`) in its workspace's tile colour.

### Pane frame (signature component)
The frame around each terminal. It's the main place attention shows up. Its three states are told apart by shape and words, not by colour alone, because an accent outline means "focus" everywhere else in Windows.
- **At rest:** 1px Control Stroke border, 4px radius, background matching the terminal. Header text in Secondary Text.
- **Focused** (only when the workspace is split): 1px strong neutral border (`ControlStrongStrokeColorDefaultBrush`), header text in Primary Text at 600 weight. Not Primary Text: a white (or black) frame was the loudest edge in the window and outshouted the 2px accent ring, and the ring must always win. A lone pane never gets a focus border, because there's nothing to tell it apart from.
- **Needs you:** 2px accent border, plus the header gets a faint accent tint (the accent brush at 16% opacity, on its own layer so the text stays at full opacity) and says **Needs you** (600, Primary Text) followed by "· <agent's message>" in Secondary Text. The full message is in a tooltip. Needs-you wins over focused, and both clear together when the user reaches the pane.
- **Header** (every pane): a strip at least 26px high (it grows with Windows' text size) with the pane's name in Meta type, the attention text when present, and, on the right, the text size readout and zoom note when they apply, then a ghost ✕ (split panes only). Clicking the header focuses the pane. While the pane can be dragged (the workspace is split and not zoomed, and find is closed), a grip sits centred in the strip: six 2px dots (3×2, Secondary Text) in a 28×20 target with the move cursor, the ghost buttons' subtle fill on hover, and the tooltip "Drag to move this pane". The grip stays centred until the name or tab strip reaches it, then gives way to the right, towards the buttons; only when it reaches them does the name trim. The whole header still drags, so the grip is a sign, not the only handle. When the strip is too narrow for both, the name trims first (down to about eight characters), because the request is what the user came to read.
- **Lone pane header:** the same strip, always shown, so a workspace's only terminal still says what it is running. It has no ✕, because closing the only pane closes the workspace (Ctrl+Shift+W and the sidebar's Close workspace still do that), and no focus state. Its name follows the pane-name rule, so a folder that only repeats the workspace's name gives way to the shell's. It costs one terminal row in every unsplit workspace, on purpose.
- **Tab strip** (a pane with two or more tabs): the 26px header holds the tabs and a ghost "+" right after the last one. Tabs keep their natural width up to 180px; when crowded they share the width and trim. A tab that needs you is never held to 180px: it keeps its full width, up to 60% of the strip, crowded or not, so "Needs you · codex: migrate schema" isn't cut. The request itself stays in its tooltip (the sidebar and the waiting list show it).
  - **Tab at rest:** Meta type, Secondary Text, transparent; hover gets the tertiary subtle fill.
  - **Active tab:** subtle fill, Primary Text at 600, and the only ✕ in the strip, like Fluent's TabView. Never the accent.
  - **Tab that needs you:** the 16% accent tint and "**Needs you** · <name>", and the pane frame takes the 2px accent ring even while that tab is in the background. Showing the tab clears both.
  - **Mouse:** click shows a tab, middle-click closes it, double-click renames it in place (Enter commits, Esc cancels, an empty name goes back to the derived one). Its context menu: Rename tab, Close tab, Close other tabs.
  - **Screen readers:** the strip is a Tab control and each tab a TabItem with its name ("migrate schema, needs you"), whether it is the one showing, and Select to show it. A pane with one tab has no strip to announce.
- **Pane name:** a name the user gave the tab, else the title the running program set (an agent's current task, "vim", a cmd command) without its leading status glyphs (a live spinner, not part of the name), else the folder, else the shell ("Windows PowerShell"). A folder that only repeats the workspace's name is skipped for the shell's name, and names still shared by siblings get "(2)", "(3)" in layout order. The same name is used in confirmations and screen-reader announcements, so three panes in one repo never all read "aghouse".

### Confirmations
One Fluent dialog (`ConfirmDialog`) in WinUI ContentDialog's shape, shown only before something irreversible happens to running work: closing a pane or tab, closing a workspace, or quitting aghouse while a program is still running. An idle prompt closes without asking. It replaced the Win32 MessageBox, which stayed light in dark mode and drew an agent's title glyph as a box.
- **Window:** an owned modal window with the system title bar ("aghouse", ✕ only), 460px wide, sized to its content, centred on the main window. The question area sits on `LayerFillColorAltBrush`; the footer band is plain Mica under a `CardStrokeColorDefaultBrush` hairline.
- **Heading:** the question, 20px semibold, WinUI's dialog title and the one place type goes above 14px: "Quit aghouse?", "Close “aghouse dev”?", "Close this pane?".
- **Body:** one 14px sentence saying what it costs: "Quitting ends these 3 programs. Next time, aghouse reopens your workspaces and folders, not the programs."
- **Rows** (quit and workspace close): the program or task in Body type, "workspace › pane" under it in Meta, Secondary Text (just the pane when the heading already names the workspace). On the right, **Needs you** (600) or the agent's state as the sidebar's neutral chip ("Working"). Waiting agents sort first, then working ones. Five rows, then "and N more" with the rest in its tooltip. Closing a single pane uses the sentence alone ("“Fix the build” is still running here. Closing the pane ends it. It's waiting on you.").
- **Names:** a program's title loses leading status glyphs (Claude Code's `✳`, braille spinners) before it is shown, because they are a live status light, not part of the name. An exe name is said the way people say it: ".exe" dropped and an all-capitals system tool lowered ("PING.EXE" → "ping").
- **Buttons:** two equal-width stock buttons. The action names itself ("Quit and end them", "Close and end it"; Alt+Q / Alt+E) and is never the accent, because ending work must not look like the inviting choice. **Cancel** is the default and has focus, so Enter and Esc both keep the work running. No warning icon: the verb carries the warning.
- Other notices (Connect Claude Code, a settings.json that won't parse, a shell that failed to start) stay the stock MessageBox.

### Sidebar footer status
One Caption line above the **Settings** row (gear in Secondary Text, "Settings" in Row type). It is a ghost button (transparent, Fluent hover fill) that opens what its state is about. It shows the most important of five states:
- **Settings problem:** settings.json couldn't be parsed. A caution glyph (`E7BA`) and "settings.json has an error on line N" in Primary Text. The tooltip has the parser's message and says the app is running on defaults until the file is fixed and aghouse restarts. The broken file is never overwritten. A click opens the file.
- **Waiting:** "2 waiting · Ctrl+Shift+U" in Primary Text, counting panes that need you across all workspaces. A click opens the waiting list.
- **Update:** "aghouse 0.1.3 installs when you quit" once a new version is downloaded and verified, or "aghouse 0.1.3 is available" when it needs a click (updates set to notify, or a copy that wasn't installed). Secondary Text, no accent, no glyph. A background download in progress says nothing. A click opens Settings › About.
- **Setup** (until Claude Code is connected or the first notification arrives): "Connect Claude Code so it can say when it needs you" in Secondary Text, allowed to wrap. A click opens Settings › Integrations. The first notification of any kind retires it for good (`notifiedOnce` in session.json).
- **Idle:** "Ctrl+Shift+U  jump to a waiting pane" in Secondary Text. A click opens the waiting list, for its Earlier section.

The Settings row's tooltip says "Settings (Ctrl+,)". On the rail the status line hides and the row keeps only its gear.

### Command palette (Ctrl+Shift+P)
An owned flyout window, 600px wide, centred 44px below the top of the main window, like Windows Terminal's.
- **Search box:** Body type, with the hint "Search actions, workspaces and panes" in Secondary Text while empty.
- **With nothing typed:** grouped lists under small section labels (12/600, Secondary Text). **Waiting** comes first and only appears when something waits. Each of its rows reads "**Needs you** · workspace › pane" with the request in Meta type. Then **Workspaces** (with branch and location, and Ctrl+1…9 as gesture text), **Panes and tabs** (only in workspaces with more than one; a tab in a pane with two or more says "tab in api-server"), and **Actions**, each with its shortcut right-aligned in Caption type like a menu's input gesture text.
- **Typing:** one ranked list. A match at the start of a title beats one at the start of a word, which beats one anywhere, which beats a match in the details. A waiting pane wins a tie, and a bare number picks that workspace.
- **Keys:** Up/Down (wrapping), PageUp/PageDown, Enter runs the item, Esc closes. Focus stays in the search box, so the selected row is announced to screen readers ("Split right, Ctrl+D, 3 of 40"), as is the top match or "No matches" while typing. Losing focus closes it too. Focus goes back to the terminal it came from.

### Waiting list (Ctrl+Shift+I, or the footer)
An owned flyout window, 380px wide, opening upward from the footer line it grew out of.
- **Waiting:** every pane that needs you, in the waiting order: panes whose agent is asking something (a permission prompt, a question, a bell, an escape code) before panes whose agent finished (Claude Code's Stop and idle reminder, `aghouse notify --finished`), each newest first. Both look the same and say "Needs you"; only the order differs, so a blocked agent is never stuck below one that is done. The palette's Waiting section, the sidebar's waiting line and Ctrl+Shift+U use the same order. Each row has "workspace › pane" in Body-small Primary Text, "**Needs you** · request" under it in Meta type, and its age ("now", "4 min", "2 h") in Caption at the right. A one-line hint, "Ctrl+Shift+U jumps to the top one", sits beside the heading while something waits. Empty: "Nothing waiting. A pane that needs you shows up here."
- **Earlier:** the last 20 handled requests, all in Secondary Text, with the request but never "Needs you". This section is runtime only and hidden while empty.
- Click or Enter goes to the pane. The selected row takes the subtle fill.

### Find bar (Ctrl+Shift+F)
It takes over the pane's 26px header row while open. The tab strip and ✕ step aside, but a pane-zoom note stays. It holds a compact Fluent TextBox (max 320px), a Caption count ("3 lines", "No matches", "200+ lines") and a ghost ✕. Matching lines drop down in a flyout popup under the header. Each line is in the terminal font, because it is terminal output, with "×3" in Caption when the same line appeared more than once. Up/Down move through the lines, Enter copies the selected one (the count says "Copied"), and Esc closes and returns focus. The count, the selected line and "Copied" are announced to screen readers, since focus stays in the box. It searches aghouse's plain-text mirror of the output, because the terminal control has no search of its own.

### Pane zoom note and text size
- **Pane zoom** (Ctrl+Shift+Enter): the zoomed pane fills the workspace. On the right of its header, "Zoomed · 2 hidden panes" in Meta, Secondary Text. When a hidden pane needs you it becomes "**Needs you** in GitHub (2) · Ctrl+Shift+U". Clicking it shows all panes again. There is no accent ring on the zoomed pane for a hidden pane's request, because the ring means *this* pane.
- **Text size** (Ctrl+= / Ctrl+- / Ctrl+0, Ctrl+wheel over a pane): per pane, runtime only. While it isn't 100%, the header shows "118%" in Caption, Secondary Text.

### Settings window (Ctrl+,)
A normal Fluent window with Mica and the system title bar, 780×600 and resizable, owned by the main window.
- **Nav:** a 200px list of General, Appearance, Notifications, Integrations and About, each with a Segoe Fluent Icons glyph. The rows look like the sidebar's: subtle fill, accent pill on the selected page, two-stroke focus ring.
- **Pages:** a 20/600 page title (WinUI's subtitle step), then rows, all in one column up to 640px wide so the controls on the right share one edge on every page. Each row has a 14px label with Meta-type help under it in Secondary Text, the stock control on the right, and a hairline Divider under it. No cards.
- **Appearance:** Mode, Dark theme and Light theme (pickers with swatch strips), the side-by-side preview, a caution line naming custom schemes that were skipped, **Import from Windows Terminal** with its result in words underneath, then Terminal font and Text size. See Themes under Colors.
- **Behaviour:** every change applies at once and is written to settings.json, keeping keys aghouse doesn't know. A broken settings.json shows a Fluent InfoBar (caution background and glyph, "Edit settings.json") and makes every control read-only.
- **Integrations:** Claude Code's state in words ("Connected…", "Connected through another copy of aghouse (path)…" when every hook runs a different aghouse, "Partly connected: Stop doesn't…", "Not connected…", "isn't valid JSON…"), and **Connect Claude Code** (**Use this copy** for another copy), which shows a stock MessageBox listing exactly what will be added and asks first (Yes / No, No by default). Under it, OpenCode's row in the same shape: "Connected…", "Connected through another copy of aghouse (path)…", "Connected with an older version…" (**Update**), "Not connected…" naming the plugin folder, and **Connect OpenCode**, whose MessageBox names the folder and the aghouse.exe it will run.
- **About:** the 48px mark, "aghouse", "Version 0.1.0 · 03d5770", the one-line description, an updates row (the last check's answer in words — "aghouse is up to date", "Downloading aghouse 0.1.3… 42%", "aghouse 0.1.3 is ready" — with a "What's new" link, **Check for updates**, and, when there is one, **Restart to update** / **Download and restart** / **Open the release page**; stock buttons, no accent), an **Install updates** row (Automatically / Ask me first / Don't check), neutral underlined links (GitHub, Releases), MIT License, and buttons for the logs folder and settings.json.

### Welcome screen (first launch only)
Shown in the terminal area when there is no session.json, before any terminal exists, so it is ordinary WPF. It is a 520px column centred in the area: the 40px mark and "aghouse", then "Run coding agents side by side" (14/600) with one Secondary sentence, then **Open a terminal in Home** (the default button, and the only filled one) and **Choose a folder…** as a ghost button beside it, so the lead action reads first without the accent. Below a Divider comes Claude Code's state with **Connect Claude Code**, and below another, "Keys worth knowing": six shortcut rows with the keys in Caption, Secondary Text. It goes away for good when the first workspace opens.

### Reordering workspaces
Drag a row. A 2px rounded line in Primary Text (never the accent) marks the gap where it will land. The row's context menu also has **Move up** and **Move down**, and its **Close workspace** shows Ctrl+Alt+W.

### Attention dot
A 10px accent circle with a faint 1px halo ring. It sits at the right edge of the sidebar row, level with the workspace name (on the rail, on the tile's top-right corner), so it can't be mistaken for the left-edge selection pill. It appears only while a pane in the workspace needs you. Together with the pane's Needs-you state, it is the whole attention vocabulary inside the app. Toasts and the taskbar flash carry it outside the window.

### Named Rules
**The Say-It Rule.** Attention is never shown by colour alone. Wherever the accent marks a pane that needs you, the words "Needs you" appear with it. Focus uses neutral brushes, so accent never means "where your keystrokes go".

**The One-Word Rule.** Attention has one vocabulary. A pane's state is "Needs you"; a count is "waiting" ("2 waiting", "jump to a waiting pane"). "Unread", "alert" and "attention" never appear in the UI; "unread" survives only as a field name in the CLI's JSON, where renaming would break scripts. An agent that is busy is "Working" ("2 working"): a neutral chip, never the accent, and it clears the moment the agent asks for you, so "Working" and "Needs you" never show for the same pane.

## Do's and Don'ts

### Do:
- **Do** bind every UI colour to a Fluent theme brush (`DynamicResource ...Brush`) so light, dark, accent and the chosen theme all follow.
- **Do** use stock Fluent controls as they are. Build a custom template only when WPF lacks the control, as the workspace list does with its NavigationView-style item.
- **Do** use one Segoe Fluent Icons glyph (10–13px) for chrome actions, with a tooltip that names the shortcut.
- **Do** put attention around panes (the 2px accent frame plus the "Needs you" header) and beside workspaces (the accent dot at the row's right edge), never over the terminal.
- **Do** show focus with neutral brushes only (a 1px strong-stroke border and bold header text), quieter than the Needs-you ring.
- **Do** match a pane frame's background to its terminal palette so frame and terminal read as one surface.
- **Do** keep chrome text at 11–14px (20px only for the dialog and Settings page titles) and build hierarchy from weight and the three text brushes.
- **Do** let agents and scripts report through the CLI (agent state into the Agents list; status, progress and log into the row and its tooltip), and keep the workspace row one line until something needs you.

### Don't:
- **Don't** make it look like a web app in a frame: no custom title bar, web fonts, CSS-style cards, or Electron-style chrome.
- **Don't** drift toward a hacker or neon terminal: no green-on-black, glow, scanlines or "cyber" styling.
- **Don't** add busy IDE chrome: no toolbars, status bars or extra panels that take space from the terminals. The only per-pane chrome is its own 26px header, which becomes the tab strip while it holds two or more tabs.
- **Don't** colour status: no green "open", purple "merged", red error chips or accent progress bars. Words and the caution glyph carry meaning.
- **Don't** splash brand colour across the UI. Logo sky, deep blue and amber stay in the logo and icon.
- **Don't** use the accent for anything except selection and attention, and never use it to show focus.
- **Don't** show attention by colour alone. The accent ring always comes with the words "Needs you".
- **Don't** add drop shadows, glows, gradients or custom acrylic to the chrome.
- **Don't** hard-code hex colours in UI code. Only scheme tables may, plus the Settings preview and swatches that draw them.
