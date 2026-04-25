using System.Windows;

namespace Llamashot.Views;

public partial class OcrResultWindow : Window
{
    public OcrResultWindow(string text)
    {
        InitializeComponent();
        ResultText.Text = text;
        ResultText.SelectAll();
        ResultText.Focus();
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(ResultText.Text);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
