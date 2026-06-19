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
    public string NameText { get; set; } = "";
    public string DateText { get; set; } = "";
    public string SizeText { get; set; } = "";
    public string TypeText { get; set; } = "";
    public string TypeDescText { get; set; } = "";
    public string TypeColor { get; set; } = "#4CAF50";
    public string ToolTipText { get; set; } = "";

    // Filtering: media type comes from the file extension; clipboard from the record origin.
    public string Ext { get; set; } = "";
    public bool IsClipboard { get; set; }

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

public enum HistoryViewMode
{
    ExtraLargeIcons, LargeIcons, MediumIcons, SmallIcons, Content
}

public partial class HistoryWindow : Window
{
    private List<HistoryItemViewModel> _allItems = new();
    private List<HistoryItemViewModel> _items = new();
    private string _activeFilter = "All";
    private HistoryViewMode _viewMode = HistoryViewMode.LargeIcons;

    // Order used by Ctrl+wheel zoom (smallest → largest).
    private static readonly HistoryViewMode[] ZoomOrder =
    {
        HistoryViewMode.Content, HistoryViewMode.SmallIcons,
        HistoryViewMode.MediumIcons, HistoryViewMode.LargeIcons, HistoryViewMode.ExtraLargeIcons
    };

    // Thumbnail height for the on-top icon templates (bound from XAML).
    public static readonly DependencyProperty ThumbHeightProperty =
        DependencyProperty.Register(nameof(ThumbHeight), typeof(double), typeof(HistoryWindow), new PropertyMetadata(130.0));
    public double ThumbHeight
    {
        get => (double)GetValue(ThumbHeightProperty);
        set => SetValue(ThumbHeightProperty, value);
    }

    // WrapPanel item width (column width) for the wrap-based modes (bound from XAML).
    public static readonly DependencyProperty ItemBoxWidthProperty =
        DependencyProperty.Register(nameof(ItemBoxWidth), typeof(double), typeof(HistoryWindow), new PropertyMetadata(190.0));
    public double ItemBoxWidth
    {
        get => (double)GetValue(ItemBoxWidthProperty);
        set => SetValue(ItemBoxWidthProperty, value);
    }

    public HistoryWindow()
    {
        InitializeComponent();

        // Size for 5 columns × 4 rows, capped to screen
        const int cols = 5, rows = 4;
        const double itemW = 225, itemH = 152; // item + margins
        const double chromeW = 80, chromeH = 160; // scrollbar + header + footer + borders
        var screenW = SystemParameters.PrimaryScreenWidth;
        var screenH = SystemParameters.PrimaryScreenHeight;
        Width = Math.Min(cols * itemW + chromeW, screenW * 0.9);
        Height = Math.Min(rows * itemH + chromeH, screenH * 0.85);

        if (Enum.TryParse<HistoryViewMode>(AppSettings.Instance.HistoryViewMode, out var saved))
            _viewMode = saved;

        LoadHistory();
        ApplyViewMode(_viewMode);
        Activated += (_, _) => LoadHistory();
    }

    // ============ VIEW MODES ============

    private void ApplyViewMode(HistoryViewMode mode)
    {
        _viewMode = mode;

        // Pick template, panel, and (for wrap modes) icon/column sizes.
        string tpl, panel;
        switch (mode)
        {
            case HistoryViewMode.ExtraLargeIcons: tpl = "TplIcon"; panel = "PanelWrapH"; ThumbHeight = 200; ItemBoxWidth = 260; break;
            case HistoryViewMode.LargeIcons: tpl = "TplIcon"; panel = "PanelWrapH"; ThumbHeight = 130; ItemBoxWidth = 190; break;
            case HistoryViewMode.MediumIcons: tpl = "TplIcon"; panel = "PanelWrapH"; ThumbHeight = 90; ItemBoxWidth = 150; break;
            case HistoryViewMode.SmallIcons: tpl = "TplIcon"; panel = "PanelWrapH"; ThumbHeight = 56; ItemBoxWidth = 120; break;
            case HistoryViewMode.Content: tpl = "TplContent"; panel = "PanelStack"; break;
            default: tpl = "TplIcon"; panel = "PanelWrapH"; ThumbHeight = 130; ItemBoxWidth = 190; break;
        }

        HistoryList.ItemTemplate = (System.Windows.DataTemplate)Resources[tpl];
        HistoryList.ItemsPanel = (System.Windows.Controls.ItemsPanelTemplate)Resources[panel];

        // Reflect the active mode in the View menu checkmarks.
        if (Resources["ViewMenu"] is System.Windows.Controls.ContextMenu menu)
        {
            foreach (var obj in menu.Items)
                if (obj is System.Windows.Controls.MenuItem mi && mi.Tag is string t)
                    mi.IsChecked = t == mode.ToString();
        }

        AppSettings.Instance.HistoryViewMode = mode.ToString();
        AppSettings.Save();
    }

    private void ViewMenu_Open(object sender, RoutedEventArgs e)
    {
        if (Resources["ViewMenu"] is System.Windows.Controls.ContextMenu menu)
        {
            // Keep checkmarks current, then drop the menu under the button.
            foreach (var obj in menu.Items)
                if (obj is System.Windows.Controls.MenuItem mi && mi.Tag is string t)
                    mi.IsChecked = t == _viewMode.ToString();
            menu.PlacementTarget = BtnView;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void ViewMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem mi && mi.Tag is string t
            && Enum.TryParse<HistoryViewMode>(t, out var mode))
            ApplyViewMode(mode);
    }

