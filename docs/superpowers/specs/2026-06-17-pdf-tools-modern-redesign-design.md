# PDF / File Tools — Modern Redesign (Soft Theme)

**Date:** 2026-06-17
**Status:** Approved (design)
**Scope:** Visual redesign only. Central theme system with a soft **light** palette (default) and a **dark** toggle. No new structural features.

## Goal

Take the File Tools feature to a modern, soft-colored look without changing its
behavior or tool set. Replace the ad-hoc, hard-coded color values with a central,
runtime-swappable theme, modernize the home layout and tool cards, and reskin all
21 tool panels and the shared overlays consistently.

## Current state (baseline)

- Single `FileToolsWindow` (dark `#1A1A1E`). Home screen is a grid of tool cards
  built imperatively in `CreateCard` (`FileToolsWindow.xaml.cs`), grouped into 5
  categories. Selecting a card swaps in one of **21 per-tool panels**, each with a
  Select → Config → Processing → Complete flow.
- **21 working tools**: 10 PDF, 6 image, 1 office, 3 video/audio, 1 YouTube.
- **No central theme**: ~3,100 XAML lines + code-behind use hard-coded hex values;
  21 per-tool accent hexes live in the `ToolDefs` array. `App.xaml` defines only a
  single `ToolbarButton` style. There is no `Themes/` or shared color dictionary.

## Non-goals (deferred to later phases)

Active Files dock / shared workspace, cross-tool "next step" suggestions, workflow
/ pipeline builder, AI tools, batch engine, new navigation model. The tool set and
each tool's logic are unchanged.

## Approach: runtime-swappable theme via DynamicResource

Hard-coded hexes cannot toggle at runtime, so all themed colors move to named
resource keys referenced with `{DynamicResource ...}`.

- **`Themes/LightTheme.xaml`** and **`Themes/DarkTheme.xaml`** — identical sets of
  named `SolidColorBrush`/`Color` keys, different values.
- **`Themes/Shared.xaml`** — shared control styles (buttons, search box, combo box,
  slider, card border, scrollbars) that reference the theme keys via `DynamicResource`.
  Merges are wired in `App.xaml` (Light merged by default, plus Shared).
- **`ThemeManager`** (static helper) — swaps the active theme dictionary in
  `Application.Current.Resources.MergedDictionaries` at runtime and persists the
  choice (reuse existing settings storage if present; otherwise a small JSON/registry
  setting). Exposes `Current` and `Toggle()` / `Apply(theme)` plus an event so
  imperative code can rebuild.
- **FileToolsWindow.xaml** — inline hexes replaced with `{DynamicResource <key>}`.
- **Code-behind brushes** (`CreateCard`, `ResetToolStates`, overlays, hover states)
  read from `Application.Current.Resources[...]`; cards are rebuilt and dynamic
  brushes re-applied on the `ThemeManager` change event so a live toggle repaints.

*Alternative considered:* swap-at-startup only (no live toggle). Rejected — worse UX
and a runtime toggle was requested.

## Soft palette (semantic keys)

| Key | Light (default) | Dark |
|---|---|---|
| `Brush.Window` | `#F5F6FB` | `#14151A` |
| `Brush.Surface` (cards) | `#FFFFFF` | `#1E2029` |
| `Brush.SurfaceAlt` (inputs/lists) | `#EEF1F8` | `#262833` |
| `Brush.Border` | `#E2E6F0` | `#313443` |
| `Brush.Text.Primary` | `#1F2430` | `#E8EAF0` |
| `Brush.Text.Secondary` | `#5B6172` | `#A8AEC0` |
| `Brush.Text.Muted` | `#9AA0B0` | `#6B7080` |
| `Brush.Accent` (primary action) | `#6E8BFF` | `#7C9CFF` |
| `Brush.Success` | `#43C59E` | `#5BD0A4` |
| `Brush.Danger` (destructive) | `#FF6B6B` | `#FF7A7A` |
| `Brush.Scrim` (overlay) | `#CCF5F6FB` | `#CC14151A` |

The 21 per-tool accent colors are softened to pastel variants (kept distinct per
tool) for the icon tiles. Cards use the tool accent tinted against `Brush.Surface`.

## Layout modernization (visual only)

- **Home header**: title left; on the right a **theme toggle (sun/moon)** and the
  existing sort control; a prominent **rounded pill search** with a search glyph;
  a lightweight **category filter chip row** (All / PDF / Image / Office /
  Video & Audio / Download) that filters the grid.
- **Tool cards**: themed surface, `CornerRadius 18`, soft drop shadow, rounded
  pastel icon tile, title + description, hover **lift + accent ring**; consistent
  responsive grid (unchanged column logic, restyled visuals).
- **Tool panels (all 21)**: reskinned via the theme dictionaries — card-surface
  containers, soft rounded inputs, **pill buttons** mapped to the palette
  (primary = `Brush.Accent`, destructive = `Brush.Danger`). Panel structure and
  controls are unchanged; only colors/styles change.
- **Processing / Complete overlays**: `Brush.Scrim` background, rounded
  `Brush.Surface` card, `Brush.Success` for the completion accent.

## Theme toggle persistence

The selected theme persists across launches via the app's existing settings
mechanism (or a minimal added setting if none is suitable). On startup the saved
theme is applied before the window renders.

## Verification

1. Build succeeds (no new warnings introduced by the refactor where avoidable).
2. Launch the app; open File Tools.
3. Screenshot **light** home, **dark** home, and 2+ tool panels in each theme.
4. Confirm the toggle swaps the entire UI live (home grid + an open tool panel +
   overlays) without restart, and that the choice persists across a relaunch.

## Risks / notes

- The redesign touches a large file (`FileToolsWindow.xaml` ~3,100 lines) and the
  imperative card/overlay code. Work proceeds tool-panel by tool-panel to keep the
  app buildable throughout.
- Per-project convention: bump the version in the `.csproj` and `AboutWindow.xaml`
  on commit.
