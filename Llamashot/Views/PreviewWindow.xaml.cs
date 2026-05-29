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

        ImagePanel.Visibility = Visibility.Collapsed;
        MediaPanel.Visibility = Visibility.Collapsed;
        TextViewer.Visibility = Visibility.Collapsed;
        PdfPanel.Visibility = Visibility.Collapsed;
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
