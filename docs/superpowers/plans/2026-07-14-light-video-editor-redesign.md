# Light Video Editor Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reshape Llamashot's `VideoEditorWindow` into a "Light Video Editor" — a Home view plus a
restructured Editor view (collapsible icon sidebar, center Preview, right Properties, bottom toolbar
+ timeline) — then implement the remaining must-have features across 5 phases.

**Architecture:** Keep the working editing engine. Add a top-level view switch inside
`VideoEditorWindow` (`HomeView` ↔ `EditorView`). Restructure only the XAML shell + add gap features.
Teal→green accent. Verification = `dotnet build` + `RunHarness` (LLAMASHOT_VIDEDIT=1) + app run.

**Tech Stack:** WPF .NET 10 (net10.0-windows10.0.19041.0, win-x64, single-file self-contained),
ffmpeg 8.0 via ProcessStartInfo, JSON project serialization.

**Spec:** `docs/superpowers/specs/2026-07-14-light-video-editor-redesign-design.md`

## Verification primitives (used by every task)

- **Build:** `dotnet build D:\Llamashot\Llamashot\Llamashot.csproj -c Debug` → expect `Build succeeded`, 0 errors.
- **Harness build:** `dotnet build D:\Llamashot\tools\RunHarness\RunHarness.csproj -c Debug`.
- **Harness run:** set `LLAMASHOT_VIDEDIT=1`, run
  `D:\Llamashot\tools\RunHarness\bin\Debug\net10.0-windows10.0.19041.0\win-x64\RunHarness.exe` →
  expect all `[timeline*]` cases OK.
- **App run:** stop any running `Llamashot.exe` first (file lock), then launch the Debug build.
- **Before committing:** bump version (csproj Version/AssemblyVersion/FileVersion + `Version X` in
  `AboutWindow.xaml` + `setup.iss` AppVersion/OutputBaseFilename), refresh root installer, and ONLY
  commit after explicit user approval (commit message ends with the Co-Authored-By trailer).

## Key x:Names to preserve (code-behind references them)

`Player, TxtNoPreview, TxtTime, MediaGrid, MediaScroll, TransScroll, Inspector, InspVideo,
InspAudio, InScale, InPosX, InPosY, InRotate, InFlipH, InFlipV, InOpacity, InSpeed, InSpeedFrom,
InSpeedTo, InSpeedX, InFadeIn, InFadeOut, InAFadeIn, InAFadeOut, InBright, InContrast, InSat, InVol,
TimelineRoot, TimelineScroll, RulerCanvas, TracksPanel, TrackHeaders, OverlayCanvas, SldZoom,
TxtProjName, Rail`. Any rename must update `VideoEditorWindow.xaml.cs` in the same task.

---

## PHASE 1 — Shell & Home

### Task 1.1: Data model — project settings + background

**Files:**
- Modify: `D:\Llamashot\Llamashot\Core\VideoEditor\TimelineModel.cs`

- [ ] **Step 1:** Add `public string BackgroundColor { get; set; } = "#000000";` to `TimelineProject`.
- [ ] **Step 2:** Add new enum member `Text` to `ClipKind` (`Video, Audio, Image, Text`).
- [ ] **Step 3:** Build. Expect success (unused enum member is fine).
- [ ] **Step 4:** Confirm nothing else broke: run harness, expect existing cases OK.

### Task 1.2: Recent-projects + settings persistence service

**Files:**
- Create: `D:\Llamashot\Llamashot\Core\VideoEditor\ProjectLibrary.cs`

- [ ] **Step 1:** Create `ProjectLibrary` static class that reads/writes a `recentProjects` list to the
  app settings location (reuse the app's existing settings/HistoryDirectory mechanism — match how
  the rest of the app finds its data dir; do NOT hardcode a user path). Each entry:
  `record RecentProject(string Path, string Name, int Width, int Height, int Fps, double DurationSec, string? ThumbPath, DateTime Modified)`.
- [ ] **Step 2:** Methods: `IReadOnlyList<RecentProject> Load()`, `void Add(RecentProject)` (dedupe by
  Path, most-recent first, cap ~30), `void Remove(string path)`.
