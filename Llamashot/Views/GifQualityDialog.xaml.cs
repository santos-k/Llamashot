using System.Windows;
using System.Windows.Input;

namespace Llamashot.Views;

public partial class GifQualityDialog : Window
{
    /// <summary>Max width in pixels (0 = original). Set after dialog closes with DialogResult=true.</summary>
    public new int MaxWidth { get; private set; }

    /// <summary>Frame skip factor (1 = all frames, 2 = every other frame). Set after dialog closes.</summary>
    public int FrameSkip { get; private set; } = 1;

    /// <summary>If true, dialog is for GIF. If false, for video (MP4).</summary>
    public bool IsGifMode { get; }

    public GifQualityDialog(bool isGif = true)
    {
        InitializeComponent();
        IsGifMode = isGif;
        TxtTitle.Text = isGif ? "GIF Quality" : "Video Quality";
    }

    private void Original_Click(object sender, RoutedEventArgs e)
    {
        MaxWidth = 0;
        FrameSkip = 1;
        DialogResult = true;
    }

    private void High_Click(object sender, RoutedEventArgs e)
    {
        MaxWidth = 720;
        FrameSkip = 1;
        DialogResult = true;
    }

    private void Compact_Click(object sender, RoutedEventArgs e)
    {
        MaxWidth = 480;
        FrameSkip = IsGifMode ? 2 : 1;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}
