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

    public PreviewWindow()
    {
        InitializeComponent();

        var screenW = SystemParameters.PrimaryScreenWidth;
        var screenH = SystemParameters.PrimaryScreenHeight;
        Width = Math.Min(screenW * 0.7, 1200);
        Height = Math.Min(screenH * 0.75, 900);

        // Image zoom is handled by ImageScroller_MouseWheel in XAML

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
        if (!File.Exists(filePath)) return;

        StopMedia();

        _currentFile = filePath;
        var previewType = FilePreviewManager.GetPreviewType(filePath);

        ImageScroller.Visibility = Visibility.Collapsed;
        MediaPanel.Visibility = Visibility.Collapsed;
        TextViewer.Visibility = Visibility.Collapsed;
        PdfPanel.Visibility = Visibility.Collapsed;
        FileInfoPanel.Visibility = Visibility.Collapsed;
        LoadingOverlay.Visibility = Visibility.Collapsed;

        var fi = new FileInfo(filePath);
        TxtFileName.Text = fi.Name;
        TxtFileSize.Text = FormatFileSize(fi.Length);

        switch (previewType)
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
            ImageScale.ScaleX = 1;
            ImageScale.ScaleY = 1;
            ImageScroller.Visibility = Visibility.Visible;
            TxtFileMeta.Text = $"{bitmap.PixelWidth} x {bitmap.PixelHeight}";
        }
        catch
        {
            ShowFileInfo(filePath);
        }
    }

    private void ImageScroller_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ImageScroller.Visibility != Visibility.Visible) return;

        _imageZoom *= e.Delta > 0 ? 1.15 : 0.87;
        _imageZoom = Math.Clamp(_imageZoom, 0.1, 10.0);
        ImageScale.ScaleX = _imageZoom;
        ImageScale.ScaleY = _imageZoom;
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
            SeekBar.Maximum = MediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
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
            if (fi.Length > 10 * 1024 * 1024) return null;

            using var fs = File.OpenRead(filePath);
            var buffer = new byte[Math.Min(8192, fi.Length)];
            var read = fs.Read(buffer, 0, buffer.Length);
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == 0) return null;
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
        // Escape, Space, and arrow keys are handled by the global keyboard hook
        // in App.xaml.cs — this is a fallback for when the window has focus
        switch (e.Key)
        {
            case Key.Escape:
            case Key.Space:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
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
