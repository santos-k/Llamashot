using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Standard formats
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".dib",
        ".ico", ".cur", ".tiff", ".tif", ".webp",
        // Apple / mobile
        ".heic", ".heif", ".heics",
        // Modern formats (Windows 10/11 with codec)
        ".avif", ".jxl",
        // RAW camera formats (WIC codecs)
        ".raw", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".rw2",
        ".dng", ".raf", ".srw", ".pef", ".rwl",
        // Other
        ".wdp", ".hdp", ".jxr", // JPEG XR / HD Photo
        ".svg", ".svgz",         // SVG (rendered as text if WIC fails)
        ".tga", ".pcx", ".pbm", ".pgm", ".ppm",
        ".exr",                   // OpenEXR
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Common containers
        ".mp4", ".m4v", ".avi", ".mov", ".mkv", ".wmv", ".webm",
        // MPEG
        ".mpg", ".mpeg", ".mpe", ".m2v", ".m2ts", ".mts", ".ts",
        // Other
        ".flv", ".f4v", ".3gp", ".3g2", ".ogv", ".vob",
        ".asf", ".rm", ".rmvb", ".divx", ".xvid",
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Common
        ".mp3", ".wav", ".flac", ".aac", ".m4a", ".wma", ".ogg",
        // Lossless
        ".alac", ".ape", ".aiff", ".aif",
        // Other
        ".opus", ".weba", ".amr", ".ac3", ".dts",
        ".mid", ".midi",
        ".ra", ".au", ".snd",
        ".pcm", ".gsm",
    };

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // C-family
        ".cs", ".csx", ".cpp", ".c", ".cc", ".cxx", ".h", ".hh", ".hpp", ".hxx",
        ".m", ".mm",  // Objective-C
        // JVM
        ".java", ".kt", ".kts", ".scala", ".groovy", ".gradle",
        // .NET / XAML
        ".vb", ".fs", ".fsx", ".csproj", ".fsproj", ".vbproj", ".sln", ".xaml", ".razor", ".cshtml",
        // Web
        ".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx",
        ".html", ".htm", ".xhtml",
        ".css", ".scss", ".sass", ".less", ".styl",
        ".vue", ".svelte", ".astro",
        // Scripting
        ".py", ".pyw", ".pyi", ".rb", ".erb", ".php", ".lua",
        ".pl", ".pm", ".perl", ".tcl",
        ".r", ".R", ".jl",  // R, Julia
        // Shell
        ".sh", ".bash", ".zsh", ".fish", ".ksh", ".csh",
        ".bat", ".cmd", ".ps1", ".psm1", ".psd1",
        // Systems
        ".go", ".rs", ".zig", ".nim", ".d",
        ".swift", ".dart", ".v", ".ex", ".exs", ".erl", ".hrl",
        ".hs", ".lhs",  // Haskell
        ".ml", ".mli",  // OCaml
        ".clj", ".cljs", ".cljc", ".edn",  // Clojure
        ".lisp", ".cl", ".el", ".scm", ".rkt",  // Lisps
        // Data / config
        ".json", ".jsonc", ".json5", ".jsonl",
        ".xml", ".xsl", ".xslt", ".xsd", ".dtd",
        ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf",
        ".env", ".properties", ".plist",
        // Database
        ".sql", ".sqlite", ".prisma",
        // Markup / docs
        ".tex", ".latex", ".bib",
        ".rst", ".adoc", ".asciidoc",
        ".org",  // Org mode
        // DevOps / infra
        ".dockerfile", ".makefile", ".cmake",
        ".tf", ".tfvars", ".hcl",  // Terraform
        ".nix", ".dhall",
        ".vagrantfile",
        // Other text
        ".txt", ".text", ".log", ".csv", ".tsv",
        ".rtf",
        ".gitignore", ".gitattributes", ".gitmodules",
        ".dockerignore", ".editorconfig", ".eslintrc", ".prettierrc",
        ".npmrc", ".nvmrc", ".babelrc",
        ".htaccess", ".nginx", ".apache",
        // Build / project
        ".cmake", ".pro", ".pri",  // Qt
        ".cabal",  // Haskell
        ".gemspec", ".gemfile",  // Ruby
        ".cargo",  // Rust
        ".mod", ".sum",  // Go
        ".lock",  // Various lock files
        ".patch", ".diff",
    };

    public enum PreviewType { Image, Gif, Video, Audio, Code, Markdown, Html, Pdf, Unsupported }

    private static readonly HashSet<string> HtmlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".xhtml",
    };

    public static PreviewType GetPreviewType(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext)) return PreviewType.Code;
        if (ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)) return PreviewType.Gif;
        if (ext.Equals(".md", StringComparison.OrdinalIgnoreCase)) return PreviewType.Markdown;
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return PreviewType.Pdf;
        if (HtmlExtensions.Contains(ext)) return PreviewType.Html;
        if (ImageExtensions.Contains(ext)) return PreviewType.Image;
        if (VideoExtensions.Contains(ext)) return PreviewType.Video;
        if (AudioExtensions.Contains(ext)) return PreviewType.Audio;
        if (CodeExtensions.Contains(ext)) return PreviewType.Code;
        return PreviewType.Unsupported;
    }

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

                        IntPtr hwnd = (IntPtr)(long)window.HWND;
                        if (hwnd != foreground) continue;

                        dynamic document = window.Document;
                        dynamic selectedItems = document.SelectedItems();
                        if (selectedItems.Count == 0) continue;

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
        catch { }

        return null;
    }

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

        // Force window to foreground (Activate alone can't steal focus from Explorer)
        _previewWindow.Topmost = true;
        _previewWindow.Activate();
        _previewWindow.Focus();
        _previewWindow.Topmost = false;
    }

    public void ClosePreview()
    {
        StopPolling();
        _previewWindow?.Close();
        _previewWindow = null;
        _currentFile = null;
    }

    public bool IsPreviewOpen => _previewWindow?.IsVisible == true;

    public void NavigateFile(int direction)
    {
        if (_folderFiles == null || _folderFiles.Length == 0) return;

        var newIndex = Math.Clamp(_currentIndex + direction, 0, _folderFiles.Length - 1);
        if (newIndex == _currentIndex) return;

        _currentIndex = newIndex;
        _currentFile = _folderFiles[_currentIndex];
        _previewWindow?.LoadFile(_currentFile);

        // Select the file in Explorer, then re-activate preview window
        SelectFileInExplorer(_currentFile);
        _previewWindow?.Activate();
    }

    private void UpdateFolderFiles(string filePath)
    {
        // Try to get file list from Explorer's current view order (no extra sorting)
        var explorerFiles = GetExplorerFileList();
        if (explorerFiles != null && explorerFiles.Length > 0)
        {
            _folderFiles = explorerFiles;
            _currentFolder = Path.GetDirectoryName(filePath);
        }
        else
        {
            // Fallback: read from filesystem (no sorting — use OS order)
            var folder = Path.GetDirectoryName(filePath);
            if (!string.Equals(folder, _currentFolder, StringComparison.OrdinalIgnoreCase))
            {
                _currentFolder = folder;
                _folderFiles = folder != null
                    ? Directory.GetFiles(folder)
                    : Array.Empty<string>();
            }
        }

        // Case-insensitive path lookup
        _currentIndex = -1;
        for (int i = 0; i < _folderFiles!.Length; i++)
        {
            if (string.Equals(_folderFiles[i], filePath, StringComparison.OrdinalIgnoreCase))
            { _currentIndex = i; break; }
        }
        if (_currentIndex < 0) _currentIndex = 0;
    }

    /// <summary>
    /// Get all files from the foreground Explorer window in its current display order.
    /// </summary>
    private string[]? GetExplorerFileList()
    {
        try
        {
            var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
            if (shellWindowsType == null) return null;

            dynamic shellWindows = Activator.CreateInstance(shellWindowsType)!;
            try
            {
                // Find the Explorer window that was foreground when preview was opened
                // or use last known HWND
                int count = shellWindows.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic? window = null;
                    try
                    {
                        window = shellWindows.Item(i);
                        if (window == null) continue;

                        // Check if this window's folder matches our current folder
                        dynamic document = window.Document;
                        dynamic folder = document.Folder;
                        dynamic allItems = folder.Items();

                        var files = new List<string>();
                        int itemCount = allItems.Count;
                        for (int j = 0; j < itemCount; j++)
                        {
                            try
                            {
                                dynamic fi = allItems.Item(j);
                                string path = fi.Path;
                                if (File.Exists(path))
                                    files.Add(path);
                            }
                            catch { }
                        }

                        // Verify this is the right folder
                        if (files.Count > 0 && _currentFolder != null)
                        {
                            var firstDir = Path.GetDirectoryName(files[0]);
                            if (string.Equals(firstDir, _currentFolder, StringComparison.OrdinalIgnoreCase))
                                return files.ToArray();
                        }
                        else if (files.Count > 0)
                        {
                            return files.ToArray();
                        }
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
        catch { }
        return null;
    }

    /// <summary>
    /// Select a file in the foreground Explorer window.
    /// </summary>
    private void SelectFileInExplorer(string filePath)
    {
        try
        {
            var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
            if (shellWindowsType == null) return;

            dynamic shellWindows = Activator.CreateInstance(shellWindowsType)!;
            try
            {
                var folder = Path.GetDirectoryName(filePath);
                var fileName = Path.GetFileName(filePath);
                int count = shellWindows.Count;

                for (int i = 0; i < count; i++)
                {
                    dynamic? window = null;
                    try
                    {
                        window = shellWindows.Item(i);
                        if (window == null) continue;

                        dynamic document = window.Document;
                        dynamic folderObj = document.Folder;
                        dynamic allItems = folderObj.Items();

                        // Check if this is the right folder
                        bool rightFolder = false;
                        int itemCount = allItems.Count;
                        for (int j = 0; j < itemCount; j++)
                        {
                            try
                            {
                                dynamic fi = allItems.Item(j);
                                string dir = Path.GetDirectoryName(fi.Path);
                                if (string.Equals(dir, folder, StringComparison.OrdinalIgnoreCase))
                                { rightFolder = true; break; }
                            }
                            catch { }
                        }

                        if (!rightFolder) continue;

                        // Select the item exclusively
                        // SVSI flags: 0x1=SELECT, 0x4=DESELECTOTHERS, 0x8=ENSUREVISIBLE, 0x10=FOCUSED
                        dynamic folderItem = folderObj.ParseName(fileName);
                        if (folderItem != null)
                        {
                            document.SelectItem(folderItem, 0x1 | 0x4 | 0x8 | 0x10);
                        }
                        return;
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
        catch { }
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
