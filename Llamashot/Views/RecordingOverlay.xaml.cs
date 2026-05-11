using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Llamashot.Core;
using Llamashot.Tools;
using Microsoft.Win32;

namespace Llamashot.Views;

public partial class RecordingOverlay : Window
{
    private readonly ScreenRecorder _recorder;
    private readonly RecordingBorder _border;
    private RecordingAnnotation? _annotationOverlay;
    private bool _finished;
    private bool _micEnabled;
    private bool _systemAudioEnabled;
    private readonly double _dipX, _dipY, _dipW, _dipH;
    private string? _activeAnnotationTool;
    private DateTime _lastEscTime = DateTime.MinValue;

    public RecordingOverlay(int pixelX, int pixelY, int pixelW, int pixelH,
                            double dipX, double dipY, double dipW, double dipH)
    {
        InitializeComponent();

        _dipX = dipX; _dipY = dipY; _dipW = dipW; _dipH = dipH;

        _recorder = new ScreenRecorder(fps: 10);
        _recorder.OnTick += () => Dispatcher.Invoke(UpdateTimer);

        _micEnabled = false;
        _systemAudioEnabled = false;
        UpdateMicUI();
        UpdateSystemAudioUI();

        _border = new RecordingBorder(dipX, dipY, dipW, dipH);
        App.ActiveRecordingOverlay = this;

        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 10;

        Loaded += async (s, e) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowDisplayAffinity(h, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

            _border.Show();
            var bh = new WindowInteropHelper(_border).Handle;
            NativeMethods.SetWindowDisplayAffinity(bh, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

            if (!_recorder.Start(pixelX, pixelY, pixelW, pixelH))
            {
                MessageBox.Show("Failed to start recording.", "Llamashot",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _border.Close();
                Close();
                return;
            }

            // Init audio if enabled in settings
            if (AppSettings.Instance.RecordAudio)
            {
                bool audioOk = await _recorder.InitAudioAsync(true, true);
                if (audioOk)
                {
                    _micEnabled = _recorder.MicActive;
                    _systemAudioEnabled = _recorder.SystemAudioActive;
                    UpdateMicUI();
                    UpdateSystemAudioUI();
                    UpdateAudioStatusText();
                }
            }
        };
    }

    private void UpdateTimer()
    {
        var elapsed = _recorder.Elapsed;
        TxtTimer.Text = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
    }

    private void UpdateMicUI()
    {
        var color = _micEnabled
            ? Color.FromRgb(0x4C, 0xAF, 0x50)  // green
            : Color.FromRgb(0x66, 0x66, 0x66);  // gray

        var brush = new SolidColorBrush(color);
        MicBody.Fill = brush;
        MicArc.Stroke = brush;
        MicStand.Stroke = brush;
        MicSlash.Visibility = _micEnabled ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateSystemAudioUI()
    {
        var color = _systemAudioEnabled
            ? Color.FromRgb(0x4C, 0xAF, 0x50)  // green
            : Color.FromRgb(0x66, 0x66, 0x66);  // gray

        var brush = new SolidColorBrush(color);
        SpeakerBody.Fill = brush;
        SpeakerWave1.Stroke = brush;
        SpeakerWave2.Stroke = brush;
        SpeakerSlash.Visibility = _systemAudioEnabled ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateAudioStatusText()
    {
        bool anyActive = _micEnabled || _systemAudioEnabled;
        if (anyActive)
        {
            string sources = (_micEnabled && _recorder.HasMic, _systemAudioEnabled && _recorder.HasSystemAudio) switch
            {
                (true, true) => "Mic+Sys",
                (true, false) => "Mic",
                (false, true) => "System",
                _ => ""
            };
            TxtAudioStatus.Text = sources;
            TxtAudioStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        }
        else
        {
            TxtAudioStatus.Text = "";
        }
    }

    private async void Mic_Click(object sender, RoutedEventArgs e) => await ToggleMic(!_micEnabled);
    private async void SystemAudio_Click(object sender, RoutedEventArgs e) => await ToggleSystemAudio(!_systemAudioEnabled);

    private async Task ToggleMic(bool enable)
    {
        BtnMic.IsEnabled = false;

        // Init audio graph if not yet created
        if (!_recorder.AudioGraphRunning && enable)
        {
            bool ok = await _recorder.InitAudioAsync(true, _systemAudioEnabled);
            _micEnabled = _recorder.MicActive;
            _systemAudioEnabled = _recorder.SystemAudioActive;
            UpdateMicUI();
            UpdateSystemAudioUI();
            if (!_recorder.HasMic)
            {
                TxtAudioStatus.Text = "No mic";
                TxtAudioStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
            }
            else
            {
                UpdateAudioStatusText();
            }
        }
        else
        {
            _recorder.SetMicActive(enable);
            _micEnabled = _recorder.MicActive;
            UpdateMicUI();
            UpdateAudioStatusText();
        }
        BtnMic.IsEnabled = true;
    }

    private async Task ToggleSystemAudio(bool enable)
    {
        BtnSystemAudio.IsEnabled = false;

        if (!_recorder.AudioGraphRunning && enable)
        {
            bool ok = await _recorder.InitAudioAsync(_micEnabled, true);
            _micEnabled = _recorder.MicActive;
            _systemAudioEnabled = _recorder.SystemAudioActive;
            UpdateMicUI();
            UpdateSystemAudioUI();
            if (!_recorder.HasSystemAudio)
            {
                TxtAudioStatus.Text = "No sys audio";
                TxtAudioStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
            }
            else
            {
                UpdateAudioStatusText();
            }
        }
        else
        {
            _recorder.SetSystemAudioActive(enable);
            _systemAudioEnabled = _recorder.SystemAudioActive;
            UpdateSystemAudioUI();
            UpdateAudioStatusText();
        }
        BtnSystemAudio.IsEnabled = true;
    }

    // ============ SHORTCUTS ============

    public void HandleShortcut(char key)
    {
        if (_finished) return;
        switch (key)
        {
            case 'P': SelectAnnotationTool("Pen"); break;
            case 'A': SelectAnnotationTool("Arrow"); break;
            case 'R': SelectAnnotationTool("Rectangle"); break;
            case 'T': SelectAnnotationTool("Text"); break;
            case 'C': _annotationOverlay?.ClearAll(); break;
            case 'M': _ = ToggleMic(!_micEnabled); break;
            case 'S': _ = ToggleSystemAudio(!_systemAudioEnabled); break;
            case ' ': Pause_Click(BtnPause, new RoutedEventArgs()); break;
            case 'Q': FinishRecording(); break;
        }
    }

    // ============ ANNOTATIONS ============

    private void AnnotationTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string toolName) return;
        SelectAnnotationTool(toolName);
    }

    private void SelectAnnotationTool(string toolName)
    {
        // Toggle off if same tool clicked again
        if (_activeAnnotationTool == toolName)
        {
            _activeAnnotationTool = null;
            _annotationOverlay?.SetTool(null);
            UpdateAnnotationToolHighlights();
            return;
        }

        _activeAnnotationTool = toolName;

        // Create annotation overlay if not yet created
        if (_annotationOverlay == null)
        {
            _annotationOverlay = new RecordingAnnotation(_dipX, _dipY, _dipW, _dipH);
            _annotationOverlay.EscapePressed += HandleEscapePress;
            _annotationOverlay.StrokeCompleted += () =>
            {
                _activeAnnotationTool = null;
                _annotationOverlay?.SetTool(null);
                UpdateAnnotationToolHighlights();
            };
            _annotationOverlay.Show();
        }

        IDrawingTool tool = toolName switch
        {
            "Pen" => new PenTool(),
            "Arrow" => new ArrowTool(),
            "Rectangle" => new RectangleTool(),
            "Text" => new TextTool(),
            _ => new PenTool()
        };
        try { tool.StrokeColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(AppSettings.Instance.DefaultColor); }
        catch { tool.StrokeColor = Color.FromRgb(0xFF, 0xFF, 0x00); }
        tool.Thickness = 3;

        _annotationOverlay.SetTool(tool);
        UpdateAnnotationToolHighlights();
    }

    private void ClearAnnotations_Click(object sender, RoutedEventArgs e)
    {
        _annotationOverlay?.ClearAll();
    }

    private void UpdateAnnotationToolHighlights()
    {
        var highlight = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0x00));
        BtnPen.Background = _activeAnnotationTool == "Pen" ? highlight : Brushes.Transparent;
        BtnArrow.Background = _activeAnnotationTool == "Arrow" ? highlight : Brushes.Transparent;
        BtnRect.Background = _activeAnnotationTool == "Rectangle" ? highlight : Brushes.Transparent;
        BtnText.Background = _activeAnnotationTool == "Text" ? highlight : Brushes.Transparent;
    }

