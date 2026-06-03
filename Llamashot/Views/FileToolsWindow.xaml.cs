using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Llamashot.Core;
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Llamashot.Views;

public partial class FileToolsWindow : Window
{
    // =====================================================================
    //  Tool card definitions
    // =====================================================================

    private static readonly (string id, string title, string desc, string color)[] ToolDefs =
    {
        ("merge_pdf",      "Merge PDF",       "Combine multiple PDFs into one",     "#E53935"),
        ("split_pdf",      "Split PDF",       "Extract pages from PDF",             "#FF7043"),
        ("compress_pdf",   "Compress PDF",    "Reduce PDF file size",               "#EF5350"),
        ("pdf_to_images",  "PDF to Images",   "Convert pages to JPG/PNG",           "#F44336"),
        ("images_to_pdf",  "Images to PDF",   "Combine images into PDF",            "#E91E63"),
        ("rotate_pdf",     "Rotate PDF",      "Rotate PDF pages",                   "#FFA726"),
        ("watermark",      "Watermark PDF",   "Add text watermark",                 "#7E57C2"),
        ("page_numbers",   "Page Numbers",    "Add numbers to PDF",                 "#5C6BC0"),
        ("compress_image", "Compress Image",  "Reduce image file size",             "#26C6DA"),
        ("resize_image",   "Resize Image",    "Change dimensions",                  "#26A69A"),
        ("crop_image",     "Crop Image",      "Crop to selection",                  "#42A5F5"),
        ("rotate_flip",    "Rotate & Flip",   "Rotate or flip images",              "#AB47BC"),
        ("convert_format", "Convert Format",  "Change image format",                "#EC407A"),
        ("compress_office","Compress Office",  "Reduce DOCX/XLSX/PPTX",             "#78909C"),
    };

    // =====================================================================
    //  Panel & view maps
    // =====================================================================

    private readonly Dictionary<string, FrameworkElement> _toolPanels = new();
    private readonly Dictionary<string, string> _toolTitles = new();
    private readonly Dictionary<string, FrameworkElement> _selectViews = new();
    private readonly Dictionary<string, FrameworkElement> _configViews = new();

    // =====================================================================
    //  Navigation state
    // =====================================================================

    private string? _currentToolId;
    private string? _lastOutputPath;
    private string? _lastOutputDir;

    // =====================================================================
    //  Observable collections
    // =====================================================================

    private readonly ObservableCollection<FileItem> _mergePdfFiles = new();
    private readonly ObservableCollection<FileItem> _imgToPdfFiles = new();
    private readonly ObservableCollection<FileItem> _compressImgFiles = new();
    private readonly ObservableCollection<FileItem> _compressOfficeFiles = new();

    // =====================================================================
    //  Single-file tool paths
    // =====================================================================

    private string? _splitPdfPath;
    private string? _compressPdfPath;
    private string? _pdfToImgPath;
    private string? _rotatePdfPath;
    private string? _watermarkPdfPath;
    private string? _pageNumPdfPath;

    // =====================================================================
    //  Crop state
    // =====================================================================

    private string? _cropSourcePath;
    private int _cropImgWidth, _cropImgHeight;
    private bool _isDraggingCrop;
    private Point _cropStart;
    private Point _cropEnd;
    private Rectangle? _cropRect;
    private readonly Rectangle[] _cropOverlays = new Rectangle[4];

    // =====================================================================
    //  Resize state
    // =====================================================================

    private string? _resizeSourcePath;
    private int _resizeOrigW, _resizeOrigH;
    private bool _suppressAspectUpdate;

    // =====================================================================
    //  Rotate/Flip state
    // =====================================================================

    private string? _rotateFlipSourcePath;

    // =====================================================================
    //  Convert state
    // =====================================================================

    private string? _convertSourcePath;

    // =====================================================================
    //  File extension lists
    // =====================================================================

    private static readonly string[] ImageExtensions =
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp" };

    private static readonly string[] OfficeExtensions =
        { ".docx", ".xlsx", ".pptx", ".odt", ".ods", ".odp" };

    // =====================================================================
    //  Constructor
    // =====================================================================

    public FileToolsWindow()
    {
        InitializeComponent();
        CreateToolCards();
        InitPanelMap();

        MergePdfList.ItemsSource = _mergePdfFiles;
        ImgToPdfList.ItemsSource = _imgToPdfFiles;
        CompressImgList.ItemsSource = _compressImgFiles;
        CompressOfficeList.ItemsSource = _compressOfficeFiles;
    }

    // =====================================================================
    //  Card creation
    // =====================================================================

    private void CreateToolCards()
    {
        foreach (var (id, title, desc, color) in ToolDefs)
        {
            var card = new Border
            {
                Width = 190,
                Height = 100,
                Margin = new Thickness(8),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252528")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                BorderThickness = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(6),
                Cursor = Cursors.Hand,
                Tag = id
            };

            var stack = new StackPanel { Margin = new Thickness(14, 12, 12, 12) };
            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            });
            stack.Children.Add(new TextBlock
            {
                Text = desc,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888")),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
            card.Child = stack;

            card.MouseLeftButtonDown += Card_Click;
            card.MouseEnter += (s, _) => ((Border)s).Background =
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E2E34"));
            card.MouseLeave += (s, _) => ((Border)s).Background =
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252528"));

            CardPanel.Children.Add(card);
        }
    }

    // =====================================================================
    //  Panel map init
    // =====================================================================

