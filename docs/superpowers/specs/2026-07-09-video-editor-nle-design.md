# Video Editor — Professional NLE redesign

## Goal
Evolve the multi-clip storyboard editor into a professional non-linear editor (NLE) matching the
reference layout (Premiere / DaVinci / Filmora style): header toolbar, left asset rail, project
media panel, large preview, right context inspector, editing toolbar, and a multi-track timeline
with a draggable playhead. Backed by ffmpeg for export.

## Honest scope / constraints
- **Real-time composited preview** of stacked tracks + effects/filters/transitions is NOT feasible
  with WPF `MediaElement` (single-file player). It needs a custom GPU compositor (Vortice/Direct2D)
  and is a later best-effort phase. Phase-1 preview shows the clip under the playhead and plays the
  timeline clip-to-clip.
- Everything else in the reference is buildable, delivered in phases (not by cutting scope).

## Phase 1 (this iteration) — layout + functional timeline
### Data model (`Core/VideoEditor/TimelineModel.cs`)
- `ClipItem` (INotifyPropertyChanged): `SourcePath`, `Kind` (Video/Audio/Image), `SrcIn`/`SrcOut`
  (source trim), `Start` (timeline seconds), `Speed`, `Volume`, transform (`Scale`, `PosX`, `PosY`,
  `Rotate`, `FlipH`, `FlipV`, `Opacity`, crop rect), `Thumb`. `TimelineDuration = (SrcOut-SrcIn)/Speed`.
- `Track` (Name, `TrackKind`, `ObservableCollection<ClipItem>`, Muted, Hidden, Locked).
- `TimelineProject` (`ObservableCollection<Track>`, Name, Fps, CanvasW/H).

### Window (`Views/VideoEditorWindow`)
- **Header:** logo/title, File menu (New/Open/Save/Save As — save/load project JSON), Undo/Redo
  (snapshot stack), editable Project Name, Save Project, Export Video ▾ (MP4/MKV; more later),
  Settings (opens app settings).
- **Left rail:** Media / Audio / Text / Transitions / Effects / Filters / Elements / Overlays / More.
  Media + Audio functional in P1; the rest select a panel labeled "coming soon".
- **Project Media panel:** Add Media, thumbnail grid (duration badge), search + type filter,
  right-click (rename/duplicate/remove), "Add to timeline" / drag to timeline.
- **Preview:** `MediaElement`, transport (skip-start, prev-frame, play/pause, next-frame, skip-end,
  loop), current/total time, mute + volume, snapshot, Fit/zoom label.
- **Inspector (right):** context-sensitive tabs. Video: Transform (Scale/Position/Rotate/Flip/
  Opacity/Crop) + Speed + Reverse. Audio: Volume + Fade. Reflects/edits selected clip.
- **Editing toolbar:** Add Track, Undo, Redo, Split (S), Trim ▾ (Trim Start / Trim Middle / Trim
  End popup), Cut, Delete, Ripple Delete, Crop, Zoom In/Out + zoom slider.
- **Timeline:** time ruler with ticks, draggable red playhead (click ruler to seek), Video Track 1 +
  Audio Track 1 (Add Track for more), clips as draggable + edge-resizable blocks with thumbnail/label,
  snapping to neighbours/playhead, selection, ripple delete (closes gap), horizontal zoom (px/sec).
- **Status bar:** project duration, current time, zoom %, fps, canvas size.

### Export (`FileToolsService.ExportTimelineAsync`)
- Video track 1: clips sorted by Start; gaps filled with black+silence; each clip normalized
  (trim → speed `setpts`/`atempo` → scale/pad to canvas → crop/transform best-effort → fps),
  per-clip volume; segments concatenated.
- Extra audio-track clips mixed in at their Start (`adelay` + `amix`).
- Reuses the cancellable `RunFFmpegAsync` + progress plumbing.

### Keyboard
Space play/pause, S split, Delete delete, Ctrl+Z / Ctrl+Shift+Z undo/redo, +/- zoom.

## Phase 2
Transitions, text/titles/subtitles, multiple video tracks with overlay compositing, filters/color
grading, fade in/out.

## Phase 3
Effects (blur/glow/green-screen), elements/overlays, keyframe animation, markers, project auto-save
+ full undo/redo history, batch + multi-format export (MOV/GIF/audio-only), proxy media.
