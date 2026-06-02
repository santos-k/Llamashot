using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO.Compression;
using System.Xml.Linq;
using Llamashot.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Llamashot.Views;

public partial class PreviewWindow : Window
{
    private string? _currentFile;
    private DispatcherTimer? _mediaTimer;
    private bool _isSeeking;
    private bool _isPlaying;
    private double _seekDuration;
    private WebView2? _webView;
    private double _imageZoom = 1.0;
    private Point _panStart;
    private Point _imageOffset;
    private bool _isPanning;

    // GIF animation
    private DispatcherTimer? _gifTimer;
    private BitmapFrame[]? _gifFrames;
    private int _gifFrameIndex;

    // Code/rendered toggle
    private bool _isCodeView;
    private string? _currentText;
    private FilePreviewManager.PreviewType _currentPreviewType;

    // Fullscreen toggle
    private bool _isFullscreen;
    private Rect _restoreBounds;
    private Thickness _restoreMargin;

    // Shell preview handler (Office docs)
    private NativeMethods.IPreviewHandler? _previewHandler;
    private System.Windows.Forms.Integration.WindowsFormsHost? _shellHost;
    private System.Windows.Forms.Panel? _shellPanel;

    public PreviewWindow()
    {
        InitializeComponent();

        var screenW = SystemParameters.PrimaryScreenWidth;
        var screenH = SystemParameters.PrimaryScreenHeight;
        Width = Math.Min(screenW * 0.7, 1200);
        Height = Math.Min(screenH * 0.75, 900);

        // Image zoom is handled by ImagePanel_MouseWheel in XAML

        _mediaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _mediaTimer.Tick += MediaTimer_Tick;

        Opacity = 0;
        Loaded += (s, e) =>
        {
            var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            anim.Completed += (_, _) => { BeginAnimation(OpacityProperty, null); Opacity = 1; };
            BeginAnimation(OpacityProperty, anim);
        };
    }