    private void InitPanelMap()
    {
        _toolPanels["merge_pdf"] = PanelMergePdf;
        _toolPanels["split_pdf"] = PanelSplitPdf;
        _toolPanels["compress_pdf"] = PanelCompressPdf;
        _toolPanels["pdf_to_images"] = PanelPdfToImages;
        _toolPanels["images_to_pdf"] = PanelImagesToPdf;
        _toolPanels["rotate_pdf"] = PanelRotatePdf;
        _toolPanels["watermark"] = PanelWatermark;
        _toolPanels["page_numbers"] = PanelPageNumbers;
        _toolPanels["compress_image"] = PanelCompressImage;
        _toolPanels["resize_image"] = PanelResizeImage;
        _toolPanels["crop_image"] = PanelCropImage;
        _toolPanels["rotate_flip"] = PanelRotateFlip;
        _toolPanels["convert_format"] = PanelConvertFormat;
        _toolPanels["compress_office"] = PanelCompressOffice;

        _selectViews["merge_pdf"] = MergeSelectView;
        _selectViews["split_pdf"] = SplitSelectView;
        _selectViews["compress_pdf"] = CompressPdfSelectView;
        _selectViews["pdf_to_images"] = PdfToImgSelectView;
        _selectViews["images_to_pdf"] = ImgToPdfSelectView;
        _selectViews["rotate_pdf"] = RotatePdfSelectView;
        _selectViews["watermark"] = WatermarkSelectView;
        _selectViews["page_numbers"] = PageNumSelectView;
        _selectViews["compress_image"] = CompressImgSelectView;
        _selectViews["resize_image"] = ResizeSelectView;
        _selectViews["crop_image"] = CropSelectView;
        _selectViews["rotate_flip"] = RotateFlipSelectView;
        _selectViews["convert_format"] = ConvertSelectView;
        _selectViews["compress_office"] = CompressOfficeSelectView;

        _configViews["merge_pdf"] = MergeConfigView;
        _configViews["split_pdf"] = SplitConfigView;
        _configViews["compress_pdf"] = CompressPdfConfigView;
        _configViews["pdf_to_images"] = PdfToImgConfigView;
        _configViews["images_to_pdf"] = ImgToPdfConfigView;
        _configViews["rotate_pdf"] = RotatePdfConfigView;
        _configViews["watermark"] = WatermarkConfigView;
        _configViews["page_numbers"] = PageNumConfigView;
        _configViews["compress_image"] = CompressImgConfigView;
        _configViews["resize_image"] = ResizeConfigView;
        _configViews["crop_image"] = CropConfigView;
        _configViews["rotate_flip"] = RotateFlipConfigView;
        _configViews["convert_format"] = ConvertConfigView;
        _configViews["compress_office"] = CompressOfficeConfigView;

        foreach (var (id, title, _, _) in ToolDefs)
            _toolTitles[id] = title;
    }

    // =====================================================================
    //  Fade animations
    // =====================================================================

