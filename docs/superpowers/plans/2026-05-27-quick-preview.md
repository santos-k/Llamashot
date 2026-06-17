# Quick Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a system-wide Quick Look feature — press Spacebar in Windows Explorer to preview files (images, video, audio, text/code, PDF, markdown) in a floating glass-themed window that auto-updates as selection changes.

**Architecture:** A low-level keyboard hook intercepts Spacebar when Explorer is the foreground window. `FilePreviewManager` queries the selected file via Shell COM automation and opens/updates a singleton `PreviewWindow`. The preview window renders content using built-in WPF controls (Image, MediaElement, RichTextBox, FlowDocument, WebView2 for PDF). History thumbnails also open the same preview window on click.

**Tech Stack:** .NET 10, WPF, Shell COM (SHCreateItemFromParsingName, IShellWindows, IFolderView2), WebView2 (pre-installed), System.Text.Json

---

### File Map

| Action | File | Responsibility |
|--------|------|---------------|
| Create | `Core/FilePreviewManager.cs` | Singleton: Explorer COM query, file type detection, preview lifecycle, arrow key navigation |
| Create | `Core/SyntaxHighlighter.cs` | Regex-based syntax highlighting for code files |
| Create | `Core/MarkdownRenderer.cs` | Convert markdown text to WPF FlowDocument |
| Create | `Views/PreviewWindow.xaml` | Glass-morphism floating window with content area and info bar |
| Create | `Views/PreviewWindow.xaml.cs` | Content switching, media controls, zoom, keyboard handling |
| Modify | `Core/NativeMethods.cs` | Add VK_SPACE, FindWindow, GetClassName buffer, Shell COM GUIDs |
| Modify | `Core/AppSettings.cs` | Add `QuickPreviewEnabled` setting |
| Modify | `App.xaml.cs` | Extend keyboard hook to handle Spacebar in Explorer |
| Modify | `Views/HistoryWindow.xaml.cs` | Change thumbnail click to open PreviewWindow |
| Modify | `Views/SettingsWindow.xaml` | Add Quick Preview toggle |
| Modify | `Views/SettingsWindow.xaml.cs` | Wire toggle to setting |

---

### Task 1: Add NativeMethods for Explorer Detection and VK_SPACE

**Files:**
- Modify: `Llamashot/Core/NativeMethods.cs`

- [ ] **Step 1: Add VK_SPACE constant and FindWindow import**

Add after line 175 (`VK_ESCAPE = 0x1B`) in NativeMethods.cs:

```csharp
public const uint VK_SPACE = 0x20;
public const uint VK_LEFT = 0x25;
public const uint VK_RIGHT = 0x27;
```

Add a new `FindWindow` P/Invoke (after the existing `GetClassName` at line 244):

```csharp
[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
```

- [ ] **Step 2: Verify build**

Run: `cd D:/Llamashot/Llamashot && dotnet build --no-restore 2>&1 | tail -5`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add Llamashot/Core/NativeMethods.cs
git commit -m "feat(preview): add VK_SPACE, arrow VK constants, and FindWindow P/Invoke"
```

---

### Task 2: Add QuickPreviewEnabled Setting

**Files:**
- Modify: `Llamashot/Core/AppSettings.cs`
- Modify: `Llamashot/Views/SettingsWindow.xaml`
- Modify: `Llamashot/Views/SettingsWindow.xaml.cs`

- [ ] **Step 1: Add setting property**

In `AppSettings.cs`, add after line 91 (`MaxHistoryItems`):

```csharp
// Quick Preview
public bool QuickPreviewEnabled { get; set; } = true;
```

- [ ] **Step 2: Add toggle in SettingsWindow.xaml**

In `SettingsWindow.xaml`, add a new section after the History section (before the closing `</StackPanel>` of the ScrollViewer, around line 226):

```xml
<!-- Quick Preview -->
<TextBlock Text="Quick Preview" Style="{StaticResource SectionTitle}" />
<Border Style="{StaticResource Card}">
    <StackPanel>
        <CheckBox x:Name="ChkQuickPreview" Content="Enable Quick Preview (Space in Explorer)" />
        <TextBlock Text="Press Spacebar in File Explorer to preview files. Supports images, video, audio, text, PDF, and more."
                   Foreground="#666" FontSize="11" Margin="20,4,0,0" TextWrapping="Wrap" />
    </StackPanel>
</Border>
```

- [ ] **Step 3: Wire toggle in SettingsWindow.xaml.cs**

In `LoadSettings()`, add after the history settings loading:

```csharp
ChkQuickPreview.IsChecked = s.QuickPreviewEnabled;
```

In `Save_Click`, add in the save block:

```csharp
s.QuickPreviewEnabled = ChkQuickPreview.IsChecked == true;
```

- [ ] **Step 4: Verify build**

Run: `cd D:/Llamashot/Llamashot && dotnet build --no-restore 2>&1 | tail -5`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add Llamashot/Core/AppSettings.cs Llamashot/Views/SettingsWindow.xaml Llamashot/Views/SettingsWindow.xaml.cs
git commit -m "feat(preview): add QuickPreviewEnabled setting with UI toggle"
```

---

### Task 3: Create FilePreviewManager — Explorer COM Query

**Files:**
- Create: `Llamashot/Core/FilePreviewManager.cs`

This is the core singleton. It queries Explorer's selected file via Shell COM and manages the preview window lifecycle.

- [ ] **Step 1: Create FilePreviewManager.cs**

