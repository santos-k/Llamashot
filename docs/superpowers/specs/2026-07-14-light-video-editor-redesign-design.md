# Light Video Editor — UI Redesign & Feature Completion

**Date:** 2026-07-14
**Scope:** Reshape Llamashot's existing `VideoEditorWindow` into a "Light Video Editor" experience —
a Home view, a collapsible left icon sidebar, center Preview, right Properties panel, and a bottom
timeline with a toolbar — then implement the remaining "must-have" feature checklist.

## Goal & Non-Goals

**Goal:** A new user understands the whole editor in 2–3 minutes. Every editing task ≤ 3 clicks.
Match the reference mockups' *layout*, keep Llamashot's *identity*.

**Non-goals (explicitly skipped, per the lightweight brief):** color grading/LUTs, chroma key,
motion tracking, keyframe animation, audio mixer, multicam, proxies, masks, blend modes, nested
timelines, AI tools, motion graphics, HDR, scopes, plugins.

## Key Decisions

- **Reshape, don't rebuild.** The editing engine (split, trim, ripple, duplicate, drag/rearrange,
  snap, zoom, playhead, ruler, undo/redo, transitions, speed + speed-ramp, fades, transform,
  opacity, color, ffmpeg export) already works and is preserved. Only the XAML shell is restructured
  and gap features are added.