    public void LoadFile(string filePath)
    {
        bool isDir = Directory.Exists(filePath);
        if (!isDir && !File.Exists(filePath)) return;

        StopMedia();

        _currentFile = filePath;
        _isCodeView = false;
        _currentText = null;

        UnloadPreviewHandler();

        ImagePanel.Visibility = Visibility.Collapsed;
        MediaPanel.Visibility = Visibility.Collapsed;
        TextViewer.Visibility = Visibility.Collapsed;
        PdfPanel.Visibility = Visibility.Collapsed;
        ShellPreviewPanel.Visibility = Visibility.Collapsed;
        FileInfoPanel.Visibility = Visibility.Collapsed;
        LoadingOverlay.Visibility = Visibility.Collapsed;
        BtnToggleCode.Visibility = Visibility.Collapsed;

        if (isDir)
        {
            var di = new DirectoryInfo(filePath);
            TxtFileName.Text = di.Name;
            TxtFileSize.Text = "";
            ShowFolderInfo(filePath);
            SetCompactMode(true);
            return;
        }

        var previewType = FilePreviewManager.GetPreviewType(filePath);
        _currentPreviewType = previewType;

        var fi = new FileInfo(filePath);
        TxtFileName.Text = fi.Name;
        TxtFileSize.Text = FormatFileSize(fi.Length);

        bool isCompact = previewType == FilePreviewManager.PreviewType.Unsupported;

        switch (previewType)
        {
            case FilePreviewManager.PreviewType.Image:
                LoadImage(filePath);
                break;
            case FilePreviewManager.PreviewType.Gif:
                LoadGif(filePath);
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
            case FilePreviewManager.PreviewType.Html:
                LoadHtml(filePath);
                break;
            case FilePreviewManager.PreviewType.Pdf:
                LoadPdf(filePath);
                break;
            case FilePreviewManager.PreviewType.ShellPreview:
                var ext2 = Path.GetExtension(filePath);
                if (ext2.Equals(".pptx", StringComparison.OrdinalIgnoreCase) ||
                    ext2.Equals(".ppt", StringComparison.OrdinalIgnoreCase) ||
                    ext2.Equals(".odp", StringComparison.OrdinalIgnoreCase))
                    LoadPptx(filePath);
                else
                    LoadShellPreview(filePath);
                break;
            default:
                ShowFileInfo(filePath);
                break;
        }
        SetCompactMode(isCompact);
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
            ImageScale.ScaleX = 1;
            ImageScale.ScaleY = 1;
            ImageTranslate.X = 0;
            ImageTranslate.Y = 0;
            _imageOffset = new Point(0, 0);
            ImagePanel.Visibility = Visibility.Visible;
            TxtFileMeta.Text = $"{bitmap.PixelWidth} x {bitmap.PixelHeight}";
        }
        catch
        {
            ShowFileInfo(filePath);
        }
    }

    private void ImagePanel_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _imageZoom *= e.Delta > 0 ? 1.15 : 0.87;
        _imageZoom = Math.Clamp(_imageZoom, 0.5, 10.0);
        ImageScale.ScaleX = _imageZoom;
        ImageScale.ScaleY = _imageZoom;

        // Reset pan when zooming back to fit
        if (_imageZoom <= 1.01)
        {
            ImageTranslate.X = 0;
            ImageTranslate.Y = 0;
            _imageOffset = new Point(0, 0);
        }
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
            BtnPlayPause.Content = "\u23F8";
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
            _seekDuration = MediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            TxtDuration.Text = $"0:00 / {FormatTime(MediaPlayer.NaturalDuration.TimeSpan)}";

            if (MediaPlayer.HasVideo)
                TxtFileMeta.Text = $"{MediaPlayer.NaturalVideoWidth} x {MediaPlayer.NaturalVideoHeight}";
        }
    }

    private void MediaPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        _isPlaying = false;
        BtnPlayPause.Content = "\u25B6";
        _mediaTimer?.Stop();
        MediaPlayer.Position = TimeSpan.Zero;
        UpdateSeekBarVisual(0);
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
            var pos = MediaPlayer.Position.TotalSeconds;
            var fraction = _seekDuration > 0 ? pos / _seekDuration : 0;
            UpdateSeekBarVisual(fraction);
            TxtDuration.Text = $"{FormatTime(MediaPlayer.Position)} / {FormatTime(MediaPlayer.NaturalDuration.TimeSpan)}";
        }
    }

    private void UpdateSeekBarVisual(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var totalWidth = SeekBarContainer.ActualWidth;
        if (totalWidth <= 0) return;
        SeekProgress.Width = fraction * totalWidth;
        SeekThumb.Margin = new Thickness(fraction * totalWidth - 6, 0, 0, 0);
    }

    private void SeekToClickPosition(MouseEventArgs e)
    {
        // Get duration live if not yet set
        if (_seekDuration <= 0 && MediaPlayer.NaturalDuration.HasTimeSpan)
            _seekDuration = MediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
        if (_seekDuration <= 0) return;

        var pos = e.GetPosition(SeekBarContainer);
        var fraction = Math.Clamp(pos.X / SeekBarContainer.ActualWidth, 0, 1);
        var seekTime = fraction * _seekDuration;

        // Pause → seek → resume for reliable WPF MediaElement seeking
        bool wasPlaying = _isPlaying;
        if (wasPlaying) MediaPlayer.Pause();
        MediaPlayer.Position = TimeSpan.FromSeconds(seekTime);
        if (wasPlaying) MediaPlayer.Play();

        UpdateSeekBarVisual(fraction);
        TxtDuration.Text = $"{FormatTime(TimeSpan.FromSeconds(seekTime))} / {FormatTime(TimeSpan.FromSeconds(_seekDuration))}";
    }

    private void SeekTrack_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = true;
        SeekBarContainer.CaptureMouse();
        SeekToClickPosition(e);
        e.Handled = true;
    }

    private void SeekTrack_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isSeeking && e.LeftButton == MouseButtonState.Pressed)
            SeekToClickPosition(e);
    }

    private void SeekTrack_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isSeeking)
        {
            SeekToClickPosition(e);
            _isSeeking = false;
            SeekBarContainer.ReleaseMouseCapture();
        }
    }

    private void LoadCode(string filePath)
    {
        try
        {
            var text = ReadFileText(filePath);
            if (text == null) { ShowFileInfo(filePath); return; }

            var doc = SyntaxHighlighter.Highlight(text, filePath);
            doc.PageWidth = 10000; // prevent word wrap, enable horizontal scroll
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

            _currentText = text;
            var doc = MarkdownRenderer.Render(text);
            TextViewer.Document = doc;
            TextViewer.Visibility = Visibility.Visible;
            BtnToggleCode.Visibility = Visibility.Visible;
            BtnToggleCode.Content = "</>";
            TxtFileMeta.Text = "Markdown";
        }
        catch
        {
            ShowFileInfo(filePath);
        }
    }

    private async void LoadHtml(string filePath)
    {
        try
        {
            var text = ReadFileText(filePath);
            if (text == null) { ShowFileInfo(filePath); return; }

            _currentText = text;

            if (_webView == null)
            {
                _webView = new WebView2();
                PdfPanel.Child = _webView;
                await _webView.EnsureCoreWebView2Async();
            }

            PdfPanel.Visibility = Visibility.Visible;
            _webView.Source = new Uri(filePath);
            BtnToggleCode.Visibility = Visibility.Visible;
            BtnToggleCode.Content = "</>";
            TxtFileMeta.Text = "HTML";
        }
        catch
        {
            // Fallback to code view
            LoadCode(filePath);
        }
    }

    private void ToggleCode_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFile == null || _currentText == null) return;

        _isCodeView = !_isCodeView;

        if (_isCodeView)
        {
            // Show raw code
            PdfPanel.Visibility = Visibility.Collapsed;
            var doc = SyntaxHighlighter.Highlight(_currentText, _currentFile);
            doc.PageWidth = 10000;
            TextViewer.Document = doc;
            TextViewer.Visibility = Visibility.Visible;
            BtnToggleCode.Content = "\u25B6"; // ▶ (rendered)
        }
        else
        {
            // Show rendered view
            TextViewer.Visibility = Visibility.Collapsed;
            PdfPanel.Visibility = Visibility.Collapsed;

            if (_currentPreviewType == FilePreviewManager.PreviewType.Markdown)
            {
                var doc = MarkdownRenderer.Render(_currentText);
                TextViewer.Document = doc;
                TextViewer.Visibility = Visibility.Visible;
            }
            else if (_currentPreviewType == FilePreviewManager.PreviewType.Html)
            {
                PdfPanel.Visibility = Visibility.Visible;
                _webView!.Source = new Uri(_currentFile);
            }
            BtnToggleCode.Content = "</>";
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

    private async void LoadPptx(string filePath)
    {
        try
        {
            var html = RenderPptxToHtml(filePath);
            if (html == null) { ShowFileInfo(filePath); return; }

            if (_webView == null)
            {
                _webView = new WebView2();
                PdfPanel.Child = _webView;
                await _webView.EnsureCoreWebView2Async();
            }

            PdfPanel.Visibility = Visibility.Visible;
            _webView.NavigateToString(html);

            int slideCount = 0;
            try
            {
                using var zip = ZipFile.OpenRead(filePath);
                slideCount = zip.Entries.Count(e =>
                    e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) &&
                    e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            }
            catch { }
            TxtFileMeta.Text = $"PPTX — {slideCount} slides";
        }
        catch
        {
            ShowFileInfo(filePath);
        }
    }

    private static string? RenderPptxToHtml(string filePath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(filePath);
            var ns_a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
            var ns_p = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
            var ns_r = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            var ns_rel = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");

            // Find slide entries sorted by number
            var slideEntries = zip.Entries
                .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                         && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e =>
                {
                    var name = Path.GetFileNameWithoutExtension(e.Name);
                    return int.TryParse(name.Replace("slide", "", StringComparison.OrdinalIgnoreCase), out int n) ? n : 999;
                })
                .ToList();

            if (slideEntries.Count == 0) return null;

            // Extract images as base64 for embedding
            var imageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    using var ms = new MemoryStream();
                    using (var s = entry.Open()) s.CopyTo(ms);
                    var ext = Path.GetExtension(entry.Name).ToLower();
                    var mime = ext switch
                    {
                        ".png" => "image/png",
                        ".jpg" or ".jpeg" => "image/jpeg",
                        ".gif" => "image/gif",
                        ".svg" => "image/svg+xml",
                        ".bmp" => "image/bmp",
                        _ => "image/png"
                    };
                    imageMap[entry.Name] = $"data:{mime};base64,{Convert.ToBase64String(ms.ToArray())}";
                }
                catch { }
            }

            var slides = new System.Text.StringBuilder();
            int slideNum = 0;

            foreach (var slideEntry in slideEntries)
            {
                slideNum++;
                XDocument slideDoc;
                using (var s = slideEntry.Open()) slideDoc = XDocument.Load(s);

                // Get image relationships for this slide
                var relMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var relPath = slideEntry.FullName.Replace("slides/", "slides/_rels/") + ".rels";
                var relEntry = zip.GetEntry(relPath);
                if (relEntry != null)
                {
                    try
                    {
                        XDocument relDoc;
                        using (var rs = relEntry.Open()) relDoc = XDocument.Load(rs);
                        foreach (var rel in relDoc.Descendants(ns_rel + "Relationship"))
                        {
                            var id = rel.Attribute("Id")?.Value;
                            var target = rel.Attribute("Target")?.Value;
                            if (id != null && target != null)
                                relMap[id] = target.Replace("../media/", "").Replace("../", "");
                        }
                    }
                    catch { }
                }

                slides.Append($"<div class=\"slide\"><div class=\"slide-num\">Slide {slideNum}</div><div class=\"slide-content\">");

                // Extract shapes with text and images
                foreach (var sp in slideDoc.Descendants(ns_p + "sp").Concat(slideDoc.Descendants(ns_p + "pic")))
                {
                    // Check for image
                    var blipFill = sp.Descendants(ns_p + "blipFill").FirstOrDefault()
                                  ?? sp.Descendants(ns_a + "blipFill").FirstOrDefault();
                    var blip = blipFill?.Descendants(ns_a + "blip").FirstOrDefault();
                    var embedId = blip?.Attribute(ns_r + "embed")?.Value;
                    if (embedId != null && relMap.TryGetValue(embedId, out var mediaFile) && imageMap.TryGetValue(mediaFile, out var dataUri))
                    {
                        slides.Append($"<img src=\"{dataUri}\" class=\"slide-img\" />");
                    }

                    // Extract text
                    var txBody = sp.Element(ns_p + "txBody");
                    if (txBody == null) continue;

                    foreach (var para in txBody.Elements(ns_a + "p"))
                    {
                        var runs = para.Elements(ns_a + "r");
                        if (!runs.Any()) continue;

                        // Check text properties for styling
                        bool isBold = false, isTitle = false;
                        int fontSize = 0;
                        foreach (var rPr in para.Descendants(ns_a + "rPr"))
                        {
                            isBold = rPr.Attribute("b")?.Value == "1";
                            if (int.TryParse(rPr.Attribute("sz")?.Value, out int sz))
                                fontSize = sz / 100; // hundredths of a point → points
                        }
                        // Check if placeholder type is title
                        var phType = sp.Descendants(ns_p + "ph").FirstOrDefault()?.Attribute("type")?.Value;
                        isTitle = phType == "title" || phType == "ctrTitle" || fontSize >= 24;

                        var text = string.Join("", runs.Select(r => System.Net.WebUtility.HtmlEncode(r.Element(ns_a + "t")?.Value ?? "")));
                        if (string.IsNullOrWhiteSpace(text)) continue;

                        if (isTitle)
                            slides.Append($"<h2 style=\"font-size:{Math.Max(fontSize, 24)}pt;margin:4px 0\">{text}</h2>");
                        else if (isBold)
                            slides.Append($"<p><strong>{text}</strong></p>");
                        else
                            slides.Append($"<p>{text}</p>");
                    }
                }

                slides.Append("</div></div>");
            }

            return $@"<!DOCTYPE html>
