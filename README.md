# Llamashot

A fast, lightweight screenshot and screen recording tool for Windows. Capture, annotate, record, and extract text with a single hotkey.

**[Live Page](https://santos-k.github.io/Llamashot/)** | **[Download Installer](https://github.com/santos-k/Llamashot/releases/latest)**

Built with .NET 10 and WPF. Made with love by **Santosh Kumar**.

## Features

### Snipping Toolbar
- **Windows Snipping Tool-style toolbar** - Press PrintScreen to open a top-center toolbar over dimmed screen
- **Three capture modes** - Screenshot (default), Video, OCR with labeled icon buttons
- **Three capture types** - Region (draw to select), Window (hover + click), Fullscreen (instant)
- **Window capture** - Hover over any window to highlight with blue border, click to capture
- **Delay timer** - Dropdown: No delay, 1s, 3s, 5s, 10s countdown before capture
- **Toolbar keyboard shortcuts** - 1/2/3 for modes, R/W/F for capture types
- **Seamless default** - Drawing a region immediately works like before, toolbar auto-hides

### Screenshot Capture
- **Region capture** - Press PrintScreen, drag to select any area
- **Fullscreen save** - Shift+PrintScreen saves entire screen to file
- **Fullscreen copy** - Ctrl+PrintScreen copies entire screen to clipboard
- **Delayed capture** - 1/3/5/10 second countdown timer
- **Multi-monitor support** - Captures across all connected displays
- **DPI aware** - PerMonitorV2 for crisp rendering on high-DPI screens
- **Double-click toggle** - Double-click selection to expand to full screen, again to revert

### Annotation Tools
- **Pencil (P)** - Freehand drawing
- **Line (L)** - Straight lines
- **Arrow (A)** - Arrows with filled heads
- **Rectangle (R)** - Outlined rectangles
- **Ellipse (E)** - Outlined ellipses
- **Text (T)** - Text annotations with configurable font size
- **Marker (M)** - Semi-transparent highlighter
- **Blur (B)** - Pixelate/blur sensitive areas
- **Check stamp (K)** - Green checkmark with circle (for reviews)
- **Cross stamp (D)** - Red X with circle (for reviews)
- **Object eraser (G)** - Click any annotation to remove it entirely (undoable)
- **Custom cursors** - Each tool shows a matching cursor icon

### Screen Recording
- **Pre-start flow** - Select region with resize handles, toggle mic/sys audio, then click Start with 3-2-1 countdown
- **Same selection as screenshot** - 8 resize handles, move by dragging, double-click for fullscreen toggle
- **Resizable recording border** - Blue L-shaped corner brackets, white midpoint bars, dimension label
- Record any selected region as MP4 video (H.264 compressed)
- **Unlimited duration**, 10fps
- **Separate mic and system audio toggles** - Independent on/off before and during recording
- **Full annotation toolkit** - All 11 screenshot tools available during recording (Pen, Line, Arrow, Rectangle, Ellipse, Text, Marker, Check, Cross, Eraser, Undo, Clear)
- **Color picker + Thickness** - Change annotation color and stroke width during recording
- **Tool toggle** - Click or shortcut again to deselect, tools stay active between strokes
- **Text input isolation** - Keyboard shortcuts suppressed while typing text annotations, Enter/Esc to dismiss
- Keyboard shortcuts for all controls (M=mic, S=system audio, Space=pause, Q=stop)
- **Clean saving UI** - All tools hidden during save, showing only "Saving..." message
- Audio source status shown on recording bar (Mic / System / both)
- Pause/resume with visual indicators
- Red pulsing border shows recorded area
- Recording toolbar excluded from capture

### OCR Text Extraction
- Extract text from any area of the screen
- **Direct OCR from toolbar** - Select OCR mode, draw region, text copied to clipboard instantly
- **Dashed selection border** - Cyan dashed outline distinguishes OCR selection
- **"Copied" notification** - Visual confirmation when text is copied
- Sub-region selection within captured screenshots
- Dark theme support (auto-inverts for better recognition)
- Scales up small text for accuracy
- Uses Windows built-in OCR (no external dependencies)

### Screenshot History
- Thumbnails for all saved and copied screenshots
- Responsive grid layout (3 columns default, adapts to window width)
- Type badges: green "Saved", blue "Copied"
- **Multi-select** with checkboxes and "Select All"
- **Bulk copy** - Single image as clipboard, multiple as file drop list
- **Save to file** - Per-item save button and bulk "Save Selected" to folder
- **Delete** - Single or bulk delete with confirmation
- One-click re-copy from history
- Configurable storage location and max items

### Pin on Screen
- Pin any screenshot as a floating always-on-top window
- Adjustable opacity with mouse wheel
- Drag to reposition, double-click to copy
- Right-click or close button to dismiss

### Additional Features
- **System tray** - Single click captures, double click opens settings
- **Silent auto-start** - Launches with Windows without popups (`--silent` flag)
- **Double-Esc emergency exit** - Global hard kill for any stuck overlay/recording
- **Selection clamping** - Selection region can't extend beyond screen bounds
- **Move tool (V)** - Drag selection and annotations together
- **Space+drag** - Photoshop-style temporary pan
- **Undo/Redo** - Ctrl+Z / Ctrl+Y
- **Thickness control (W)** - Dropdown 1-10 with visual preview
- **Color picker (C)** - 24-color palette with persistent color memory
- **Adaptive toolbar** - Auto-switches between 1 and 2 columns based on screen height
- **Colorful tool icons** - Each tool has a distinct colored icon with matching active highlight
- **Tool toggle** - Click or press shortcut again to deselect any tool
- **Seamless auto-update** - Check for updates from About, downloads and installs silently in the background
- **22 configurable shortcuts** - Every tool and action has a keyboard shortcut

## Keyboard Shortcuts

All 22 shortcuts are customizable in Settings.

### Global Hotkeys

| Action | Default |
|--------|---------|
| Capture region | PrintScreen |
| Fullscreen save | Shift+PrintScreen |
| Fullscreen copy | Ctrl+PrintScreen |

### Snipping Toolbar Shortcuts (before region selection)

| Action | Default |
|--------|---------|
| Screenshot mode | 1 |
| Video mode | 2 |
| OCR mode | 3 |
| Region capture | R |
| Window capture | W |
| Fullscreen capture | F |
| Close | Escape |

### Overlay Shortcuts

| Action | Default |
|--------|---------|
| Save | Ctrl+S |
| Copy | Ctrl+C |
| Undo | Ctrl+Z |
| Redo | Ctrl+Y |
| Pencil | P |
| Line | L |
| Arrow | A |
| Rectangle | R |
| Ellipse | E |
| Text | T |
| Marker | M |
| Blur | B |
| Check stamp | K |
| Cross stamp | D |
| Object eraser | G |
| Undo last | X |
| Move | V |
| Color picker | C |
| Thickness | W |
| History | H |
| Record | Ctrl+R |
| OCR | O |
| Pin | F |
| Pan (temporary) | Space+drag |
| Double-click | Toggle full region |
| Close overlay | Escape |
| **Double-Esc** | **Force close all overlays** |

### Recording Shortcuts (global during recording)

| Action | Default |
|--------|---------|
| Pen | P |
| Line | L |
| Arrow | A |
| Rectangle | R |
| Ellipse | E |
| Text | T |
| Marker | H |
| Check stamp | K |
| Cross stamp | D |
| Eraser | G |
| Undo | X |
| Clear all | C |
| Toggle microphone | M |
| Toggle system audio | S |
| Pause / Resume | Space |
| Stop recording | Q |

## Installation

### Installer
Download and run the latest installer from the [Releases](https://github.com/santos-k/Llamashot/releases/latest) page.

### Build from Source
```bash
git clone https://github.com/santos-k/Llamashot.git
cd Llamashot/Llamashot
dotnet build
dotnet run
```

## Tech Stack

- **Framework**: .NET 10, WPF
- **Screen capture**: GDI BitBlt via P/Invoke
- **Video encoding**: WinRT MediaComposition (JPEG frames to MP4)
- **Audio capture**: WinRT AudioGraph (microphone + system loopback)
- **OCR**: Windows.Media.Ocr (built-in, offline)
- **Settings**: JSON file in AppData/Roaming
- **Installer**: Inno Setup 6
- **Target**: Windows 10 (1903+) / Windows 11

## License

MIT
