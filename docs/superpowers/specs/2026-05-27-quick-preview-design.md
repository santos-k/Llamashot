# Quick Preview (Quick Look) — Design Spec

## Overview

A system-wide file preview feature for Llamashot. Press **Spacebar** when a file is selected in Windows Explorer to instantly preview it in a floating glass-themed window. The preview auto-updates as you navigate files. Also accessible by clicking thumbnails in Llamashot's History window.

## Goals

- Native Quick Look experience on Windows (Space to preview, Space/Esc to dismiss)
- Support images, video, audio, text/code, PDF, and markdown with zero added dependencies
- Auto-update preview when Explorer selection changes
- Integrate with History window (click thumbnail to preview)
- Minimal app size impact (all built-in WPF/Windows APIs)

## Architecture

### New Components

#### 1. `PreviewWindow.xaml` (Views)

Floating glass-morphism window:
- Always-on-top, no taskbar entry (`ShowInTaskbar=false`, `Topmost=true`)
- Centered on screen, ~70% of screen size (capped to reasonable max)
- Glass background matching Llamashot's existing dark-navy aesthetic
- Content area swaps between renderers based on file type
- Bottom bar: filename, file size, dimensions/duration
- Dismiss: Space, Escape, or click outside

#### 2. `FilePreviewManager.cs` (Core)

Singleton managing preview lifecycle:
- Determines file type from extension and selects appropriate renderer
- Queries Explorer's selected file via Shell COM automation (`SHCreateItemFromParsingName`, `IFolderView2`, `IShellView`)
- When preview is open, polls Explorer selection on a short timer (~200ms) to auto-update
- Arrow key navigation: Left/Right to prev/next file in same folder
- Manages open/close/toggle state

#### 3. Keyboard hook extension in `App.xaml.cs`

Extend the existing `EscapeHookCallback`:
- On `VK_SPACE` keydown: check if foreground window class is `CabinetWClass` (Explorer)
- If preview is open → close it (toggle)
- If preview is closed → query selected file, open preview
- Safety: Only intercepts Space when foreground is Explorer. All other apps unaffected.

### File Type Renderers

All renderers use built-in WPF/Windows APIs. Zero NuGet packages added.

| Type | Extensions | Renderer | Details |
|------|-----------|----------|---------|
| **Image** | png, jpg, jpeg, bmp, gif, ico, tiff, webp | WPF `Image` + `BitmapImage` | Mouse wheel zoom, actual dimensions shown |
| **Video** | mp4, avi, mov, mkv, wmv, webm | WPF `MediaElement` | Auto-play, hover controls (play/pause, seek bar, duration) |
| **Audio** | mp3, wav, flac, aac, wma, ogg | WPF `MediaElement` + visual | File icon + waveform visual, auto-play, hover controls |
| **Text/Code** | cs, py, js, ts, json, xml, html, css, cpp, java, go, rs, txt, log, ini, yaml, toml, bat, ps1, sh, sql, md (fallback), cfg, gitignore, etc. | `RichTextBox` with syntax highlighting | Regex-based highlighter: keywords, strings, comments, numbers. Per-language color schemes. |
| **Markdown** | md | WPF `FlowDocument` | Rendered headings, bold, italic, inline code, code blocks, links, lists, horizontal rules |
| **PDF** | pdf | `WebView2` control | Edge's built-in PDF renderer (pre-installed Win10/11). Falls back to file info if WebView2 unavailable. |
| **Unsupported** | docx, xlsx, pptx, zip, exe, dll, etc. | File info card | Large file type icon, filename, size, type description, created/modified dates, "Open with default app" button |

### Syntax Highlighting (Text/Code)

Lightweight regex-based highlighter, ~200-300 lines, covering:
- **Keywords** per language (if/else/for/while/class/function/def/return/etc.)
- **Strings** (single/double quoted, template literals)
- **Comments** (line `//` `#` and block `/* */`)
- **Numbers** (int, float, hex)
- **Language detection** by file extension mapping

Color scheme: dark background with colored tokens (similar to VS Code dark theme).

### Markdown Rendering

Convert markdown to WPF `FlowDocument` elements:
- `# H1` through `###### H6` → `Paragraph` with scaled font sizes
- `**bold**` / `*italic*` → `Bold` / `Italic` inlines
- `` `code` `` → `Run` with monospace font and background
- Code blocks (```) → `Paragraph` with monospace, dark background
- `- item` / `1. item` → `List` elements
- `[text](url)` → `Hyperlink`
- `---` → horizontal rule

### Preview Window UI

```
+--------------------------------------------------+
|  [glass background, dark navy]                    |
|                                                   |
|          [CONTENT AREA - 90% of window]           |
|          (image / video / text / etc.)            |
|                                                   |
|                                                   |
+--------------------------------------------------+
|  filename.ext    |    1.2 MB    |   1920x1080     |
+--------------------------------------------------+
```

- Title bar: hidden (borderless window)
- Close button: top-right corner, visible on hover
- Content area: centered, respects aspect ratio for images/video
- Bottom info bar: semi-transparent, filename + size + metadata
- Transitions: fade-in on open, content crossfade on file change

### Video/Audio Playback Controls

Hover-visible controls overlay:
- Play/pause button (center, large)
- Progress/seek bar (bottom)
- Current time / total duration
- Auto-play on preview open
- Pause when preview closes or file changes to non-media

### Explorer Selection Query

Use Windows Shell COM interfaces to get the currently selected file:

1. `FindWindow("CabinetWClass", ...)` to get Explorer window handle
2. `SHCreateItemFromParsingName` or `IServiceProvider` → `IShellBrowser` → `IShellView` → `IFolderView2`
3. `GetSelection()` → `IShellItemArray` → first item's `SIGDN_FILESYSPATH`

This is the same approach used by QuickLook and other Windows file preview tools.

### Arrow Key Navigation

When preview is open:
- Left arrow → previous file in folder (alphabetical)
- Right arrow → next file in folder (alphabetical)
- Uses `Directory.GetFiles()` on the parent folder, sorted, find current index, move ±1

### History Integration

In `HistoryWindow.xaml.cs`:
- Clicking a thumbnail opens `PreviewWindow` with the history item's file path
- Reuses the same `PreviewWindow` instance (singleton pattern)
- No arrow key navigation in history context (History has its own grid navigation)

## Settings

Add to `AppSettings.cs`:
- `QuickPreviewEnabled` (bool, default: `true`) — master on/off toggle
- `ShortcutQuickPreview` (string, default: `"Space"`) — configurable trigger key

Add to `SettingsWindow.xaml.cs`:
- Toggle for Quick Preview enable/disable
- Shortcut editor for the trigger key

## Size Impact

- **WebView2 runtime:** Pre-installed on Windows 10 (April 2018+) and Windows 11. Zero added binary size.
- **All renderers:** Built-in WPF and Windows APIs. No new NuGet packages.
- **Code addition:** ~800-1200 lines across 3-4 new files + minor edits to existing files.
- **Binary size increase:** Negligible (~50-100 KB compiled).

## Edge Cases

- **No file selected:** Do nothing (no preview opens)
- **File deleted while preview open:** Show error state, auto-close after 2s
- **Very large files (>100MB text):** Only load first 10,000 lines, show "File truncated" notice
- **Binary files with text extension:** Detect null bytes in first 8KB, fall back to file info card
- **Multiple Explorer windows:** Use the foreground Explorer window
- **WebView2 not available (old Win10):** PDF falls back to file info card with "Open in default app" button
- **Protected/locked files:** Show file info card with error message
