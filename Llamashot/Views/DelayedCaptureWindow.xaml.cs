using System.Windows;
using System.Windows.Threading;

namespace Llamashot.Views;

public partial class DelayedCaptureWindow : Window
{
    private DispatcherTimer? _timer;
    private int _remaining;

    public DelayedCaptureWindow()
    {
        InitializeComponent();
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        _remaining = DelayCombo.SelectedIndex switch
        {
            0 => 1,
            1 => 3,
            2 => 5,
            3 => 10,
            _ => 3
        };

        CountdownText.Text = _remaining.ToString();
        CountdownText.Visibility = Visibility.Visible;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        _remaining--;

        if (_remaining <= 0)
        {
            _timer?.Stop();
            Hide();

            // Small delay to let the window fully hide
            var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            delay.Tick += (s, args) =>
            {
                delay.Stop();
                var overlay = new OverlayWindow();
                overlay.StartCapture();
                Close();
            };
            delay.Start();
        }
        else
        {
            CountdownText.Text = _remaining.ToString();
        }
    }
}