- [ ] **Step 3:** Build. Expect success.
- [ ] **Step 4:** Add a harness case `[recent-projects]` that redirects the settings/data dir to a temp
  folder, calls Add twice + Load, asserts dedupe + ordering, and NEVER writes real user data.
- [ ] **Step 5:** Run harness, expect `[recent-projects]` OK.

### Task 1.3: Autosave + recovery

**Files:**
- Modify: `D:\Llamashot\Llamashot\Views\VideoEditorWindow.xaml.cs`
- Possibly: `D:\Llamashot\Llamashot\Core\VideoEditor\ProjectLibrary.cs`

- [ ] **Step 1:** Add a `DispatcherTimer` (e.g. 20s) that, when the project is dirty and has clips,
  serializes to a sibling recovery file (`<projectpath>.autosave` or, for unsaved projects, a temp
  recovery file in the data dir). Track a `_dirty` flag set by edits/PushUndo.
- [ ] **Step 2:** On opening a project (or on startup), if a newer `.autosave` exists, offer recovery
  via MessageBox (Yes = load autosave, No = ignore/delete).
- [ ] **Step 3:** Update the header autosave-status text ("Auto Save: hh:mm:ss") after each autosave.
- [ ] **Step 4:** Build. Expect success. Run app, edit, wait for autosave tick, confirm status updates
  and recovery file appears in the data dir (not real user projects).

### Task 1.4: EditorView shell restructure (XAML)

**Files:**
- Modify: `D:\Llamashot\Llamashot\Views\VideoEditorWindow.xaml`
- Modify: `D:\Llamashot\Llamashot\Views\VideoEditorWindow.xaml.cs`

- [ ] **Step 1:** Wrap the entire current editor layout in a root Grid with two children:
  `HomeViewRoot` (Grid, added in Task 1.6) and `EditorViewRoot` (Grid, the current layout). Add
  `x:Name` to both.
- [ ] **Step 2:** Restructure `EditorViewRoot` header into a menu strip (File / Edit / View / Help
  Menus mapping to existing handlers) + centered editable project name (`TxtProjName`) + autosave
  status text (`TxtAutosave`) + `Save` + `Export` buttons. Keep all existing handler wiring.
- [ ] **Step 3:** Convert the left rail (`Rail`) to a collapsible icon sidebar: icon-only tab buttons
  (Media, Text, Transitions, Effects, Audio, Elements) with a chevron button `BtnPanelCollapse` that
  animates the adjacent panel column width to 0 and back. Keep `Rail_Changed` semantics; ensure the
  six tabs exist (Text/Effects/Elements may still be placeholders until their phases).
- [ ] **Step 4:** Add a Properties header with tabs `Video / Audio / Text` above the inspector
  (`InspVideo`/`InspAudio` already exist; add empty `InspText` placeholder).
- [ ] **Step 5:** Restyle the bottom toolbar to the mockup action set: Undo, Redo, Split, Trim,
  Delete, Duplicate, Crop, Text, Transition, Effect (wire Crop/Text/Transition/Effect to focus the
  matching sidebar tab or inspector section; real behavior lands in later phases).
- [ ] **Step 6:** Build. Expect success. **This is the invasive task** — verify every preserved
  `x:Name` still resolves (build catches missing names referenced in code-behind).
- [ ] **Step 7:** Run harness (no export logic changed) → existing cases OK. Run app → confirm editor
  layout matches mockup, sidebar collapses, tabs switch, no crashes.

### Task 1.5: Project-settings surface + Export options

**Files:**
- Modify: `D:\Llamashot\Llamashot\Views\VideoEditorWindow.xaml` / `.xaml.cs`
- Modify: `D:\Llamashot\Llamashot\Core\FileToolsService.cs`

- [ ] **Step 1:** Replace hardcoded `ProjW/ProjH/ProjFps` usage with values from the current
  `TimelineProject` (CanvasW/H/Fps/BackgroundColor). Compositing canvas + fps derive from the project.