```csharp
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Llamashot.Core;

public class FilePreviewManager
{
    private static FilePreviewManager? _instance;
    public static FilePreviewManager Instance => _instance ??= new FilePreviewManager();

    private Views.PreviewWindow? _previewWindow;
    private DispatcherTimer? _pollTimer;
    private string? _currentFile;
    private string? _currentFolder;
    private string[]? _folderFiles;
    private int _currentIndex;

    // Shell COM interfaces for querying Explorer selection
    [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class ShellWindows { }

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"),
     InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IShellWindows
    {
        [DispId(0x60020000)] int Count { get; }
        [DispId(0)] object Item([In] object index);
    }

    // Supported file type categories
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tiff", ".tif", ".webp"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mov", ".mkv", ".wmv", ".webm"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".aac", ".wma", ".ogg", ".m4a"
    };

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".py", ".js", ".ts", ".jsx", ".tsx", ".json", ".xml", ".html", ".htm",
        ".css", ".scss", ".less", ".cpp", ".c", ".h", ".hpp", ".java", ".go", ".rs",
        ".rb", ".php", ".swift", ".kt", ".scala", ".r", ".sql", ".sh", ".bash",
        ".ps1", ".bat", ".cmd", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf",
        ".txt", ".log", ".csv", ".env", ".gitignore", ".dockerignore", ".editorconfig",
        ".csproj", ".sln", ".xaml", ".svg", ".makefile", ".dockerfile"
    };

    public enum PreviewType { Image, Video, Audio, Code, Markdown, Pdf, Unsupported }

    public static PreviewType GetPreviewType(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext)) return PreviewType.Code; // extensionless = text
        if (ext.Equals(".md", StringComparison.OrdinalIgnoreCase)) return PreviewType.Markdown;
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return PreviewType.Pdf;
        if (ImageExtensions.Contains(ext)) return PreviewType.Image;
        if (VideoExtensions.Contains(ext)) return PreviewType.Video;
        if (AudioExtensions.Contains(ext)) return PreviewType.Audio;
        if (CodeExtensions.Contains(ext)) return PreviewType.Code;
        return PreviewType.Unsupported;
    }

    /// <summary>
    /// Gets the selected file path from the foreground Explorer window using Shell COM.
    /// </summary>
    public string? GetExplorerSelectedFile()
    {
        try
        {
            var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
            if (shellWindowsType == null) return null;

            dynamic shellWindows = Activator.CreateInstance(shellWindowsType)!;
            var foreground = NativeMethods.GetForegroundWindow();

            try
            {
                int count = shellWindows.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic? window = null;
                    try
                    {
                        window = shellWindows.Item(i);
                        if (window == null) continue;

                        // Check if this shell window matches the foreground Explorer window
                        IntPtr hwnd = (IntPtr)(long)window.HWND;
                        if (hwnd != foreground) continue;

                        // Get the selected items via the Document.SelectedItems collection
                        dynamic document = window.Document;
                        dynamic selectedItems = document.SelectedItems();
                        if (selectedItems.Count == 0) continue;

                        // Return the first selected item's path
                        dynamic item = selectedItems.Item(0);
                        string path = item.Path;

                        if (File.Exists(path))
                            return path;
                    }
                    finally
                    {
                        if (window != null) Marshal.ReleaseComObject(window);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(shellWindows);
            }
        }
        catch
        {
            // COM errors are expected if no Explorer window is focused
        }

        return null;
    }

    /// <summary>
    /// Toggle preview: if open, close it. If closed, open with the given file.
    /// </summary>
    public void TogglePreview(string? filePath)
    {
        if (_previewWindow != null && _previewWindow.IsVisible)
        {
            ClosePreview();
            return;
        }

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        ShowPreview(filePath);
    }

    public void ShowPreview(string filePath)
    {
        if (!File.Exists(filePath)) return;

        _currentFile = filePath;
        UpdateFolderFiles(filePath);

        if (_previewWindow == null || !_previewWindow.IsVisible)
        {
            _previewWindow = new Views.PreviewWindow();
            _previewWindow.Closed += (s, e) =>
            {
                StopPolling();
                _previewWindow = null;
                _currentFile = null;
            };
            _previewWindow.Show();
            StartPolling();
        }

        _previewWindow.LoadFile(filePath);
    }

    public void ClosePreview()
    {
        StopPolling();
        _previewWindow?.Close();
        _previewWindow = null;
        _currentFile = null;
    }

    public bool IsPreviewOpen => _previewWindow?.IsVisible == true;

    /// <summary>
    /// Navigate to the previous/next file in the current folder.
    /// </summary>
    public void NavigateFile(int direction)
    {
        if (_folderFiles == null || _folderFiles.Length == 0) return;

        _currentIndex = Math.Clamp(_currentIndex + direction, 0, _folderFiles.Length - 1);
        var newFile = _folderFiles[_currentIndex];
        if (newFile != _currentFile)
        {
            _currentFile = newFile;
            _previewWindow?.LoadFile(newFile);
        }
    }

    private void UpdateFolderFiles(string filePath)
    {
        var folder = Path.GetDirectoryName(filePath);
        if (folder != _currentFolder)
        {
            _currentFolder = folder;
            _folderFiles = folder != null
                ? Directory.GetFiles(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();
        }
        _currentIndex = Array.IndexOf(_folderFiles!, filePath);
        if (_currentIndex < 0) _currentIndex = 0;
    }

    private void StartPolling()
    {
        _pollTimer?.Stop();
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _pollTimer.Tick += (s, e) => PollExplorerSelection();
        _pollTimer.Start();
    }

    private void StopPolling()
    {
        _pollTimer?.Stop();
        _pollTimer = null;
    }

    private void PollExplorerSelection()
    {
        if (_previewWindow == null || !_previewWindow.IsVisible) return;

        // Only poll when Explorer is still the foreground app
        var foreground = NativeMethods.GetForegroundWindow();
        var className = new StringBuilder(256);
        NativeMethods.GetClassName(foreground, className, 256);
        if (className.ToString() != "CabinetWClass") return;

        var selected = GetExplorerSelectedFile();
        if (selected != null && selected != _currentFile)
        {
            _currentFile = selected;
            UpdateFolderFiles(selected);
            _previewWindow.LoadFile(selected);
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `cd D:/Llamashot/Llamashot && dotnet build --no-restore 2>&1 | tail -5`
Expected: Build succeeded (PreviewWindow doesn't exist yet, so may get error — that's fine, we'll stub it next)

- [ ] **Step 3: Commit**

```bash
git add Llamashot/Core/FilePreviewManager.cs
git commit -m "feat(preview): add FilePreviewManager with Explorer COM query and file navigation"
```

---

### Task 4: Create SyntaxHighlighter

**Files:**
- Create: `Llamashot/Core/SyntaxHighlighter.cs`

- [ ] **Step 1: Create SyntaxHighlighter.cs**

```csharp
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Llamashot.Core;

public static class SyntaxHighlighter
{
    private static readonly Color KeywordColor = Color.FromRgb(0x56, 0x9C, 0xD6);    // Blue
    private static readonly Color StringColor = Color.FromRgb(0xCE, 0x91, 0x78);      // Orange
    private static readonly Color CommentColor = Color.FromRgb(0x6A, 0x99, 0x55);     // Green
    private static readonly Color NumberColor = Color.FromRgb(0xB5, 0xCE, 0xA8);      // Light green
    private static readonly Color TypeColor = Color.FromRgb(0x4E, 0xC9, 0xB0);        // Teal
    private static readonly Color DefaultColor = Color.FromRgb(0xDC, 0xDC, 0xDC);     // Light gray
    private static readonly Color XmlTagColor = Color.FromRgb(0x56, 0x9C, 0xD6);      // Blue
    private static readonly Color XmlAttrColor = Color.FromRgb(0x9C, 0xDC, 0xFE);     // Light blue
    private static readonly Color JsonKeyColor = Color.FromRgb(0x9C, 0xDC, 0xFE);     // Light blue

