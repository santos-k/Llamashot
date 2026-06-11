using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Llamashot.Views;

public class SignatureDialog : Window
{
    private readonly InkCanvas _ink;
    public string? ResultPngPath { get; private set; }

    public SignatureDialog()
    {
        Title = "Signature";
        Width = 560; Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1E));

        var root = new DockPanel { Margin = new Thickness(12) };

        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var clearBtn = new Button { Content = "Clear", Margin = new Thickness(4,0,4,0), Padding = new Thickness(14,6,14,6) };
        clearBtn.Click += (_, _) => _ink!.Strokes.Clear();
        var uploadBtn = new Button { Content = "Upload image…", Margin = new Thickness(4,0,4,0), Padding = new Thickness(14,6,14,6) };
        uploadBtn.Click += Upload_Click;
        var okBtn = new Button { Content = "Use drawing", Margin = new Thickness(4,0,4,0), Padding = new Thickness(14,6,14,6) };
        okBtn.Click += Ok_Click;
        var cancelBtn = new Button { Content = "Cancel", Margin = new Thickness(4,0,0,0), Padding = new Thickness(14,6,14,6) };
        cancelBtn.Click += (_, _) => { DialogResult = false; };
        buttons.Children.Add(clearBtn);
        buttons.Children.Add(uploadBtn);
        buttons.Children.Add(okBtn);
        buttons.Children.Add(cancelBtn);

        _ink = new InkCanvas { Background = Brushes.White };
        _ink.DefaultDrawingAttributes = new DrawingAttributes { Color = Colors.Black, Width = 3, Height = 3, FitToCurve = true };
        var inkBorder = new Border { Child = _ink, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };

        root.Children.Add(buttons);
        root.Children.Add(inkBorder);
        Content = root;
    }

    private void Upload_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg" };
        if (dlg.ShowDialog() == true)
        {
            ResultPngPath = dlg.FileName;
            DialogResult = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_ink.Strokes.Count == 0) { MessageBox.Show("Draw a signature or use Upload image."); return; }
        var b = _ink.Strokes.GetBounds();
        if (b.IsEmpty || b.Width < 1 || b.Height < 1) { MessageBox.Show("Draw a signature first."); return; }
        int margin = 8;
        int w = (int)Math.Ceiling(b.Right) + margin;
        int h = (int)Math.Ceiling(b.Bottom) + margin;
        var rtb = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            foreach (var s in _ink.Strokes) s.Draw(dc);
        rtb.Render(visual);
        string path = Path.Combine(Path.GetTempPath(), "fillsign_sig_" + Guid.NewGuid().ToString("N") + ".png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(path)) enc.Save(fs);
        ResultPngPath = path;
        DialogResult = true;
    }
}
