# Snipping Toolbar & Color Persistence — Design Spec

## Overview

Two features for v2.11.0:
1. **Color persistence** — remember selected annotation color across sessions
2. **Snipping toolbar** — Windows Snipping Tool-style toolbar when PrintScreen is pressed, with mode switching (Screenshot/Video/OCR), capture type selection (Region/Window/Fullscreen), delay timer, and window capture

Design principles: lightweight, fast, seamless, reliable.

---

## Feature 1: Color Persistence

### Current State
- `_currentColor` is hardcoded to `Colors.Yellow` in `OverlayWindow.xaml.cs:51`
- `AppSettings.DefaultColor` exists (`#FF0000`) but is never read by the overlay
- Color resets to yellow every time the overlay opens

### Changes
- **AppSettings.cs**: Change `DefaultColor` default from `"#FF0000"` to `"#FFFF00"` (yellow, matching current hardcoded default)
- **OverlayWindow constructor**: Load `_currentColor` from `AppSettings.Instance.DefaultColor` using `ColorConverter`
- **Color palette click handler** (line ~136): After setting `_currentColor`, save to `AppSettings.Instance.DefaultColor` and call `AppSettings.Save()`
- **RecordingOverlay**: Load annotation color from `AppSettings.Instance.DefaultColor` instead of hardcoded yellow

### Files Modified
- `Core/AppSettings.cs` — change default value
- `Views/OverlayWindow.xaml.cs` — load color on init, save on change
- `Views/RecordingOverlay.xaml.cs` — load color from settings

---

## Feature 2: Snipping Toolbar

### Architecture

Integrated into the existing `OverlayWindow` as a new initial phase. No new windows except popups for dropdown menus.

### State Machine

```
OverlayWindow states (updated):

ToolbarIdle          — snipping toolbar visible, waiting for user action
  ↓ (user draws)
Selecting            — drawing region rectangle (existing)
  ↓ (mouse up)
HasSelection         — annotation tools visible (existing, screenshot mode only)

WindowSelecting      — hovering over windows, highlight follows cursor
  ↓ (click)
HasSelection / RecordingOverlay / OCR extract

Drawing / Resizing / Moving / OcrSelecting — existing states unchanged
```

### New Interaction Enum Values
- `ToolbarIdle` — initial state when overlay opens
- `WindowSelecting` — window capture hover mode

### Capture Modes (new enum: `CaptureMode`)
- `Screenshot` (default)
- `Video`
- `Ocr`

### Capture Types (new enum: `CaptureType`)
- `Region` (default) — draw to select
- `Window` — hover to highlight, click to capture
- `Fullscreen` — immediate capture of entire screen

### Flow by Mode × Capture Type

| Mode | Region | Window | Fullscreen |
|------|--------|--------|------------|
| **Screenshot** | Draw region → annotation toolbar | Hover+click window → annotation toolbar | Immediate full-screen selection → annotation toolbar |
| **Video** | Dim removed, draw on live screen → RecordingOverlay | Dim removed, hover+click → RecordingOverlay | Immediately launch RecordingOverlay |
| **OCR** | Draw region → extract text → copy → close | Click window → extract text → copy → close | Extract from full screen → copy → close |

### Delay Timer
- Options: None (default), 1s, 3s, 5s, 10s
- When delay > 0 and user begins capture (click/draw):
  1. Close the overlay entirely
  2. Show a small countdown overlay (centered number, like existing `DelayedCaptureWindow`)
  3. After countdown completes, re-capture screen and proceed based on mode
- Delay applies to all modes (screenshot, video, OCR)

### Snipping Toolbar UI

XAML element: `SnippingToolbar` Border inside OverlayWindow's `ToolbarCanvas`, positioned at top center.

```
[📷 Screenshot] [🎥 Video] | [📐 Region ▾] | [🔍 OCR] [⏱ No delay ▾] | [✕]
     ___
```