- [ ] **Step 2:** Export dialog/flow surfaces Resolution (720p/1080p), FPS (30/60), Quality
  (Low/Med/High → CRF 28/23/18). Thread these into `ExportTimelineAsync` (add params with sane
  defaults so existing callers/harness keep compiling).
- [ ] **Step 3:** Build. Expect success.
- [ ] **Step 4:** Add harness case `[timeline-720p]` exporting at 1280x720/CRF28; assert output size ~
  1280x720. Run harness, expect OK + existing cases still OK.

### Task 1.6: HomeView (XAML + logic)

**Files:**
- Modify: `D:\Llamashot\Llamashot\Views\VideoEditorWindow.xaml` (`HomeViewRoot`)
- Modify: `D:\Llamashot\Llamashot\Views\VideoEditorWindow.xaml.cs`

- [ ] **Step 1:** Build `HomeViewRoot`: left nav (Home / Recent Projects / Templates / Sample Media /
  Settings), "Create New Project" aspect-ratio preset cards (16:9 1920x1080, 9:16 1080x1920, 1:1
  1080x1080, 4:3 1440x1080, 21:9 2560x1080), a Templates strip (a few starter presets), and a
  Project Settings panel (name, fps 24/30/60, resolution dropdown, background color) with a Create
  button. Teal accent throughout.
- [ ] **Step 2:** Recent Projects section: bind `ProjectLibrary.Load()`, with search box, Sort-by,
  grid/list toggle, Open + "…" (reveal in folder / remove) + inline rename. Double-click / Open →
  load project + `SwitchView(true)`.
- [ ] **Step 3:** `Create` builds a blank `TimelineProject` with chosen preset (CanvasW/H/Fps/BG +
  name), resets tracks (video + audio), then `SwitchView(true)`.
- [ ] **Step 4:** `SwitchView(bool showEditor)` toggles `HomeViewRoot`/`EditorViewRoot` visibility and
  gates editor keyboard shortcuts on Home. Add a "Home" affordance in the editor (File menu → Home)
  that returns to Home (prompt to save if dirty).
- [ ] **Step 5:** On successful Save/Open/Export of a project, call `ProjectLibrary.Add(...)` so it
  shows in Recent.
- [ ] **Step 6:** Startup: show HomeView first (`SwitchView(false)`). Confirm however
  `VideoEditorWindow` is currently launched still works (it now opens on Home).
- [ ] **Step 7:** Build. Run app → Home shows first, presets create correct-sized projects, recent
  list populates after save, Home↔Editor switching works, shortcuts inactive on Home.

### Task 1.7: Phase 1 verification + version bump + commit

- [ ] **Step 1:** Full build (app + harness). Run harness → ALL cases OK
  (`[timeline]`, `[timeline-fx]`, `[timeline-tr]`, `[timeline-ramp]`, `[recent-projects]`,
  `[timeline-720p]`).
- [ ] **Step 2:** Run app, walk the mockup workflow end to end (Home → create → edit → export).
- [ ] **Step 3:** Bump version (→ 8.9.22) across csproj/AboutWindow.xaml/setup.iss; refresh root
  installer (publish Release → ISCC → copy to root `LlamashotSetup.exe`).
- [ ] **Step 4:** ASK the user to approve the commit; on approval, commit Phase 1
  (spec + plan + code).

---

## PHASE 2 — Text / Titles

### Task 2.1: Text clip model + serialization

**Files:** `TimelineModel.cs`, `VideoEditorWindow.xaml.cs` (ProjectDto/ClipDto), `FileToolsService.cs`

- [ ] **Step 1:** Add text fields to `ClipItem`: `Text, FontFamily, FontSizePct (double), FontColor
  (string), Bold (bool), AlignH (string L/C/R), AlignV (string T/M/B), PosXPct, PosYPct, BgBoxColor
  (string? nullable)`. Include in `Clone()`.
- [ ] **Step 2:** Extend `ClipDto` serialize/deserialize with these fields (defaults so old projects
  load).
- [ ] **Step 3:** Add `record TimelineTextClip(string Text, string FontFamily, double FontSizePct,
  string FontColor, bool Bold, string AlignH, string AlignV, double PosXPct, double PosYPct,
  string? BgBoxColor, double StartSec, double EndSec)` to `FileToolsService`.