    // ============ RECORDING CONTROLS ============

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder.IsPaused)
        {
            _recorder.Resume();
            RecDot.Fill = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
            PauseIcon.Visibility = Visibility.Visible;
            PlayIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            _recorder.Pause();
            RecDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xA7, 0x26));
            PauseIcon.Visibility = Visibility.Collapsed;
            PlayIcon.Visibility = Visibility.Visible;
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        FinishRecording();
    }

    private async void FinishRecording()
    {
        if (_finished) return;
        _finished = true;

        int frameCount = _recorder.FramesCaptured;
        _recorder.Stop();
        _border.Close();
        _annotationOverlay?.Close();
        _annotationOverlay = null;

        if (frameCount == 0)
        {
            _recorder.CleanupFrames();
            _recorder.Dispose();
            Close();
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "MP4 Video|*.mp4",
            DefaultExt = "mp4",
            FileName = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}",
            InitialDirectory = AppSettings.Instance.LastSaveDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            TxtTimer.Text = "Saving...";
            RecDot.Fill = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));
            BtnStop.IsEnabled = false;
            BtnPause.IsEnabled = false;
            BtnMic.IsEnabled = false;
            BtnSystemAudio.IsEnabled = false;

            bool success = await _recorder.SaveAsync(dialog.FileName);
            Hide();

            if (!success)
            {
                MessageBox.Show($"Failed to save recording.\n{_recorder.LastError}", "Llamashot",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        _recorder.CleanupFrames();
        _recorder.Dispose();
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HandleEscapePress();
            e.Handled = true;
        }
    }

    private void HandleEscapePress()
    {
        var now = DateTime.Now;
        if ((now - _lastEscTime).TotalMilliseconds < 500)
        {
            // Double-Esc: force close everything
            ForceClose();
            return;
        }
        _lastEscTime = now;
    }

    private void ForceClose()
    {
        _finished = true;
        _recorder.Stop();
        _border.Close();
        _annotationOverlay?.Close();
        _annotationOverlay = null;
        _recorder.CleanupFrames();
        _recorder.Dispose();
        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    protected override void OnClosed(EventArgs e)
    {
        App.ActiveRecordingOverlay = null;
        if (!_finished)
        {
            _recorder.Stop();
            _border.Close();
            _annotationOverlay?.Close();
            _recorder.CleanupFrames();
        }
        _recorder.Dispose();
        base.OnClosed(e);
    }
}