    private static readonly Dictionary<string, string[]> LanguageKeywords = new()
    {
        { "cs", new[] { "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "var", "virtual", "void", "volatile", "while", "async", "await", "record", "init", "required" } },
        { "py", new[] { "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else", "except", "False", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "None", "nonlocal", "not", "or", "pass", "raise", "return", "True", "try", "while", "with", "yield" } },
        { "js", new[] { "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else", "export", "extends", "false", "finally", "for", "function", "if", "import", "in", "instanceof", "let", "new", "null", "of", "return", "super", "switch", "this", "throw", "true", "try", "typeof", "undefined", "var", "void", "while", "with", "yield" } },
        { "java", new[] { "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class", "const", "continue", "default", "do", "double", "else", "enum", "extends", "final", "finally", "float", "for", "if", "implements", "import", "instanceof", "int", "interface", "long", "native", "new", "null", "package", "private", "protected", "public", "return", "short", "static", "strictfp", "super", "switch", "synchronized", "this", "throw", "throws", "transient", "try", "void", "volatile", "while", "true", "false", "var", "record", "sealed" } },
        { "go", new[] { "break", "case", "chan", "const", "continue", "default", "defer", "else", "fallthrough", "for", "func", "go", "goto", "if", "import", "interface", "map", "package", "range", "return", "select", "struct", "switch", "type", "var", "nil", "true", "false" } },
        { "rs", new[] { "as", "async", "await", "break", "const", "continue", "crate", "dyn", "else", "enum", "extern", "false", "fn", "for", "if", "impl", "in", "let", "loop", "match", "mod", "move", "mut", "pub", "ref", "return", "self", "Self", "static", "struct", "super", "trait", "true", "type", "unsafe", "use", "where", "while" } },
        { "sql", new[] { "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "CREATE", "DROP", "ALTER", "TABLE", "INDEX", "VIEW", "JOIN", "INNER", "LEFT", "RIGHT", "OUTER", "ON", "AND", "OR", "NOT", "NULL", "IS", "IN", "BETWEEN", "LIKE", "ORDER", "BY", "GROUP", "HAVING", "LIMIT", "OFFSET", "AS", "SET", "VALUES", "INTO", "DISTINCT", "COUNT", "SUM", "AVG", "MAX", "MIN", "UNION", "ALL", "EXISTS", "CASE", "WHEN", "THEN", "ELSE", "END", "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "CONSTRAINT", "DEFAULT", "CHECK", "UNIQUE" } },
    };

    // Map file extensions to language keys
    private static readonly Dictionary<string, string> ExtToLang = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".cs", "cs" }, { ".csx", "cs" },
        { ".py", "py" }, { ".pyw", "py" },
        { ".js", "js" }, { ".jsx", "js" }, { ".mjs", "js" },
        { ".ts", "js" }, { ".tsx", "js" },
        { ".java", "java" }, { ".kt", "java" }, { ".scala", "java" },
        { ".go", "go" },
        { ".rs", "rs" },
        { ".sql", "sql" },
        { ".rb", "js" }, { ".php", "js" }, { ".swift", "js" },
        { ".cpp", "cs" }, { ".c", "cs" }, { ".h", "cs" }, { ".hpp", "cs" },
    };

    // Comment styles per language
    private static readonly Dictionary<string, (string line, string? blockStart, string? blockEnd)> CommentStyles = new()
    {
        { "cs", ("//", "/*", "*/") },
        { "py", ("#", "\"\"\"", "\"\"\"") },
        { "js", ("//", "/*", "*/") },
        { "java", ("//", "/*", "*/") },
        { "go", ("//", "/*", "*/") },
        { "rs", ("//", "/*", "*/") },
        { "sql", ("--", "/*", "*/") },
    };

    public static FlowDocument Highlight(string text, string filePath)
    {
        var doc = new FlowDocument
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = new SolidColorBrush(DefaultColor),
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
            FontSize = 13,
            PagePadding = new Thickness(16, 12, 16, 12)
        };

        var ext = Path.GetExtension(filePath);

        // Handle XML/HTML/XAML/SVG
        if (IsXmlLike(ext))
        {
            HighlightXml(doc, text);
            return doc;
        }

