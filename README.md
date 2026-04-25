# Llamashot

A fast, lightweight screenshot and screen recording tool for Windows. Capture, annotate, record, and extract text with a single hotkey.

**[Live Page](https://santos-k.github.io/Llamashot/)** | **[Download Installer](https://github.com/santos-k/Llamashot/raw/main/LlamashotSetup.exe)** | **[Download Executable](https://github.com/santos-k/Llamashot/raw/main/Llamashot.exe)**

Built with .NET 10 and WPF. Made with love by **Santosh Kumar**.

## Features

### Screenshot Capture
- **Region capture** - Press PrintScreen, drag to select any area
- **Fullscreen save** - Shift+PrintScreen saves entire screen to file
- **Fullscreen copy** - Ctrl+PrintScreen copies entire screen to clipboard
- **Delayed capture** - 1/3/5/10 second countdown timer
- **Multi-monitor support** - Captures across all connected displays
- **DPI aware** - PerMonitorV2 for crisp rendering on high-DPI screens

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

### Screen Recording
- Record any selected region as MP4 video
- Max 2 minutes duration, 10fps
- Pause/resume with visual indicators
- Red pulsing border shows recorded area
- Recording toolbar excluded from capture

### OCR Text Extraction
- Extract text from any area of the screen
- Sub-region selection within the captured screenshot
- Dark theme support (auto-inverts for better recognition)
- Scales up small text for accuracy
- Uses Windows built-in OCR (no external dependencies)

### Screenshot History
- Thumbnails for all saved and copied screenshots
- Type badges: green "Saved", blue "Copied"
- One-click re-copy from history
- Configurable storage location and max items

### Pin on Screen
- Pin any screenshot as a floating always-on-top window
- Adjustable opacity with mouse wheel
- Drag to reposition, double-click to copy
- Right-click or close button to dismiss

### Additional Features
- **System tray** - Single click captures, double click opens settings
- **Move tool (V)** - Drag selection and annotations together
- **Space+drag** - Photoshop-style temporary pan
- **Undo/Redo** - Ctrl+Z / Ctrl+Y
- **Thickness control (W)** - Dropdown 1-10 with visual preview
- **Color picker (C)** - 24-color palette
- **Adaptive toolbar** - Auto-switches between 1 and 2 columns based on screen height
- **21 configurable shortcuts** - Every tool and action has a keyboard shortcut

## Keyboard Shortcuts

All 21 shortcuts are customizable in Settings.

### Global Hotkeys

| Action | Default |
|--------|---------|
| Capture region | PrintScreen |
| Fullscreen save | Shift+PrintScreen |
| Fullscreen copy | Ctrl+PrintScreen |

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
| Undo last | X |
| Move | V |
| Color picker | C |
| Thickness | W |
| History | H |
| Record | Ctrl+R |
| OCR | O |
| Pin | F |
| Pan (temporary) | Space+drag |
| Close overlay | Escape |

## Installation

### Installer
Download and run `LlamashotSetup.exe` from the [Releases](https://github.com/santos-k/Llamashot/raw/main/LlamashotSetup.exe) page.

### Standalone Executable
Download `Llamashot.exe` directly — self-contained, no runtime needed:
[Download Llamashot.exe](https://github.com/santos-k/Llamashot/raw/main/Llamashot.exe)

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
- **OCR**: Windows.Media.Ocr (built-in, offline)
- **Settings**: JSON file in AppData/Roaming
- **Installer**: Inno Setup 6
- **Target**: Windows 10 (1903+) / Windows 11

## License

MIT
