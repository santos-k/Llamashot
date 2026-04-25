using System.Windows;

namespace Llamashot.Views;

public partial class OcrResultWindow : Window
{
    public OcrResultWindow(string text)
    {
        InitializeComponent();
        ResultText.Text = text;

        var chars = text.Length;
        var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        TxtCharCount.Text = $"{words} word{(words != 1 ? "s" : "")}, {chars} char{(chars != 1 ? "s" : "")}";

        ResultText.Focus();
        ResultText.SelectAll();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        ResultText.Focus();
        ResultText.SelectAll();
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ResultText.Text))
            Clipboard.SetText(ResultText.Text);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