        // Handle JSON
        if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            HighlightJson(doc, text);
            return doc;
        }

        // Handle INI/TOML/YAML/config files with simple highlighting
        if (IsConfigFile(ext))
        {
            HighlightConfig(doc, text, ext);
            return doc;
        }

        // General code highlighting
        var lang = ExtToLang.GetValueOrDefault(ext, "");
        var keywords = lang != "" ? LanguageKeywords.GetValueOrDefault(lang) : null;
        var commentStyle = lang != "" ? CommentStyles.GetValueOrDefault(lang) : null;
        var lineComment = commentStyle?.line ?? (ext == ".bat" || ext == ".cmd" ? "REM" : "#");

        HighlightCode(doc, text, keywords, lineComment);
        return doc;
    }

    private static bool IsXmlLike(string ext) =>
        ext.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".htm", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".xaml", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".svg", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".sln", StringComparison.OrdinalIgnoreCase);

    private static bool IsConfigFile(string ext) =>
        ext.Equals(".ini", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".toml", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".yml", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".cfg", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".conf", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".env", StringComparison.OrdinalIgnoreCase);

    private static void HighlightCode(FlowDocument doc, string text, string[]? keywords, string lineComment)
    {
        var keywordSet = keywords != null ? new HashSet<string>(keywords) : null;
        var lines = text.Split('\n');
        var maxLines = Math.Min(lines.Length, 10000);

        for (int i = 0; i < maxLines; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var para = new Paragraph { Margin = new Thickness(0), LineHeight = 20 };

            // Line number
            para.Inlines.Add(new Run($"{i + 1,5}  ")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
            });

            // Check if entire line is a comment
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(lineComment))
            {
                para.Inlines.Add(new Run(line) { Foreground = new SolidColorBrush(CommentColor) });
                doc.Blocks.Add(para);
                continue;
            }

            // Tokenize the line
            HighlightLine(para, line, keywordSet);
            doc.Blocks.Add(para);
        }

        if (lines.Length > maxLines)
        {
            var truncPara = new Paragraph { Margin = new Thickness(0, 10, 0, 0) };
            truncPara.Inlines.Add(new Run($"\n--- File truncated ({lines.Length:N0} lines total) ---")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontStyle = FontStyles.Italic
            });
            doc.Blocks.Add(truncPara);
        }
    }

    private static void HighlightLine(Paragraph para, string line, HashSet<string>? keywords)
    {
        // Regex: strings, numbers, identifiers
        var pattern = @"(""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|`(?:[^`\\]|\\.)*`)|(\b\d+\.?\d*\b)|(//.*$)|(#.*$)|(\b[A-Za-z_]\w*\b)|(.)";
        var matches = Regex.Matches(line, pattern);

        foreach (Match m in matches)
        {
            if (m.Groups[1].Success) // String
                para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(StringColor) });
            else if (m.Groups[2].Success) // Number
                para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(NumberColor) });
            else if (m.Groups[3].Success || m.Groups[4].Success) // Comment
                para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(CommentColor) });
            else if (m.Groups[5].Success) // Identifier or keyword
            {
                if (keywords != null && keywords.Contains(m.Value))
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(KeywordColor) });
                else if (m.Value.Length > 0 && char.IsUpper(m.Value[0]))
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(TypeColor) });
                else
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(DefaultColor) });
            }
            else
                para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(DefaultColor) });
        }
    }

    private static void HighlightXml(FlowDocument doc, string text)
    {
        var lines = text.Split('\n');
        var maxLines = Math.Min(lines.Length, 10000);

        for (int i = 0; i < maxLines; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var para = new Paragraph { Margin = new Thickness(0), LineHeight = 20 };
            para.Inlines.Add(new Run($"{i + 1,5}  ")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
            });

            // Simple XML highlighting: tags, attributes, strings, comments
            var xmlPattern = @"(<!--.*?-->)|(""[^""]*"")|(</?\w[\w:.-]*)(/?>|>)|([^<""]+)";
            foreach (Match m in Regex.Matches(line, xmlPattern))
            {
                if (m.Groups[1].Success) // Comment
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(CommentColor) });
                else if (m.Groups[2].Success) // Attribute value (string)
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(StringColor) });
                else if (m.Groups[3].Success) // Tag name
                {
                    para.Inlines.Add(new Run(m.Groups[3].Value) { Foreground = new SolidColorBrush(XmlTagColor) });
                    if (m.Groups[4].Success)
                        para.Inlines.Add(new Run(m.Groups[4].Value) { Foreground = new SolidColorBrush(DefaultColor) });
                }
                else
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(DefaultColor) });
            }

            doc.Blocks.Add(para);
        }
    }

    private static void HighlightJson(FlowDocument doc, string text)
    {
        var lines = text.Split('\n');
        var maxLines = Math.Min(lines.Length, 10000);

        for (int i = 0; i < maxLines; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var para = new Paragraph { Margin = new Thickness(0), LineHeight = 20 };
            para.Inlines.Add(new Run($"{i + 1,5}  ")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
            });

            // JSON: keys (strings before colon), values (strings, numbers, true/false/null)
            var jsonPattern = @"(""(?:[^""\\]|\\.)*"")\s*(:)|(""(?:[^""\\]|\\.)*"")|(\b(?:true|false|null)\b)|(-?\d+\.?\d*(?:[eE][+-]?\d+)?)|([{}\[\]:,])|(.)";
            foreach (Match m in Regex.Matches(line, jsonPattern))
            {
                if (m.Groups[1].Success) // Key
                {
                    para.Inlines.Add(new Run(m.Groups[1].Value) { Foreground = new SolidColorBrush(JsonKeyColor) });
                    para.Inlines.Add(new Run(m.Groups[2].Value) { Foreground = new SolidColorBrush(DefaultColor) });
                }
                else if (m.Groups[3].Success) // String value
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(StringColor) });
                else if (m.Groups[4].Success) // Boolean/null
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(KeywordColor) });
                else if (m.Groups[5].Success) // Number
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(NumberColor) });
                else
                    para.Inlines.Add(new Run(m.Value) { Foreground = new SolidColorBrush(DefaultColor) });
            }

            doc.Blocks.Add(para);
        }
    }

    private static void HighlightConfig(FlowDocument doc, string text, string ext)
    {
        var lines = text.Split('\n');
        var maxLines = Math.Min(lines.Length, 10000);

        for (int i = 0; i < maxLines; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var para = new Paragraph { Margin = new Thickness(0), LineHeight = 20 };
            para.Inlines.Add(new Run($"{i + 1,5}  ")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
            });

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#') || trimmed.StartsWith(';'))
            {
                para.Inlines.Add(new Run(line) { Foreground = new SolidColorBrush(CommentColor) });
            }
            else if (trimmed.StartsWith('[') && trimmed.Contains(']'))
            {
                para.Inlines.Add(new Run(line) { Foreground = new SolidColorBrush(TypeColor) });
            }
            else if (trimmed.Contains('=') || trimmed.Contains(':'))
            {
                var sep = trimmed.Contains('=') ? '=' : ':';
                var idx = line.IndexOf(sep);
                para.Inlines.Add(new Run(line[..idx]) { Foreground = new SolidColorBrush(JsonKeyColor) });
                para.Inlines.Add(new Run(line[idx].ToString()) { Foreground = new SolidColorBrush(DefaultColor) });
                para.Inlines.Add(new Run(line[(idx + 1)..]) { Foreground = new SolidColorBrush(StringColor) });
            }
            else
            {
                para.Inlines.Add(new Run(line) { Foreground = new SolidColorBrush(DefaultColor) });
            }

            doc.Blocks.Add(para);
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `cd D:/Llamashot/Llamashot && dotnet build --no-restore 2>&1 | tail -5`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add Llamashot/Core/SyntaxHighlighter.cs
git commit -m "feat(preview): add regex-based syntax highlighter for 15+ languages"
```

---

### Task 5: Create MarkdownRenderer

**Files:**
- Create: `Llamashot/Core/MarkdownRenderer.cs`

- [ ] **Step 1: Create MarkdownRenderer.cs**

```csharp
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Llamashot.Core;

public static class MarkdownRenderer
{
    private static readonly FontFamily MonoFont = new("Cascadia Code, Consolas, Courier New");
    private static readonly FontFamily SansFont = new("Segoe UI, Arial");
    private static readonly Color TextColor = Color.FromRgb(0xDC, 0xDC, 0xDC);
    private static readonly Color HeadingColor = Color.FromRgb(0x56, 0x9C, 0xD6);
    private static readonly Color CodeBgColor = Color.FromRgb(0x2D, 0x2D, 0x2D);
    private static readonly Color CodeColor = Color.FromRgb(0xCE, 0x91, 0x78);
    private static readonly Color LinkColor = Color.FromRgb(0x42, 0xA5, 0xF5);
    private static readonly Color QuoteColor = Color.FromRgb(0x88, 0x88, 0x88);
    private static readonly Color HrColor = Color.FromRgb(0x44, 0x44, 0x44);

    public static FlowDocument Render(string markdown)
    {
        var doc = new FlowDocument
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = new SolidColorBrush(TextColor),
            FontFamily = SansFont,
            FontSize = 14,
            PagePadding = new Thickness(24, 16, 24, 16)
        };

        var lines = markdown.Split('\n');
        int i = 0;
        bool inCodeBlock = false;
        var codeBlockLines = new List<string>();

        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd('\r');

            // Fenced code block
            if (line.TrimStart().StartsWith("```"))
            {
                if (inCodeBlock)
                {
                    // End of code block
                    AddCodeBlock(doc, string.Join("\n", codeBlockLines));
                    codeBlockLines.Clear();
                    inCodeBlock = false;
                }
                else
                {
                    inCodeBlock = true;
                }
                i++;
                continue;
            }

            if (inCodeBlock)
            {
                codeBlockLines.Add(line);
                i++;
                continue;
            }

            // Blank line
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            // Horizontal rule
            var trimmed = line.Trim();
            if (Regex.IsMatch(trimmed, @"^[-*_]{3,}$"))
            {
                var hr = new Paragraph
                {
                    Margin = new Thickness(0, 8, 0, 8),
                    BorderBrush = new SolidColorBrush(HrColor),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(0, 0, 0, 8)
                };
                doc.Blocks.Add(hr);
                i++;
                continue;
            }

            // Headings
            var headingMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (headingMatch.Success)
            {
                var level = headingMatch.Groups[1].Value.Length;
                var text = headingMatch.Groups[2].Value;
                var fontSize = level switch
                {
                    1 => 28.0,
                    2 => 24.0,
                    3 => 20.0,
                    4 => 17.0,
                    5 => 15.0,
                    _ => 14.0
                };

                var para = new Paragraph
                {
                    FontSize = fontSize,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(HeadingColor),
                    Margin = new Thickness(0, level <= 2 ? 16 : 10, 0, 6)
                };
                AddInlineMarkdown(para, text);
                doc.Blocks.Add(para);
                i++;
                continue;
            }

            // Block quotes
            if (trimmed.StartsWith('>'))
            {
                var quoteText = trimmed.TrimStart('>').TrimStart();
                var para = new Paragraph
                {
                    Foreground = new SolidColorBrush(QuoteColor),
                    FontStyle = FontStyles.Italic,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(12, 4, 0, 4),
                    Margin = new Thickness(0, 4, 0, 4)
                };
                AddInlineMarkdown(para, quoteText);
                doc.Blocks.Add(para);
                i++;
                continue;
            }

            // Unordered list
            var ulMatch = Regex.Match(line, @"^(\s*)[-*+]\s+(.+)$");
            if (ulMatch.Success)
            {
                var indent = ulMatch.Groups[1].Value.Length / 2;
                var text = ulMatch.Groups[2].Value;
                var para = new Paragraph
                {
                    Margin = new Thickness(16 + indent * 16, 2, 0, 2)
                };
                para.Inlines.Add(new Run("\u2022  ") { Foreground = new SolidColorBrush(HeadingColor) });
                AddInlineMarkdown(para, text);
                doc.Blocks.Add(para);
                i++;
                continue;
            }

            // Ordered list
            var olMatch = Regex.Match(line, @"^(\s*)(\d+)\.\s+(.+)$");
            if (olMatch.Success)
            {
                var indent = olMatch.Groups[1].Value.Length / 2;
                var num = olMatch.Groups[2].Value;
                var text = olMatch.Groups[3].Value;
                var para = new Paragraph
                {
                    Margin = new Thickness(16 + indent * 16, 2, 0, 2)
                };
                para.Inlines.Add(new Run($"{num}.  ") { Foreground = new SolidColorBrush(HeadingColor) });
                AddInlineMarkdown(para, text);
                doc.Blocks.Add(para);
                i++;
                continue;
            }

            // Regular paragraph
            {
                var para = new Paragraph { Margin = new Thickness(0, 3, 0, 3) };
                AddInlineMarkdown(para, line);
                doc.Blocks.Add(para);
            }

            i++;
        }

        // Close unclosed code block
        if (inCodeBlock && codeBlockLines.Count > 0)
            AddCodeBlock(doc, string.Join("\n", codeBlockLines));

        return doc;
    }

    private static void AddCodeBlock(FlowDocument doc, string code)
    {
        var para = new Paragraph
        {
            FontFamily = MonoFont,
            FontSize = 12,
            Background = new SolidColorBrush(CodeBgColor),
            Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC)),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 6, 0, 6),
            LineHeight = 18
        };
        para.Inlines.Add(new Run(code));
        doc.Blocks.Add(para);
    }

    private static void AddInlineMarkdown(Paragraph para, string text)
    {
        // Process inline elements: bold, italic, code, links, images
        // Pattern order matters: bold before italic
        var pattern = @"(\*\*|__)(.*?)\1|(\*|_)(.*?)\3|`([^`]+)`|\[([^\]]+)\]\(([^)]+)\)|!\[([^\]]*)\]\(([^)]+)\)|(.+?)";
        int lastIndex = 0;
        var remaining = text;

        // Simple state machine for inline parsing
        var inlinePattern = @"(?:\*\*|__)(.+?)(?:\*\*|__)|(?:\*|_)(.+?)(?:\*|_)|`([^`]+)`|\[([^\]]+)\]\(([^)]+)\)";
        var matches = Regex.Matches(text, inlinePattern);

        if (matches.Count == 0)
        {
            para.Inlines.Add(new Run(text));
            return;
        }

        foreach (Match m in matches)
        {
            // Add text before match
            if (m.Index > lastIndex)
                para.Inlines.Add(new Run(text[lastIndex..m.Index]));

            if (m.Groups[1].Success) // Bold
            {
                para.Inlines.Add(new Run(m.Groups[1].Value) { FontWeight = FontWeights.Bold });
            }
            else if (m.Groups[2].Success) // Italic
            {
                para.Inlines.Add(new Run(m.Groups[2].Value) { FontStyle = FontStyles.Italic });
            }
            else if (m.Groups[3].Success) // Inline code
            {
                para.Inlines.Add(new Run(m.Groups[3].Value)
                {
                    FontFamily = MonoFont,
                    Background = new SolidColorBrush(CodeBgColor),
                    Foreground = new SolidColorBrush(CodeColor),
                    FontSize = 12
                });
            }
            else if (m.Groups[4].Success) // Link
            {
                var hyperlink = new Hyperlink(new Run(m.Groups[4].Value))
                {
                    Foreground = new SolidColorBrush(LinkColor),
                    TextDecorations = null
                };
                try
                {
                    hyperlink.NavigateUri = new Uri(m.Groups[5].Value, UriKind.RelativeOrAbsolute);
                    hyperlink.RequestNavigate += (s, e) =>
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
                        { UseShellExecute = true });
                    };
                }
                catch { /* Invalid URI — ignore */ }
                para.Inlines.Add(hyperlink);
            }

            lastIndex = m.Index + m.Length;
        }

        // Add remaining text
        if (lastIndex < text.Length)
            para.Inlines.Add(new Run(text[lastIndex..]));
    }
}
```

- [ ] **Step 2: Verify build**

Run: `cd D:/Llamashot/Llamashot && dotnet build --no-restore 2>&1 | tail -5`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add Llamashot/Core/MarkdownRenderer.cs
git commit -m "feat(preview): add markdown renderer converting md to WPF FlowDocument"
```