    private void HistoryScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return; // normal scroll
        e.Handled = true;
        int idx = Array.IndexOf(ZoomOrder, _viewMode);
        if (idx < 0) idx = Array.IndexOf(ZoomOrder, HistoryViewMode.LargeIcons);
        idx = Math.Clamp(idx + (e.Delta > 0 ? 1 : -1), 0, ZoomOrder.Length - 1);
        ApplyViewMode(ZoomOrder[idx]);
    }

    private void LoadHistory()
    {
        HistoryManager.Load();

        _allItems = HistoryManager.Records.Select(r =>
        {
            var (typeText, typeColor, typeDesc) = r.Type switch
            {
                RecordType.Clipboard => ("Copied", "#42A5F5", "Copied to clipboard"),
                RecordType.Recording when r.FilePath?.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) == true
                    => ("GIF", "#4CAF50", "GIF recording"),
                RecordType.Recording => ("Video", "#F44336", "Video recording"),
                _ => ("Saved", "#66BB6A", "Saved to file")
            };
            string shortType = typeText switch
            {
                "Saved" => "Image",
                "Copied" => "Clipboard",
                _ => typeText // "Video" / "GIF"
            };
            string fileName = string.IsNullOrEmpty(r.FilePath) ? $"Clipboard {r.CapturedAt:HHmmss}" : Path.GetFileName(r.FilePath);
            return new HistoryItemViewModel
            {
                ThumbnailPath = r.ThumbnailPath,
                FilePath = r.FilePath ?? "",
                NameText = fileName,
                DateText = r.CapturedAt.ToString("MMM dd, HH:mm"),
                SizeText = $"{r.Width} x {r.Height}",
                TypeText = typeText,
                TypeDescText = shortType,
                TypeColor = typeColor,
                Ext = Path.GetExtension(r.FilePath ?? "").ToLowerInvariant(),
                IsClipboard = r.Type == RecordType.Clipboard,
                ToolTipText = $"{r.FilePath}\n{r.CapturedAt:yyyy-MM-dd HH:mm:ss}\n{r.Width} x {r.Height}\n{typeDesc}"
            };
        }).ToList();

        ApplyFilter();
    }

    // "Images" = still pictures (GIFs have their own filter, so they're excluded here).
    private static bool IsImageExt(string ext) =>
        ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".tif" or ".tiff";

    private static bool IsVideoExt(string ext) =>
        ext is ".mp4" or ".webm" or ".avi" or ".mov" or ".mkv" or ".wmv";

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string filter)
        {
            _activeFilter = filter;
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        _items = _activeFilter switch
        {
            "Images" => _allItems.Where(i => IsImageExt(i.Ext)).ToList(),
            "Videos" => _allItems.Where(i => IsVideoExt(i.Ext)).ToList(),
            "GIFs" => _allItems.Where(i => i.Ext == ".gif").ToList(),
            "Clipboard" => _allItems.Where(i => i.IsClipboard).ToList(),
            _ => _allItems.ToList()
        };

        HistoryList.ItemsSource = _items;
        TxtCount.Text = $"{_items.Count} item{(_items.Count != 1 ? "s" : "")}";
        TxtEmpty.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelectionUI();
        UpdateFilterButtons();
    }

    private void UpdateFilterButtons()
    {
        var activeBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x42, 0xA5, 0xF5));
        var inactiveBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
        var activeFg = System.Windows.Media.Brushes.White;
        var inactiveFg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAA, 0xAA, 0xAA));

        foreach (var child in FilterPanel.Children)
        {
            if (child is System.Windows.Controls.Button btn)
            {
                bool active = btn.Tag as string == _activeFilter;
                btn.Background = active ? activeBg : inactiveBg;
                btn.Foreground = active ? activeFg : inactiveFg;
                btn.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
                btn.BorderThickness = active ? new Thickness(0) : new Thickness(1);
            }
        }
    }

    private void Item_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is HistoryItemViewModel item)
        {
            if (File.Exists(item.FilePath))
                Core.FilePreviewManager.Instance.ShowPreview(item.FilePath);
        }
    }

    private void OpenPreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is HistoryItemViewModel item)
        {
            if (File.Exists(item.FilePath))
                Core.FilePreviewManager.Instance.ShowPreview(item.FilePath);
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

    private void SaveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not HistoryItemViewModel item) return;
        if (!File.Exists(item.FilePath)) return;

        var ext = Path.GetExtension(item.FilePath).ToLowerInvariant();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap Image|*.bmp|All Files|*.*",
            DefaultExt = string.IsNullOrEmpty(ext) ? ".png" : ext,
            FileName = Path.GetFileName(item.FilePath)
        };
        dialog.FilterIndex = ext switch
        {
            ".jpg" or ".jpeg" => 2,
            ".bmp" => 3,
            _ => 1
        };

        if (dialog.ShowDialog() == true)
            File.Copy(item.FilePath, dialog.FileName, overwrite: true);
    }

    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is HistoryItemViewModel item)
        {
            if (File.Exists(item.FilePath))
                Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
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

    private async void SaveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var selected = _items.Where(i => i.IsSelected && File.Exists(i.FilePath)).ToList();
        if (selected.Count == 0) return;

        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = $"Save {selected.Count} screenshot{(selected.Count != 1 ? "s" : "")} to folder",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        foreach (var item in selected)
        {
            var destPath = Path.Combine(dialog.SelectedPath, Path.GetFileName(item.FilePath));
            File.Copy(item.FilePath, destPath, overwrite: true);
        }

        var original = btn.Content;
        btn.Content = "Saved!";
        btn.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x66, 0xBB, 0x6A));
        await Task.Delay(2000);
        btn.Content = original;
        btn.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x42, 0xA5, 0xF5));
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
