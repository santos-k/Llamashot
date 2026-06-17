# Scrolling Screenshot Design

**Date:** 2026-05-16
**Branch:** feature/customizable-shortcuts (will create new branch for implementation)

## Goal

Add scrolling/long screenshot capture that works with any scrollable window (browsers, chats, file explorers, desktop apps). Two capture modes: automatic scroll and manual scroll. Full annotation toolkit available on the stitched result before saving.

## User Flow

### Entry Point
- 4th mode on snipping toolbar: "Scroll" (alongside Screenshot, Video, OCR)
- Configurable shortcut key (default: `4` on toolbar, `Ctrl+Shift+S` overlay shortcut)

### Step 1 — Select Target Window
- User clicks on any window
- App detects the window via `WindowFromPoint` + `GetAncestor(GA_ROOTOWNER)` (same as existing window capture)
- Highlight the window with a blue border overlay

### Step 2 — Choose Mode
A small toolbar appears over the selected window:
- **Auto Scroll** button — app takes full control
- **Manual Scroll** button — user scrolls, app captures
- **Close** button

### Step 3 — Capture

**Auto Scroll Mode:**
1. App activates the target window
2. Scrolls to top (optional, enabled by default)
3. Capture loop:
   - Capture viewport via GDI BitBlt
   - Send mouse wheel scroll via `SendInput` (WHEEL_DELTA * 3)
   - Wait 300ms for content to settle
   - Capture next frame
   - Repeat until two consecutive frames are pixel-identical, or max 100 iterations
4. Progress bar shown on toolbar (based on frame count or UIA scroll percentage if available)

**Manual Scroll Mode:**
1. App shows floating "Capturing..." indicator with frame counter
2. User scrolls naturally at their own pace
3. App captures a frame on each detected scroll event (debounced at 150ms intervals)
4. User clicks "Done" button or presses Enter/Escape to finish

### Step 4 — Stitch + Preview + Annotate + Crop
After capture completes, show a preview window with the stitched long image:
- Scrollable image view (long screenshots can be very tall)
- Full annotation toolkit (all 11 tools: Pen, Line, Arrow, Rectangle, Ellipse, Text, Marker, Blur, Check, Cross, Eraser)
- Undo/Redo support
- Color picker + Thickness control
- Crop handles (top/bottom/left/right) with dimmed areas outside
- Save / Copy / Discard buttons

## Core Algorithm

### Stitching
1. Take consecutive frames (N, N+1)
2. Extract bottom strip of frame N (height = 30% of viewport)
3. Slide this strip down through frame N+1 from top, comparing pixel rows
4. Find the offset with best match (row-by-row pixel comparison)
5. Crop frame N+1 above the overlap offset
6. Append the non-overlapping portion to the accumulated result
7. Optimization: compare sampled pixel columns (every 4th pixel) for speed, then verify full row on candidates

### Fixed Element Detection

**Top fixed elements (sticky headers):**
- Compare top K rows (up to 50px) of frame 1 vs frame 2
- If identical pixel rows found → sticky header of that height
- Include header only in the first frame
- Crop header height from all subsequent frames before stitching

**Bottom fixed elements (scrollbar, sticky footer):**
- Compare bottom K rows (up to 50px) of two consecutive frames
- If identical pixel rows found → fixed footer of that height
- Exclude from all intermediate frames
- Include only in the final frame

### Scroll Completion Detection (Auto Mode)
- **Primary:** Two consecutive frames are pixel-identical (within a small tolerance for anti-aliasing) → done
- **Secondary:** If UIA `ScrollPattern` available on target, check `VerticalScrollPercent >= 99` → done
- **Safety:** Maximum 100 iterations, then stop with what we have

### Scroll Simulation
- Use `SendInput` with `MOUSEEVENTF_WHEEL` flag
- Scroll amount: `WHEEL_DELTA * 3` (3 notches, ~= 80% of viewport for most apps)
- Cursor positioned at center of target window
- Target window must be foreground and visible

### Manual Mode Frame Detection
- Install a temporary low-level mouse hook (`WH_MOUSE_LL`)
- Listen for `WM_MOUSEWHEEL` events on the target window
- Debounce: capture at most once per 150ms
- Also listen for keyboard Page Down / arrow keys via `WH_KEYBOARD_LL`

## Architecture

### New Files