---

### Task 6: Create PreviewWindow XAML

**Files:**
- Create: `Llamashot/Views/PreviewWindow.xaml`

- [ ] **Step 1: Create PreviewWindow.xaml**

```xml
<Window x:Class="Llamashot.Views.PreviewWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        WindowStartupLocation="CenterScreen"
        KeyDown="Window_KeyDown"
        MouseLeftButtonDown="Window_MouseLeftButtonDown">

    <Border x:Name="RootBorder" CornerRadius="10"
            BorderBrush="#33FFFFFF" BorderThickness="1">
        <Border.Background>
            <SolidColorBrush Color="#1A1A2E" Opacity="0.95" />
        </Border.Background>
        <Border.Effect>
            <DropShadowEffect BlurRadius="30" ShadowDepth="0" Opacity="0.6" Color="Black" />
        </Border.Effect>

        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="*" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <!-- Content Area -->
            <Grid x:Name="ContentArea" Margin="2" ClipToBounds="True">
                <Border x:Name="ContentBorder" CornerRadius="8,8,0,0" Background="#1E1E1E" />

                <!-- Image preview -->
                <ScrollViewer x:Name="ImageScroller" Visibility="Collapsed"
                              HorizontalScrollBarVisibility="Auto"
                              VerticalScrollBarVisibility="Auto"
                              Background="Transparent">
                    <Image x:Name="PreviewImage" Stretch="Uniform"
                           RenderOptions.BitmapScalingMode="HighQuality" />
                </ScrollViewer>

                <!-- Video/Audio preview -->
                <Grid x:Name="MediaPanel" Visibility="Collapsed">
                    <MediaElement x:Name="MediaPlayer" LoadedBehavior="Manual"
                                  UnloadedBehavior="Stop" Stretch="Uniform"
                                  MediaOpened="MediaPlayer_MediaOpened"
                                  MediaEnded="MediaPlayer_MediaEnded" />

                    <!-- Audio visual (shown only for audio files) -->
                    <StackPanel x:Name="AudioVisual" Visibility="Collapsed"
                                VerticalAlignment="Center" HorizontalAlignment="Center">
                        <TextBlock Text="&#x266B;" FontSize="80" Foreground="#42A5F5"
                                   HorizontalAlignment="Center" />
                        <TextBlock x:Name="AudioFileName" Foreground="#CCC" FontSize="16"
                                   HorizontalAlignment="Center" Margin="0,12,0,0" />
                    </StackPanel>

                    <!-- Media controls overlay (visible on hover) -->
                    <Border x:Name="MediaControls" VerticalAlignment="Bottom"
                            Background="#CC000000" Padding="12,8" Opacity="0"
                            CornerRadius="0,0,0,0">
                        <Border.Style>
                            <Style TargetType="Border">
                                <Style.Triggers>
                                    <Trigger Property="IsMouseOver" Value="True">
                                        <Setter Property="Opacity" Value="1" />
                                    </Trigger>
                                </Style.Triggers>
                            </Style>
                        </Border.Style>
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <Button x:Name="BtnPlayPause" Content="&#x23F8;" FontSize="18"
                                    Click="PlayPause_Click" Width="36" Height="36"
                                    Background="Transparent" Foreground="White"
                                    BorderThickness="0" Cursor="Hand"
                                    VerticalAlignment="Center" />
                            <Slider x:Name="SeekBar" Grid.Column="1"
                                    VerticalAlignment="Center" Margin="10,0"
                                    Minimum="0" Maximum="100"
                                    Thumb.DragStarted="SeekBar_DragStarted"
                                    Thumb.DragCompleted="SeekBar_DragCompleted"
                                    ValueChanged="SeekBar_ValueChanged" />
                            <TextBlock x:Name="TxtDuration" Grid.Column="2"
                                       Foreground="#AAA" FontSize="12"
                                       VerticalAlignment="Center" Text="0:00 / 0:00" />
                        </Grid>
                    </Border>
                </Grid>

                <!-- Text/Code preview -->
                <FlowDocumentScrollViewer x:Name="TextViewer" Visibility="Collapsed"
                                          VerticalScrollBarVisibility="Auto"
                                          IsToolBarVisible="False"
                                          Background="#1E1E1E"
                                          Zoom="100" />

                <!-- PDF preview (WebView2) -->
                <Border x:Name="PdfPanel" Visibility="Collapsed" Background="#1E1E1E">
                    <!-- WebView2 added in code-behind when needed -->
                </Border>

                <!-- Unsupported file info card -->
                <StackPanel x:Name="FileInfoPanel" Visibility="Collapsed"
                            VerticalAlignment="Center" HorizontalAlignment="Center">
                    <TextBlock x:Name="FileInfoIcon" Text="&#x1F4C4;" FontSize="64"
                               HorizontalAlignment="Center" Margin="0,0,0,16" />
                    <TextBlock x:Name="FileInfoName" Foreground="#EEE" FontSize="18"
                               FontWeight="SemiBold" HorizontalAlignment="Center"
                               TextWrapping="Wrap" MaxWidth="400" TextAlignment="Center" />
                    <TextBlock x:Name="FileInfoType" Foreground="#888" FontSize="13"
                               HorizontalAlignment="Center" Margin="0,6,0,0" />
                    <TextBlock x:Name="FileInfoSize" Foreground="#888" FontSize="13"
                               HorizontalAlignment="Center" Margin="0,4,0,0" />
                    <TextBlock x:Name="FileInfoDates" Foreground="#666" FontSize="12"
                               HorizontalAlignment="Center" Margin="0,8,0,0"
                               TextAlignment="Center" />
                    <Button x:Name="BtnOpenWith" Content="Open with default app"
                            Margin="0,20,0,0" Padding="16,8" Cursor="Hand"
                            Click="OpenWith_Click"
                            Background="#42A5F5" Foreground="White"
                            BorderThickness="0" FontSize="13" />
                </StackPanel>

                <!-- Loading indicator -->
                <Border x:Name="LoadingOverlay" Visibility="Collapsed"
                        Background="#CC1A1A2E">
                    <TextBlock Text="Loading..." Foreground="#888" FontSize="16"
                               VerticalAlignment="Center" HorizontalAlignment="Center" />
                </Border>
            </Grid>

            <!-- Bottom info bar -->
            <Border Grid.Row="1" Background="#12FFFFFF" Padding="14,8"
                    CornerRadius="0,0,10,10">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBlock x:Name="TxtFileName" Foreground="#CCC" FontSize="12"
                               VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                    <TextBlock x:Name="TxtFileSize" Grid.Column="1" Foreground="#888"
                               FontSize="12" VerticalAlignment="Center" Margin="16,0" />
                    <TextBlock x:Name="TxtFileMeta" Grid.Column="2" Foreground="#888"
                               FontSize="12" VerticalAlignment="Center" />
                </Grid>
            </Border>

            <!-- Close button (top-right) -->
            <Button x:Name="CloseBtn" HorizontalAlignment="Right" VerticalAlignment="Top"
                    Width="32" Height="32" Margin="6" Click="Close_Click"
                    Opacity="0" Cursor="Hand" Style="{x:Null}">
                <Border Background="#CC333333" CornerRadius="16" Width="32" Height="32">
                    <TextBlock Text="&#x2715;" Foreground="White" FontSize="14"
                               HorizontalAlignment="Center" VerticalAlignment="Center" />
                </Border>
            </Button>
        </Grid>
    </Border>
</Window>
```