<html><head><meta charset=""utf-8""><style>
body {{ margin:0; padding:20px; background:#2d2d2d; font-family:Segoe UI,sans-serif; color:#e0e0e0; }}
.slide {{ background:#fff; color:#333; border-radius:8px; margin:0 auto 24px; padding:32px 40px;
  max-width:900px; box-shadow:0 2px 12px rgba(0,0,0,0.4); position:relative; min-height:120px; }}
.slide-num {{ position:absolute; top:8px; right:14px; font-size:11px; color:#999; }}
.slide-content h2 {{ color:#1a3a5c; margin:8px 0; }}
.slide-content p {{ margin:6px 0; line-height:1.5; font-size:14px; }}
.slide-img {{ max-width:100%; max-height:300px; margin:10px 0; border-radius:4px; }}
</style></head><body>{slides}</body></html>";
        }
        catch { return null; }
    }

    private void LoadShellPreview(string filePath)
    {
        try
        {
            var ext = Path.GetExtension(filePath);
            var clsid = FilePreviewManager.GetPreviewHandlerCLSID(ext);
            if (clsid == Guid.Empty) { ShowFileInfo(filePath); return; }

            var handlerType = Type.GetTypeFromCLSID(clsid);
            if (handlerType == null) { ShowFileInfo(filePath); return; }

            // Create hosting panel first so HWND is ready
            if (_shellHost == null)
            {
                _shellPanel = new System.Windows.Forms.Panel();
                _shellPanel.BackColor = System.Drawing.Color.FromArgb(0xF0, 0xF0, 0xF0);
                _shellPanel.Resize += (s, e) => UpdatePreviewHandlerRect();
                _shellHost = new System.Windows.Forms.Integration.WindowsFormsHost();
                _shellHost.Child = _shellPanel;
                ShellPreviewPanel.Child = _shellHost;
            }

            ShellPreviewPanel.Visibility = Visibility.Visible;
            // Force layout so the panel gets a real size
            ShellPreviewPanel.UpdateLayout();
            _shellHost.UpdateLayout();

            var comObj = Activator.CreateInstance(handlerType);
            if (comObj is not NativeMethods.IPreviewHandler handler)
            {
                if (comObj != null) Marshal.ReleaseComObject(comObj);
                ShowFileInfo(filePath); return;
            }

            // Initialize: try IInitializeWithFile first, then IInitializeWithStream
            bool initialized = false;
            if (comObj is NativeMethods.IInitializeWithFile initFile)
            {
                try { initFile.Initialize(filePath, NativeMethods.STGM_READ); initialized = true; }
                catch { }
            }
            if (!initialized && comObj is NativeMethods.IInitializeWithStream initStream)
            {
                try
                {
                    int hr = NativeMethods.SHCreateStreamOnFileEx(filePath, NativeMethods.STGM_READ, 0, false, IntPtr.Zero, out var stream);
                    if (hr == 0)
                    {
                        initStream.Initialize(stream, NativeMethods.STGM_READ);
                        initialized = true;
                    }
                }
                catch { }
            }

            if (!initialized)
            {
                Marshal.ReleaseComObject(comObj);
                ShellPreviewPanel.Visibility = Visibility.Collapsed;
                ShowFileInfo(filePath);
                return;
            }

            // Set window and render
            int w = Math.Max(_shellPanel!.Width, 200);
            int h = Math.Max(_shellPanel.Height, 200);
            var rect = new NativeMethods.RECT { Left = 0, Top = 0, Right = w, Bottom = h };
            handler.SetWindow(_shellPanel.Handle, ref rect);
            handler.SetRect(ref rect);
            handler.DoPreview();

            _previewHandler = handler;

            // Deferred re-layout: give the handler time to initialize
            var layoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            layoutTimer.Tick += (s, _) =>
            {
                layoutTimer.Stop();
                if (_previewHandler == null || _shellPanel == null) return;
                try
                {
                    var r2 = new NativeMethods.RECT
                    {
                        Left = 0, Top = 0,
                        Right = _shellPanel.Width,
                        Bottom = _shellPanel.Height
                    };
                    _previewHandler.SetRect(ref r2);
                }
                catch { }
            };
            layoutTimer.Start();

            var fi = new FileInfo(filePath);
            TxtFileMeta.Text = fi.Extension.TrimStart('.').ToUpperInvariant();
        }
        catch
        {
            ShellPreviewPanel.Visibility = Visibility.Collapsed;
            ShowFileInfo(filePath);
        }
    }

    private void UpdatePreviewHandlerRect()
    {
        if (_previewHandler == null || _shellPanel == null) return;
        try
        {
            var rect = new NativeMethods.RECT
            {
                Left = 0, Top = 0,
                Right = _shellPanel.Width,
                Bottom = _shellPanel.Height
            };
            _previewHandler.SetRect(ref rect);
        }
        catch { }
    }

    private void UnloadPreviewHandler()
    {
        if (_previewHandler != null)
        {
            try { _previewHandler.Unload(); } catch { }
            try { Marshal.ReleaseComObject(_previewHandler); } catch { }
            _previewHandler = null;
        }
    }

    private void SetFileIcon(string path)
    {
        try
        {
            var shinfo = new NativeMethods.SHFILEINFO();
            NativeMethods.SHGetFileInfo(path, 0, ref shinfo,
                (uint)Marshal.SizeOf(shinfo),
                NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);

            if (shinfo.hIcon != IntPtr.Zero)
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    shinfo.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                FileInfoImage.Source = source;
                NativeMethods.DestroyIcon(shinfo.hIcon);
                return;
            }
        }
        catch { }
        FileInfoImage.Source = null;
    }

    private void ShowFileInfo(string filePath)
    {
        FileInfoPanel.Visibility = Visibility.Visible;
        SetFileIcon(filePath);
        BtnOpenWith.Visibility = Visibility.Visible;
        var fi = new FileInfo(filePath);
        FileInfoName.Text = fi.Name;
        FileInfoType.Text = fi.Extension.TrimStart('.').ToUpperInvariant() + " file";
        FileInfoSize.Text = FormatFileSize(fi.Length);
        FileInfoDates.Text = $"Created: {fi.CreationTime:MMM dd, yyyy HH:mm}\nModified: {fi.LastWriteTime:MMM dd, yyyy HH:mm}";
        TxtFileMeta.Text = fi.Extension.TrimStart('.').ToUpperInvariant();
    }

    private void ShowFolderInfo(string folderPath)
    {
        FileInfoPanel.Visibility = Visibility.Visible;
        BtnOpenWith.Visibility = Visibility.Collapsed;
        SetFileIcon(folderPath);
        var di = new DirectoryInfo(folderPath);
        FileInfoName.Text = di.Name;

        try
        {
            int fileCount = di.GetFiles().Length;
            int folderCount = di.GetDirectories().Length;
            FileInfoType.Text = $"{fileCount} files, {folderCount} folders";

            long totalSize = 0;
            foreach (var f in di.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { totalSize += f.Length; } catch { }
            }
            FileInfoSize.Text = FormatFileSize(totalSize);
        }
        catch
        {
            FileInfoType.Text = "Folder";
            FileInfoSize.Text = "";
        }

        FileInfoDates.Text = $"Created: {di.CreationTime:MMM dd, yyyy HH:mm}\nModified: {di.LastWriteTime:MMM dd, yyyy HH:mm}";
        TxtFileMeta.Text = "Folder";
        TxtFileName.Text = di.Name;
    }

    private double _normalWidth, _normalHeight, _normalLeft, _normalTop;
    private bool _isCompact;

    private void SetCompactMode(bool compact)
    {
        if (compact == _isCompact) return;

        if (compact)
        {
            if (!_isCompact)
            {
                _normalWidth = Width;
                _normalHeight = Height;
                _normalLeft = Left;
                _normalTop = Top;
            }
            Width = 340;
            Height = 250;
            BtnPrev.Visibility = Visibility.Collapsed;
            BtnNext.Visibility = Visibility.Collapsed;
            BtnOpenWith.Visibility = Visibility.Collapsed;
            CenterOnScreen();
        }
        else if (_isCompact)
        {
            Width = _normalWidth > 0 ? _normalWidth : Math.Min(SystemParameters.PrimaryScreenWidth * 0.7, 1200);
            Height = _normalHeight > 0 ? _normalHeight : Math.Min(SystemParameters.PrimaryScreenHeight * 0.75, 900);
            BtnPrev.Visibility = Visibility.Visible;
            BtnNext.Visibility = Visibility.Visible;
            Left = _normalLeft;
            Top = _normalTop;
        }
        _isCompact = compact;
    }

    private void CenterOnScreen()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
    }

    private void LoadGif(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count <= 1)
            {
                // Static GIF — treat as image
                LoadImage(filePath);
                return;
            }

            _gifFrames = decoder.Frames.ToArray();
            _gifFrameIndex = 0;

            // Get frame delay from metadata (default 100ms)
            int delayMs = 100;
            try
            {
                var metadata = decoder.Frames[0].Metadata as BitmapMetadata;
                var delay = metadata?.GetQuery("/grctlext/Delay");
                if (delay is ushort d && d > 0) delayMs = d * 10;
            }
            catch { }
            if (delayMs < 20) delayMs = 100;

            PreviewImage.Source = _gifFrames[0];
            _imageZoom = 1.0;
            ImageScale.ScaleX = 1;
            ImageScale.ScaleY = 1;
            ImageTranslate.X = 0;
            ImageTranslate.Y = 0;
            _imageOffset = new Point(0, 0);
            ImagePanel.Visibility = Visibility.Visible;
            TxtFileMeta.Text = $"{_gifFrames[0].PixelWidth} x {_gifFrames[0].PixelHeight} ({_gifFrames.Length} frames)";

            _gifTimer?.Stop();
            _gifTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            _gifTimer.Tick += (s, e) =>
            {
                if (_gifFrames == null) return;
                _gifFrameIndex = (_gifFrameIndex + 1) % _gifFrames.Length;
                PreviewImage.Source = _gifFrames[_gifFrameIndex];
            };
            _gifTimer.Start();
        }
        catch
        {
            LoadImage(filePath);
        }
    }

    private void StopMedia()
    {
        _mediaTimer?.Stop();
        _isPlaying = false;
        _seekDuration = 0;
        try { MediaPlayer.Stop(); MediaPlayer.Source = null; } catch { }

        // Stop GIF animation
        _gifTimer?.Stop();
        _gifTimer = null;
        _gifFrames = null;
    }

    private string? ReadFileText(string filePath)
    {
        try
        {
            var fi = new FileInfo(filePath);
            if (fi.Length > 10 * 1024 * 1024) return null;

            byte[] header;
            using (var fs = File.OpenRead(filePath))
            {
                header = new byte[Math.Min(8192, fi.Length)];
                fs.Read(header, 0, header.Length);
            }

            // Detect UTF-16 BOM — these files naturally contain null bytes
            bool isUtf16 = header.Length >= 2 &&
                ((header[0] == 0xFF && header[1] == 0xFE) ||
                 (header[0] == 0xFE && header[1] == 0xFF));

            if (!isUtf16)
            {
                for (int i = 0; i < header.Length; i++)
                {
                    if (header[i] == 0) return null;
                }
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

    private void InfoBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;

        if (_isFullscreen)
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            _restoreMargin = RootBorder.Margin;
            RootBorder.CornerRadius = new CornerRadius(0);
            RootBorder.Margin = new Thickness(0);
            WindowState = WindowState.Maximized;
            TxtFullscreenBarIcon.Text = "\u2750"; // restore icon
        }
        else
        {
            WindowState = WindowState.Normal;
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
            RootBorder.CornerRadius = new CornerRadius(10);
            RootBorder.Margin = new Thickness(3);
            TxtFullscreenBarIcon.Text = "\u26F6"; // fullscreen icon
        }
    }

    private void ResizeEdge_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not string edge) return;

        int dir = edge switch
        {
            "Left" => 1,
            "Right" => 2,
            "Top" => 3,
            "TopLeft" => 4,
            "TopRight" => 5,
            "Bottom" => 6,
            "BottomLeft" => 7,
            "BottomRight" => 8,
            _ => 0
        };
        if (dir == 0) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(hwnd, 0x112 /* WM_SYSCOMMAND */, (IntPtr)(0xF000 + dir), IntPtr.Zero);
    }

    // Image pan when zoomed
    private void ImagePanel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_imageZoom > 1.0)
        {
            _isPanning = true;
            _panStart = e.GetPosition(ImagePanel);
            ImagePanel.CaptureMouse();
            ImagePanel.Cursor = Cursors.Hand;
            e.Handled = true;
        }
    }

    private void ImagePanel_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(ImagePanel);
            ImageTranslate.X = _imageOffset.X + (pos.X - _panStart.X);
            ImageTranslate.Y = _imageOffset.Y + (pos.Y - _panStart.Y);
            e.Handled = true;
        }
    }

    private void ImagePanel_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            _imageOffset = new Point(ImageTranslate.X, ImageTranslate.Y);
            ImagePanel.ReleaseMouseCapture();
            ImagePanel.Cursor = null;
        }
    }

    private void PrevFile_Click(object sender, RoutedEventArgs e)
        => FilePreviewManager.Instance.NavigateFile(-1);

    private void NextFile_Click(object sender, RoutedEventArgs e)
        => FilePreviewManager.Instance.NavigateFile(1);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        StopMedia();
        UnloadPreviewHandler();
        _shellHost?.Dispose();
        _shellHost = null;
        _shellPanel?.Dispose();
        _shellPanel = null;
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
