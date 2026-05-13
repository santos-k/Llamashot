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
    private bool _preRecording = true;
    private bool _micEnabled;
    private bool _systemAudioEnabled;
    private int _pixelX, _pixelY, _pixelW, _pixelH;
    private double _dipX, _dipY, _dipW, _dipH;
    private string? _activeAnnotationTool;
    private DateTime _lastEscTime = DateTime.MinValue;

    public RecordingOverlay(int pixelX, int pixelY, int pixelW, int pixelH,
                            double dipX, double dipY, double dipW, double dipH,
                            bool startImmediately = false, bool micEnabled = false, bool sysAudioEnabled = false)
    {
        InitializeComponent();

        _dipX = dipX; _dipY = dipY; _dipW = dipW; _dipH = dipH;
        _pixelX = pixelX; _pixelY = pixelY; _pixelW = pixelW; _pixelH = pixelH;

        _recorder = new ScreenRecorder(fps: 10);
        _recorder.OnTick += () => Dispatcher.Invoke(UpdateTimer);

        _micEnabled = micEnabled;
        _systemAudioEnabled = sysAudioEnabled;
        UpdateMicUI();
        UpdateSystemAudioUI();

        // Load persisted color
        try { _recColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(AppSettings.Instance.DefaultColor); }
        catch { _recColor = Colors.Yellow; }
        RecColorIndicator.Fill = new SolidColorBrush(_recColor);

        _border = new RecordingBorder(dipX, dipY, dipW, dipH);
        App.ActiveRecordingOverlay = this;

        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 10;

        if (startImmediately)
        {
            // Skip pre-recording phase — go straight to recording
            SetPreRecordingUI(false);

            Loaded += async (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                NativeMethods.SetWindowDisplayAffinity(h, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

                _border.Show();
                _border.SetDashed(false);
                var bh = new WindowInteropHelper(_border).Handle;
                NativeMethods.SetWindowDisplayAffinity(bh, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

                if (!_recorder.Start(_pixelX, _pixelY, _pixelW, _pixelH))
                {
                    MessageBox.Show("Failed to start recording.", "Llamashot",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    _border.Close();
                    Close();
                    return;
                }

                _preRecording = false;

                // Init audio with requested settings
                bool audioOk = await _recorder.InitAudioAsync(micEnabled, sysAudioEnabled);
                if (audioOk)
                {
                    _micEnabled = _recorder.MicActive;
                    _systemAudioEnabled = _recorder.SystemAudioActive;
                }
                else
                {
                    // Audio init failed — reset to off but keep buttons enabled
                    _micEnabled = false;
                    _systemAudioEnabled = false;
                }
                UpdateMicUI();
                UpdateSystemAudioUI();
                UpdateAudioStatusText();
            };
        }
        else
        {
            // Pre-recording: show Start button, dashed border, allow resize
            SetPreRecordingUI(true);

            Loaded += async (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                NativeMethods.SetWindowDisplayAffinity(h, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

                _border.Show();
                _border.SetDashed(true);
                var bh = new WindowInteropHelper(_border).Handle;
                NativeMethods.SetWindowDisplayAffinity(bh, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

                bool audioOk = await _recorder.InitAudioAsync(true, true);
                if (audioOk)
                {
                    _micEnabled = _recorder.MicActive;
                    _systemAudioEnabled = _recorder.SystemAudioActive;
                    UpdateMicUI();
                    UpdateSystemAudioUI();
                    UpdateAudioStatusText();
                }
            };
        }
    }

    private void SetPreRecordingUI(bool preRecording)
    {
        _preRecording = preRecording;
        var pre = preRecording ? Visibility.Visible : Visibility.Collapsed;
        var rec = preRecording ? Visibility.Collapsed : Visibility.Visible;

        BtnStart.Visibility = pre;
        BtnPreClose.Visibility = pre;
        RecDot.Visibility = rec;
        SepAnnotations.Visibility = rec;
        BtnPen.Visibility = rec;
        BtnLine.Visibility = rec;
        BtnArrow.Visibility = rec;
        BtnRect.Visibility = rec;
        BtnEllipse.Visibility = rec;
        BtnText.Visibility = rec;
        BtnMarker.Visibility = rec;
        BtnCheck.Visibility = rec;
        BtnCross.Visibility = rec;
        BtnEraser.Visibility = rec;
        BtnUndo.Visibility = rec;
        BtnClearAnnotations.Visibility = rec;
        BtnRecColor.Visibility = rec;
        BtnRecThickness.Visibility = rec;
        PanelRecControls.Visibility = rec;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        BtnStart.IsEnabled = false;

        // 3-2-1 countdown
        for (int i = 3; i > 0; i--)
        {
            TxtTimer.Text = i.ToString();
            TxtTimer.FontSize = 20;
            await Task.Delay(1000);
        }

        TxtTimer.FontSize = 16;
        TxtTimer.Text = "00:00:00";

        // Read final bounds from (possibly resized) border
        var region = _border.GetRegionDip();
        var source = PresentationSource.FromVisual(this);
        double dpi = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        _pixelX = (int)(region.X * dpi);
        _pixelY = (int)(region.Y * dpi);
        _pixelW = (int)(region.Width * dpi);
        _pixelH = (int)(region.Height * dpi);

        // Start recording
        if (!_recorder.Start(_pixelX, _pixelY, _pixelW, _pixelH))
        {
            MessageBox.Show("Failed to start recording.", "Llamashot",
                MessageBoxButton.OK, MessageBoxImage.Error);
            _border.Close();
            Close();
            return;
        }

        // Update DIP bounds from resized border
        _dipX = region.X; _dipY = region.Y; _dipW = region.Width; _dipH = region.Height;

        // Switch border to solid red pulsing
        _border.SetDashed(false);

        // Switch to recording UI
        SetPreRecordingUI(false);
    }

    private void PreClose_Click(object sender, RoutedEventArgs e)
    {
        _border.Close();
        Close();
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

    private async void Mic_Click(object sender, RoutedEventArgs e) { Activate(); await ToggleMic(!_micEnabled); }
    private async void SystemAudio_Click(object sender, RoutedEventArgs e) { Activate(); await ToggleSystemAudio(!_systemAudioEnabled); }

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
        if (_preRecording)
        {
            // Only allow audio toggles and close during pre-recording
            switch (key)
            {
                case 'M': _ = ToggleMic(!_micEnabled); break;
                case 'S': _ = ToggleSystemAudio(!_systemAudioEnabled); break;
            }
            return;
        }
        switch (key)
        {
            case 'P': SelectAnnotationTool("Pen"); break;
            case 'L': SelectAnnotationTool("Line"); break;
            case 'A': SelectAnnotationTool("Arrow"); break;
            case 'R': SelectAnnotationTool("Rectangle"); break;
            case 'E': SelectAnnotationTool("Ellipse"); break;
            case 'T': SelectAnnotationTool("Text"); break;
            case 'H': SelectAnnotationTool("Marker"); break;
            case 'K': SelectAnnotationTool("Check"); break;
            case 'D': SelectAnnotationTool("CrossMark"); break;
            case 'G': SelectAnnotationTool("Eraser"); break;
            case 'X': _annotationOverlay?.Undo(); break;
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
        // Finalize any active text input first
        _annotationOverlay?.FinalizeText();
        RecordingAnnotation.IsTextInputActive = false;

        // Toggle off if same tool clicked again
        if (_activeAnnotationTool == toolName)
        {
            _activeAnnotationTool = null;
            _annotationOverlay?.SetTool(null);
            _annotationOverlay?.SetEraserMode(false);
            UpdateAnnotationToolHighlights();
            return;
        }

        _activeAnnotationTool = toolName;

        // Create annotation overlay if not yet created
        if (_annotationOverlay == null)
        {
            _annotationOverlay = new RecordingAnnotation(_dipX, _dipY, _dipW, _dipH);
            _annotationOverlay.EscapePressed += HandleEscapePress;
            _annotationOverlay.Show();
        }

        if (toolName == "Eraser")
        {
            _annotationOverlay.SetEraserMode(true);
            UpdateAnnotationToolHighlights();
            Activate(); // Bring toolbar back on top
            return;
        }

        _annotationOverlay.SetEraserMode(false);

        IDrawingTool tool = toolName switch
        {
            "Pen" => new PenTool(),
            "Line" => new LineTool(),
            "Arrow" => new ArrowTool(),
            "Rectangle" => new RectangleTool(),
            "Ellipse" => new EllipseTool(),
            "Text" => new TextTool(),
            "Marker" => new MarkerTool(),
            "Check" => new StampTool(StampType.Check),
            "CrossMark" => new StampTool(StampType.Cross),
            _ => new PenTool()
        };
        tool.StrokeColor = _recColor;
        tool.Thickness = _recThickness;

        _annotationOverlay.SetTool(tool);
        UpdateAnnotationToolHighlights();
        Activate(); // Bring toolbar back on top of annotation overlay
    }

    private void ClearAnnotations_Click(object sender, RoutedEventArgs e)
    {
        _annotationOverlay?.ClearAll();
    }

    private void UpdateAnnotationToolHighlights()
    {
        var tools = new (Button btn, string tag, Color color)[]
        {
            (BtnPen, "Pen", Color.FromRgb(0xFF, 0xA7, 0x26)),
            (BtnLine, "Line", Color.FromRgb(0x64, 0xB5, 0xF6)),
            (BtnArrow, "Arrow", Color.FromRgb(0x26, 0xC6, 0xDA)),
            (BtnRect, "Rectangle", Color.FromRgb(0x42, 0xA5, 0xF5)),
            (BtnEllipse, "Ellipse", Color.FromRgb(0xAB, 0x47, 0xBC)),
            (BtnText, "Text", Color.FromRgb(0xFF, 0xCA, 0x28)),
            (BtnMarker, "Marker", Color.FromRgb(0xFF, 0xEE, 0x58)),
            (BtnCheck, "Check", Color.FromRgb(0x4C, 0xAF, 0x50)),
            (BtnCross, "CrossMark", Color.FromRgb(0xF4, 0x43, 0x36)),
            (BtnEraser, "Eraser", Color.FromRgb(0xEF, 0x53, 0x50)),
        };
        foreach (var (btn, tag, c) in tools)
            btn.Background = _activeAnnotationTool == tag
                ? new SolidColorBrush(Color.FromArgb(0x50, c.R, c.G, c.B))
                : Brushes.Transparent;
    }

    private void SetSavingUI()
    {
        // Hide everything except saving panel
        BtnStart.Visibility = Visibility.Collapsed;
        RecDot.Visibility = Visibility.Collapsed;
        TxtTimer.Visibility = Visibility.Collapsed;
        BtnMic.Visibility = Visibility.Collapsed;
        BtnSystemAudio.Visibility = Visibility.Collapsed;
        TxtAudioStatus.Visibility = Visibility.Collapsed;
        SepAnnotations.Visibility = Visibility.Collapsed;
        BtnPen.Visibility = Visibility.Collapsed;
        BtnLine.Visibility = Visibility.Collapsed;
        BtnArrow.Visibility = Visibility.Collapsed;
        BtnRect.Visibility = Visibility.Collapsed;
        BtnEllipse.Visibility = Visibility.Collapsed;
        BtnText.Visibility = Visibility.Collapsed;
        BtnMarker.Visibility = Visibility.Collapsed;
        BtnCheck.Visibility = Visibility.Collapsed;
        BtnCross.Visibility = Visibility.Collapsed;
        BtnEraser.Visibility = Visibility.Collapsed;
        BtnUndo.Visibility = Visibility.Collapsed;
        BtnClearAnnotations.Visibility = Visibility.Collapsed;
        BtnRecColor.Visibility = Visibility.Collapsed;
        BtnRecThickness.Visibility = Visibility.Collapsed;
        BtnPreClose.Visibility = Visibility.Collapsed;
        PanelRecControls.Visibility = Visibility.Collapsed;
        SavingPanel.Visibility = Visibility.Visible;
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        _annotationOverlay?.Undo();
    }

    private Color _recColor = Colors.Yellow;
    private double _recThickness = 3;

    private void RecColor_Click(object sender, RoutedEventArgs e)
    {
        // Cycle through common colors
        var colors = new[] {
            Colors.Yellow, Colors.Red, Colors.OrangeRed, Colors.Orange,
            Colors.LimeGreen, Colors.Green, Colors.DodgerBlue, Colors.Blue,
            Colors.Purple, Colors.Magenta, Colors.White, Colors.Black
        };
        int idx = Array.IndexOf(colors, _recColor);
        _recColor = colors[(idx + 1) % colors.Length];
        RecColorIndicator.Fill = new SolidColorBrush(_recColor);
    }

    private void RecThickness_Click(object sender, RoutedEventArgs e)
    {
        _recThickness = _recThickness >= 8 ? 1 : _recThickness + 1;
        RecThicknessLabel.Text = ((int)_recThickness).ToString();
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
            // Hide all tools, show only saving message
            SetSavingUI();

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