| File | Purpose |
|------|---------|
| `Core/ScrollCapture.cs` | Scroll orchestration: auto-scroll loop, manual-scroll frame collection, SendInput scroll simulation, UIA scroll detection |
| `Core/ImageStitcher.cs` | Pixel-row matching, overlap detection, fixed element detection (top/bottom), final image assembly from frame list |
| `Views/ScrollPreviewWindow.xaml` | Preview UI: scrollable image, annotation toolbar, crop handles, save/copy/discard |
| `Views/ScrollPreviewWindow.xaml.cs` | Preview logic: annotation tool management (reuses IDrawingTool), crop handle dragging, save/copy actions |
| `Views/ScrollToolbar.xaml` | Small floating toolbar: Auto/Manual buttons, progress bar, Done/Close buttons |
| `Views/ScrollToolbar.xaml.cs` | Toolbar logic: mode selection, progress updates, communicates with ScrollCapture |

### Modified Files

| File | Change |
|------|--------|
| `Views/OverlayWindow.xaml` | Add "Scroll" mode button to SnippingToolbar |
| `Views/OverlayWindow.xaml.cs` | Add `CaptureMode.Scroll` enum value, handle scroll mode selection, launch scroll capture flow |
| `Core/AppSettings.cs` | Add `ShortcutModeScroll` (default "D4"), `ShortcutScrollCapture` (default "Ctrl+Shift+S") |
| `Views/SettingsWindow.xaml.cs` | Add new shortcuts to ToolbarShortcuts and ToolShortcuts arrays |
| `Core/NativeMethods.cs` | Add `SendInput`, `INPUT`, `MOUSEINPUT` structs, `MOUSEEVENTF_WHEEL` if not present |

### Reused Components
- `IDrawingTool` and all tool implementations (Pen, Line, Arrow, etc.) — same as OverlayWindow
- `DrawingAction` / undo-redo stack — same pattern
- `ScreenCapture.CaptureRegion()` or GDI BitBlt for frame capture
- `ShortcutHelper` for keyboard shortcuts
- Color palette and thickness popup — same pattern as OverlayWindow

## Preview Window Layout

```
+------------------------------------------+
| [Pen][Line][Arrow][Rect][Ellipse][Text]  |
| [Marker][Blur][Check][Cross][Eraser]     |
| [Undo][Redo] [Color][Thickness]          |
| [Crop Mode]                              |
+------------------------------------------+
|                                          |
|          Scrollable Image View           |
|          (with DrawingCanvas overlay)    |
|          (crop handles when in crop mode)|
|                                          |
+------------------------------------------+
| [Save]  [Copy]  [Discard]               |
+------------------------------------------+
```

- Dark theme matching existing app style
- When crop mode active: 4 edge handles, dimmed area outside crop region
- Drawing canvas overlays the image for annotations
- Image scrolls vertically with mouse wheel

## Parameters

| Parameter | Default | Notes |
|-----------|---------|-------|
| ScrollDelay | 300ms | Wait between scroll + capture in auto mode |
| ScrollAmount | 3 | Mouse wheel notches per scroll (WHEEL_DELTA * N) |
| FrameDebounce | 150ms | Min interval between captures in manual mode |
| MaxIterations | 100 | Safety cap for auto mode |
| FixedElementThreshold | 50px | Max height to check for fixed headers/footers |
| MatchTolerance | 2 | Per-channel pixel difference tolerance for anti-aliasing |
| AutoScrollToTop | true | Scroll to top before auto capture |

## Edge Cases

- **Animated content:** Tolerance-based matching (MatchTolerance) handles minor pixel variations. Blinking cursors or ads may cause false negatives — the max iteration cap prevents infinite loops.
- **Lazy-loaded content:** In auto mode, the 300ms delay gives most apps time to load. If content loads slower, the user can increase ScrollDelay in settings or use manual mode.
- **Very long content:** Max 100 iterations * ~80% viewport per scroll = ~80 full viewports. For extreme cases, user can adjust MaxIterations.
- **Window loses focus:** If target window is occluded during capture, BitBlt captures what's on screen — results will be corrupted. Show a warning if foreground window changes.
- **High DPI:** Capture at native pixel resolution (same DPI-aware approach as existing ScreenCapture).
- **Smooth scrolling:** The 300ms delay accounts for smooth scroll animations in most apps.

## What This Does NOT Include
- No horizontal scroll support (vertical only for v1)
- No browser CDP integration (pixel-perfect web capture — can add later)
- No automatic scrollable region detection within a window (captures the full window viewport)
- No settings UI for scroll parameters (hardcoded defaults, configurable later if needed)