- **Accent stays teal→green** (#2DD4BF → #34D399) to match the rest of Llamashot. Adopt the mockup
  layout, not its purple accent.
- **Home is a view inside the editor window**, not a separate window. `VideoEditorWindow` hosts two
  top-level views — `HomeView` and `EditorView` — and swaps between them. No new window class.
- **Dark theme only** (matches app + mockups).

## Architecture

`VideoEditorWindow` (existing) gains a top-level view switch:

```
VideoEditorWindow
├── HomeView               (Grid, visible at startup / on "Home")
│   ├── Left nav rail      (Home, Recent Projects, Templates, Sample Media, Settings)
│   ├── Create New Project (aspect-ratio preset cards: 16:9, 9:16, 1:1, 4:3, 21:9)
│   ├── Start with Template (template strip)
│   ├── Recent Projects    (search + sort + grid/list toggle)
│   └── Project Settings    panel (name, fps, resolution, background) → Create
└── EditorView             (the current 3-panel + timeline layout, restructured)
    ├── Header             (menu strip, project name, autosave status, Save, Export)
    ├── Collapsible sidebar (icon tabs: Media, Text, Transitions, Effects, Audio, Elements)
    ├── Panel              (content for the active sidebar tab)
    ├── Preview            (player, transport, Fit/fullscreen, snapshot)
    ├── Properties         (Video / Audio / Text inspector tabs)
    ├── Toolbar            (Undo, Redo, Split, Trim, Delete, Duplicate, Crop, Text, Transition, Effect)
    └── Timeline           (ruler, track headers, tracks, playhead, zoom)
```

**View swap:** a single `SwitchView(bool showEditor)` toggles `Visibility` on the two root Grids and
routes keyboard shortcuts (editor shortcuts inactive on Home).

**Sidebar collapse:** the icon rail is always visible (~64px). The *panel* beside it collapses via a
chevron, animating width to 0. Active tab persists.

## Data Model Changes (`TimelineModel.cs`)

- `TimelineProject` already has `Name/Fps/CanvasW/CanvasH` — expose them in UI; add
  `string BackgroundColor = "#000000"`.
- New `TextClip` support: reuse `ClipItem` with `Kind = ClipKind.Text` (new enum value) plus fields
  `Text`, `FontFamily`, `FontSizePct`, `FontColor`, `Bold`, `AlignH`, `AlignV`, `PosXPct`, `PosYPct`,
  `BgBoxColor` (optional), so titles live on a text track and serialize like any clip.
- Effects: add `double Blur = 0` and `string EffectPreset = "none"` to `ClipItem`
  (presets: none, bw, vintage). B&W/vintage are saturation/contrast/curve shortcuts; Blur maps to
  ffmpeg `boxblur`/`gblur`.

## Features by Phase

Each phase builds, passes the harness, and is independently shippable.

### Phase 1 — Shell & Home
- HomeView with New Project (aspect-ratio preset cards drive CanvasW/H), Recent Projects
  (persisted list in settings.json — path, name, resolution, fps, duration, thumbnail, modified),
  Templates strip (starter presets — a few blank-with-title projects), Sample Media, and a
  Project Settings side panel (name, fps 24/30/60, resolution 720p/1080p/preset, background color).
- Restructure EditorView shell to the 3-panel layout; collapsible icon sidebar; restyled top menu
  strip (File/Edit/View/Help as menus) + autosave status + Save + Export.
- Restyled bottom toolbar with the mockup's action set.
- **Recent projects persistence** + **Autosave** (timer writes to a recovery copy; on open, offer
  recovery if a newer autosave exists).

### Phase 2 — Text / Titles
- Text sidebar tab: presets (Title, Subtitle, Lower-third) → adds a `Text` clip to a Text track.
- On-preview title rendering (WPF overlay for edit-time), Properties → Text tab (font, size, color,
  bold, position, alignment, background box).
- Export: `drawtext` filter (escaped text, font file, color, x/y from position %, enable between
  start/end, optional box).

### Phase 3 — Effects
- Effects sidebar tab: one-click **Blur**, **Black & White**, **Vintage** (+ existing
  Brightness/Contrast/Saturation surfaced here too). Applied per-clip, shown as a badge.
- Export: extend the clip filter chain with `boxblur`/`gblur` and the B&W/vintage curves.

### Phase 4 — Editing polish
- **Multi-select** (Ctrl/Shift+click; marquee optional), batch delete/move.
- **Copy/Paste** clips (Ctrl+C/V) at playhead.
- **Reverse** video (ffmpeg `reverse`/`areverse`, guarded for long clips).
- **Crop** inputs in Properties (crop rect → ffmpeg `crop`).
- **Fullscreen preview** toggle.
- Keyboard-shortcut pass to match the spec table (Space, Ctrl+Z/Y, Del, Ctrl+C/V, Ctrl+S, S).

### Phase 5 — Real composited preview (hard; last)
- Preview reflects transform/color/opacity/fades/text/transitions live (frame compositor).
- Deferred and re-scoped after phases 1–4 ship; may remain "seek-accurate, effects-on-export"
  if a live GPU compositor proves too heavy for the lightweight goal.

## Export (`FileToolsService.cs`)

Existing `ExportTimelineAsync` is extended, not replaced:
- `TimelineVideoClip` gains `Blur`, `EffectPreset`, and crop fields; filter chain appends
  `crop`, `boxblur`/`gblur`, and B&W/vintage curves.
- New `TimelineTextClip` (text, font, color, size%, x/y%, start, end, box) → `drawtext` overlay
  applied on the composited timeline (after concat, or per-segment where it falls).
- Export dialog surfaces **resolution (720p/1080p)**, **fps (30/60)**, **quality (Low/Med/High →
  CRF 28/23/18)** — currently hardcoded.

## Testing

`tools/RunHarness/Program.cs` (env-gated `LLAMASHOT_VIDEDIT=1`, synthetic media) gains cases:
- `[timeline-text]` — a title over a clip, assert output duration + that drawtext ran.
- `[timeline-fx]` extended — blur + B&W + vintage on clips.
- `[timeline-crop]` — cropped clip exports at canvas size.
- `[timeline-reverse]` — reversed clip, duration preserved.
- Existing `[timeline]`, `[timeline-tr]`, `[timeline-ramp]` must keep passing.
- Harness must snapshot/redirect settings.json + HistoryDirectory (recent-projects/autosave now
  write there) — never touch real user data.

## Risks / Mitigations

- **XAML shell restructure is invasive** → do it as one focused phase, keep all `x:Name`s the code
  references; verify build after each region.
- **drawtext font path** on Windows → bundle/point at a known TTF; escape text robustly.
- **Live composited preview** is the expensive unknown → deferred to last, scoped to degrade
  gracefully.
- **File locks** → stop any running Llamashot.exe before rebuild/republish.

## Versioning / Delivery

Each committed phase bumps the version (csproj Version/AssemblyVersion/FileVersion + "Version X" in
AboutWindow.xaml + setup.iss AppVersion/OutputBaseFilename) and refreshes the root installer, per
project convention. Commits only on explicit user approval.