**Layout**: Horizontal StackPanel inside a Border.
- Background: `#FF111111`, border `#333`, corner radius 8, drop shadow
- Button style: reuse existing `TBtn` style (30x30, transparent, hover #333)
- Active mode: blue underline for Screenshot, red for Video, cyan for OCR
- Separators: vertical 1px `#333` lines between groups

**Buttons:**
1. Screenshot — camera icon (`#64B5F6`), default active
2. Video — video camera icon (`#F44336` when active, `#888` inactive)
3. Separator
4. Capture mode — dashed rectangle + dropdown arrow. Click opens popup with 3 options:
   - Region (dashed rect icon) — default
   - Window (window frame icon)
   - Fullscreen (monitor icon)
5. Separator
6. OCR — magnifying glass with T (`#26C6DA`)
7. Delay — clock icon + dropdown arrow. Click opens popup with options:
   - No delay (default)
   - 1 second / 3 seconds / 5 seconds / 10 seconds
8. Separator
9. Close — red X

**Dropdown popups**: Small Border elements (like existing color/thickness popups) that appear below the toolbar button. Click outside dismisses.

### Toolbar Visibility Logic
- **Visible**: When `_interaction == ToolbarIdle` or `WindowSelecting`
- **Hidden**: As soon as region selection starts (`Selecting`), or when transitioning to annotation/recording/OCR
- Also hidden during delay countdown

### Cursor by State
- `ToolbarIdle` + Region mode: Crosshair
- `ToolbarIdle` + Fullscreen mode: Arrow (no selection needed, buttons handle it)
- `WindowSelecting`: Arrow
- `Selecting`: Crosshair (existing)

### Window Capture Implementation

**New Win32 interop** in `NativeMethods.cs`:
- `WindowFromPoint(POINT)` — get window handle at screen point
- `GetAncestor(hwnd, GA_ROOT)` — get top-level parent
- `GetWindowRect(hwnd, out RECT)` — get window bounds in pixels

**Flow:**
1. On mouse move in `WindowSelecting` state:
   - Convert WPF mouse position to screen pixels (apply DPI + virtual bounds offset)
   - Call `WindowFromPoint` → `GetAncestor(GA_ROOT)` to get top-level window
   - Skip if hwnd is our own overlay window
   - Call `GetWindowRect` to get bounds
   - Convert pixel bounds back to DIPs
   - Draw/update a highlight rectangle (`#2196F3`, 3px, semi-transparent fill `#202196F3`) on a highlight layer
2. On click:
   - Use highlighted window bounds as the selection rectangle
   - Proceed based on capture mode

**Highlight element**: A single `Rectangle` on `MainCanvas` that moves with the cursor. Removed when capture completes or mode changes.

### Video Mode Transition

When capture mode is Video and the user starts interacting (mousedown for region, or click for window/fullscreen):
1. Remove dimming — set `DimmingPath.Fill` to transparent or hide `DimmingCanvas`
2. For region: let user draw on the now-transparent overlay, then:
   - Get the selected region bounds
   - Close overlay
   - Launch `RecordingOverlay` with those bounds (existing code in `Record_Click`)
3. For window: remove dim, enter `WindowSelecting`, on click → close overlay → launch RecordingOverlay
4. For fullscreen: close overlay → launch RecordingOverlay with full screen (existing `StartRecordCapture` code)

### OCR Simplified Flow

Current OCR flow: overlay → sub-region selection → `OcrResultWindow` with text.

New OCR flow:
1. User selects OCR mode on toolbar
2. User draws region (or selects window/fullscreen)
3. Extract text using existing `OcrHelper.ExtractTextAsync()`
4. Copy text to clipboard via `Clipboard.SetText()`
5. Show brief tray notification: "Text copied to clipboard"
6. Close overlay

No `OcrResultWindow`, no sub-region selection within captured area.

### Keyboard Shortcuts on Toolbar

These shortcuts are **only active during `ToolbarIdle` state** (before any region is drawn). Once a region is selected and annotation tools appear, the existing tool shortcuts (R=Rectangle, W=Thickness, F=Pin, etc.) take over. No conflicts.

- `Escape` — close overlay
- `1` — Screenshot mode
- `2` — Video mode  
- `3` — OCR mode
- `R` — Region capture type
- `W` — Window capture type
- `F` — Fullscreen capture type
- Existing global shortcuts (Shift+PrintScreen, Ctrl+PrintScreen) still work as direct hotkeys bypassing toolbar

**Seamless default**: Since the default is Screenshot + Region, pressing PrintScreen and immediately drawing works exactly like before — toolbar appears but doesn't block interaction. It auto-hides when selection starts.

### Changes to Existing Hotkey Behavior

- **PrintScreen** (`StartRegionCapture`): Opens OverlayWindow in `ToolbarIdle` state (with snipping toolbar). Previously went directly into region selection.
- **Shift+PrintScreen** (`FullscreenSave`): Unchanged — direct fullscreen save.
- **Ctrl+PrintScreen** (`FullscreenClipboard`): Unchanged — direct fullscreen copy.

### Tray Menu Updates

- "Take Screenshot" → opens overlay with snipping toolbar (same as PrintScreen)
- "Record Screen" → opens overlay with snipping toolbar, Video mode + Fullscreen capture type pre-selected. User can change to Region/Window if desired before starting.
- "Delayed Capture..." → keep for backwards compatibility

---

## Files Modified

### Core
- `Core/AppSettings.cs` — change `DefaultColor` default to `#FFFF00`
- `Core/NativeMethods.cs` — add `WindowFromPoint`, `GetAncestor`, `GetWindowRect`, `POINT`/`RECT` structs

### Views
- `Views/OverlayWindow.xaml` — add SnippingToolbar Border with all buttons, dropdown popup elements
- `Views/OverlayWindow.xaml.cs` — new fields (`_captureMode`, `_captureType`, `_delaySeconds`), new `Interaction` enum values, toolbar button handlers, window detection logic, modified `StartCapture()` to begin in `ToolbarIdle`, color persistence load/save, simplified OCR flow
- `Views/RecordingOverlay.xaml.cs` — load annotation color from settings

### Removed/Simplified
- `Views/DelayedCaptureWindow.xaml/.cs` — keep for now but tray menu may stop referencing it
- OCR result window is bypassed (still exists but not invoked from snipping toolbar flow)

---

## Non-Goals

- Freeform selection (like Snipping Tool's freeform snip) — not in scope
- Multi-monitor window detection — `WindowFromPoint` naturally works across monitors
- Window thumbnail preview in dropdown — too complex, not needed
- Settings UI for toolbar customization — use what works, iterate later
