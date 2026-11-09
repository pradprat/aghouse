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
    textColor: "{colors.text-tertiary-dark}"
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
- **System Accent** (`AccentFillColorDefaultBrush`; reference value `system-accent`, the dark-theme fill for Windows' default blue): the user's own Windows accent. It is the only chromatic colour in the chrome. It marks the selected workspace (a 3×16 pill), workspaces with unread attention (a filled dot with a faint halo), and panes that need you (a 2px frame). Never hard-code it. Always bind the brush so the user's accent wins.

### Neutral
- **Mica Backdrop** (`mica-dark` / `mica-light`): the window background, provided by Windows 11's Mica material. The app never paints its own window background.
- **Primary Text** (`TextFillColorPrimaryBrush`; `text-primary-dark`): workspace names (inherited by default), the focused pane's 1px border and header title, and "Needs you".
- **Secondary Text** (`TextFillColorSecondaryBrush`; `text-secondary-dark`): the "Workspaces" label, git branch, cwd, and the agent's waiting message (in a Needs-you pane header and under the workspace name).
- **Tertiary Text** (`TextFillColorTertiaryBrush`; `text-tertiary-dark`): the least important text, such as header titles of panes at rest and the idle shortcut hint at the foot of the sidebar.
- **Subtle Fill** (`SubtleFillColorSecondaryBrush`; `subtle-fill-dark`): background for hovered and selected sidebar items.
- **Control Stroke** (`ControlStrokeColorDefaultBrush`; `control-stroke-dark`): the 1px frame around inactive panes.

The `-dark` values above are reference values for the dark theme. The brushes are the source of truth.

### Terminal palettes
These are fixed because the terminal is a native child window with its own renderer.
- **Terminal Night** (`terminal-night` on `terminal-night-ink`, selection `terminal-night-selection`): the dark terminal, using Windows Terminal's Campbell 16-colour table. The pane frame's background matches it so the frame and terminal read as one surface.
- **Terminal Paper** (`terminal-paper` on `terminal-paper-ink`, selection `terminal-paper-selection`): the light terminal, using the One Half Light 16-colour table.
- The terminal theme is fixed when the app launches (setting `theme`: `system` / `dark` / `light`) and does not follow a live Windows theme switch.

### Logo colours
- **Sky** (`logo-sky`), **Deep Blue** (`logo-deep-blue`), **Amber** (`logo-amber`): used only in the logo and icon assets (`assets/`). The amber dot is the logo's picture of attention. Inside the app, attention uses the system accent.

### Named Rules
**The Borrowed Colour Rule.** The chrome has no colours of its own. Every surface, text and stroke colour in the UI is a Fluent theme brush, and the only chromatic colour is the user's accent. A hex value in UI code is a defect, except in the terminal palettes. The single exception is semantic: the warning glyph for a broken settings.json uses Fluent's `SystemFillColorCautionBrush`, exactly as a Windows InfoBar would.

**The One Accent Rule.** The accent marks only selection and attention. Never use it for decoration, headings, icons at rest, or hover states.

## Typography

**UI Font:** Segoe UI Variable (with Segoe UI), inherited from the Fluent theme and not set anywhere in code
**Terminal Font:** Cascadia Mono, 11pt by default, configurable in `settings.json`
**Icon Font:** Segoe Fluent Icons (with Segoe MDL2 Assets), the `IconFont` resource

**Character:** This is the operating system's own voice. Segoe in the chrome and Cascadia in the panes are exactly what a Windows Terminal user already reads all day.

### Hierarchy
- **Title** (600, 14px): workspace names in the sidebar, trimmed with an ellipsis.
- **Section Label** (600, 13px, secondary text): the "Workspaces" heading. It is the only heading in the chrome.
- **Body** (400, 14px): Fluent default for menus, tooltips and the rename text box.
- **Meta** (400, 12px, secondary or tertiary text): git branch, cwd, last message, and pane header text.
- **Caption** (400, 11px): the footer status line at the foot of the sidebar (tertiary text for the idle hint, secondary for the waiting count, primary for a settings problem).
- **Icon** (Segoe Fluent Icons, 10–13px): add (`E710`), chevron (`E70D`), settings (`E713`), close (`E711`). The git branch icon is a hand-drawn 11px stroke path at 1.4 thickness.

### Named Rules
**The System Voice Rule.** Never load a web font or a custom display face. Hierarchy comes from weight (400 vs 600) and from the three text brushes, not from size jumps. The whole UI spans 11–14px.

## Layout

The window has three columns: the sidebar (248px by default, resizable from 170 to 480px), a 4px invisible splitter, and the terminal host, which fills the rest. The window defaults to 1280×800, with a minimum of 640×400. Outer margins are 8px on the window edges and 4px where the two regions meet (sidebar `8,4,4,8`, host `2,4,8,8`).

The sidebar docks three bands: a header row (the "Workspaces" label plus add and profile-menu buttons), a scrolling list of workspaces, and a footer (a one-line status plus settings gear). Each workspace row is a stack (name, branch only if in a git repo, cwd, waiting message only while a pane needs you) with the attention dot in a right-hand column on the name line.

Each workspace is a tree of split panes built from nested Grids. There is a 6px invisible splitter between panes, and each pane has a minimum size of 60px wide or 40px tall. A pane header strip (26px high) appears only when the workspace is split.

The spacing rhythm is small steps of 1, 2, 4, 6, 8 and 10px. Row padding is `10,7,8,7`, and there is 1px between rows.

### Named Rules
**The Airspace Rule.** The terminal is a native HWND, and WPF can never draw on top of it. Every indicator, header or overlay sits beside or around a pane, never over it. Attention is a frame around the pane, not a badge on it.

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
- **Selected:** the same subtle fill, plus a 3×16 accent pill at the left edge, vertically centred.
- **Focus:** keyboard focus gets Fluent's two-stroke focus ring (`FocusStrokeColorOuterBrush` 2px over `FocusStrokeColorInnerBrush` 1px) on the row's 4px shape. It's neutral, never the accent, and only appears for keyboard focus (Ctrl+Arrow moves it without changing the selection).
- **Accessibility:** each row's automation name is the workspace name, its help text is the location, and its item status is "Needs you: <message>" while a pane waits. Icon-only buttons carry an automation name ("New workspace", "Open settings", "Close pane"), not just a tooltip. When a pane starts needing you, the window raises a UIA notification ("claude agent needs you: Waiting for approval") so screen readers hear what sighted users see.
- **Waiting message:** a fourth line, in Meta type and Secondary Text, showing the newest message from a pane that still needs you. It is derived from the waiting panes, so it disappears as soon as nothing is waiting; the sidebar never shows a request that was already handled. The tooltip lists every waiting message, newest first.
- **Rename:** the name swaps in place for a compact Fluent text box (padding `4,1`). Enter commits, Esc cancels.
- **Context menu:** Rename, Split right, Split down, separator, Close workspace. Each item shows its shortcut as input-gesture text.

### Pane frame (signature component)
The frame around each terminal. It's the main place attention shows up. Its three states are told apart by shape and words, not by colour alone, because an accent outline means "focus" everywhere else in Windows.
- **At rest:** 1px Control Stroke border, 4px radius, background matching the terminal. Header text in Tertiary Text.
- **Focused** (only when the workspace is split): 1px Primary Text border, header text in Primary Text at 600 weight. A lone pane never gets a focus border, because there's nothing to tell it apart from.
- **Needs you:** 2px accent border, plus the header gets a faint accent tint (the accent brush at 16% opacity, on its own layer so the text stays at full opacity) and says **Needs you** (600, Primary Text) followed by "· <agent's message>" in Secondary Text. The full message is in a tooltip. Needs-you wins over focused, and both clear together when the user reaches the pane.
- **Header** (only when split): 26px strip with the pane's folder or program title in Meta type, the attention text when present, and a ghost ✕ on the right. Clicking the header focuses the pane.

### Confirmations
The stock Windows message box, never a custom dialog. It's used only before something irreversible happens to running work, such as closing a pane or workspace while a program is still running in it. An idle prompt closes without asking.
- **Wording:** names the program and the place, then asks the question: "PING.EXE is still running in “aghouse”. Close the pane and end it?"
- **Buttons:** Yes / No with the warning icon. **No** is the default button, so a stray Enter never ends a process.

### Sidebar footer status
One Caption line beside the settings gear. It shows the most important of three states:
- **Settings problem:** settings.json couldn't be parsed. A caution glyph (`E7BA`) and "settings.json has an error on line N" in Primary Text. The tooltip has the parser's message and says the app is running on defaults until the file is fixed and aghouse restarts. The broken file is never overwritten.
- **Waiting:** "2 waiting · Ctrl+Shift+U" in Secondary Text, counting panes that need you across all workspaces.
- **Idle:** "Ctrl+Shift+U  jump to unread" in Tertiary Text.

The gear's tooltip says "Open settings.json (changes apply on restart)", because settings are read only at launch.

### Attention dot
A 10px accent circle with a faint 1px halo ring. It sits at the right edge of the sidebar row, level with the workspace name, so it can't be mistaken for the left-edge selection pill. It appears only while the workspace has unread attention. Together with the pane's Needs-you state, it is the whole attention vocabulary inside the app. Toasts and the taskbar flash carry it outside the window.

### Named Rules
**The Say-It Rule.** Attention is never shown by colour alone. Wherever the accent marks a pane that needs you, the words "Needs you" appear with it. Focus uses neutral brushes, so accent never means "where your keystrokes go".

## Do's and Don'ts

### Do:
- **Do** bind every UI colour to a Fluent theme brush (`DynamicResource ...Brush`) so light, dark and accent follow Windows.
- **Do** use stock Fluent controls as they are. Build a custom template only when WPF lacks the control, as the workspace list does with its NavigationView-style item.
- **Do** use one Segoe Fluent Icons glyph (10–13px) for chrome actions, with a tooltip that names the shortcut.
- **Do** put attention around panes (the 2px accent frame plus the "Needs you" header) and beside workspaces (the accent dot at the row's right edge), never over the terminal.
- **Do** show focus with neutral brushes only (a 1px Primary Text border and bold header text).
- **Do** match a pane frame's background to its terminal palette so frame and terminal read as one surface.
- **Do** keep chrome text at 11–14px and build hierarchy from weight and the three text brushes.

### Don't:
- **Don't** make it look like a web app in a frame: no custom title bar, web fonts, CSS-style cards, or Electron-style chrome.
- **Don't** drift toward a hacker or neon terminal: no green-on-black, glow, scanlines or "cyber" styling.
- **Don't** add busy IDE chrome: no toolbars, tab strips, status bars or extra panels that take space from the terminals.
- **Don't** splash brand colour across the UI. Logo sky, deep blue and amber stay in the logo and icon.
- **Don't** use the accent for anything except selection and attention, and never use it to show focus.
- **Don't** show attention by colour alone. The accent ring always comes with the words "Needs you".
- **Don't** add drop shadows, glows, gradients or custom acrylic to the chrome.
- **Don't** hard-code hex colours in UI code. Only the terminal palettes may.
