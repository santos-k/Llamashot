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
        ".png", ".jpg", ".jpeg", ".bmp", ".ico", ".tiff", ".tif", ".webp"
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

    public enum PreviewType { Image, Gif, Video, Audio, Code, Markdown, Pdf, Unsupported }

    public static PreviewType GetPreviewType(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext)) return PreviewType.Code;
        if (ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)) return PreviewType.Gif;
        if (ext.Equals(".md", StringComparison.OrdinalIgnoreCase)) return PreviewType.Markdown;
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return PreviewType.Pdf;
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
            _previewWindow.Activate();
            _previewWindow.Focus();
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

    public void NavigateFile(int direction)
    {
        if (_folderFiles == null || _folderFiles.Length == 0) return;

        var newIndex = Math.Clamp(_currentIndex + direction, 0, _folderFiles.Length - 1);
        if (newIndex == _currentIndex) return;

        _currentIndex = newIndex;
        _currentFile = _folderFiles[_currentIndex];
        _previewWindow?.LoadFile(_currentFile);
        _previewWindow?.Activate();
    }

    private void UpdateFolderFiles(string filePath)
    {
        var folder = Path.GetDirectoryName(filePath);
        if (!string.Equals(folder, _currentFolder, StringComparison.OrdinalIgnoreCase))
        {
            _currentFolder = folder;
            _folderFiles = folder != null
                ? Directory.GetFiles(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();
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