- [ ] **Step 2: Verify XAML compiles**

Run: `cd D:/Llamashot/Llamashot && dotnet build --no-restore 2>&1 | tail -5`
Expected: May fail (code-behind not yet created). That's fine — next task.

- [ ] **Step 3: Commit**

```bash
git add Llamashot/Views/PreviewWindow.xaml
git commit -m "feat(preview): add PreviewWindow XAML with glass-morphism floating design"
```

---

### Task 7: Create PreviewWindow Code-Behind

**Files:**
- Create: `Llamashot/Views/PreviewWindow.xaml.cs`

- [ ] **Step 1: Create PreviewWindow.xaml.cs**

```csharp
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Llamashot.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Llamashot.Views;

public partial class PreviewWindow : Window
{
    private string? _currentFile;
    private DispatcherTimer? _mediaTimer;
    private bool _isSeeking;
    private bool _isPlaying;
    private WebView2? _webView;
    private double _imageZoom = 1.0;
    private FilePreviewManager.PreviewType _currentType;

    public PreviewWindow()
    {
        InitializeComponent();

        // Size to ~70% of screen, capped
        var screenW = SystemParameters.PrimaryScreenWidth;
        var screenH = SystemParameters.PrimaryScreenHeight;
        Width = Math.Min(screenW * 0.7, 1200);
        Height = Math.Min(screenH * 0.75, 900);

        // Hover effects for close button
        MouseEnter += (s, e) => CloseBtn.Opacity = 1;
        MouseLeave += (s, e) => CloseBtn.Opacity = 0;

        // Mouse wheel zoom for images
        PreviewImage.MouseWheel += Image_MouseWheel;

        // Media timer for seek bar
        _mediaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _mediaTimer.Tick += MediaTimer_Tick;

        // Fade in
        Opacity = 0;
        Loaded += (s, e) =>
        {
            var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            BeginAnimation(OpacityProperty, anim);
        };

        // Show media controls on mouse move over media panel
        MediaPanel.MouseEnter += (s, e) => MediaControls.Opacity = 1;
        MediaPanel.MouseLeave += (s, e) => { if (!_isSeeking) MediaControls.Opacity = 0; };
    }

    public void LoadFile(string filePath)
    {
        if (!File.Exists(filePath)) return;

        // Stop any current media
        StopMedia();

        _currentFile = filePath;
        _currentType = FilePreviewManager.GetPreviewType(filePath);

        // Hide all panels
        ImageScroller.Visibility = Visibility.Collapsed;
        MediaPanel.Visibility = Visibility.Collapsed;
        TextViewer.Visibility = Visibility.Collapsed;
        PdfPanel.Visibility = Visibility.Collapsed;
        FileInfoPanel.Visibility = Visibility.Collapsed;
        LoadingOverlay.Visibility = Visibility.Collapsed;

        // Update info bar
        var fi = new FileInfo(filePath);
        TxtFileName.Text = fi.Name;
        TxtFileSize.Text = FormatFileSize(fi.Length);

        switch (_currentType)
        {
            case FilePreviewManager.PreviewType.Image:
                LoadImage(filePath);
                break;
            case FilePreviewManager.PreviewType.Video:
                LoadMedia(filePath, isAudio: false);
                break;
            case FilePreviewManager.PreviewType.Audio:
                LoadMedia(filePath, isAudio: true);
                break;
            case FilePreviewManager.PreviewType.Code:
                LoadCode(filePath);
                break;
            case FilePreviewManager.PreviewType.Markdown:
                LoadMarkdown(filePath);
                break;
            case FilePreviewManager.PreviewType.Pdf:
                LoadPdf(filePath);
                break;
            default:
                ShowFileInfo(filePath);
                break;
        }
    }

    private void LoadImage(string filePath)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            PreviewImage.Source = bitmap;
            _imageZoom = 1.0;
            PreviewImage.LayoutTransform = Transform.Identity;
            ImageScroller.Visibility = Visibility.Visible;
            TxtFileMeta.Text = $"{bitmap.PixelWidth} x {bitmap.PixelHeight}";
        }
        catch
        {
            ShowFileInfo(filePath);
        }
    }

    private void Image_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _imageZoom *= e.Delta > 0 ? 1.15 : 0.87;
        _imageZoom = Math.Clamp(_imageZoom, 0.1, 10.0);
        PreviewImage.LayoutTransform = new ScaleTransform(_imageZoom, _imageZoom);
        e.Handled = true;
    }

    private void LoadMedia(string filePath, bool isAudio)
    {
        MediaPanel.Visibility = Visibility.Visible;
        AudioVisual.Visibility = isAudio ? Visibility.Visible : Visibility.Collapsed;
        if (isAudio) AudioFileName.Text = Path.GetFileName(filePath);

        try
        {
            MediaPlayer.Source = new Uri(filePath);
            MediaPlayer.Play();
            _isPlaying = true;
            BtnPlayPause.Content = "\u23F8"; // Pause icon
            _mediaTimer?.Start();
            TxtFileMeta.Text = isAudio ? "Audio" : "Video";
        }
        catch
        {
            ShowFileInfo(filePath);
        }
    }

    private void MediaPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (MediaPlayer.NaturalDuration.HasTimeSpan)
        {
            SeekBar.Maximum = MediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            TxtDuration.Text = $"0:00 / {FormatTime(MediaPlayer.NaturalDuration.TimeSpan)}";

            if (MediaPlayer.HasVideo)
                TxtFileMeta.Text = $"{MediaPlayer.NaturalVideoWidth} x {MediaPlayer.NaturalVideoHeight}";
        }
    }

    private void MediaPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        _isPlaying = false;
        BtnPlayPause.Content = "\u25B6"; // Play icon
        _mediaTimer?.Stop();
        MediaPlayer.Position = TimeSpan.Zero;
        SeekBar.Value = 0;
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            MediaPlayer.Pause();
            _isPlaying = false;
            BtnPlayPause.Content = "\u25B6";
            _mediaTimer?.Stop();
        }
        else
        {
            MediaPlayer.Play();
            _isPlaying = true;
            BtnPlayPause.Content = "\u23F8";
            _mediaTimer?.Start();
        }
    }

    private void MediaTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isSeeking && MediaPlayer.NaturalDuration.HasTimeSpan)
        {
            SeekBar.Value = MediaPlayer.Position.TotalSeconds;
            TxtDuration.Text = $"{FormatTime(MediaPlayer.Position)} / {FormatTime(MediaPlayer.NaturalDuration.TimeSpan)}";
        }
    }

    private void SeekBar_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        => _isSeeking = true;

    private void SeekBar_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _isSeeking = false;
        MediaPlayer.Position = TimeSpan.FromSeconds(SeekBar.Value);
    }

    private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSeeking)
            MediaPlayer.Position = TimeSpan.FromSeconds(SeekBar.Value);
    }

    private void LoadCode(string filePath)
    {
        try
        {
            var text = ReadFileText(filePath);
            if (text == null) { ShowFileInfo(filePath); return; }

            var doc = SyntaxHighlighter.Highlight(text, filePath);
            TextViewer.Document = doc;
            TextViewer.Visibility = Visibility.Visible;

            var lineCount = text.Split('\n').Length;
            TxtFileMeta.Text = $"{lineCount:N0} lines";
        }
        catch
        {
            ShowFileInfo(filePath);
        }
    }

    private void LoadMarkdown(string filePath)
    {
        try
        {
            var text = ReadFileText(filePath);
            if (text == null) { ShowFileInfo(filePath); return; }

            var doc = MarkdownRenderer.Render(text);
            TextViewer.Document = doc;
            TextViewer.Visibility = Visibility.Visible;
            TxtFileMeta.Text = "Markdown";
        }
        catch
        {
            ShowFileInfo(filePath);
        }
    }

    private async void LoadPdf(string filePath)
    {
        try
        {
            if (_webView == null)
            {
                _webView = new WebView2();
                PdfPanel.Child = _webView;
                await _webView.EnsureCoreWebView2Async();
            }

            PdfPanel.Visibility = Visibility.Visible;
            _webView.Source = new Uri(filePath);
            TxtFileMeta.Text = "PDF";
        }
        catch
        {
            ShowFileInfo(filePath);
        }
    }

    private void ShowFileInfo(string filePath)
    {
        FileInfoPanel.Visibility = Visibility.Visible;
        var fi = new FileInfo(filePath);
        FileInfoName.Text = fi.Name;
        FileInfoType.Text = fi.Extension.TrimStart('.').ToUpperInvariant() + " file";
        FileInfoSize.Text = FormatFileSize(fi.Length);
        FileInfoDates.Text = $"Created: {fi.CreationTime:MMM dd, yyyy HH:mm}\nModified: {fi.LastWriteTime:MMM dd, yyyy HH:mm}";
        TxtFileMeta.Text = fi.Extension.TrimStart('.').ToUpperInvariant();
    }

    private void StopMedia()
    {
        _mediaTimer?.Stop();
        _isPlaying = false;
        try { MediaPlayer.Stop(); MediaPlayer.Source = null; } catch { }
    }

    private string? ReadFileText(string filePath)
    {
        try
        {
            var fi = new FileInfo(filePath);
            // Skip files > 10MB
            if (fi.Length > 10 * 1024 * 1024) return null;

            // Check for binary content (null bytes in first 8KB)
            using var fs = File.OpenRead(filePath);
            var buffer = new byte[Math.Min(8192, fi.Length)];
            var read = fs.Read(buffer, 0, buffer.Length);
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == 0) return null; // Binary file
            }

            return File.ReadAllText(filePath);
        }
        catch { return null; }
    }

    private void OpenWith_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFile != null)
            Process.Start(new ProcessStartInfo(_currentFile) { UseShellExecute = true });
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
            case Key.Space:
                Close();
                e.Handled = true;
                break;
            case Key.Left:
                FilePreviewManager.Instance.NavigateFile(-1);
                e.Handled = true;
                break;
            case Key.Right:
                FilePreviewManager.Instance.NavigateFile(1);
                e.Handled = true;
                break;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Allow dragging the window
        if (e.ClickCount == 1)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        StopMedia();
        _webView?.Dispose();
        _webView = null;
        base.OnClosed(e);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    private static string FormatTime(TimeSpan ts)
    {
        return ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
```

