using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Llamashot.Core;

namespace Llamashot.Views;

public class HistoryItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public string ThumbnailPath { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string DateText { get; set; } = "";
    public string SizeText { get; set; } = "";
    public string TypeText { get; set; } = "";
    public string TypeColor { get; set; } = "#4CAF50";
    public string ToolTipText { get; set; } = "";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class HistoryWindow : Window
{
    private List<HistoryItemViewModel> _items = new();

    public HistoryWindow()
    {
        InitializeComponent();
        LoadHistory();
    }

    private void LoadHistory()
    {
        HistoryManager.Load();

        _items = HistoryManager.Records.Select(r => new HistoryItemViewModel
        {
            ThumbnailPath = r.ThumbnailPath,
            FilePath = r.FilePath,
            DateText = r.CapturedAt.ToString("MMM dd, HH:mm"),
            SizeText = $"{r.Width} x {r.Height}",
            TypeText = r.Type == RecordType.Clipboard ? "Copied" : "Saved",
            TypeColor = r.Type == RecordType.Clipboard ? "#42A5F5" : "#66BB6A",
            ToolTipText = $"{r.FilePath}\n{r.CapturedAt:yyyy-MM-dd HH:mm:ss}\n{r.Width} x {r.Height}\n{(r.Type == RecordType.Clipboard ? "Copied to clipboard" : "Saved to file")}"
        }).ToList();

        HistoryList.ItemsSource = _items;
        TxtCount.Text = $"{_items.Count} screenshot{(_items.Count != 1 ? "s" : "")}";
        TxtEmpty.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelectionUI();
    }

    private void Item_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is HistoryItemViewModel item)
        {
            if (File.Exists(item.FilePath))
                Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
        }
    }

    private void CopyItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is HistoryItemViewModel item)
        {
            if (File.Exists(item.FilePath))
            {
                var bmp = new BitmapImage(new System.Uri(item.FilePath));
                Clipboard.SetImage(bmp);
            }
        }
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is HistoryItemViewModel item)
        {
            var result = MessageBox.Show(
                "Delete this screenshot from history?",
                "Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                HistoryManager.DeleteRecords(new[] { item.FilePath });
                LoadHistory();
            }
        }
    }

    // ============ SELECTION ============

    private void ItemCheckbox_Click(object sender, RoutedEventArgs e)
    {
        UpdateSelectionUI();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        bool selectAll = ChkSelectAll.IsChecked ?? false;
        foreach (var item in _items)
            item.IsSelected = selectAll;
        UpdateSelectionUI();
    }

    private void UpdateSelectionUI()
    {
        int count = _items.Count(i => i.IsSelected);
        if (count > 0)
        {
            SelectionActions.Visibility = Visibility.Visible;
            TxtSelected.Text = $"{count} selected";
        }
        else
        {
            SelectionActions.Visibility = Visibility.Collapsed;
        }
    }

    // ============ BULK ACTIONS ============

    private async void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var selected = _items.Where(i => i.IsSelected && File.Exists(i.FilePath)).ToList();
        if (selected.Count == 0) return;

        if (selected.Count == 1)
        {
            var bmp = new BitmapImage(new System.Uri(selected[0].FilePath));
            Clipboard.SetImage(bmp);
        }
        else
        {
            var files = new StringCollection();
            foreach (var item in selected)
                files.Add(item.FilePath);
            Clipboard.SetFileDropList(files);
        }

        var original = btn.Content;
        btn.Content = "Copied!";
        btn.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x42, 0xA5, 0xF5));
        await Task.Delay(2000);
        btn.Content = original;
        btn.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x66, 0xBB, 0x6A));
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        var result = MessageBox.Show(
            $"Delete {selected.Count} screenshot{(selected.Count != 1 ? "s" : "")} from history?",
            "Delete Selected", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            HistoryManager.DeleteRecords(selected.Select(s => s.FilePath));
            LoadHistory();
        }
    }

    // ============ OTHER ACTIONS ============

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Delete all screenshot history and thumbnails?",
            "Clear History", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            HistoryManager.Clear();
            LoadHistory();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
