using System.Windows;

namespace Llamashot.Views;

public partial class RecordingBorder : Window
{
    public RecordingBorder(double x, double y, double width, double height)
    {
        InitializeComponent();
        Left = x - 3;
        Top = y - 3;
        Width = width + 6;
        Height = height + 6;
    }
}
