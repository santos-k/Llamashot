# Llamashot

A fast, lightweight screenshot and screen recording tool for Windows. Capture, annotate, record, extract text, and preview files with a single hotkey.

**[Live Page](https://santos-k.github.io/Llamashot/)** | **[Download Installer](https://github.com/santos-k/Llamashot/releases/latest)**

Built with .NET 10 and WPF. Made with love by **Santosh Kumar**.

## Features

### Snipping Toolbar
- **Windows Snipping Tool-style toolbar** - Press PrintScreen to open a top-center toolbar over dimmed screen
- **Five capture modes** - Screenshot (default), Video, GIF, Scroll, Text Extract with labeled icon buttons
- **Three capture types** - Region (draw to select), Window (hover + click), Fullscreen (instant)
- **Window capture** - Hover over any window to highlight with blue border, click to capture
- **Delay timer** - Dropdown: No delay, 1s, 3s, 5s, 10s countdown before capture
- **Toolbar keyboard shortcuts** - 1/2/3/4/5 for modes, R/W/F for capture types
- **Seamless default** - Drawing a region immediately works like before, toolbar auto-hides

- **Cursor tooltip** - Context-aware message follows cursor (e.g. "Drag to select capture area", "Click on a window to capture scroll")

### Scrolling Screenshot
- **Auto scroll mode** - Click a window, app scrolls via Page Down and captures frames automatically
- **Manual scroll mode** - Scroll the window yourself, app captures on each scroll event
- **Auto/Manual toggle** - Persisted toggle on toolbar, remembers preference across sessions
- **Scroll Down button** - Manual mode includes button to trigger scroll + capture without mouse wheel
- **Pixel-row stitching** - Intelligent overlap detection with hash-based row matching
- **Fixed element detection** - Automatically detects and removes duplicate headers/footers
- **Full annotation toolkit** - All 11 tools available in scroll preview (same as screenshot)
- **Handle-based crop** - 8 handles (4 corners + 4 edge midpoints) for precise cropping
- **Stop & Cancel** - Stop keeps stitched result, Cancel discards everything
- **Progress excluded from capture** - Popup windows hidden from screenshots via SetWindowDisplayAffinity
- **No content interaction** - Auto scroll uses keyboard-only (no clicks that open links/emails)
- **Escape to stop** - Press Esc during auto scroll to stop and stitch what's captured so far

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
- **Highlighter (H)** - Semi-transparent highlighter
- **Blur (B)** - Pixelate/blur sensitive areas
- **Check stamp (K)** - Green checkmark with circle (for reviews)
- **Cross stamp (D)** - Red X with circle (for reviews)
- **Emoji (J)** - Place colorful emojis as annotations (250+ emojis, 10 categories, search, recent history)
- **Object eraser (G)** - Click any annotation to remove it entirely (undoable)
- **Shift constraints** - Hold Shift for straight lines/arrows (0/45/90°), perfect squares, circles, horizontal/vertical highlights
- **Custom cursors** - Each tool shows a matching cursor icon

### Screen Recording
- **Pre-start flow** - Select region with resize handles, toggle mic/sys audio, then click Start with 3-2-1 countdown
- **Same selection as screenshot** - 8 resize handles, move by dragging, double-click for fullscreen toggle
- **Resizable recording border** - Blue L-shaped corner brackets, white midpoint bars, dimension label
- Record any selected region as MP4 video (H.264 compressed)
- **GIF recording mode** - Dedicated mode (key 3) with 30-second limit, countdown timer, auto-stop
- **Quality picker** - Choose Original (full res), High (720p), or Compact (480p) when saving GIF or video
- **Unlimited duration** for video, 10fps
- **Separate mic and system audio toggles** - Independent on/off before and during recording
- **Full annotation toolkit** - All 12 screenshot tools + blur available during recording (Pen, Line, Arrow, Rectangle, Ellipse, Text, Marker, Blur, Check, Cross, Emoji, Eraser, Undo, Clear)
- **Color picker + Thickness** - Change annotation color and stroke width during recording
- **Tool toggle** - Click or shortcut again to deselect, tools stay active between strokes
- **Text input isolation** - Keyboard shortcuts suppressed while typing text annotations, Enter/Esc to dismiss
- Keyboard shortcuts for all controls (M=mic, S=system audio, Space=pause, Q=stop)
- **Clean saving UI** - All tools hidden during save, showing only "Saving..." message
- Audio source status shown on recording bar (Mic / System / both)
- Pause/resume with visual indicators
- Red pulsing border shows recorded area
- Recording toolbar excluded from capture
- **Blur tool in recording** — blur sensitive areas before and during recording
- **Pre-recording blur** — blur, undo, erase available on the pre-start toolbar before clicking Start
- **GIF audio cleanup** — mic and system audio buttons hidden in GIF mode pre-start toolbar
- **Emoji excluded from capture** — emoji picker popup excluded from screen recording via SetWindowDisplayAffinity

### Text Extraction
- Extract text from any area of the screen
- **Direct extraction from toolbar** - Select Text Extract mode, draw region, text copied to clipboard instantly
- **Dashed selection border** - Cyan dashed outline distinguishes OCR selection
- **"Copied" notification** - Visual confirmation when text is copied
- Sub-region selection within captured screenshots
- Dark theme support (auto-inverts for better recognition)
- Scales up small text for accuracy
- Uses Windows built-in OCR (no external dependencies)

### History
- Thumbnails for all saved screenshots, clipboard captures, and video recordings
- Responsive grid layout (3 columns default, adapts to window width)
- Type badges: green "Saved", blue "Copied", red "Video"
- **Open in File Explorer** - One-click to reveal any file in Explorer
- **Auto-refresh** - History updates when window gets focus after new captures
- **Multi-select** with checkboxes and "Select All"
- **Bulk copy** - Single image as clipboard, multiple as file drop list
- **Save to file** - Per-item save button and bulk "Save Selected" to folder
- **Delete** - Single or bulk delete with confirmation
- One-click re-copy from history
- **Category filter** - Filter by All, Images, Videos, GIFs, Clipboard
- **Click to preview** - Thumbnails open in Quick Preview window
- Configurable storage location and max items

### Pin on Screen
- Pin any screenshot as a floating always-on-top window
- Adjustable opacity with mouse wheel
- Drag to reposition, double-click to copy
- Right-click or close button to dismiss

### Quick Preview (Space in Explorer)
- **Instant file preview** - Select any file in Explorer, press Space to preview
- **200+ file types** - Images (PNG, JPG, HEIC, RAW, AVIF), video, audio, code, PDF, markdown
- **Syntax highlighting** - 15+ languages with VS Code-style colors (C#, Python, JS, Go, Rust, SQL, and more)
- **Markdown rendering** - Headings, bold, italic, code blocks, links, lists rendered beautifully
- **Animated GIF playback** - Frame-by-frame animation with correct timing
- **Video/audio auto-play** - Click-to-seek progress bar, play/pause controls
- **PDF preview** - Rendered via built-in WebView2 (no extra software needed)
- **Image zoom & pan** - Mouse wheel to zoom, drag to pan when zoomed
- **Arrow key navigation** - Left/Right to browse files in Explorer's sort order
- **Explorer sync** - Navigating updates Explorer's selection highlight
- **Auto-update** - Preview updates when you click a different file in Explorer
- **Configurable shortcut** - Change the trigger key in Settings (default: Space)
- **File info fallback** - Unsupported files show name, size, dates, and "Open with" button
- **Resizable window** — drag edges and corners to resize freely
- **Fullscreen toggle** — expand to fill entire screen with one click
- **Code/rendered toggle (C)** — switch between rendered view and raw code for Markdown and HTML files
- **Markdown tables** — rendered with styled headers and cell borders
- **HTML preview** — rendered via WebView2, with source code toggle
- **Folder preview** — shows file count, folder count, total size, and dates
- **Shell icons** — unsupported files and folders show their actual Windows icon
- **Compact info view** — small card for unsupported files/folders instead of full window
- **Taskbar integration** — preview shows in taskbar, Space brings it forward when in background
- **UTF-16 support** — correctly detects and previews UTF-16 encoded text files

### Additional Features
- **System tray** - Single click captures, double click opens settings
- **Silent startup** - No popup when already running; launches quietly with Windows
- **Double-Esc emergency exit** - Global hard kill for any stuck overlay/recording
- **Selection clamping** - Selection region can't extend beyond screen bounds
- **Move tool (V)** - Drag selection and annotations together
- **Space+drag** - Photoshop-style temporary pan
- **Undo/Redo** - Ctrl+Z / Ctrl+Y
- **Thickness control (W)** - Dropdown 1-10 with visual preview
- **Color picker (C)** - 24-color palette with persistent color memory
- **Adaptive toolbar** - Auto-switches between 1 and 2 columns based on screen height
- **Glass morphism UI** - Gradient dark-navy toolbar backgrounds with subtle borders and shadows
- **Pill-shaped mode buttons** - Active mode highlighted with filled background and color glow
- **Colorful tool icons** - Each tool has a distinct colored icon with matching active highlight
- **Tool toggle** - Click or press shortcut again to deselect any tool
- **Floating delay countdown** - Big centered number counts down during delayed captures
- **Seamless auto-update** - Check for updates from About, downloads and installs silently in the background
- **28+ configurable shortcuts** - Every tool and action has a keyboard shortcut

## Keyboard Shortcuts

All shortcuts are customizable in Settings.

### Global Hotkeys

| Action | Default |
|--------|---------|
| Capture region | PrintScreen |
| Fullscreen save | Shift+PrintScreen |
| Fullscreen copy | Ctrl+PrintScreen |
| History | Alt+PrintScreen |

### Snipping Toolbar Shortcuts (before region selection)

| Action | Default |
|--------|---------|
| Screenshot mode | 1 |
| Recording mode | 2 |
| GIF mode | 3 |
| Long Shot mode | 4 |
| Text Extract mode | 5 |
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
| Highlighter | H |
| Blur | B |
| Check stamp | K |
| Cross stamp | D |
| Object eraser | G |
| Undo last | X |
| Move | V |
| Color picker | C |
| Thickness | W |
| History | Ctrl+H |
| Record | Ctrl+R |
| Text Extract | O |
| Pin | F |
| Pan (temporary) | Space+drag |
| Quick Preview | Space (in Explorer) |
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
| Highlighter | H |
| Check stamp | K |
| Cross stamp | D |
| Eraser | G |
| Undo | X |
| Clear all | Ctrl+Delete |
| Toggle microphone | N |
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
- **GIF encoding**: Custom GIF89a encoder with per-frame color tables
- **Audio capture**: WinRT AudioGraph (microphone + system loopback)
- **OCR**: Windows.Media.Ocr (built-in, offline)
- **Settings**: JSON file in AppData/Roaming
- **File preview**: WebView2 (PDF), GifBitmapDecoder (animated GIF), WPF FlowDocument (markdown/code)
- **Installer**: Inno Setup 6
- **Target**: Windows 10 (1903+) / Windows 11

## License

MIT
