# Customizable Shortcuts Design

**Date:** 2026-05-16
**Branch:** feature/customizable-shortcuts

## Goal

Make all keyboard shortcuts configurable in Settings. Currently, screenshot overlay shortcuts are customizable but recording shortcuts and snipping toolbar shortcuts are hardcoded. Add a global hotkey for History.

## Shortcut Groups

### 1. Global Hotkeys (system-wide, Win32 RegisterHotKey)

| Setting | Default | Status |
|---------|---------|--------|
| CaptureHotkey | PrintScreen | Exists |
| FullscreenSaveHotkey | Shift+PrintScreen | Exists |
| FullscreenClipboardHotkey | Ctrl+PrintScreen | Exists |
| HistoryHotkey | Alt+PrintScreen | **NEW** |

### 2. Tool Shortcuts (shared between screenshot overlay & recording)

| Setting | Default | Status |
|---------|---------|--------|
| ShortcutPen | P | Exists |
| ShortcutLine | L | Exists |
| ShortcutArrow | A | Exists |
| ShortcutRectangle | R | Exists |
| ShortcutEllipse | E | Exists |
| ShortcutText | T | Exists |
| ShortcutMarker | M | Exists |
| ShortcutBlur | B | Exists |
| ShortcutCheck | K | Exists |
| ShortcutCross | D | Exists |
| ShortcutObjectEraser | G | Exists |
| ShortcutEraser | X | Exists |
| ShortcutMove | V | Exists |
| ShortcutColor | C | Exists |
| ShortcutThickness | W | Exists |
| ShortcutHistory | H | Exists |
| ShortcutRecord | Ctrl+R | Exists |
| ShortcutOcr | O | Exists |
| ShortcutPin | F | Exists |
| ShortcutSave | Ctrl+S | Exists |
| ShortcutCopy | Ctrl+C | Exists |
| ShortcutUndo | Ctrl+Z | Exists |
| ShortcutRedo | Ctrl+Y | Exists |

### 3. Recording-Only Shortcuts (new settings)

| Setting | Default |
|---------|---------|
| ShortcutRecMic | M |
| ShortcutRecSystemAudio | S |
| ShortcutRecPause | Space |
| ShortcutRecStop | Q |
| ShortcutRecClearAll | C |

### 4. Snipping Toolbar Shortcuts (new settings)

| Setting | Default |
|---------|---------|
| ShortcutModeScreenshot | 1 |
| ShortcutModeVideo | 2 |
| ShortcutModeOcr | 3 |
| ShortcutToolbarRegion | R |
| ShortcutToolbarWindow | W |
| ShortcutToolbarFullscreen | F |

## Architecture Changes

### AppSettings.cs
- Add 11 new properties: HistoryHotkey, 5 recording-only, 5 snipping toolbar (+ 1 toolbar: ToolbarFullscreen is 6th)
- All with string defaults matching current hardcoded values

### App.xaml.cs
- Register HistoryHotkey in HotkeyManager → opens HistoryWindow
- Replace hardcoded virtual key codes in EscapeHookCallback() with dynamic lookup using ShortcutHelper
- Build a reverse map from AppSettings shortcut strings to virtual key codes at startup (and when settings change) for efficient matching in the low-level keyboard hook

### OverlayWindow.xaml.cs
- Replace hardcoded Key.D1/D2/D3/R/W/F in toolbar mode with ShortcutHelper.Matches against new toolbar settings

### RecordingOverlay.xaml.cs
- HandleShortcut() changes from char matching to comparing against AppSettings shortcut values

### SettingsWindow.xaml.cs
- Add new shortcuts to ToolShortcuts array with section grouping (Global Hotkeys, Tool Shortcuts, Recording Shortcuts, Toolbar Shortcuts)
- Duplicate validation spans all groups

### No changes needed
- ShortcutHelper.cs — already supports parsing and matching
- HotkeyManager.cs — already supports dynamic registration
- JSON persistence — just more properties
- Settings UI pattern — same TextBox capture mechanism

## Key Decisions
- Tool shortcuts are **shared** between screenshot and recording (changing Pen to J affects both)
- Recording-only shortcuts (Mic, System Audio, Pause, Stop, Clear All) are separate settings
- Snipping toolbar shortcuts (mode switches, capture types) are configurable
- Duplicate detection spans all shortcut groups