    private void FadeIn(UIElement el, double durationMs = 250)
    {
        el.Visibility = Visibility.Visible;
        el.Opacity = 0;
        var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs));
        anim.Completed += (s, _) =>
        {
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = 1;
        };
        el.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void FadeOut(UIElement el, double durationMs = 250, Action? onComplete = null)
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(durationMs));
        anim.Completed += (s, _) =>
        {
            el.Visibility = Visibility.Collapsed;
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = 1;
            onComplete?.Invoke();
        };
        el.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    // =====================================================================
    //  State transitions
    // =====================================================================

    private void ShowSelectState(string toolId)
    {
        if (_selectViews.TryGetValue(toolId, out var selectView))
        {
            selectView.Visibility = Visibility.Visible;
            selectView.Opacity = 1;
        }
        if (_configViews.TryGetValue(toolId, out var configView))
            configView.Visibility = Visibility.Collapsed;
    }

    private void ShowConfigState(string toolId)
    {
        if (_selectViews.TryGetValue(toolId, out var selectView))
        {
            FadeOut(selectView, 150, () =>
            {
                if (_configViews.TryGetValue(toolId, out var configView))
                    FadeIn(configView);
            });
        }
    }

    private void ShowProcessing(string message)
    {
        TxtProcessing.Text = message;
        MainProgress.Value = 0;
        TxtProgressPct.Text = "0%";
        FadeIn(ProcessingOverlay);
    }

    private IProgress<int> CreateProgress()
    {
        return new Progress<int>(v =>
        {
            MainProgress.Value = v;
            TxtProgressPct.Text = $"{v}%";
        });
    }

    private void ShowComplete(string message, string? detail = null, string? filePath = null, string? folderPath = null)
    {
        _lastOutputPath = filePath;
        _lastOutputDir = folderPath ?? (filePath != null ? System.IO.Path.GetDirectoryName(filePath) : null);
        TxtCompleteMsg.Text = message;
        TxtCompleteDetail.Text = detail ?? "";
        TxtCompleteDetail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        BtnCompleteOpen.Visibility = filePath != null ? Visibility.Visible : Visibility.Collapsed;
        BtnCompleteFolder.Visibility = _lastOutputDir != null ? Visibility.Visible : Visibility.Collapsed;
        FadeOut(ProcessingOverlay, 200, () => FadeIn(CompleteOverlay));
    }

    private void ResetToolStates()
    {
        foreach (var sv in _selectViews.Values)
        {
            sv.Visibility = Visibility.Visible;
            sv.Opacity = 1;
        }
        foreach (var cv in _configViews.Values)
            cv.Visibility = Visibility.Collapsed;

        // Clear collections
        _mergePdfFiles.Clear();
        _imgToPdfFiles.Clear();
        _compressImgFiles.Clear();
        _compressOfficeFiles.Clear();

        // Clear single-file paths
        _splitPdfPath = null;
        _compressPdfPath = null;
        _pdfToImgPath = null;
        _rotatePdfPath = null;
        _watermarkPdfPath = null;
        _pageNumPdfPath = null;
        _cropSourcePath = null;
        _resizeSourcePath = null;
        _rotateFlipSourcePath = null;
        _convertSourcePath = null;

        // Reset crop
        ClearCropRect();
        CropPreviewImage.Source = null;
    }

    // =====================================================================
    //  Complete overlay handlers
    // =====================================================================

    private void Complete_OpenFile(object sender, RoutedEventArgs e)
    {
        if (_lastOutputPath != null)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_lastOutputPath) { UseShellExecute = true });
    }

    private void Complete_OpenFolder(object sender, RoutedEventArgs e)
    {
        if (_lastOutputDir != null)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", _lastOutputDir));
    }

    private void Complete_StartOver(object sender, MouseButtonEventArgs e)
    {
        FadeOut(CompleteOverlay, 200, () => GoBack_Click(sender, new RoutedEventArgs()));
    }

    // =====================================================================
    //  Navigation
    // =====================================================================

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        string id = (string)((Border)sender).Tag;
        _currentToolId = id;
        FadeOut(HomePanel, 150, () =>
        {
            BtnBack.Visibility = Visibility.Visible;
            TxtToolTitle.Text = _toolTitles.GetValueOrDefault(id, "File Tools");
            if (_toolPanels.TryGetValue(id, out var panel))
                FadeIn(panel);
        });
    }

    private void GoBack_Click(object sender, RoutedEventArgs e)
    {
        // Hide overlays immediately
        CompleteOverlay.Visibility = Visibility.Collapsed;
        CompleteOverlay.Opacity = 1;
        ProcessingOverlay.Visibility = Visibility.Collapsed;
        ProcessingOverlay.Opacity = 1;

        // Hide all tool panels
        foreach (var p in _toolPanels.Values)
            p.Visibility = Visibility.Collapsed;

        // Reset all tool states
        ResetToolStates();

        BtnBack.Visibility = Visibility.Collapsed;
        TxtToolTitle.Text = "File Tools";
        FadeIn(HomePanel);
        _currentToolId = null;
    }

    // =====================================================================
    //  Shared drag-over handler
    // =====================================================================

    private void Generic_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static bool IsImageFile(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return ImageExtensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsOfficeFile(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return OfficeExtensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPdfFile(string path)
    {
        return string.Equals(System.IO.Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] GetDroppedFiles(DragEventArgs e, Func<string, bool> filter)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return Array.Empty<string>();
        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        return files.Where(f => File.Exists(f) && filter(f)).ToArray();
    }

    private static Microsoft.Win32.OpenFileDialog CreateImageOpenDialog(bool multiSelect = true)
    {
        return new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif;*.webp|All Files|*.*",
            Multiselect = multiSelect
        };
    }

    private static Microsoft.Win32.OpenFileDialog CreatePdfOpenDialog(bool multiSelect = true)
    {
        return new Microsoft.Win32.OpenFileDialog
        {
            Filter = "PDF Files|*.pdf|All Files|*.*",
            Multiselect = multiSelect
        };
    }

    private static BitmapImage? LoadThumbnail(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelWidth = 40;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private void AddImagesToList(ObservableCollection<FileItem> list, string[] paths)
    {
        foreach (var path in paths)
        {
            if (list.Any(f => f.FilePath == path)) continue;
            var info = new FileInfo(path);
            list.Add(new FileItem
            {
                Index = list.Count + 1,
                FilePath = path,
                FileName = System.IO.Path.GetFileName(path),
                FileSize = FileToolsService.FormatFileSize(info.Length),
                Thumbnail = LoadThumbnail(path)
            });
        }
    }

    private void AddFilesToList(ObservableCollection<FileItem> list, string[] paths, string extra = "")
    {
        foreach (var path in paths)
        {
            if (list.Any(f => f.FilePath == path)) continue;
            var info = new FileInfo(path);
            list.Add(new FileItem
            {
                Index = list.Count + 1,
                FilePath = path,
                FileName = System.IO.Path.GetFileName(path),
                FileSize = FileToolsService.FormatFileSize(info.Length),
                Extra = extra
            });
        }
    }

    private void RemoveFromList(ObservableCollection<FileItem> list, object sender)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string path)
        {
            var item = list.FirstOrDefault(f => f.FilePath == path);
            if (item != null) list.Remove(item);
            RenumberList(list);
        }
    }

    private static void RenumberList(ObservableCollection<FileItem> list)
    {
        for (int i = 0; i < list.Count; i++)
            list[i].Index = i + 1;
    }

    // =====================================================================
    //  1. Merge PDF
    // =====================================================================

    private void MergePdf_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = CreatePdfOpenDialog(true);
        if (dlg.ShowDialog() != true) return;
        _ = AddPdfsToMergeList(dlg.FileNames);
        ShowConfigState("merge_pdf");
    }

    private void MergePdf_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsPdfFile);
        if (files.Length == 0) return;
        _ = AddPdfsToMergeList(files);
        ShowConfigState("merge_pdf");
    }

    private void MergePdf_AddMore(object sender, MouseButtonEventArgs e)
    {
        var dlg = CreatePdfOpenDialog(true);
        if (dlg.ShowDialog() == true)
            _ = AddPdfsToMergeList(dlg.FileNames);
    }

    private async Task AddPdfsToMergeList(string[] paths)
    {
        foreach (var path in paths)
        {
            if (_mergePdfFiles.Any(f => f.FilePath == path)) continue;
            var info = new FileInfo(path);
            string extra = "";
            try
            {
                int pages = await FileToolsService.GetPdfPageCountAsync(path);
                extra = $"{pages} page(s)";
            }
            catch { /* ignore */ }

            _mergePdfFiles.Add(new FileItem
            {
                Index = _mergePdfFiles.Count + 1,
                FilePath = path,
                FileName = System.IO.Path.GetFileName(path),
                FileSize = FileToolsService.FormatFileSize(info.Length),
                Extra = extra
            });
        }
    }

    private void MergePdf_Remove(object sender, RoutedEventArgs e) => RemoveFromList(_mergePdfFiles, sender);

    private async void MergePdf_Execute(object sender, RoutedEventArgs e)
    {
        if (_mergePdfFiles.Count < 2) return;

        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PDF|*.pdf", FileName = "merged.pdf" };
        if (dlg.ShowDialog() != true) return;

        ShowProcessing("Merging PDF files...");
        try
        {
            var paths = _mergePdfFiles.Select(f => f.FilePath).ToArray();
            await FileToolsService.MergePdfsAsync(paths, dlg.FileName, CreateProgress());
            var info = new FileInfo(dlg.FileName);
            ShowComplete("PDFs merged successfully!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    //  2. Split PDF
    // =====================================================================

    private async void SplitPdf_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = CreatePdfOpenDialog(false);
        if (dlg.ShowDialog() != true) return;
        await LoadSplitPdf(dlg.FileName);
    }

    private async void SplitPdf_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsPdfFile);
        if (files.Length == 0) return;
        await LoadSplitPdf(files[0]);
    }

    private async Task LoadSplitPdf(string path)
    {
        _splitPdfPath = path;
        TxtSplitFileName.Text = System.IO.Path.GetFileName(path);
        try
        {
            int pages = await FileToolsService.GetPdfPageCountAsync(path);
            TxtSplitInfo.Text = $"Total pages: {pages}";
            TxtSplitFrom.Text = "1";
            TxtSplitTo.Text = pages.ToString();
        }
        catch (Exception ex)
        {
            TxtSplitInfo.Text = $"Error: {ex.Message}";
        }
        ShowConfigState("split_pdf");
    }

    private async void SplitPdf_Execute(object sender, RoutedEventArgs e)
    {
        if (_splitPdfPath == null) return;
        if (!int.TryParse(TxtSplitFrom.Text, out int from) || !int.TryParse(TxtSplitTo.Text, out int to)
            || from < 1 || to < from)
        {
            System.Windows.MessageBox.Show("Enter a valid page range.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var folderDlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select output folder" };
        if (folderDlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        ShowProcessing("Splitting PDF...");
        try
        {
            var results = await FileToolsService.SplitPdfAsync(_splitPdfPath, folderDlg.SelectedPath, from, to, CreateProgress());
            ShowComplete($"Extracted {results.Length} page(s)!",
                $"Saved to {folderDlg.SelectedPath}",
                folderPath: folderDlg.SelectedPath);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    //  3. Compress PDF
    // =====================================================================

    private void CompressPdf_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = CreatePdfOpenDialog(false);
        if (dlg.ShowDialog() != true) return;
        LoadCompressPdf(dlg.FileName);
    }

    private void CompressPdf_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsPdfFile);
        if (files.Length == 0) return;
        LoadCompressPdf(files[0]);
    }

    private void LoadCompressPdf(string path)
    {
        _compressPdfPath = path;
        TxtCompressPdfFileName.Text = System.IO.Path.GetFileName(path);
        var info = new FileInfo(path);
        TxtCompressPdfInfo.Text = $"Size: {FileToolsService.FormatFileSize(info.Length)}";
        ShowConfigState("compress_pdf");
    }

    private void CompressPdf_QualityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtPdfQualityVal != null)
            TxtPdfQualityVal.Text = ((int)SldPdfQuality.Value).ToString();
    }

    private async void CompressPdf_Execute(object sender, RoutedEventArgs e)
    {
        if (_compressPdfPath == null) return;

        string name = System.IO.Path.GetFileNameWithoutExtension(_compressPdfPath);
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PDF|*.pdf", FileName = $"{name}_compressed.pdf" };
        if (dlg.ShowDialog() != true) return;

        int quality = (int)SldPdfQuality.Value;
        ShowProcessing("Compressing PDF...");
        try
        {
            long result = await FileToolsService.CompressFileAsync(_compressPdfPath, dlg.FileName, quality, 0, CreateProgress());
            long original = new FileInfo(_compressPdfPath).Length;
            long saved = original - result;
            double pct = original > 0 ? (saved * 100.0 / original) : 0;
            string detail = saved > 0
                ? $"{FileToolsService.FormatFileSize(original)} \u2192 {FileToolsService.FormatFileSize(result)} ({pct:F1}% saved)"
                : $"{FileToolsService.FormatFileSize(result)} (no reduction)";
            ShowComplete("PDF compressed successfully!", detail, dlg.FileName);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    //  4. PDF to Images
    // =====================================================================

    private async void PdfToImg_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = CreatePdfOpenDialog(false);
        if (dlg.ShowDialog() != true) return;
        await LoadPdfToImg(dlg.FileName);
    }

    private async void PdfToImg_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsPdfFile);
        if (files.Length == 0) return;
        await LoadPdfToImg(files[0]);
    }

    private async Task LoadPdfToImg(string path)
    {
        _pdfToImgPath = path;
        TxtPdfToImgFileName.Text = System.IO.Path.GetFileName(path);
        try
        {
            int pages = await FileToolsService.GetPdfPageCountAsync(path);
            TxtPdfToImgInfo.Text = $"Pages: {pages}";
        }
        catch
        {
            TxtPdfToImgInfo.Text = "";
        }
        ShowConfigState("pdf_to_images");
    }

    private async void PdfToImg_Execute(object sender, RoutedEventArgs e)
    {
        if (_pdfToImgPath == null) return;

        var folderDlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select output folder for images" };
        if (folderDlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var format = (CmbPdfImgFormat.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "PNG";
        var dpiText = (CmbPdfImgDpi.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "150";
        int.TryParse(dpiText, out int dpi);
        if (dpi == 0) dpi = 150;

        ShowProcessing("Converting PDF to images...");
        try
        {
            var results = await FileToolsService.PdfToImagesAsync(_pdfToImgPath, folderDlg.SelectedPath, format, dpi, CreateProgress());
            ShowComplete($"Converted {results.Length} pages!",
                $"Saved to {folderDlg.SelectedPath}",
                folderPath: folderDlg.SelectedPath);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    //  5. Images to PDF
    // =====================================================================

    private void ImgToPdf_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = CreateImageOpenDialog();
        if (dlg.ShowDialog() != true) return;
        AddImagesToList(_imgToPdfFiles, dlg.FileNames);
        ShowConfigState("images_to_pdf");
    }

    private void ImgToPdf_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsImageFile);
        if (files.Length == 0) return;
        AddImagesToList(_imgToPdfFiles, files);
        ShowConfigState("images_to_pdf");
    }

    private void ImgToPdf_AddMore(object sender, MouseButtonEventArgs e)
    {
        var dlg = CreateImageOpenDialog();
        if (dlg.ShowDialog() == true)
            AddImagesToList(_imgToPdfFiles, dlg.FileNames);
    }

    private void ImgToPdf_Remove(object sender, RoutedEventArgs e) => RemoveFromList(_imgToPdfFiles, sender);

    private async void ImgToPdf_Execute(object sender, RoutedEventArgs e)
    {
        if (_imgToPdfFiles.Count == 0) return;

        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PDF|*.pdf", FileName = "output.pdf" };
        if (dlg.ShowDialog() != true) return;

        ShowProcessing("Creating PDF from images...");
        try
        {
            var paths = _imgToPdfFiles.Select(f => f.FilePath).ToArray();
            await FileToolsService.ImagesToPdfAsync(paths, dlg.FileName, CreateProgress());
            var info = new FileInfo(dlg.FileName);
            ShowComplete("PDF created successfully!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    //  6. Rotate PDF
    // =====================================================================

    private async void RotatePdf_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = CreatePdfOpenDialog(false);
        if (dlg.ShowDialog() != true) return;
        await LoadRotatePdf(dlg.FileName);
    }

    private async void RotatePdf_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsPdfFile);
        if (files.Length == 0) return;
        await LoadRotatePdf(files[0]);
    }

    private async Task LoadRotatePdf(string path)
    {
        _rotatePdfPath = path;
        TxtRotatePdfFileName.Text = System.IO.Path.GetFileName(path);
        try
        {
            int pages = await FileToolsService.GetPdfPageCountAsync(path);
            TxtRotatePdfInfo.Text = $"Pages: {pages}";
        }
        catch
        {
            TxtRotatePdfInfo.Text = "";
        }
        ShowConfigState("rotate_pdf");
    }

    private async void RotatePdf_CW(object sender, RoutedEventArgs e) => await DoRotatePdf(90);
    private async void RotatePdf_CCW(object sender, RoutedEventArgs e) => await DoRotatePdf(270);
    private async void RotatePdf_180(object sender, RoutedEventArgs e) => await DoRotatePdf(180);

    private async Task DoRotatePdf(int degrees)
    {
        if (_rotatePdfPath == null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF|*.pdf",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_rotatePdfPath) + $"_rot{degrees}.pdf"
        };
        if (dlg.ShowDialog() != true) return;

        ShowProcessing($"Rotating PDF {degrees}\u00B0...");
        try
        {
            await FileToolsService.RotatePdfAsync(_rotatePdfPath, dlg.FileName, degrees, CreateProgress());
            var info = new FileInfo(dlg.FileName);
            ShowComplete("PDF rotated successfully!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    //  7. Watermark PDF
    // =====================================================================

    private void Watermark_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = CreatePdfOpenDialog(false);
        if (dlg.ShowDialog() != true) return;
        LoadWatermarkPdf(dlg.FileName);
    }

    private void Watermark_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsPdfFile);
        if (files.Length == 0) return;
        LoadWatermarkPdf(files[0]);
    }

    private void LoadWatermarkPdf(string path)
    {
        _watermarkPdfPath = path;
        TxtWatermarkFileName.Text = System.IO.Path.GetFileName(path);
        ShowConfigState("watermark");
    }

    private void Watermark_OpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtWatermarkOpacityVal != null)
            TxtWatermarkOpacityVal.Text = $"{(int)SldWatermarkOpacity.Value}%";
    }

    private void Watermark_FontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtWatermarkFontSizeVal != null)
            TxtWatermarkFontSizeVal.Text = ((int)SldWatermarkFontSize.Value).ToString();
    }

    private async void Watermark_Execute(object sender, RoutedEventArgs e)
    {
        if (_watermarkPdfPath == null) return;
        if (string.IsNullOrWhiteSpace(TxtWatermarkText.Text))
        {
            System.Windows.MessageBox.Show("Enter watermark text.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF|*.pdf",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_watermarkPdfPath) + "_watermarked.pdf"
        };
        if (dlg.ShowDialog() != true) return;

        double opacity = (int)SldWatermarkOpacity.Value / 100.0;
        int fontSize = (int)SldWatermarkFontSize.Value;

        ShowProcessing("Adding watermark...");
        try
        {
            await FileToolsService.WatermarkPdfAsync(_watermarkPdfPath, dlg.FileName, TxtWatermarkText.Text.Trim(), opacity, fontSize, CreateProgress());
            var info = new FileInfo(dlg.FileName);
            ShowComplete("Watermark added successfully!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    //  8. Page Numbers
    // =====================================================================

    private void PageNum_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = CreatePdfOpenDialog(false);
        if (dlg.ShowDialog() != true) return;
        LoadPageNumPdf(dlg.FileName);
    }

    private void PageNum_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsPdfFile);
        if (files.Length == 0) return;
        LoadPageNumPdf(files[0]);
    }

    private void LoadPageNumPdf(string path)
    {
        _pageNumPdfPath = path;
        TxtPageNumFileName.Text = System.IO.Path.GetFileName(path);
        ShowConfigState("page_numbers");
    }

    private async void PageNum_Execute(object sender, RoutedEventArgs e)
    {
        if (_pageNumPdfPath == null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF|*.pdf",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_pageNumPdfPath) + "_numbered.pdf"
        };
        if (dlg.ShowDialog() != true) return;

        string position = (CmbPageNumPos.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Bottom Center";

        ShowProcessing("Adding page numbers...");
        try
        {
            await FileToolsService.AddPageNumbersAsync(_pageNumPdfPath, dlg.FileName, position, CreateProgress());
            var info = new FileInfo(dlg.FileName);
            ShowComplete("Page numbers added!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    //  9. Compress Image
    // =====================================================================

    private void CompressImg_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = CreateImageOpenDialog();
        if (dlg.ShowDialog() != true) return;
        AddImagesToList(_compressImgFiles, dlg.FileNames);
        ShowConfigState("compress_image");
    }

    private void CompressImg_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsImageFile);
        if (files.Length == 0) return;
        AddImagesToList(_compressImgFiles, files);
        ShowConfigState("compress_image");
    }

    private void CompressImg_AddMore(object sender, MouseButtonEventArgs e)
    {
        var dlg = CreateImageOpenDialog();
        if (dlg.ShowDialog() == true)
            AddImagesToList(_compressImgFiles, dlg.FileNames);
    }

    private void CompressImg_Remove(object sender, RoutedEventArgs e) => RemoveFromList(_compressImgFiles, sender);

    private void CompressImg_QualityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtImgQualityVal != null)
            TxtImgQualityVal.Text = ((int)SldImgQuality.Value).ToString();
    }

    private async void CompressImg_Execute(object sender, RoutedEventArgs e)
    {
        if (_compressImgFiles.Count == 0) return;

        int quality = (int)SldImgQuality.Value;
        int maxDim = 0;
        if (!string.IsNullOrWhiteSpace(TxtImgMaxDim.Text))
            int.TryParse(TxtImgMaxDim.Text.Trim(), out maxDim);

        ShowProcessing("Compressing images...");

        long totalOriginal = 0, totalCompressed = 0;
        int count = _compressImgFiles.Count;

        try
        {
            for (int i = 0; i < count; i++)
            {
                var file = _compressImgFiles[i];
                var info = new FileInfo(file.FilePath);
                totalOriginal += info.Length;

                string dir = System.IO.Path.GetDirectoryName(file.FilePath)!;
                string name = System.IO.Path.GetFileNameWithoutExtension(file.FilePath);
                string ext = System.IO.Path.GetExtension(file.FilePath);
                string outputPath = System.IO.Path.Combine(dir, $"{name}_compressed{ext}");

                long compressedSize = await FileToolsService.CompressImageAsync(file.FilePath, outputPath, quality, maxDim);
                totalCompressed += compressedSize;

                MainProgress.Value = (int)((i + 1) * 100.0 / count);
                TxtProgressPct.Text = $"{(int)((i + 1) * 100.0 / count)}%";
            }

            long saved = totalOriginal - totalCompressed;
            double pct = totalOriginal > 0 ? (saved * 100.0 / totalOriginal) : 0;
            string detail = saved > 0
                ? $"{FileToolsService.FormatFileSize(totalOriginal)} \u2192 {FileToolsService.FormatFileSize(totalCompressed)} ({pct:F1}% saved)"
                : $"Compressed {count} image(s) \u2014 no size reduction";
            string firstDir = System.IO.Path.GetDirectoryName(_compressImgFiles[0].FilePath)!;
            ShowComplete($"{count} image(s) compressed!", detail, folderPath: firstDir);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    //  10. Resize Image
    // =====================================================================

    private void Resize_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = CreateImageOpenDialog(false);
        if (dlg.ShowDialog() != true) return;
        LoadResizeImage(dlg.FileName);
    }

    private void Resize_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsImageFile);
        if (files.Length == 0) return;
        LoadResizeImage(files[0]);
    }

    private void LoadResizeImage(string path)
    {
        _resizeSourcePath = path;
        TxtResizeFileName.Text = System.IO.Path.GetFileName(path);
        try
        {
            var (w, h, size, fmt) = FileToolsService.GetImageInfo(path);
            _resizeOrigW = w;
            _resizeOrigH = h;
            TxtResizeInfo.Text = $"Current: {w} x {h}  ({FileToolsService.FormatFileSize(size)}, {fmt})";
            _suppressAspectUpdate = true;
            TxtResizeW.Text = w.ToString();
            TxtResizeH.Text = h.ToString();
            _suppressAspectUpdate = false;
        }
        catch (Exception ex)
        {
            TxtResizeInfo.Text = $"Error: {ex.Message}";
        }
        ShowConfigState("resize_image");
    }

    private void ResizeW_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressAspectUpdate || ChkResizeAspect?.IsChecked != true) return;
        if (_resizeOrigW == 0 || _resizeOrigH == 0) return;
        if (!int.TryParse(TxtResizeW.Text, out int w) || w <= 0) return;

        _suppressAspectUpdate = true;
        int h = (int)Math.Round((double)w / _resizeOrigW * _resizeOrigH);
        TxtResizeH.Text = h.ToString();
        _suppressAspectUpdate = false;
    }

    private void ResizeH_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressAspectUpdate || ChkResizeAspect?.IsChecked != true) return;
        if (_resizeOrigW == 0 || _resizeOrigH == 0) return;
        if (!int.TryParse(TxtResizeH.Text, out int h) || h <= 0) return;

        _suppressAspectUpdate = true;
        int w = (int)Math.Round((double)h / _resizeOrigH * _resizeOrigW);
        TxtResizeW.Text = w.ToString();
        _suppressAspectUpdate = false;
    }

    private async void Resize_Execute(object sender, RoutedEventArgs e)
    {
        if (_resizeSourcePath == null) return;
        if (!int.TryParse(TxtResizeW.Text, out int w) || !int.TryParse(TxtResizeH.Text, out int h)
            || w <= 0 || h <= 0)
        {
            System.Windows.MessageBox.Show("Enter valid width and height.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_resizeSourcePath) + "_resized"
        };
        if (dlg.ShowDialog() != true) return;

        ShowProcessing("Resizing image...");
        try
        {
            await FileToolsService.ResizeImageAsync(_resizeSourcePath, dlg.FileName, w, h, ChkResizeAspect.IsChecked == true);
            var info = new FileInfo(dlg.FileName);
            ShowComplete("Image resized successfully!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    //  11. Crop Image
    // =====================================================================

    private void Crop_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = CreateImageOpenDialog(false);
        if (dlg.ShowDialog() != true) return;
        LoadCropImage(dlg.FileName);
        ShowConfigState("crop_image");
    }

    private void Crop_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsImageFile);
        if (files.Length == 0) return;
        LoadCropImage(files[0]);
        ShowConfigState("crop_image");
    }

    private void LoadCropImage(string path)
    {
        _cropSourcePath = path;
        TxtCropFileName.Text = System.IO.Path.GetFileName(path);
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();

            CropPreviewImage.Source = bmp;
            _cropImgWidth = bmp.PixelWidth;
            _cropImgHeight = bmp.PixelHeight;
            ClearCropRect();
            TxtCropInfo.Text = $"Image: {_cropImgWidth} x {_cropImgHeight}  |  X: 0  Y: 0  W: 0  H: 0";
        }
        catch { /* ignore */ }
    }

    private void ClearCropRect()
    {
        CropCanvas.Children.Clear();
        _cropRect = null;
        _isDraggingCrop = false;
        for (int i = 0; i < _cropOverlays.Length; i++)
            _cropOverlays[i] = null!;
    }

    private void CropCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (CropPreviewImage.Source == null) return;
        ClearCropRect();

        _isDraggingCrop = true;
        _cropStart = e.GetPosition(CropCanvas);
        _cropEnd = _cropStart;

        _cropRect = new Rectangle
        {
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#42A5F5")),
            StrokeThickness = 2,
            Fill = Brushes.Transparent
        };
        CropCanvas.Children.Add(_cropRect);

        for (int i = 0; i < 4; i++)
        {
            _cropOverlays[i] = new Rectangle
            {
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x88, 0, 0, 0))
            };
            CropCanvas.Children.Add(_cropOverlays[i]);
        }

        CropCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void CropCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDraggingCrop || _cropRect == null) return;
        _cropEnd = e.GetPosition(CropCanvas);
        UpdateCropRect();
    }

    private void CropCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingCrop) return;
        _isDraggingCrop = false;
        CropCanvas.ReleaseMouseCapture();
    }

    private void UpdateCropRect()
    {
        if (_cropRect == null) return;

        double canvasW = CropCanvas.ActualWidth;
        double canvasH = CropCanvas.ActualHeight;

        double x = Math.Max(0, Math.Min(_cropStart.X, _cropEnd.X));
        double y = Math.Max(0, Math.Min(_cropStart.Y, _cropEnd.Y));
        double w = Math.Min(Math.Abs(_cropEnd.X - _cropStart.X), canvasW - x);
        double h = Math.Min(Math.Abs(_cropEnd.Y - _cropStart.Y), canvasH - y);

        if (x + w > canvasW) w = canvasW - x;
        if (y + h > canvasH) h = canvasH - y;

        Canvas.SetLeft(_cropRect, x);
        Canvas.SetTop(_cropRect, y);
        _cropRect.Width = w;
        _cropRect.Height = h;

        // Top overlay
        Canvas.SetLeft(_cropOverlays[0], 0);
        Canvas.SetTop(_cropOverlays[0], 0);
        _cropOverlays[0].Width = canvasW;
        _cropOverlays[0].Height = y;

        // Bottom overlay
        Canvas.SetLeft(_cropOverlays[1], 0);
        Canvas.SetTop(_cropOverlays[1], y + h);
        _cropOverlays[1].Width = canvasW;
        _cropOverlays[1].Height = Math.Max(0, canvasH - y - h);

        // Left overlay
        Canvas.SetLeft(_cropOverlays[2], 0);
        Canvas.SetTop(_cropOverlays[2], y);
        _cropOverlays[2].Width = x;
        _cropOverlays[2].Height = h;

        // Right overlay
        Canvas.SetLeft(_cropOverlays[3], x + w);
        Canvas.SetTop(_cropOverlays[3], y);
        _cropOverlays[3].Width = Math.Max(0, canvasW - x - w);
        _cropOverlays[3].Height = h;

        var (scaleX, scaleY, offsetX, offsetY) = GetImageScale();
        int imgX = Math.Max(0, (int)Math.Round((x - offsetX) * scaleX));
        int imgY = Math.Max(0, (int)Math.Round((y - offsetY) * scaleY));
        int imgW = (int)Math.Round(w * scaleX);
        int imgH = (int)Math.Round(h * scaleY);
        imgW = Math.Min(imgW, _cropImgWidth - imgX);
        imgH = Math.Min(imgH, _cropImgHeight - imgY);

        TxtCropInfo.Text = $"X: {imgX}  Y: {imgY}  W: {imgW}  H: {imgH}";
    }

    private (double scaleX, double scaleY, double offsetX, double offsetY) GetImageScale()
    {
        if (CropPreviewImage.Source == null || _cropImgWidth == 0 || _cropImgHeight == 0)
            return (1, 1, 0, 0);

        double canvasW = CropCanvas.ActualWidth;
        double canvasH = CropCanvas.ActualHeight;

        double imageAspect = (double)_cropImgWidth / _cropImgHeight;
        double canvasAspect = canvasW / canvasH;

        double displayW, displayH, offsetX, offsetY;
        if (imageAspect > canvasAspect)
        {
            displayW = canvasW;
            displayH = canvasW / imageAspect;
            offsetX = 0;
            offsetY = (canvasH - displayH) / 2;
        }
        else
        {
            displayH = canvasH;
            displayW = canvasH * imageAspect;
            offsetX = (canvasW - displayW) / 2;
            offsetY = 0;
        }

        double scaleX = (double)_cropImgWidth / displayW;
        double scaleY = (double)_cropImgHeight / displayH;
        return (scaleX, scaleY, offsetX, offsetY);
    }

    private async void Crop_Execute(object sender, RoutedEventArgs e)
    {
        if (_cropSourcePath == null || _cropRect == null)
        {
            System.Windows.MessageBox.Show("Load an image and draw a crop area first.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        double x = Canvas.GetLeft(_cropRect);
        double y = Canvas.GetTop(_cropRect);
        double w = _cropRect.Width;
        double h = _cropRect.Height;
        if (w < 1 || h < 1)
        {
            System.Windows.MessageBox.Show("Draw a crop area on the image.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (scaleX, scaleY, offsetX, offsetY) = GetImageScale();
        int imgX = Math.Max(0, (int)Math.Round((x - offsetX) * scaleX));
        int imgY = Math.Max(0, (int)Math.Round((y - offsetY) * scaleY));
        int imgW = (int)Math.Round(w * scaleX);
        int imgH = (int)Math.Round(h * scaleY);
        imgW = Math.Min(imgW, _cropImgWidth - imgX);
        imgH = Math.Min(imgH, _cropImgHeight - imgY);

        if (imgW < 1 || imgH < 1)
        {
            System.Windows.MessageBox.Show("Invalid crop area.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_cropSourcePath) + "_cropped"
        };
        if (dlg.ShowDialog() != true) return;

        ShowProcessing("Cropping image...");
        try
        {
            var rect = new Int32Rect(imgX, imgY, imgW, imgH);
            await FileToolsService.CropImageAsync(_cropSourcePath, dlg.FileName, rect);
            var info = new FileInfo(dlg.FileName);
            ShowComplete("Image cropped successfully!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    //  12. Rotate & Flip (Image)
    // =====================================================================

    private void RotateFlip_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = CreateImageOpenDialog(false);
        if (dlg.ShowDialog() != true) return;
        LoadRotateFlipImage(dlg.FileName);
    }

    private void RotateFlip_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsImageFile);
        if (files.Length == 0) return;
        LoadRotateFlipImage(files[0]);
    }

    private void LoadRotateFlipImage(string path)
    {
        _rotateFlipSourcePath = path;
        TxtRotateFlipFileName.Text = System.IO.Path.GetFileName(path);
        ShowConfigState("rotate_flip");
    }

    private async void RotateFlip_CW(object sender, RoutedEventArgs e) => await DoRotateImage(90);
    private async void RotateFlip_CCW(object sender, RoutedEventArgs e) => await DoRotateImage(270);
    private async void RotateFlip_180(object sender, RoutedEventArgs e) => await DoRotateImage(180);

    private async Task DoRotateImage(int degrees)
    {
        if (_rotateFlipSourcePath == null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_rotateFlipSourcePath) + $"_rot{degrees}"
        };
        if (dlg.ShowDialog() != true) return;

        ShowProcessing($"Rotating {degrees}\u00B0...");
        try
        {
            await FileToolsService.RotateImageAsync(_rotateFlipSourcePath, dlg.FileName, degrees);
            var info = new FileInfo(dlg.FileName);
            ShowComplete("Image rotated successfully!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RotateFlip_FlipH(object sender, RoutedEventArgs e) => await DoFlipImage(true);
    private async void RotateFlip_FlipV(object sender, RoutedEventArgs e) => await DoFlipImage(false);

    private async Task DoFlipImage(bool horizontal)
    {
        if (_rotateFlipSourcePath == null) return;

        string suffix = horizontal ? "_flipH" : "_flipV";
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_rotateFlipSourcePath) + suffix
        };
        if (dlg.ShowDialog() != true) return;

        ShowProcessing(horizontal ? "Flipping horizontally..." : "Flipping vertically...");
        try
        {
            await FileToolsService.FlipImageAsync(_rotateFlipSourcePath, dlg.FileName, horizontal);
            var info = new FileInfo(dlg.FileName);
            ShowComplete("Image flipped successfully!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    //  13. Convert Format
    // =====================================================================

    private void Convert_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = CreateImageOpenDialog(false);
        if (dlg.ShowDialog() != true) return;
        LoadConvertImage(dlg.FileName);
    }

    private void Convert_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsImageFile);
        if (files.Length == 0) return;
        LoadConvertImage(files[0]);
    }

    private void LoadConvertImage(string path)
    {
        _convertSourcePath = path;
        TxtConvertFileName.Text = System.IO.Path.GetFileName(path);
        try
        {
            var (w, h, size, fmt) = FileToolsService.GetImageInfo(path);
            TxtConvertInfo.Text = $"Current: {w} x {h}  ({FileToolsService.FormatFileSize(size)}, {fmt})";
        }
        catch
        {
            TxtConvertInfo.Text = "";
        }
        ShowConfigState("convert_format");
    }

    private void Convert_FormatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConvertQualityPanel == null) return;
        var selected = (CmbConvertFormat.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        ConvertQualityPanel.Visibility = selected == "JPG" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Convert_Execute(object sender, RoutedEventArgs e)
    {
        if (_convertSourcePath == null) return;

        var targetFormat = (CmbConvertFormat.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "PNG";
        string ext = targetFormat.ToLowerInvariant() switch
        {
            "jpg" => ".jpg",
            "bmp" => ".bmp",
            "gif" => ".gif",
            "tiff" => ".tiff",
            _ => ".png"
        };

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = $"{targetFormat}|*{ext}",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_convertSourcePath) + ext
        };
        if (dlg.ShowDialog() != true) return;

        int jpegQuality = targetFormat == "JPG" ? (int)SldConvertQuality.Value : 90;

        ShowProcessing($"Converting to {targetFormat}...");
        try
        {
            await FileToolsService.ConvertImageFormatAsync(_convertSourcePath, dlg.FileName, jpegQuality);
            var info = new FileInfo(dlg.FileName);
            ShowComplete("Image converted successfully!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    //  14. Compress Office
    // =====================================================================

    private void CompressOffice_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Office Files|*.docx;*.xlsx;*.pptx;*.odt;*.ods;*.odp|All Files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        AddFilesToList(_compressOfficeFiles, dlg.FileNames);
        ShowConfigState("compress_office");
    }

    private void CompressOffice_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsOfficeFile);
        if (files.Length == 0) return;
        AddFilesToList(_compressOfficeFiles, files);
        ShowConfigState("compress_office");
    }

    private void CompressOffice_AddMore(object sender, MouseButtonEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Office Files|*.docx;*.xlsx;*.pptx;*.odt;*.ods;*.odp|All Files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() == true)
            AddFilesToList(_compressOfficeFiles, dlg.FileNames);
    }

    private void CompressOffice_Remove(object sender, RoutedEventArgs e) => RemoveFromList(_compressOfficeFiles, sender);

    private void CompressOffice_QualityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtOfficeQualityVal != null)
            TxtOfficeQualityVal.Text = ((int)SldOfficeQuality.Value).ToString();
    }

    private async void CompressOffice_Execute(object sender, RoutedEventArgs e)
    {
        if (_compressOfficeFiles.Count == 0) return;

        int quality = (int)SldOfficeQuality.Value;
        ShowProcessing("Compressing office files...");

        long totalOriginal = 0, totalCompressed = 0;
        int count = _compressOfficeFiles.Count;

        try
        {
            for (int i = 0; i < count; i++)
            {
                var file = _compressOfficeFiles[i];
                var info = new FileInfo(file.FilePath);
                totalOriginal += info.Length;

                string dir = System.IO.Path.GetDirectoryName(file.FilePath)!;
                string name = System.IO.Path.GetFileNameWithoutExtension(file.FilePath);
                string ext = System.IO.Path.GetExtension(file.FilePath);
                string outputPath = System.IO.Path.Combine(dir, $"{name}_compressed{ext}");

                var fileProgress = new Progress<int>(v =>
                {
                    int overall = (int)((i * 100.0 + v) / count);
                    MainProgress.Value = overall;
                    TxtProgressPct.Text = $"{overall}%";
                });

                long compressedSize = await FileToolsService.CompressFileAsync(
                    file.FilePath, outputPath, quality, 0, fileProgress);
                totalCompressed += compressedSize;

                int pctDone = (int)((i + 1) * 100.0 / count);
                MainProgress.Value = pctDone;
                TxtProgressPct.Text = $"{pctDone}%";
            }

            long saved = totalOriginal - totalCompressed;
            double pct = totalOriginal > 0 ? (saved * 100.0 / totalOriginal) : 0;
            string detail = saved > 0
                ? $"{FileToolsService.FormatFileSize(totalOriginal)} \u2192 {FileToolsService.FormatFileSize(totalCompressed)} ({pct:F1}% saved)"
                : $"Compressed {count} file(s) \u2014 no size reduction";
            string firstDir = System.IO.Path.GetDirectoryName(_compressOfficeFiles[0].FilePath)!;
            ShowComplete($"{count} file(s) compressed!", detail, folderPath: firstDir);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

// =========================================================================
//  Data item for file lists
// =========================================================================

public class FileItem : INotifyPropertyChanged
{
    private int _index;
    public int Index
    {
        get => _index;
        set { _index = value; OnPropertyChanged(); }
    }

    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FileSize { get; set; } = "";
    public string Extra { get; set; } = "";
    public BitmapImage? Thumbnail { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