- [ ] **Step 2: Add WebView2 NuGet package (for PDF support)**

Run: `cd D:/Llamashot/Llamashot && dotnet add package Microsoft.Web.WebView2`

This is the only new dependency. WebView2 runtime is already pre-installed on Windows 10/11 — the NuGet package is just the WPF control wrapper (~2MB).

- [ ] **Step 3: Verify build**

Run: `cd D:/Llamashot/Llamashot && dotnet build --no-restore 2>&1 | tail -10`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add Llamashot/Views/PreviewWindow.xaml.cs Llamashot/Llamashot.csproj
git commit -m "feat(preview): add PreviewWindow code-behind with image/video/audio/text/PDF/info renderers"
```

---

### Task 8: Integrate Keyboard Hook — Spacebar in Explorer

**Files:**
- Modify: `Llamashot/App.xaml.cs`

- [ ] **Step 1: Add preview fields and hook logic**

In `App.xaml.cs`, add a field near the existing `_lastEscTime` field:

```csharp
private DateTime _lastSpaceTime = DateTime.MinValue;
```

In `EscapeHookCallback`, add the Spacebar handling **before** the recording shortcuts section (after the double-Esc block, around line 136). Insert this block:

```csharp
// Quick Preview: Spacebar in Explorer
if (vkCode == NativeMethods.VK_SPACE && AppSettings.Instance.QuickPreviewEnabled)
{
    var fgHwnd = NativeMethods.GetForegroundWindow();
    var classNameBuf = new System.Text.StringBuilder(256);
    NativeMethods.GetClassName(fgHwnd, classNameBuf, 256);
    if (classNameBuf.ToString() == "CabinetWClass")
    {
        // Debounce rapid space presses
        var now = DateTime.Now;
        if ((now - _lastSpaceTime).TotalMilliseconds > 300)
        {
            _lastSpaceTime = now;
            Dispatcher.BeginInvoke(() =>
            {
                var mgr = Core.FilePreviewManager.Instance;
                if (mgr.IsPreviewOpen)
                {
                    mgr.ClosePreview();
                }
                else
                {
                    var file = mgr.GetExplorerSelectedFile();
                    if (file != null)
                        mgr.ShowPreview(file);
                }
            });
            // Consume the keypress so Explorer doesn't scroll
            return (IntPtr)1;
        }
    }
}

