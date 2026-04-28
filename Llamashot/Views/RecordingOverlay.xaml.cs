using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Llamashot.Core;
using Microsoft.Win32;

namespace Llamashot.Views;

public partial class RecordingOverlay : Window
{
    private readonly ScreenRecorder _recorder;
    private readonly RecordingBorder _border;
    private bool _finished;
    private bool _audioEnabled;

    public RecordingOverlay(int pixelX, int pixelY, int pixelW, int pixelH,
                            double dipX, double dipY, double dipW, double dipH)
    {
        InitializeComponent();

        _recorder = new ScreenRecorder(fps: 10);
        _recorder.OnTick += () => Dispatcher.Invoke(UpdateTimer);

        _audioEnabled = AppSettings.Instance.RecordAudio;
        UpdateAudioUI();

        _border = new RecordingBorder(dipX, dipY, dipW, dipH);

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

            if (_audioEnabled)
            {
                BtnAudio.IsEnabled = false;
                bool audioOk = await _recorder.StartAudioAsync();
                BtnAudio.IsEnabled = true;

                if (!audioOk)
                {
                    _audioEnabled = false;
                    UpdateAudioUI();
                    TxtAudioStatus.Text = "No audio device";
                    TxtAudioStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                }
                else
                {
                    UpdateAudioUI();
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

    private void UpdateAudioUI()
    {
        var color = _audioEnabled
            ? Color.FromRgb(0x4C, 0xAF, 0x50)  // green
            : Color.FromRgb(0x66, 0x66, 0x66);  // gray

        var brush = new SolidColorBrush(color);
        MicBody.Fill = brush;
        MicArc.Stroke = brush;
        MicStand.Stroke = brush;
        MicSlash.Visibility = _audioEnabled ? Visibility.Collapsed : Visibility.Visible;

        if (_audioEnabled)
        {
            // Show what sources are active
            string sources = (_recorder.HasMic, _recorder.HasSystemAudio) switch
            {
                (true, true) => "Mic + System",
                (true, false) => "Mic",
                (false, true) => "System",
                _ => "Audio ON"
            };
            TxtAudioStatus.Text = sources;
            TxtAudioStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        }
        else
        {
            TxtAudioStatus.Text = "";
            TxtAudioStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        }
    }

    private async void Audio_Click(object sender, RoutedEventArgs e)
    {
        BtnAudio.IsEnabled = false;
        try
        {
            if (_audioEnabled)
            {
                await _recorder.StopAudioAsync();
                _audioEnabled = false;
            }
            else
            {
                bool ok = await _recorder.StartAudioAsync();
                if (ok)
                {
                    _audioEnabled = true;
                }
                else
                {
                    TxtAudioStatus.Text = "No audio device";
                    TxtAudioStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                    BtnAudio.IsEnabled = true;
                    return;
                }
            }
            UpdateAudioUI();
        }
        finally
        {
            BtnAudio.IsEnabled = true;
        }
    }

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
            BtnAudio.IsEnabled = false;

            bool success = await _recorder.SaveAsMp4Async(dialog.FileName);
            Hide();

            if (!success)
            {
                MessageBox.Show($"Failed to encode MP4.\n{_recorder.LastError}", "Llamashot",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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
        if (!_finished)
        {
            _recorder.Stop();
            _border.Close();
            _recorder.CleanupFrames();
        }
        _recorder.Dispose();
        base.OnClosed(e);
    }
}
