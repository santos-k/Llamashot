using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Llamashot.Core;
using Microsoft.Win32;

namespace Llamashot.Views;

public partial class RecordingOverlay : Window
{
    private readonly ScreenRecorder _recorder;
    private readonly RecordingBorder _border;
    private bool _finished;

    public RecordingOverlay(int pixelX, int pixelY, int pixelW, int pixelH,
                            double dipX, double dipY, double dipW, double dipH)
    {
        InitializeComponent();

        _recorder = new ScreenRecorder(fps: 10, maxSeconds: 120);
        _recorder.OnTick += () => Dispatcher.Invoke(UpdateTimer);
        _recorder.OnMaxReached += () => Dispatcher.Invoke(FinishRecording);

        _border = new RecordingBorder(dipX, dipY, dipW, dipH);

        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 10;

        Loaded += (s, e) =>
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
            }
        };
    }

    private void UpdateTimer()
    {
        var elapsed = _recorder.Elapsed;
        TxtTimer.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
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