// Arrow keys for preview navigation
if ((vkCode == NativeMethods.VK_LEFT || vkCode == NativeMethods.VK_RIGHT)
    && Core.FilePreviewManager.Instance.IsPreviewOpen)
{
    int direction = vkCode == NativeMethods.VK_LEFT ? -1 : 1;
    Dispatcher.BeginInvoke(() => Core.FilePreviewManager.Instance.NavigateFile(direction));
    return (IntPtr)1;
}
```

- [ ] **Step 2: Add System.Text using if not already present**

Check imports at top of App.xaml.cs. `System.Text` is likely needed for `StringBuilder`. If not present, add:

```csharp
using System.Text;
```

- [ ] **Step 3: Verify build**

Run: `cd D:/Llamashot/Llamashot && dotnet build --no-restore 2>&1 | tail -5`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add Llamashot/App.xaml.cs
git commit -m "feat(preview): hook Spacebar in Explorer to toggle Quick Preview"
```

---

### Task 9: Integrate History Window — Thumbnail Click Opens Preview

**Files:**
- Modify: `Llamashot/Views/HistoryWindow.xaml.cs`

- [ ] **Step 1: Change Item_Click to open PreviewWindow**

Replace the existing `Item_Click` method (lines 83-90) with:

```csharp
private void Item_Click(object sender, MouseButtonEventArgs e)
{
    if (sender is FrameworkElement fe && fe.DataContext is HistoryItemViewModel item)
    {
        if (File.Exists(item.FilePath))
            Core.FilePreviewManager.Instance.ShowPreview(item.FilePath);
    }
}
```

This changes behavior from "open with default app" to "open in preview window." The preview's "Open with default app" button still provides the old behavior.

- [ ] **Step 2: Verify build**

Run: `cd D:/Llamashot/Llamashot && dotnet build --no-restore 2>&1 | tail -5`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add Llamashot/Views/HistoryWindow.xaml.cs
git commit -m "feat(preview): history thumbnail click opens Quick Preview instead of default app"
```

---

### Task 10: Manual Testing and Fixes

**Files:**
- Potentially any files from Tasks 1-9

- [ ] **Step 1: Kill any running Llamashot instance**

Run: `taskkill //F //IM Llamashot.exe 2>/dev/null; echo done`

- [ ] **Step 2: Build and run**

Run: `cd D:/Llamashot/Llamashot && dotnet build && dotnet run`

- [ ] **Step 3: Test Explorer preview**

1. Open a File Explorer window with various files (images, text, code, video)
2. Select an image file → Press Space → Verify preview opens with image
3. Press Left/Right arrows → Verify navigation to adjacent files
4. Press Space or Esc → Verify preview closes
5. Select a .cs file → Press Space → Verify syntax highlighting
6. Select a .md file → Verify rendered markdown
7. Select a .mp4 file → Verify auto-play with hover controls
8. Select a .pdf → Verify WebView2 PDF rendering
9. Select a .docx → Verify file info card with "Open with default app"
10. While preview is open, click a different file in Explorer → Verify auto-update

- [ ] **Step 4: Test History integration**

1. Open History (Alt+PrintScreen)
2. Click any thumbnail → Verify preview opens
3. Close preview → Verify it dismisses

- [ ] **Step 5: Test Settings toggle**

1. Open Settings
2. Uncheck "Enable Quick Preview"
3. Save, go to Explorer, press Space → Verify preview does NOT open
4. Re-enable and verify it works again

- [ ] **Step 6: Fix any issues found**

Address any bugs discovered during testing. Common issues to watch:
- COM exceptions on certain Explorer states → wrap in try/catch
- MediaElement not releasing file handles → ensure StopMedia is called
- WebView2 initialization failure on first PDF → add null check fallback
- DPI scaling issues → verify window sizes account for DPI

- [ ] **Step 7: Commit fixes**

```bash
git add -A
git commit -m "fix(preview): address issues found during manual testing"
```

---

### Task 11: Final Build Verification

- [ ] **Step 1: Clean build**

Run: `cd D:/Llamashot/Llamashot && dotnet clean && dotnet build -c Release 2>&1 | tail -10`
Expected: Build succeeded with 0 errors

- [ ] **Step 2: Verify no warnings**

Check output for CS warnings. Fix any that appear.

- [ ] **Step 3: Commit if any fixes**

```bash
git add -A
git commit -m "chore: fix build warnings"
```