- [ ] **Step 4:** Build. Expect success. Harness existing cases OK.

### Task 2.2: Text sidebar tab + Properties Text inspector

**Files:** `VideoEditorWindow.xaml` / `.xaml.cs`

- [ ] **Step 1:** Text sidebar tab: preset buttons (Title, Subtitle, Lower-third) that add a `Text`
  `ClipItem` to a Text track (create the track if absent) at the playhead with a default duration.
- [ ] **Step 2:** `InspText` inspector: text box, font family, size, color picker, bold toggle,
  position (X/Y%), alignment. Two-way bind to selected text clip; `PushUndo` on change.
- [ ] **Step 3:** Render text clips on the timeline (distinct colored block with the text label).
- [ ] **Step 4:** Build. Run app → add a title, edit its text/color/position, see it on the timeline.

### Task 2.3: drawtext export

**Files:** `FileToolsService.cs`, `VideoEditorWindow.xaml.cs`

- [ ] **Step 1:** Map text clips → `drawtext` overlays on the composited timeline: escape text,
  point `fontfile` at a bundled/known Windows TTF (e.g. `C:\Windows\Fonts\arialbd.ttf` for bold /
  `arial.ttf`), compute x/y from Pos%×canvas + alignment, `enable='between(t,start,end)'`,
  `fontcolor`, `fontsize` from FontSizePct×canvasH, optional `box=1:boxcolor`.
- [ ] **Step 2:** Pass text clips from the editor into `ExportTimelineAsync` (new param, default
  empty).
- [ ] **Step 3:** Build. Add harness `[timeline-text]` (a title over clipA); assert output duration
  and that ffmpeg ran drawtext without error. Run harness → OK + existing OK.

### Task 2.4: Phase 2 version bump + commit (on approval)

- [ ] Build + full harness green; bump version; refresh installer; ASK; commit.

---

## PHASE 3 — Effects

### Task 3.1: Effect fields on clip + export chain

**Files:** `TimelineModel.cs`, `VideoEditorWindow.xaml.cs`, `FileToolsService.cs`

- [ ] **Step 1:** Add `double Blur = 0` and `string EffectPreset = "none"` to `ClipItem` (+ Clone +
  ClipDto). Add matching fields to `TimelineVideoClip`.
- [ ] **Step 2:** Extend the per-clip filter chain: `boxblur`/`gblur` when Blur>0; B&W =
  `hue=s=0` (or `colorchannelmixer` greyscale); Vintage = warm curve + slight desat + vignette-ish
  `eq`+`curves`. Compose with existing eq/scale/fade order.
- [ ] **Step 3:** Build. Extend harness `[timeline-fx]` to include blur + bw + vintage clips; run →
  OK.

### Task 3.2: Effects sidebar tab

**Files:** `VideoEditorWindow.xaml` / `.xaml.cs`

- [ ] **Step 1:** Effects tab: one-click Blur, Black & White, Vintage cards + Brightness/Contrast/
  Saturation sliders (surface existing `InBright/InContrast/InSat` here too). Apply to selected clip,
  `PushUndo`, show an effect badge on the clip.
- [ ] **Step 2:** Build. Run app → apply each effect, confirm badge + inspector reflect it.

### Task 3.3: Phase 3 version bump + commit (on approval)

- [ ] Build + full harness green; bump version; refresh installer; ASK; commit.

---

## PHASE 4 — Editing polish

### Task 4.1: Multi-select

**Files:** `VideoEditorWindow.xaml.cs`

- [ ] **Step 1:** Introduce a `HashSet<ClipItem> _selection` alongside `_sel` (primary). Ctrl+click
  toggles, Shift+click range-selects on a track. Render all selected clips highlighted.
- [ ] **Step 2:** Make Delete / Duplicate / Move operate on the whole selection.
- [ ] **Step 3:** Build. Run app → multi-select + batch delete/move works; single-select unchanged.

### Task 4.2: Copy / Paste

**Files:** `VideoEditorWindow.xaml.cs`

