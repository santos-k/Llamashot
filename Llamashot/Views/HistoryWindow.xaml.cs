using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Llamashot.Core;

namespace Llamashot.Views;

public class HistoryItemViewModel
{
    public string ThumbnailPath { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string DateText { get; set; } = "";
    public string SizeText { get; set; } = "";
    public string ToolTipText { get; set; } = "";
}

public partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
        LoadHistory();
    }

    private void LoadHistory()
    {
        HistoryManager.Load();

        var items = HistoryManager.Records.Select(r => new HistoryItemViewModel
        {
            ThumbnailPath = r.ThumbnailPath,
            FilePath = r.FilePath,
            DateText = r.CapturedAt.ToString("MMM dd, HH:mm"),
            SizeText = $"{r.Width}x{r.Height}",
            ToolTipText = $"{r.FilePath}\n{r.CapturedAt:yyyy-MM-dd HH:mm:ss}\n{r.Width}x{r.Height}"
        }).ToList();

        HistoryList.ItemsSource = items;
    }

    private void Item_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is HistoryItemViewModel item)
        {
            if (File.Exists(item.FilePath))
            {
                Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
            }
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Clear all screenshot history?", "Confirm",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            HistoryManager.Clear();
            LoadHistory();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
