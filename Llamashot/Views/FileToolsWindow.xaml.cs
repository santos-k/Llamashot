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
        ("extract_pages",  "Extract Pages",   "Pick specific pages from PDF",       "#FF8A65"),
        ("insert_pages",   "Insert Pages",    "Add new pages to a PDF",             "#A1887F"),
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
    private readonly ObservableCollection<FileItem> _insertImages = new();

    // =====================================================================
    //  Single-file tool paths
    // =====================================================================

    private string? _splitPdfPath;
    private string? _compressPdfPath;
    private string? _pdfToImgPath;
    private string? _rotatePdfPath;
    private string? _watermarkPdfPath;
    private string? _pageNumPdfPath;
    private string? _extractPdfPath;
    private string? _insertBasePath;
    private double _rpAngle = 0;

    // =====================================================================
    //  Crop state
    // =====================================================================

    private string? _cropSourcePath;
    private int _cropImgWidth, _cropImgHeight;
    private bool _isDraggingCrop;
    private Point _cropDragStart;
    private Rect _cropSelectionRect;
    private CropDragMode _cropMode = CropDragMode.None;
    private Rectangle? _cropRect;
    private readonly Rectangle[] _cropOverlays = new Rectangle[4];
    private readonly Border[] _cropHandles = new Border[8]; // TL, TC, TR, ML, MR, BL, BC, BR
    private double _cropAspectRatio;

    private enum CropDragMode { None, Create, Move, ResizeTL, ResizeTC, ResizeTR, ResizeML, ResizeMR, ResizeBL, ResizeBC, ResizeBR }

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
    private double _rfAngle = 0;
    private bool _rfFlipH = false, _rfFlipV = false;

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
        InsertImageList.ItemsSource = _insertImages;
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
        _toolPanels["extract_pages"] = PanelExtractPages;
        _toolPanels["insert_pages"] = PanelInsertPages;
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
        _selectViews["extract_pages"] = ExtractSelectView;
        _selectViews["insert_pages"] = InsertSelectView;
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
        _configViews["extract_pages"] = ExtractConfigView;
        _configViews["insert_pages"] = InsertConfigView;
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
        _extractPdfPath = null;
        _insertBasePath = null;
        _insertImages.Clear();

        // Reset rotate PDF preview
        _rpAngle = 0;
        RotatePdfPreview.Source = null;
        RpRotateTransform.Angle = 0;

        // Reset crop
        ClearCropVisuals();
        _cropSelectionRect = Rect.Empty;
        CropPreviewImage.Source = null;

        // Reset resize/convert previews
        ResizePreviewImage.Source = null;
        ConvertPreviewImage.Source = null;

        // Reset rotate/flip
        RotateFlipPreview.Source = null;
        _rfAngle = 0; _rfFlipH = false; _rfFlipV = false;
        RfRotateTransform.Angle = 0;
        RfFlipTransform.ScaleX = 1; RfFlipTransform.ScaleY = 1;
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

    private void PreviewFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string path && File.Exists(path))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

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
    //  6. Rotate PDF (live preview)
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
        await LoadRotatePdfPreview(path);
        _rpAngle = 0;
        RpRotateTransform.Angle = 0;
        ShowConfigState("rotate_pdf");
    }

    private async Task LoadRotatePdfPreview(string pdfPath)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            using var page = pdfDoc.GetPage(0);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var options = new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = 400 };
            await page.RenderToStreamAsync(stream, options);
            stream.Seek(0);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = stream.AsStreamForRead();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            RotatePdfPreview.Source = bmp;
        }
        catch { }
    }

    private void RotatePdf_CW(object sender, RoutedEventArgs e)
    {
        _rpAngle = (_rpAngle + 90) % 360;
        RpRotateTransform.Angle = _rpAngle;
    }

    private void RotatePdf_CCW(object sender, RoutedEventArgs e)
    {
        _rpAngle = (_rpAngle + 270) % 360;
        RpRotateTransform.Angle = _rpAngle;
    }

    private void RotatePdf_180(object sender, RoutedEventArgs e)
    {
        _rpAngle = (_rpAngle + 180) % 360;
        RpRotateTransform.Angle = _rpAngle;
    }

    private async void RotatePdf_Execute(object sender, RoutedEventArgs e)
    {
        if (_rotatePdfPath == null || _rpAngle == 0) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF|*.pdf",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_rotatePdfPath) + "_rotated.pdf"
        };
        if (dlg.ShowDialog() != true) return;

        ShowProcessing("Rotating PDF...");
        try
        {
            await FileToolsService.RotatePdfAsync(_rotatePdfPath, dlg.FileName, (int)_rpAngle, CreateProgress());
            var info = new FileInfo(dlg.FileName);
            ShowComplete("PDF rotated!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            ProcessingOverlay.Visibility = Visibility.Collapsed;
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
    //  9. Extract Pages
    // =====================================================================

    private async void Extract_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "PDF Files|*.pdf" };
        if (dlg.ShowDialog() != true) return;
        await LoadExtractPdf(dlg.FileName);
    }

    private async void Extract_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsPdfFile);
        if (files.Length > 0) await LoadExtractPdf(files[0]);
    }

    private async Task LoadExtractPdf(string path)
    {
        _extractPdfPath = path;
        TxtExtractFileName.Text = System.IO.Path.GetFileName(path);
        try
        {
            int pages = await FileToolsService.GetPdfPageCountAsync(path);
            TxtExtractInfo.Text = $"Pages: {pages}";
            TxtExtractPages.Text = "";
        }
        catch { TxtExtractInfo.Text = ""; }
        ShowConfigState("extract_pages");
    }

    private async void Extract_Execute(object sender, RoutedEventArgs e)
    {
        if (_extractPdfPath == null || string.IsNullOrWhiteSpace(TxtExtractPages.Text)) return;

        var pageNumbers = ParsePageNumbers(TxtExtractPages.Text.Trim());
        if (pageNumbers.Length == 0)
        {
            System.Windows.MessageBox.Show("Enter valid page numbers.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF|*.pdf",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_extractPdfPath) + "_extracted.pdf"
        };
        if (dlg.ShowDialog() != true) return;

        ShowProcessing("Extracting pages...");
        try
        {
            await FileToolsService.ExtractPdfPagesAsync(_extractPdfPath, pageNumbers, dlg.FileName, CreateProgress());
            var info = new FileInfo(dlg.FileName);
            ShowComplete("Pages extracted!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {pageNumbers.Length} pages \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            ProcessingOverlay.Visibility = Visibility.Collapsed;
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static int[] ParsePageNumbers(string input)
    {
        var result = new List<int>();
        foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Contains('-'))
            {
                var range = part.Split('-');
                if (range.Length == 2 && int.TryParse(range[0].Trim(), out int from) && int.TryParse(range[1].Trim(), out int to))
                    for (int i = from; i <= to; i++) result.Add(i);
            }
            else if (int.TryParse(part.Trim(), out int num))
                result.Add(num);
        }
        return result.Distinct().OrderBy(x => x).ToArray();
    }

    // =====================================================================
    //  10. Insert Pages
    // =====================================================================

    private async void Insert_SelectBase(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "PDF Files|*.pdf" };
        if (dlg.ShowDialog() != true) return;
        _insertBasePath = dlg.FileName;
        TxtInsertBaseName.Text = System.IO.Path.GetFileName(dlg.FileName);
        try
        {
            int pages = await FileToolsService.GetPdfPageCountAsync(dlg.FileName);
            TxtInsertBaseInfo.Text = $"Pages: {pages}";
            TxtInsertAfterPage.Text = pages.ToString();
        }
        catch { TxtInsertBaseInfo.Text = ""; }
        ShowConfigState("insert_pages");
    }

    private void Insert_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsPdfFile);
        if (files.Length > 0)
        {
            _insertBasePath = files[0];
            TxtInsertBaseName.Text = System.IO.Path.GetFileName(files[0]);
            _ = LoadInsertBaseInfoAsync(files[0]);
            ShowConfigState("insert_pages");
        }
    }

    private async Task LoadInsertBaseInfoAsync(string path)
    {
        try
        {
            int pages = await FileToolsService.GetPdfPageCountAsync(path);
            TxtInsertBaseInfo.Text = $"Pages: {pages}";
            TxtInsertAfterPage.Text = pages.ToString();
        }
        catch { TxtInsertBaseInfo.Text = ""; }
    }

    private void Insert_BrowseImages(object sender, MouseButtonEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp",
            Multiselect = true
        };
        if (dlg.ShowDialog() == true)
            AddFilesToList(_insertImages, dlg.FileNames);
    }

    private void Insert_ImageDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsImageFile);
        if (files.Length > 0) AddFilesToList(_insertImages, files);
    }

    private void Insert_RemoveImage(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string path)
        {
            var item = _insertImages.FirstOrDefault(f => f.FilePath == path);
            if (item != null) _insertImages.Remove(item);
            RenumberList(_insertImages);
        }
    }

    private async void Insert_Execute(object sender, RoutedEventArgs e)
    {
        if (_insertBasePath == null || _insertImages.Count == 0) return;
        if (!int.TryParse(TxtInsertAfterPage.Text, out int afterPage))
        {
            System.Windows.MessageBox.Show("Enter a valid page number.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF|*.pdf",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_insertBasePath) + "_with_inserts.pdf"
        };
        if (dlg.ShowDialog() != true) return;

        ShowProcessing("Inserting pages...");
        try
        {
            var imagePaths = _insertImages.Select(f => f.FilePath).ToArray();
            await FileToolsService.InsertPdfPagesAsync(_insertBasePath, imagePaths, afterPage, dlg.FileName, CreateProgress());
            var info = new FileInfo(dlg.FileName);
            ShowComplete("Pages inserted!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            ProcessingOverlay.Visibility = Visibility.Collapsed;
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    //  11. Compress Image
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
    //  12. Resize Image
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

            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.UriSource = new Uri(path); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit(); bmp.Freeze();
            ResizePreviewImage.Source = bmp;
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
    //  13. Crop Image
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
            TxtCropImgSize.Text = $"{_cropImgWidth} x {_cropImgHeight}";
            CreateCropHandles();
            TxtCropInfo.Text = "Draw a crop area on the image";
        }
        catch { /* ignore */ }
    }

    private void CreateCropHandles()
    {
        CropCanvas.Children.Clear();

        // Create 4 overlay rectangles
        for (int i = 0; i < 4; i++)
        {
            _cropOverlays[i] = new Rectangle { Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xAA, 0, 0, 0)) };
            CropCanvas.Children.Add(_cropOverlays[i]);
        }

        // Create crop rectangle
        _cropRect = new Rectangle
        {
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#42A5F5")),
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            StrokeDashArray = new DoubleCollection { 4, 2 }
        };
        CropCanvas.Children.Add(_cropRect);

        // Create 8 handles (small white squares with blue border)
        var cursors = new[] { Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE, Cursors.SizeWE, Cursors.SizeNESW, Cursors.SizeNS, Cursors.SizeNWSE };
        var modes = new[] { CropDragMode.ResizeTL, CropDragMode.ResizeTC, CropDragMode.ResizeTR, CropDragMode.ResizeML, CropDragMode.ResizeMR, CropDragMode.ResizeBL, CropDragMode.ResizeBC, CropDragMode.ResizeBR };

        for (int i = 0; i < 8; i++)
        {
            var handle = new Border
            {
                Width = 10, Height = 10,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#42A5F5")),
                BorderThickness = new Thickness(1),
                Cursor = cursors[i],
                Tag = modes[i],
                Visibility = Visibility.Collapsed
            };
            handle.MouseLeftButtonDown += CropHandle_MouseDown;
            CropCanvas.Children.Add(handle);
            _cropHandles[i] = handle;
        }

        // Initialize with empty selection
        _cropSelectionRect = Rect.Empty;
    }

    private void ClearCropVisuals()
    {
        CropCanvas.Children.Clear();
        _cropRect = null;
        _isDraggingCrop = false;
        _cropMode = CropDragMode.None;
        for (int i = 0; i < _cropOverlays.Length; i++)
            _cropOverlays[i] = null!;
        for (int i = 0; i < _cropHandles.Length; i++)
            _cropHandles[i] = null!;
    }

    private void CropCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (CropPreviewImage.Source == null) return;
        var pos = e.GetPosition(CropCanvas);

        // Check if clicking inside existing crop rect (move mode)
        if (!_cropSelectionRect.IsEmpty && _cropSelectionRect.Contains(pos))
        {
            _cropMode = CropDragMode.Move;
            _cropDragStart = pos;
            _isDraggingCrop = true;
            CropCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        // Otherwise start new crop
        _cropMode = CropDragMode.Create;
        _cropDragStart = pos;
        _cropSelectionRect = new Rect(pos, new System.Windows.Size(0, 0));
        _isDraggingCrop = true;
        CropCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void CropHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border handle && handle.Tag is CropDragMode mode)
        {
            _cropMode = mode;
            _cropDragStart = e.GetPosition(CropCanvas);
            _isDraggingCrop = true;
            CropCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void CropCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDraggingCrop) return;
        var pos = e.GetPosition(CropCanvas);
        double canvasW = CropCanvas.ActualWidth;
        double canvasH = CropCanvas.ActualHeight;

        // Clamp to canvas
        pos = new Point(Math.Max(0, Math.Min(pos.X, canvasW)), Math.Max(0, Math.Min(pos.Y, canvasH)));

        var dx = pos.X - _cropDragStart.X;
        var dy = pos.Y - _cropDragStart.Y;

        switch (_cropMode)
        {
            case CropDragMode.Create:
                double x = Math.Min(_cropDragStart.X, pos.X);
                double y = Math.Min(_cropDragStart.Y, pos.Y);
                double w = Math.Abs(pos.X - _cropDragStart.X);
                double h = Math.Abs(pos.Y - _cropDragStart.Y);
                _cropSelectionRect = new Rect(x, y, w, h);
                break;

            case CropDragMode.Move:
                var moved = _cropSelectionRect;
                moved.Offset(dx, dy);
                if (moved.Left < 0) moved.X = 0;
                if (moved.Top < 0) moved.Y = 0;
                if (moved.Right > canvasW) moved.X = canvasW - moved.Width;
                if (moved.Bottom > canvasH) moved.Y = canvasH - moved.Height;
                _cropSelectionRect = moved;
                _cropDragStart = pos;
                break;

            case CropDragMode.ResizeTL:
                _cropSelectionRect = new Rect(pos.X, pos.Y, _cropSelectionRect.Right - pos.X, _cropSelectionRect.Bottom - pos.Y);
                break;
            case CropDragMode.ResizeTR:
                _cropSelectionRect = new Rect(_cropSelectionRect.Left, pos.Y, pos.X - _cropSelectionRect.Left, _cropSelectionRect.Bottom - pos.Y);
                break;
            case CropDragMode.ResizeBL:
                _cropSelectionRect = new Rect(pos.X, _cropSelectionRect.Top, _cropSelectionRect.Right - pos.X, pos.Y - _cropSelectionRect.Top);
                break;
            case CropDragMode.ResizeBR:
                _cropSelectionRect = new Rect(_cropSelectionRect.Left, _cropSelectionRect.Top, pos.X - _cropSelectionRect.Left, pos.Y - _cropSelectionRect.Top);
                break;
            case CropDragMode.ResizeTC:
                _cropSelectionRect = new Rect(_cropSelectionRect.Left, pos.Y, _cropSelectionRect.Width, _cropSelectionRect.Bottom - pos.Y);
                break;
            case CropDragMode.ResizeBC:
                _cropSelectionRect = new Rect(_cropSelectionRect.Left, _cropSelectionRect.Top, _cropSelectionRect.Width, pos.Y - _cropSelectionRect.Top);
                break;
            case CropDragMode.ResizeML:
                _cropSelectionRect = new Rect(pos.X, _cropSelectionRect.Top, _cropSelectionRect.Right - pos.X, _cropSelectionRect.Height);
                break;
            case CropDragMode.ResizeMR:
                _cropSelectionRect = new Rect(_cropSelectionRect.Left, _cropSelectionRect.Top, pos.X - _cropSelectionRect.Left, _cropSelectionRect.Height);
                break;
        }

        // Normalize (ensure positive width/height)
        if (_cropSelectionRect.Width < 0 || _cropSelectionRect.Height < 0)
            _cropSelectionRect = new Rect(
                Math.Min(_cropSelectionRect.Left, _cropSelectionRect.Right),
                Math.Min(_cropSelectionRect.Top, _cropSelectionRect.Bottom),
                Math.Abs(_cropSelectionRect.Width),
                Math.Abs(_cropSelectionRect.Height));

        UpdateCropVisuals();
    }

    private void CropCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingCrop = false;
        _cropMode = CropDragMode.None;
        CropCanvas.ReleaseMouseCapture();
    }

    private void UpdateCropVisuals()
    {
        if (_cropRect == null || _cropSelectionRect.IsEmpty) return;
        var r = _cropSelectionRect;
        double canvasW = CropCanvas.ActualWidth;
        double canvasH = CropCanvas.ActualHeight;

        // Position crop rectangle
        Canvas.SetLeft(_cropRect, r.X);
        Canvas.SetTop(_cropRect, r.Y);
        _cropRect.Width = Math.Max(0, r.Width);
        _cropRect.Height = Math.Max(0, r.Height);

        // Overlays (darkening)
        Canvas.SetLeft(_cropOverlays[0], 0); Canvas.SetTop(_cropOverlays[0], 0); // Top
        _cropOverlays[0].Width = canvasW; _cropOverlays[0].Height = Math.Max(0, r.Y);

        Canvas.SetLeft(_cropOverlays[1], 0); Canvas.SetTop(_cropOverlays[1], r.Bottom); // Bottom
        _cropOverlays[1].Width = canvasW; _cropOverlays[1].Height = Math.Max(0, canvasH - r.Bottom);

        Canvas.SetLeft(_cropOverlays[2], 0); Canvas.SetTop(_cropOverlays[2], r.Y); // Left
        _cropOverlays[2].Width = Math.Max(0, r.X); _cropOverlays[2].Height = Math.Max(0, r.Height);

        Canvas.SetLeft(_cropOverlays[3], r.Right); Canvas.SetTop(_cropOverlays[3], r.Y); // Right
        _cropOverlays[3].Width = Math.Max(0, canvasW - r.Right); _cropOverlays[3].Height = Math.Max(0, r.Height);

        // Position 8 handles (TL, TC, TR, ML, MR, BL, BC, BR)
        double hs = 5; // half handle size
        var positions = new Point[]
        {
            new(r.X - hs, r.Y - hs),                           // TL
            new(r.X + r.Width / 2 - hs, r.Y - hs),            // TC
            new(r.Right - hs, r.Y - hs),                       // TR
            new(r.X - hs, r.Y + r.Height / 2 - hs),           // ML
            new(r.Right - hs, r.Y + r.Height / 2 - hs),       // MR
            new(r.X - hs, r.Bottom - hs),                      // BL
            new(r.X + r.Width / 2 - hs, r.Bottom - hs),       // BC
            new(r.Right - hs, r.Bottom - hs),                  // BR
        };

        for (int i = 0; i < 8; i++)
        {
            if (_cropHandles[i] == null) continue;
            Canvas.SetLeft(_cropHandles[i], positions[i].X);
            Canvas.SetTop(_cropHandles[i], positions[i].Y);
            _cropHandles[i].Visibility = r.Width > 10 && r.Height > 10 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Update info text with image-space coordinates
        var (scaleX, scaleY, offsetX, offsetY) = GetImageScale();
        int imgX = Math.Max(0, (int)Math.Round((r.X - offsetX) * scaleX));
        int imgY = Math.Max(0, (int)Math.Round((r.Y - offsetY) * scaleY));
        int imgW = Math.Min((int)Math.Round(r.Width * scaleX), _cropImgWidth - imgX);
        int imgH = Math.Min((int)Math.Round(r.Height * scaleY), _cropImgHeight - imgY);
        TxtCropInfo.Text = $"Crop: {imgW} x {imgH}  (X:{imgX}  Y:{imgY})";
    }

    private void Crop_Reset(object sender, RoutedEventArgs e)
    {
        _cropSelectionRect = Rect.Empty;
        CreateCropHandles();
        TxtCropInfo.Text = "Draw a crop area on the image";
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
        if (_cropSourcePath == null || _cropSelectionRect.IsEmpty)
        {
            System.Windows.MessageBox.Show("Load an image and draw a crop area first.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = _cropSelectionRect;
        if (r.Width < 1 || r.Height < 1)
        {
            System.Windows.MessageBox.Show("Draw a crop area on the image.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (scaleX, scaleY, offsetX, offsetY) = GetImageScale();
        int imgX = Math.Max(0, (int)Math.Round((r.X - offsetX) * scaleX));
        int imgY = Math.Max(0, (int)Math.Round((r.Y - offsetY) * scaleY));
        int imgW = (int)Math.Round(r.Width * scaleX);
        int imgH = (int)Math.Round(r.Height * scaleY);
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
    //  14. Rotate & Flip (Image)
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

        var bmp = new BitmapImage();
        bmp.BeginInit(); bmp.UriSource = new Uri(path); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit(); bmp.Freeze();
        RotateFlipPreview.Source = bmp;
        _rfAngle = 0; _rfFlipH = false; _rfFlipV = false;
        RfRotateTransform.Angle = 0;
        RfFlipTransform.ScaleX = 1; RfFlipTransform.ScaleY = 1;

        ShowConfigState("rotate_flip");
    }

    private void RotateFlip_CW(object sender, RoutedEventArgs e)
    {
        _rfAngle = (_rfAngle + 90) % 360;
        RfRotateTransform.Angle = _rfAngle;
    }

    private void RotateFlip_CCW(object sender, RoutedEventArgs e)
    {
        _rfAngle = (_rfAngle + 270) % 360;
        RfRotateTransform.Angle = _rfAngle;
    }

    private void RotateFlip_180(object sender, RoutedEventArgs e)
    {
        _rfAngle = (_rfAngle + 180) % 360;
        RfRotateTransform.Angle = _rfAngle;
    }

    private void RotateFlip_FlipH(object sender, RoutedEventArgs e)
    {
        _rfFlipH = !_rfFlipH;
        RfFlipTransform.ScaleX = _rfFlipH ? -1 : 1;
    }

    private void RotateFlip_FlipV(object sender, RoutedEventArgs e)
    {
        _rfFlipV = !_rfFlipV;
        RfFlipTransform.ScaleY = _rfFlipV ? -1 : 1;
    }

    private async void RotateFlip_Execute(object sender, RoutedEventArgs e)
    {
        if (_rotateFlipSourcePath == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp", FileName = System.IO.Path.GetFileNameWithoutExtension(_rotateFlipSourcePath) + "_edited" };
        if (dlg.ShowDialog() != true) return;

        ShowProcessing("Applying transforms...");
        try
        {
            string tempPath = _rotateFlipSourcePath;
            string outputPath = dlg.FileName;

            // Apply rotation if non-zero
            if (_rfAngle != 0)
            {
                string tempRotated = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"llamashot_rf_{Guid.NewGuid():N}.png");
                await FileToolsService.RotateImageAsync(tempPath, tempRotated, (int)_rfAngle);
                if (tempPath != _rotateFlipSourcePath) File.Delete(tempPath);
                tempPath = tempRotated;
            }

            // Apply horizontal flip
            if (_rfFlipH)
            {
                string tempFlipped = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"llamashot_rf_{Guid.NewGuid():N}.png");
                await FileToolsService.FlipImageAsync(tempPath, tempFlipped, true);
                if (tempPath != _rotateFlipSourcePath) File.Delete(tempPath);
                tempPath = tempFlipped;
            }

            // Apply vertical flip
            if (_rfFlipV)
            {
                string tempFlipped = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"llamashot_rf_{Guid.NewGuid():N}.png");
                await FileToolsService.FlipImageAsync(tempPath, tempFlipped, false);
                if (tempPath != _rotateFlipSourcePath) File.Delete(tempPath);
                tempPath = tempFlipped;
            }

            // Move final result to output
            if (tempPath != _rotateFlipSourcePath)
            {
                File.Copy(tempPath, outputPath, true);
                File.Delete(tempPath);
            }
            else
            {
                File.Copy(tempPath, outputPath, true);
            }

            var info = new FileInfo(outputPath);
            ShowComplete("Image transformed!", $"{System.IO.Path.GetFileName(outputPath)} \u2014 {FileToolsService.FormatFileSize(info.Length)}", outputPath);
        }
        catch (Exception ex)
        {
            ProcessingOverlay.Visibility = Visibility.Collapsed;
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    //  15. Convert Format
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

            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.UriSource = new Uri(path); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit(); bmp.Freeze();
            ConvertPreviewImage.Source = bmp;
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
    //  16. Compress Office
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