- [ ] **Step 1:** `_clipboard = List<ClipItem clones>`. Ctrl+C copies selection; Ctrl+V pastes at
  playhead on the same-kind track(s), preserving relative offsets; `PushUndo`.
- [ ] **Step 2:** Build. Run app → copy/paste round-trips.

### Task 4.3: Reverse video

**Files:** `VideoEditorWindow.xaml.cs`, `FileToolsService.cs`, `TimelineModel.cs`

- [ ] **Step 1:** Add `bool Reverse` to `ClipItem` (+Clone+Dto) and `TimelineVideoClip`; export adds
  `reverse`/`areverse` (guard with a warning for clips over ~30s).
- [ ] **Step 2:** Add a Reverse toggle in the inspector Speed section.
- [ ] **Step 3:** Build. Add harness `[timeline-reverse]`; run → OK (duration preserved).

### Task 4.4: Crop inputs

**Files:** `VideoEditorWindow.xaml` / `.xaml.cs`, `FileToolsService.cs`, `TimelineModel.cs`

- [ ] **Step 1:** Add crop fields `CropL/CropT/CropR/CropB` (percent) to `ClipItem`
  (+Clone+Dto) and `TimelineVideoClip`; export applies `crop` before scale.
- [ ] **Step 2:** Crop inputs in the Properties Video tab (the mockup's Crop section).
- [ ] **Step 3:** Build. Add harness `[timeline-crop]`; run → OK (exports at canvas size).

### Task 4.5: Fullscreen preview + keyboard-shortcut pass

**Files:** `VideoEditorWindow.xaml` / `.xaml.cs`

- [ ] **Step 1:** Fullscreen toggle on the Preview (expand player to a borderless overlay / F key +
  the mockup's fullscreen button). Esc exits.
- [ ] **Step 2:** Verify/complete the spec's shortcut table: Space play/pause, Ctrl+Z undo, Ctrl+Y
  redo, Del delete, Ctrl+C copy, Ctrl+V paste, Ctrl+S save, S split. Fix any gaps.
- [ ] **Step 3:** Build. Run app → fullscreen + all shortcuts work.

### Task 4.6: Phase 4 version bump + commit (on approval)

- [ ] Build + full harness green; bump version; refresh installer; ASK; commit.

---

## PHASE 5 — Real composited preview (hard; last)

### Task 5.1: Spike + decision

**Files:** (spike) `VideoEditorWindow.xaml.cs`

- [ ] **Step 1:** Prototype a frame compositor: on seek/pause, render the current timeline frame with
  transform/color/opacity/text/fades applied (WPF visual layering over the player, or an ffmpeg
  single-frame extract of the composed graph shown as an image). Measure latency.
- [ ] **Step 2:** Decide: live WPF overlay compositor vs "seek-accurate still, effects-on-export".
  Record the decision in the spec. If too heavy for the lightweight goal, ship the graceful-degrade
  version (transform/opacity/text via WPF overlay; heavy filters export-only).

### Task 5.2: Implement chosen preview + Phase 5 commit (on approval)

- [ ] Implement per 5.1 decision. Build. Run app → preview reflects edits per the decision.
- [ ] Bump version; refresh installer; ASK; commit.

---

## Self-Review Notes

- **Spec coverage:** Home (1.6), presets/settings (1.5/1.6), collapsible sidebar (1.4), toolbar
  (1.4), autosave (1.3), recent projects (1.2/1.6), templates (1.6), Text (Phase 2), Effects
  (Phase 3), multi-select/copy-paste/reverse/crop/fullscreen/shortcuts (Phase 4), composited preview
  (Phase 5), export options (1.5). All spec sections mapped.
- **Preserved x:Names** listed up top; Task 1.4 flagged as the invasive one, build-verified.
- **No real user data:** Tasks 1.2/1.3 route persistence through the app data dir; harness cases
  redirect it to temp (per project rule).
- **Types consistent:** `RecentProject`, `TimelineTextClip`, `TimelineVideoClip` fields, and
  `SwitchView`/`ProjectLibrary` names used consistently across tasks.
