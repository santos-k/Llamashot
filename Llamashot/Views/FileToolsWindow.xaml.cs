using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
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

    private static readonly (string id, string title, string desc, string color, string icon, string category)[] ToolDefs =
    {
        ("merge_pdf",      "Merge PDF",       "Combine multiple PDFs into one",      "#E53935", "\u229E", "PDF Tools"),
        ("split_pdf",      "Split PDF",       "Extract pages from PDF",              "#FF7043", "\u2016", "PDF Tools"),
        ("compress_pdf",   "Compress PDF",    "Reduce PDF file size",                "#EF5350", "\u2B07", "PDF Tools"),
        ("pdf_to_images",  "PDF to Images",   "Convert pages to JPG/PNG",            "#F44336", "\u29C9", "PDF Tools"),
        ("images_to_pdf",  "Images to PDF",   "Combine images into PDF",             "#E91E63", "\u2B1C", "PDF Tools"),
        ("rotate_pdf",     "Rotate PDF",      "Rotate PDF pages",                    "#FFA726", "\u21BB", "PDF Tools"),
        ("watermark",      "Watermark PDF",   "Add text watermark",                  "#7E57C2", "\u2666", "PDF Tools"),
        ("page_numbers",   "Page Numbers",    "Add numbers to PDF",                  "#5C6BC0", "#",      "PDF Tools"),
        ("extract_pages",  "Extract Pages",   "Pick specific pages from PDF",        "#FF8A65", "\u2398", "PDF Tools"),
        ("insert_pages",   "Insert Pages",    "Add new pages to a PDF",              "#A1887F", "\u2295", "PDF Tools"),
        ("compress_image", "Compress Image",  "Reduce image file size",              "#26C6DA", "\u2B07", "Image Tools"),
        ("resize_image",   "Resize Image",    "Change dimensions",                   "#26A69A", "\u2922", "Image Tools"),
        ("crop_image",     "Crop Image",      "Crop to selection",                   "#42A5F5", "\u2702", "Image Tools"),
        ("rotate_flip",    "Rotate & Flip",   "Rotate or flip images",               "#AB47BC", "\u21BA", "Image Tools"),
        ("convert_format", "Convert Format",  "Change image format",                 "#EC407A", "\u21C4", "Image Tools"),
        ("compress_office","Compress Office",  "Reduce DOCX/XLSX/PPTX",              "#78909C", "\u2263", "Office Tools"),
        ("video_tools",   "Video Tools",      "Trim, crop, rotate, flip & extract",  "#F44336", "\u25B6", "Video & Audio"),
        ("extract_audio", "Extract Audio",    "Extract audio from video",            "#00BCD4", "\u266B", "Video & Audio"),
        ("trim_audio",    "Trim Audio",       "Cut start and end of audio",          "#009688", "\u2702", "Video & Audio"),
        ("youtube_dl",    "YouTube Download", "Download video or audio from URL",    "#FF0000", "\u25B6", "Download"),
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
    private readonly HashSet<int> _extractSelectedPages = new(); // 1-based page numbers
    private string? _insertBasePath;
    private int _insertAfterPage = 0;
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
    //  Video tool state
    // =====================================================================

    private string? _videoToolsPath;
    private double _videoAngle = 0;
    private bool _videoFlipH = false, _videoFlipV = false;
    private bool _isVideoPlaying;
    private DispatcherTimer? _videoTimer;
    private double _videoTrimStartPct = 0;
    private double _videoTrimEndPct = 1;
    private enum VtDragMode { None, Left, Right, Seek }
    private VtDragMode _vtDragMode = VtDragMode.None;

    // =====================================================================
    //  Extract audio state
    // =====================================================================

    private readonly ObservableCollection<FileItem> _extractAudioFiles = new();

    // =====================================================================
    //  YouTube download state
    // =====================================================================

    private readonly ObservableCollection<YtVideoItem> _ytVideos = new();
    private CancellationTokenSource? _ytCancelSource;
    private string? _ytUrl;

    // =====================================================================
    //  Video crop state
    // =====================================================================

    private bool _isVideoCropMode;
    private bool _isVideoCropDragging;
    private Point _videoCropStart;
    private Rect _videoCropRect = Rect.Empty;
    private Rectangle? _videoCropSelection;
    private readonly Rectangle[] _videoCropOverlays = new Rectangle[4];

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
        ExtractAudioList.ItemsSource = _extractAudioFiles;
        TrimAudioFileList.ItemsSource = _trimAudioFiles;
        YtVideoList.ItemsSource = _ytVideos;
    }

    // =====================================================================
    //  Card creation
    // =====================================================================

    private void CreateToolCards()
    {
        RebuildCardPanel();
        Loaded += (s, e) => RebuildCardPanel();
    }

    private Border CreateCard(string id, string title, string desc, string color, string icon)
    {
        var accentColor = (Color)ColorConverter.ConvertFromString(color);
        var accentBrush = new SolidColorBrush(accentColor);

        var card = new Border
        {
            MinWidth = 250, Height = 88, Margin = new Thickness(5),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E24")),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x44, accentColor.R, accentColor.G, accentColor.B)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Cursor = Cursors.Hand, Tag = id,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.3, Color = Colors.Black },
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1)
        };

        var grid = new Grid { Margin = new Thickness(14, 0, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconBorder = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(14),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new LinearGradientBrush(
                System.Windows.Media.Color.FromArgb(0x44, accentColor.R, accentColor.G, accentColor.B),
                System.Windows.Media.Color.FromArgb(0x15, accentColor.R, accentColor.G, accentColor.B), 45)
        };
        iconBorder.Child = new TextBlock
        {
            Text = icon, FontSize = 22, Foreground = accentBrush,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center, FontWeight = FontWeights.Bold
        };
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
        textStack.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold });
        textStack.Children.Add(new TextBlock
        {
            Text = desc, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#777")),
            FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        var arrow = new TextBlock
        {
            Text = "\u276F", FontSize = 18, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555")),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(arrow, 2);
        grid.Children.Add(arrow);
        card.Child = grid;

        card.MouseLeftButtonDown += Card_Click;
        card.MouseEnter += (s, _) =>
        {
            var b = (Border)s;
            b.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#282830"));
            b.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x88, accentColor.R, accentColor.G, accentColor.B));
            ((ScaleTransform)b.RenderTransform).ScaleX = 1.02;
            ((ScaleTransform)b.RenderTransform).ScaleY = 1.02;
        };
        card.MouseLeave += (s, _) =>
        {
            var b = (Border)s;
            b.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E24"));
            b.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x44, accentColor.R, accentColor.G, accentColor.B));
            ((ScaleTransform)b.RenderTransform).ScaleX = 1.0;
            ((ScaleTransform)b.RenderTransform).ScaleY = 1.0;
        };
        return card;
    }

    private void RebuildCardPanel()
    {
        CardPanel.Children.Clear();
        string filter = TxtToolSearch?.Text?.Trim().ToLowerInvariant() ?? "";
        int sortMode = CmbToolSort?.SelectedIndex ?? 0;

        var tools = ToolDefs.AsEnumerable();

        // Filter by search
        if (!string.IsNullOrEmpty(filter))
            tools = tools.Where(t => t.title.ToLowerInvariant().Contains(filter) || t.desc.ToLowerInvariant().Contains(filter));

        // Sort
        var toolList = sortMode switch
        {
            1 => tools.OrderBy(t => t.title).ToList(),
            2 => tools.OrderByDescending(t => t.title).ToList(),
            _ => tools.ToList() // default: grouped by category
        };

        if (sortMode == 0 && string.IsNullOrEmpty(filter))
        {
            // Group by category with headers
            string? lastCategory = null;
            foreach (var t in toolList)
            {
                if (t.category != lastCategory)
                {
                    lastCategory = t.category;
                    var header = new TextBlock
                    {
                        Text = t.category, FontSize = 14, FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666")),
                        Margin = new Thickness(6, lastCategory == "PDF Tools" ? 0 : 16, 0, 6)
                    };
                    CardPanel.Children.Add(header);
                    CardPanel.Children.Add(new WrapPanel { Tag = t.category });
                }

                var panel = CardPanel.Children.OfType<WrapPanel>().LastOrDefault();
                if (panel != null)
                    panel.Children.Add(CreateCard(t.id, t.title, t.desc, t.color, t.icon));
            }

            // Adjust widths for each WrapPanel
            foreach (var wp in CardPanel.Children.OfType<WrapPanel>())
            {
                wp.SizeChanged += (s, e) =>
                {
                    var p = (WrapPanel)s;
                    double availW = p.ActualWidth;
                    if (availW <= 0) return;
                    int cols = Math.Max(1, (int)(availW / 300));
                    double cardW = (availW - cols * 10) / cols;
                    foreach (var child in p.Children) if (child is Border b) b.Width = cardW;
                };
            }
        }
        else
        {
            // Flat list (search or sorted)
            var wp = new WrapPanel();
            foreach (var t in toolList)
                wp.Children.Add(CreateCard(t.id, t.title, t.desc, t.color, t.icon));

            wp.SizeChanged += (s, e) =>
            {
                var p = (WrapPanel)s;
                double availW = p.ActualWidth;
                if (availW <= 0) return;
                int cols = Math.Max(1, (int)(availW / 300));
                double cardW = (availW - cols * 10) / cols;
                foreach (var child in p.Children) if (child is Border b) b.Width = cardW;
            };
            CardPanel.Children.Add(wp);
        }
    }

    private void ToolSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (TxtSearchPlaceholder != null)
            TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtToolSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        RebuildCardPanel();
    }

    private void ToolSort_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CardPanel != null) RebuildCardPanel();
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
        _toolPanels["video_tools"] = PanelVideoTools;
        _toolPanels["extract_audio"] = PanelExtractAudio;
        _toolPanels["trim_audio"] = PanelTrimAudio;
        _toolPanels["youtube_dl"] = PanelYouTubeDl;

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
        _selectViews["video_tools"] = VideoSelectView;
        _selectViews["extract_audio"] = ExtractAudioSelectView;
        _selectViews["trim_audio"] = TrimAudioSelectView;
        _selectViews["youtube_dl"] = YtSelectView;

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
        _configViews["video_tools"] = VideoConfigView;
        _configViews["extract_audio"] = ExtractAudioConfigView;
        _configViews["trim_audio"] = TrimAudioConfigView;
        _configViews["youtube_dl"] = YtConfigView;

        foreach (var (id, title, _, _, _, _) in ToolDefs)
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
        _videoToolsPath = null;
        _videoAngle = 0; _videoFlipH = false; _videoFlipV = false;
        _isVideoPlaying = false;
        _videoTrimStartPct = 0;
        _videoTrimEndPct = 1;
        _videoTimer?.Stop();
        VideoPreview.Stop();
        VideoPreview.Source = null;
        _isVideoCropMode = false;
        _videoCropRect = Rect.Empty;
        VideoCropCanvas.Visibility = Visibility.Collapsed;
        VideoCropCanvas.Children.Clear();
        _extractAudioFiles.Clear();
        _extAudioOutputFolder = null;
        // Trim audio cleanup
        StopTrimPlayback();
        foreach (var item in _trimAudioItems)
        {
            item.Player?.Stop();
            if (item.WaveformTempPath != null) try { File.Delete(item.WaveformTempPath); } catch { }
        }
        _trimAudioItems.Clear();
        _trimAudioFiles.Clear();
        TrimWaveformPanel.Children.Clear();
        _trimOutputFolder = null;
        _audioPlayTimer?.Stop();
        _ytUrl = null;
        _ytVideos.Clear();
        _ytCancelSource?.Cancel();
        _ytCancelSource = null;
        BtnYtStop.Visibility = Visibility.Collapsed;
        TxtYtUrl.Text = "";
        TxtYtOverallProgress.Text = "";
        _extractPdfPath = null;
        _extractSelectedPages.Clear();
        ExtractPageGrid.Children.Clear();
        _insertBasePath = null;
        _insertAfterPage = 0;
        InsertPageGrid.Children.Clear();
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

    private static bool IsVideoFile(string path)
    {
        return FileToolsService.IsVideoExtension(System.IO.Path.GetExtension(path));
    }

    private bool CheckFFmpeg()
    {
        if (FileToolsService.IsFFmpegAvailable()) return true;
        System.Windows.MessageBox.Show("FFmpeg is required for video tools.\n\nInstall FFmpeg and add it to your system PATH:\nhttps://ffmpeg.org/download.html",
            "FFmpeg Required", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
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
        }
        catch { TxtExtractInfo.Text = ""; }
        ShowConfigState("extract_pages");
        await LoadExtractThumbnails(path);
    }

    private async void Extract_Execute(object sender, RoutedEventArgs e)
    {
        if (_extractPdfPath == null || _extractSelectedPages.Count == 0)
        {
            System.Windows.MessageBox.Show("Select at least one page to extract.", "Extract Pages", MessageBoxButton.OK);
            return;
        }

        var pageNumbers = _extractSelectedPages.OrderBy(x => x).ToArray();
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

    private async Task LoadExtractThumbnails(string pdfPath)
    {
        ExtractPageGrid.Children.Clear();
        _extractSelectedPages.Clear();

        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            uint pageCount = pdfDoc.PageCount;

            for (uint i = 0; i < pageCount; i++)
            {
                using var page = pdfDoc.GetPage(i);
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var options = new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = 150 };
                await page.RenderToStreamAsync(stream, options);
                stream.Seek(0);

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = stream.AsStreamForRead();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                int pageNum = (int)i + 1;

                var thumbnail = new Border
                {
                    Width = 130, Height = 180, Margin = new Thickness(6),
                    CornerRadius = new CornerRadius(6),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444")),
                    BorderThickness = new Thickness(2),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#222")),
                    Cursor = Cursors.Hand,
                    Tag = pageNum,
                    ClipToBounds = true,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 6, ShadowDepth = 2, Opacity = 0.3, Color = Colors.Black
                    }
                };

                var grid = new Grid();
                grid.Children.Add(new System.Windows.Controls.Image
                {
                    Source = bmp, Stretch = Stretch.Uniform,
                    Margin = new Thickness(2)
                });

                // Page number label
                var label = new Border
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xCC, 0, 0, 0)),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    Padding = new Thickness(0, 3, 0, 3)
                };
                label.Child = new TextBlock
                {
                    Text = $"Page {pageNum}",
                    Foreground = Brushes.White, FontSize = 11,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                };
                grid.Children.Add(label);

                // Selection check overlay (initially hidden)
                var checkOverlay = new Border
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x44, 0x42, 0xA5, 0xF5)),
                    Visibility = Visibility.Collapsed,
                    CornerRadius = new CornerRadius(4)
                };
                var checkMark = new TextBlock
                {
                    Text = "\u2713", FontSize = 28, FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                checkOverlay.Child = checkMark;
                grid.Children.Add(checkOverlay);

                thumbnail.Child = grid;

                // Click to toggle selection
                thumbnail.MouseLeftButtonDown += (s, _) =>
                {
                    var border = (Border)s;
                    int pn = (int)border.Tag;
                    // Find the check overlay (3rd child of grid)
                    var g = (Grid)border.Child;
                    var overlay = (Border)g.Children[2];

                    if (_extractSelectedPages.Contains(pn))
                    {
                        _extractSelectedPages.Remove(pn);
                        border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444"));
                        border.BorderThickness = new Thickness(2);
                        overlay.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        _extractSelectedPages.Add(pn);
                        border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#42A5F5"));
                        border.BorderThickness = new Thickness(3);
                        overlay.Visibility = Visibility.Visible;
                    }

                    UpdateExtractSelection();
                };

                // Hover effect
                thumbnail.MouseEnter += (s, _) =>
                {
                    var b = (Border)s;
                    ((System.Windows.Media.Effects.DropShadowEffect)b.Effect).BlurRadius = 12;
                };
                thumbnail.MouseLeave += (s, _) =>
                {
                    var b = (Border)s;
                    ((System.Windows.Media.Effects.DropShadowEffect)b.Effect).BlurRadius = 6;
                };

                ExtractPageGrid.Children.Add(thumbnail);
            }

            UpdateExtractSelection();
        }
        catch (Exception ex)
        {
            TxtExtractInfo.Text = $"Error loading pages: {ex.Message}";
        }
    }

    private void UpdateExtractSelection()
    {
        int count = _extractSelectedPages.Count;
        TxtExtractSelection.Text = count == 0
            ? "No pages selected"
            : $"{count} page{(count > 1 ? "s" : "")} selected: {string.Join(", ", _extractSelectedPages.OrderBy(x => x))}";
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
        }
        catch { TxtInsertBaseInfo.Text = ""; }
        ShowConfigState("insert_pages");
        await LoadInsertThumbnails(dlg.FileName);
    }

    private async void Insert_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsPdfFile);
        if (files.Length > 0)
        {
            _insertBasePath = files[0];
            TxtInsertBaseName.Text = System.IO.Path.GetFileName(files[0]);
            try
            {
                int pages = await FileToolsService.GetPdfPageCountAsync(files[0]);
                TxtInsertBaseInfo.Text = $"Pages: {pages}";
            }
            catch { TxtInsertBaseInfo.Text = ""; }
            ShowConfigState("insert_pages");
            await LoadInsertThumbnails(files[0]);
        }
    }

    private async Task LoadInsertThumbnails(string pdfPath)
    {
        InsertPageGrid.Children.Clear();
        _insertAfterPage = 0;

        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            uint pageCount = pdfDoc.PageCount;

            // Add initial insertion point (before page 1)
            AddInsertionPoint(0, pageCount);

            for (uint i = 0; i < pageCount; i++)
            {
                using var page = pdfDoc.GetPage(i);
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var options = new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = 120 };
                await page.RenderToStreamAsync(stream, options);
                stream.Seek(0);

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = stream.AsStreamForRead();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                int pageNum = (int)i + 1;

                // Page thumbnail (smaller than extract -- just for reference)
                var thumbnail = new Border
                {
                    Width = 100, Height = 140, Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(4),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444")),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#222")),
                    ClipToBounds = true
                };
                var grid = new Grid();
                grid.Children.Add(new System.Windows.Controls.Image { Source = bmp, Stretch = Stretch.Uniform, Margin = new Thickness(2) });
                var label = new Border
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xBB, 0, 0, 0)),
                    VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(0, 2, 0, 2)
                };
                label.Child = new TextBlock { Text = $"{pageNum}", Foreground = Brushes.White, FontSize = 10, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
                grid.Children.Add(label);
                thumbnail.Child = grid;

                InsertPageGrid.Children.Add(thumbnail);

                // Add insertion point after this page
                AddInsertionPoint(pageNum, pageCount);
            }

            UpdateInsertPosition();
        }
        catch { }
    }

    private void AddInsertionPoint(int afterPage, uint totalPages)
    {
        var btn = new Border
        {
            Width = 28, Height = 28, Margin = new Thickness(2, 56, 2, 56),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(afterPage == _insertAfterPage ? "#42A5F5" : "#444")),
            Cursor = Cursors.Hand,
            Tag = afterPage,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = afterPage == 0 ? "Insert at beginning" : $"Insert after page {afterPage}"
        };
        btn.Child = new TextBlock
        {
            Text = "\u2295", FontSize = 16, Foreground = Brushes.White,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        btn.MouseLeftButtonDown += (s, _) =>
        {
            _insertAfterPage = (int)((Border)s).Tag;
            // Update all insertion point visuals
            foreach (var child in InsertPageGrid.Children)
            {
                if (child is Border b && b.Tag is int pos)
                {
                    b.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                        pos == _insertAfterPage ? "#42A5F5" : "#444"));
                }
            }
            UpdateInsertPosition();
        };
        btn.MouseEnter += (s, _) =>
        {
            var b = (Border)s;
            if ((int)b.Tag != _insertAfterPage)
                b.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666"));
        };
        btn.MouseLeave += (s, _) =>
        {
            var b = (Border)s;
            if ((int)b.Tag != _insertAfterPage)
                b.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444"));
        };
        InsertPageGrid.Children.Add(btn);
    }

    private void UpdateInsertPosition()
    {
        TxtInsertPosition.Text = _insertAfterPage == 0
            ? "Insert position: at the beginning"
            : $"Insert position: after page {_insertAfterPage}";
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
        int afterPage = _insertAfterPage;

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
            TxtResizeInfo.Text = $"Current: {w} x {h} px  ({FileToolsService.FormatFileSize(size)}, {fmt})";
            RbResizePixels.IsChecked = true;
            _suppressAspectUpdate = true;
            TxtResizeW.Text = w.ToString();
            TxtResizeH.Text = h.ToString();
            _suppressAspectUpdate = false;
            UpdateResizeOutput();

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

    private string GetResizeUnit()
    {
        if (RbResizePercent?.IsChecked == true) return "%";
        if (RbResizeInches?.IsChecked == true) return "in";
        if (RbResizeCm?.IsChecked == true) return "cm";
        if (RbResizeMm?.IsChecked == true) return "mm";
        return "px";
    }

    private (int pixelW, int pixelH) GetResizePixels()
    {
        if (!double.TryParse(TxtResizeW?.Text, out double valW) || !double.TryParse(TxtResizeH?.Text, out double valH)
            || valW <= 0 || valH <= 0)
            return (0, 0);

        double dpi = 96;
        if (TxtResizeDpi != null) double.TryParse(TxtResizeDpi.Text, out dpi);
        if (dpi <= 0) dpi = 96;

        string unit = GetResizeUnit();
        return unit switch
        {
            "%" => ((int)Math.Round(_resizeOrigW * valW / 100), (int)Math.Round(_resizeOrigH * valH / 100)),
            "in" => ((int)Math.Round(valW * dpi), (int)Math.Round(valH * dpi)),
            "cm" => ((int)Math.Round(valW / 2.54 * dpi), (int)Math.Round(valH / 2.54 * dpi)),
            "mm" => ((int)Math.Round(valW / 25.4 * dpi), (int)Math.Round(valH / 25.4 * dpi)),
            _ => ((int)valW, (int)valH)
        };
    }

    private void UpdateResizeOutput()
    {
        var (pw, ph) = GetResizePixels();
        if (TxtResizeOutput != null)
            TxtResizeOutput.Text = pw > 0 && ph > 0 ? $"Output: {pw} x {ph} px" : "";
    }

    private void ResizeUnit_Changed(object sender, RoutedEventArgs e)
    {
        if (_resizeOrigW == 0) return;
        _suppressAspectUpdate = true;

        string unit = GetResizeUnit();
        bool showDpi = unit is "in" or "cm" or "mm";
        if (ResizeDpiRow != null) ResizeDpiRow.Visibility = showDpi ? Visibility.Visible : Visibility.Collapsed;
        if (TxtResizeUnit != null) TxtResizeUnit.Text = unit;

        double dpi = 96;
        if (TxtResizeDpi != null) double.TryParse(TxtResizeDpi.Text, out dpi);
        if (dpi <= 0) dpi = 96;

        switch (unit)
        {
            case "px":
                TxtResizeW.Text = _resizeOrigW.ToString();
                TxtResizeH.Text = _resizeOrigH.ToString();
                break;
            case "%":
                TxtResizeW.Text = "100";
                TxtResizeH.Text = "100";
                break;
            case "in":
                TxtResizeW.Text = Math.Round(_resizeOrigW / dpi, 2).ToString();
                TxtResizeH.Text = Math.Round(_resizeOrigH / dpi, 2).ToString();
                break;
            case "cm":
                TxtResizeW.Text = Math.Round(_resizeOrigW / dpi * 2.54, 2).ToString();
                TxtResizeH.Text = Math.Round(_resizeOrigH / dpi * 2.54, 2).ToString();
                break;
            case "mm":
                TxtResizeW.Text = Math.Round(_resizeOrigW / dpi * 25.4, 1).ToString();
                TxtResizeH.Text = Math.Round(_resizeOrigH / dpi * 25.4, 1).ToString();
                break;
        }
        _suppressAspectUpdate = false;
        UpdateResizeOutput();
    }

    private void ResizeW_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressAspectUpdate || ChkResizeAspect?.IsChecked != true) return;
        if (_resizeOrigW == 0 || _resizeOrigH == 0) return;
        if (!double.TryParse(TxtResizeW.Text, out double w) || w <= 0) return;

        _suppressAspectUpdate = true;
        double h = Math.Round(w / _resizeOrigW * _resizeOrigH, 2);
        TxtResizeH.Text = GetResizeUnit() == "px" ? ((int)h).ToString() : h.ToString();
        _suppressAspectUpdate = false;
        UpdateResizeOutput();
    }

    private void ResizeH_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressAspectUpdate || ChkResizeAspect?.IsChecked != true) return;
        if (_resizeOrigW == 0 || _resizeOrigH == 0) return;
        if (!double.TryParse(TxtResizeH.Text, out double h) || h <= 0) return;

        _suppressAspectUpdate = true;
        double w = Math.Round(h / _resizeOrigH * _resizeOrigW, 2);
        TxtResizeW.Text = GetResizeUnit() == "px" ? ((int)w).ToString() : w.ToString();
        _suppressAspectUpdate = false;
        UpdateResizeOutput();
    }

    private async void Resize_Execute(object sender, RoutedEventArgs e)
    {
        if (_resizeSourcePath == null) return;
        var (w, h) = GetResizePixels();
        if (w <= 0 || h <= 0)
        {
            System.Windows.MessageBox.Show("Enter valid dimensions.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            TxtCropInfo.Text = "Drag handles to adjust crop area";
        }
        catch { /* ignore */ }
    }

    private readonly System.Windows.Shapes.Line[] _cropGridLines = new System.Windows.Shapes.Line[4];

    private void CreateCropHandles()
    {
        CropCanvas.Children.Clear();

        // Create 4 overlay rectangles (darkening outside crop)
        for (int i = 0; i < 4; i++)
        {
            _cropOverlays[i] = new Rectangle { Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xAA, 0, 0, 0)) };
            CropCanvas.Children.Add(_cropOverlays[i]);
        }

        // Create crop rectangle border
        _cropRect = new Rectangle
        {
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#42A5F5")),
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            StrokeDashArray = new DoubleCollection { 4, 2 }
        };
        CropCanvas.Children.Add(_cropRect);

        // Create rule-of-thirds grid lines (2 horizontal + 2 vertical)
        for (int i = 0; i < 4; i++)
        {
            _cropGridLines[i] = new System.Windows.Shapes.Line
            {
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            CropCanvas.Children.Add(_cropGridLines[i]);
        }

        // Create 8 handles
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

        // Default selection = full image (set after layout)
        Dispatcher.BeginInvoke(() =>
        {
            var imgBounds = GetImageDisplayBounds();
            if (imgBounds.Width > 0 && imgBounds.Height > 0)
            {
                _cropSelectionRect = imgBounds;
                UpdateCropVisuals();
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
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
        var imgBounds = GetImageDisplayBounds();

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

        // Only allow starting crop within image bounds
        if (!imgBounds.Contains(pos)) return;

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

    private Rect GetImageDisplayBounds()
    {
        var (_, _, offsetX, offsetY) = GetImageScale();
        double canvasW = CropCanvas.ActualWidth;
        double canvasH = CropCanvas.ActualHeight;
        double imageAspect = (double)_cropImgWidth / _cropImgHeight;
        double canvasAspect = canvasW / canvasH;
        double displayW, displayH;
        if (imageAspect > canvasAspect) { displayW = canvasW; displayH = canvasW / imageAspect; }
        else { displayH = canvasH; displayW = canvasH * imageAspect; }
        return new Rect(offsetX, offsetY, displayW, displayH);
    }

    private void CropCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDraggingCrop) return;
        var pos = e.GetPosition(CropCanvas);

        // Clamp to image display bounds (not full canvas)
        var imgBounds = GetImageDisplayBounds();
        pos = new Point(
            Math.Max(imgBounds.Left, Math.Min(pos.X, imgBounds.Right)),
            Math.Max(imgBounds.Top, Math.Min(pos.Y, imgBounds.Bottom)));

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
                if (moved.Left < imgBounds.Left) moved.X = imgBounds.Left;
                if (moved.Top < imgBounds.Top) moved.Y = imgBounds.Top;
                if (moved.Right > imgBounds.Right) moved.X = imgBounds.Right - moved.Width;
                if (moved.Bottom > imgBounds.Bottom) moved.Y = imgBounds.Bottom - moved.Height;
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

        // Final clamp to image bounds
        _cropSelectionRect = Rect.Intersect(_cropSelectionRect, imgBounds);
        if (_cropSelectionRect.IsEmpty)
            _cropSelectionRect = new Rect(imgBounds.X, imgBounds.Y, 0, 0);

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

        // Rule-of-thirds grid lines
        bool showGrid = r.Width > 30 && r.Height > 30;
        for (int i = 0; i < 4; i++)
        {
            if (_cropGridLines[i] == null) continue;
            _cropGridLines[i].Visibility = showGrid ? Visibility.Visible : Visibility.Collapsed;
        }
        if (showGrid)
        {
            // Vertical lines at 1/3 and 2/3
            _cropGridLines[0].X1 = r.X + r.Width / 3; _cropGridLines[0].Y1 = r.Y;
            _cropGridLines[0].X2 = r.X + r.Width / 3; _cropGridLines[0].Y2 = r.Bottom;
            _cropGridLines[1].X1 = r.X + r.Width * 2 / 3; _cropGridLines[1].Y1 = r.Y;
            _cropGridLines[1].X2 = r.X + r.Width * 2 / 3; _cropGridLines[1].Y2 = r.Bottom;
            // Horizontal lines at 1/3 and 2/3
            _cropGridLines[2].X1 = r.X; _cropGridLines[2].Y1 = r.Y + r.Height / 3;
            _cropGridLines[2].X2 = r.Right; _cropGridLines[2].Y2 = r.Y + r.Height / 3;
            _cropGridLines[3].X1 = r.X; _cropGridLines[3].Y1 = r.Y + r.Height * 2 / 3;
            _cropGridLines[3].X2 = r.Right; _cropGridLines[3].Y2 = r.Y + r.Height * 2 / 3;
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
        // Reset to full image selection
        var imgBounds = GetImageDisplayBounds();
        _cropSelectionRect = imgBounds.Width > 0 ? imgBounds : Rect.Empty;
        CreateCropHandles();
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

    // =====================================================================
    //  17. Video Tools (unified editor)
    // =====================================================================

    private TimeSpan _videoDuration;
    private int _videoW, _videoH;

    private void VideoTools_SelectFiles(object sender, RoutedEventArgs e)
    {
        if (!CheckFFmpeg()) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Video Files|*.mp4;*.avi;*.mov;*.mkv;*.wmv;*.webm;*.m4v|All Files|*.*" };
        if (dlg.ShowDialog() != true) return;
        LoadVideoTools(dlg.FileName);
    }

    private void VideoTools_SelectDrop(object sender, DragEventArgs e)
    {
        if (!CheckFFmpeg()) return;
        var files = GetDroppedFiles(e, IsVideoFile);
        if (files.Length > 0) LoadVideoTools(files[0]);
    }

    private async void LoadVideoTools(string path)
    {
        _videoToolsPath = path;
        _videoAngle = 0; _videoFlipH = false; _videoFlipV = false;
        VideoRotateTransform.Angle = 0;
        VideoFlipTransform.ScaleX = 1; VideoFlipTransform.ScaleY = 1;
        TxtVideoTransform.Text = "";
        TxtVideoFileName.Text = System.IO.Path.GetFileName(path);

        _videoTrimStartPct = 0;
        _videoTrimEndPct = 1;

        try
        {
            var (duration, w, h, codec) = await FileToolsService.GetVideoInfoAsync(path);
            _videoDuration = duration;
            _videoW = w; _videoH = h;
            TxtVideoInfo.Text = $"{w}x{h} | {codec} | {FileToolsService.FormatTimeSpan(duration)}";
        }
        catch { TxtVideoInfo.Text = "Could not read video info"; }

        // Load video into MediaElement
        VideoPreview.Source = new Uri(path);
        VideoPreview.Play();
        VideoPreview.Pause();
        _isVideoPlaying = false;
        BtnVideoPlay.Content = "\u25B6"; // play symbol

        // Start timer for seek bar updates
        _videoTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _videoTimer.Tick -= VideoTimer_Tick;
        _videoTimer.Tick += VideoTimer_Tick;
        _videoTimer.Start();

        ShowConfigState("video_tools");
        SelectVideoTool("trim");
        Dispatcher.BeginInvoke(() => UpdateVideoTimeline(), System.Windows.Threading.DispatcherPriority.Loaded);

        // Populate info panel
        TxtVideoDetailInfo.Text = $"File: {System.IO.Path.GetFileName(path)}\n" +
            $"Path: {path}\n" +
            $"Resolution: {_videoW}x{_videoH}\n" +
            $"Duration: {FileToolsService.FormatTimeSpan(_videoDuration)}\n" +
            $"Size: {FileToolsService.FormatFileSize(new FileInfo(path).Length)}";
    }

    // ----- Video tool sidebar selection -----

    private string _videoActiveTool = "trim";
    private static readonly string[] _videoToolNames = { "trim", "crop", "rotate", "flip", "info" };

    private void VideoToolSelect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string tool)
            SelectVideoTool(tool);
    }

    private void SelectVideoTool(string tool)
    {
        _videoActiveTool = tool;
        var teal = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4DB6AC"));
        var gray = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888"));
        var activeBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A2A2A"));
        var transp = Brushes.Transparent;

        // Highlight sidebar buttons
        var buttons = new Dictionary<string, System.Windows.Controls.Button>
        {
            ["trim"] = BtnToolTrim, ["crop"] = BtnToolCrop, ["rotate"] = BtnToolRotate,
            ["flip"] = BtnToolFlip, ["info"] = BtnToolInfo
        };
        foreach (var (id, btn) in buttons)
        {
            bool active = id == tool;
            btn.Background = active ? activeBg : transp;
            var sp = btn.Content as StackPanel;
            if (sp != null)
                foreach (var child in sp.Children.OfType<TextBlock>())
                    child.Foreground = active ? teal : gray;
        }

        // Toggle crop mode based on tool
        if (tool == "crop" && !_isVideoCropMode)
            ActivateVideoCrop();
        else if (tool != "crop" && _isVideoCropMode)
            DeactivateVideoCrop();

        // Show/hide right panel sections (info hidden unless selected; others always visible)
        VideoPropInfo.Visibility = tool == "info" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ActivateVideoCrop()
    {
        if (_isVideoCropMode) return;
        _isVideoCropMode = true;
        VideoCropCanvas.Visibility = Visibility.Visible;
        InitVideoCropOverlays();
        BtnVideoCropToggle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4DB6AC"));
    }

    private void DeactivateVideoCrop()
    {
        if (!_isVideoCropMode) return;
        _isVideoCropMode = false;
        VideoCropCanvas.Visibility = Visibility.Collapsed;
        VideoCropCanvas.Children.Clear();
        _videoCropSelection = null;
        _videoCropRect = Rect.Empty;
        BtnVideoCropToggle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCC"));
    }

    private void VideoCropReset_Click(object sender, MouseButtonEventArgs e)
    {
        _videoCropRect = Rect.Empty;
        if (_isVideoCropMode)
        {
            DeactivateVideoCrop();
            if (_videoActiveTool == "crop") ActivateVideoCrop();
        }
        TxtVideoCropInfo.Text = "";
        TxtVideoCropX.Text = "0"; TxtVideoCropY.Text = "0";
        TxtVideoCropW.Text = _videoW.ToString(); TxtVideoCropH.Text = _videoH.ToString();
    }

    private void VideoRotateReset_Click(object sender, MouseButtonEventArgs e)
    {
        _videoAngle = 0;
        VideoRotateTransform.Angle = 0;
        TxtVideoAngle.Text = "0\u00B0";
        UpdateVideoTransformText();
    }

    // ----- Video playback handlers -----

    private void VideoPreview_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (VideoPreview.NaturalDuration.HasTimeSpan)
            _videoDuration = VideoPreview.NaturalDuration.TimeSpan;
    }

    private void VideoPreview_MediaEnded(object sender, RoutedEventArgs e)
    {
        VideoPreview.Position = TimeSpan.Zero;
        VideoPreview.Pause();
        _isVideoPlaying = false;
        BtnVideoPlay.Content = "\u25B6";
    }

    private void VideoPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_isVideoPlaying) { VideoPreview.Pause(); BtnVideoPlay.Content = "\u25B6"; }
        else { VideoPreview.Play(); BtnVideoPlay.Content = "\u23F8"; }
        _isVideoPlaying = !_isVideoPlaying;
    }

    private void VideoTimer_Tick(object? sender, EventArgs e)
    {
        if (_vtDragMode != VtDragMode.None || _videoDuration.TotalSeconds <= 0) return;
        var pos = VideoPreview.Position;
        double pct = pos.TotalSeconds / _videoDuration.TotalSeconds;
        UpdateVideoPlayhead(pct);

        // Auto-stop at trim end during playback
        if (_isVideoPlaying && pct >= _videoTrimEndPct)
        {
            VideoPreview.Pause();
            _isVideoPlaying = false;
            BtnVideoPlay.Content = "\u25B6";
            VideoPreview.Position = TimeSpan.FromSeconds(_videoTrimEndPct * _videoDuration.TotalSeconds);
            UpdateVideoPlayhead(_videoTrimEndPct);
        }

        string cur = FileToolsService.FormatTimeSpan(pos);
        string total = FileToolsService.FormatTimeSpan(_videoDuration);
        TxtVideoTime.Text = $"{cur} / {total}";
    }

    // ----- Visual timeline handlers -----

    private void UpdateVideoTimeline()
    {
        double containerW = VideoTimelineContainer.ActualWidth;
        if (containerW <= 0) return;

        double leftX = _videoTrimStartPct * containerW;
        double rightX = _videoTrimEndPct * containerW;

        // Position trim region
        VtTrimRegion.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        VtTrimRegion.Width = Math.Max(0, rightX - leftX);
        VtTrimRegion.Margin = new Thickness(leftX, 0, 0, 0);

        // Position handles
        VtHandleLeft.Margin = new Thickness(leftX - 7, 0, 0, 0);
        VtHandleRight.Margin = new Thickness(rightX - 7, 0, 0, 0);

        // Update labels
        if (_videoDuration.TotalSeconds > 0)
        {
            var startTs = TimeSpan.FromSeconds(_videoTrimStartPct * _videoDuration.TotalSeconds);
            var endTs = TimeSpan.FromSeconds(_videoTrimEndPct * _videoDuration.TotalSeconds);
            var dur = endTs - startTs;
            VtStartLabel.Text = startTs.ToString(@"hh\:mm\:ss");
            VtEndLabel.Text = endTs.ToString(@"hh\:mm\:ss");
            VtDurationLabel.Text = $"\u23F1 {FileToolsService.FormatTimeSpan(dur)}";
            TxtVtStartTime.Text = FormatMs(startTs);
            TxtVtDuration.Text = FormatMs(dur);
        }
    }

    private void VideoTimeline_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(VideoTimelineContainer);
        double containerW = VideoTimelineContainer.ActualWidth;
        if (containerW <= 0) return;

        double leftX = _videoTrimStartPct * containerW;
        double rightX = _videoTrimEndPct * containerW;

        if (Math.Abs(pos.X - leftX) < 18)
            _vtDragMode = VtDragMode.Left;
        else if (Math.Abs(pos.X - rightX) < 18)
            _vtDragMode = VtDragMode.Right;
        else
        {
            _vtDragMode = VtDragMode.Seek;
            double pct = Math.Max(0, Math.Min(1, pos.X / containerW));
            VideoPreview.Position = TimeSpan.FromSeconds(pct * _videoDuration.TotalSeconds);
            UpdateVideoPlayhead(pct);
        }

        VideoTimelineContainer.CaptureMouse();
        e.Handled = true;
    }

    private void VideoTimeline_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_vtDragMode == VtDragMode.None) return;
        double containerW = VideoTimelineContainer.ActualWidth;
        if (containerW <= 0) return;

        double pct = Math.Max(0, Math.Min(1, e.GetPosition(VideoTimelineContainer).X / containerW));

        if (_vtDragMode == VtDragMode.Left)
        {
            _videoTrimStartPct = Math.Min(pct, _videoTrimEndPct - 0.01);
            UpdateVideoTimeline();
        }
        else if (_vtDragMode == VtDragMode.Right)
        {
            _videoTrimEndPct = Math.Max(pct, _videoTrimStartPct + 0.01);
            UpdateVideoTimeline();
        }
        else if (_vtDragMode == VtDragMode.Seek)
        {
            VideoPreview.Position = TimeSpan.FromSeconds(pct * _videoDuration.TotalSeconds);
            UpdateVideoPlayhead(pct);
        }
    }

    private void VideoTimeline_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _vtDragMode = VtDragMode.None;
        VideoTimelineContainer.ReleaseMouseCapture();
    }

    private void UpdateVideoPlayhead(double pct)
    {
        double containerW = VideoTimelineContainer.ActualWidth;
        if (containerW <= 0) return;
        VtPlayhead.Margin = new Thickness(pct * containerW - 1, 0, 0, 0);
    }

    // ----- Rotate/Flip handlers (live preview) -----

    private void VideoRotate_CW(object sender, RoutedEventArgs e)
    {
        _videoAngle = (_videoAngle + 90) % 360;
        VideoRotateTransform.Angle = _videoAngle;
        UpdateVideoTransformText();
    }
    private void VideoRotate_CCW(object sender, RoutedEventArgs e)
    {
        _videoAngle = (_videoAngle + 270) % 360;
        VideoRotateTransform.Angle = _videoAngle;
        UpdateVideoTransformText();
    }
    private void VideoRotate_180(object sender, RoutedEventArgs e)
    {
        _videoAngle = (_videoAngle + 180) % 360;
        VideoRotateTransform.Angle = _videoAngle;
        UpdateVideoTransformText();
    }
    private void VideoFlip_H(object sender, RoutedEventArgs e)
    {
        _videoFlipH = !_videoFlipH;
        VideoFlipTransform.ScaleX = _videoFlipH ? -1 : 1;
        UpdateVideoTransformText();
    }
    private void VideoFlip_V(object sender, RoutedEventArgs e)
    {
        _videoFlipV = !_videoFlipV;
        VideoFlipTransform.ScaleY = _videoFlipV ? -1 : 1;
        UpdateVideoTransformText();
    }
    private void UpdateVideoTransformText()
    {
        var parts = new List<string>();
        if (_videoAngle != 0) parts.Add($"Rotate {_videoAngle}\u00B0");
        if (_videoFlipH) parts.Add("Flip H");
        if (_videoFlipV) parts.Add("Flip V");
        TxtVideoTransform.Text = parts.Count > 0 ? string.Join(" + ", parts) : "";
        TxtVideoAngle.Text = $"{_videoAngle}\u00B0";
        // Highlight active flip buttons
        var teal = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4DB6AC"));
        var normal = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCC"));
        BtnVideoFlipH.Foreground = _videoFlipH ? teal : normal;
        BtnVideoFlipV.Foreground = _videoFlipV ? teal : normal;
    }

    // ----- Set trim to current position -----

    private void VideoSetTrimStart(object sender, RoutedEventArgs e)
    {
        if (_videoDuration.TotalSeconds <= 0) return;
        _videoTrimStartPct = VideoPreview.Position.TotalSeconds / _videoDuration.TotalSeconds;
        UpdateVideoTimeline();
    }

    private void VideoSetTrimEnd(object sender, RoutedEventArgs e)
    {
        if (_videoDuration.TotalSeconds <= 0) return;
        _videoTrimEndPct = VideoPreview.Position.TotalSeconds / _videoDuration.TotalSeconds;
        UpdateVideoTimeline();
    }

    // ----- Export: chains all operations via FFmpeg -----

    private async void VideoExport_Execute(object sender, RoutedEventArgs e)
    {
        if (_videoToolsPath == null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "MP4|*.mp4|AVI|*.avi|MKV|*.mkv",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_videoToolsPath) + "_edited"
        };
        if (dlg.ShowDialog() != true) return;

        // Stop playback
        VideoPreview.Pause();
        _isVideoPlaying = false;
        BtnVideoPlay.Content = "\u25B6";

        ShowProcessing("Exporting video...");
        try
        {
            // Determine trim
            TimeSpan? trimStart = null, trimEnd = null;
            if (_videoTrimStartPct > 0.001 || _videoTrimEndPct < 0.999)
            {
                trimStart = TimeSpan.FromSeconds(_videoTrimStartPct * _videoDuration.TotalSeconds);
                trimEnd = TimeSpan.FromSeconds(_videoTrimEndPct * _videoDuration.TotalSeconds);
            }

            // Determine crop from canvas selection
            int cw = _videoW, ch = _videoH, cx = 0, cy = 0;
            if (!_videoCropRect.IsEmpty && _videoCropRect.Width > 10 && _videoCropRect.Height > 10)
            {
                double canvasW = VideoCropCanvas.ActualWidth;
                double canvasH = VideoCropCanvas.ActualHeight;
                double videoAspect = (double)_videoW / _videoH;
                double canvasAspect = canvasW / canvasH;
                double displayW, displayH, offsetX, offsetY;
                if (videoAspect > canvasAspect)
                { displayW = canvasW; displayH = canvasW / videoAspect; offsetX = 0; offsetY = (canvasH - displayH) / 2; }
                else
                { displayH = canvasH; displayW = canvasH * videoAspect; offsetX = (canvasW - displayW) / 2; offsetY = 0; }

                double scaleX = _videoW / displayW;
                double scaleY = _videoH / displayH;
                cx = Math.Max(0, (int)Math.Round((_videoCropRect.X - offsetX) * scaleX));
                cy = Math.Max(0, (int)Math.Round((_videoCropRect.Y - offsetY) * scaleY));
                cw = Math.Min((int)Math.Round(_videoCropRect.Width * scaleX), _videoW - cx);
                ch = Math.Min((int)Math.Round(_videoCropRect.Height * scaleY), _videoH - cy);
            }

            // Single-pass export with all operations combined
            await FileToolsService.ExportVideoAsync(
                _videoToolsPath, dlg.FileName,
                trimStart, trimEnd,
                (int)_videoAngle, _videoFlipH, _videoFlipV,
                cw, ch, cx, cy, _videoW, _videoH,
                CreateProgress());

            var info = new FileInfo(dlg.FileName);
            string detail = $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}";
            ShowComplete("Video exported!", detail, dlg.FileName);
        }
        catch (Exception ex)
        {
            ProcessingOverlay.Visibility = Visibility.Collapsed;
            System.Windows.MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ----- Video crop handlers -----

    private void VideoCrop_Toggle(object sender, RoutedEventArgs e)
    {
        if (_isVideoCropMode)
            DeactivateVideoCrop();
        else
            ActivateVideoCrop();
    }

    private void InitVideoCropOverlays()
    {
        VideoCropCanvas.Children.Clear();
        for (int i = 0; i < 4; i++)
        {
            _videoCropOverlays[i] = new Rectangle
            {
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x88, 0, 0, 0))
            };
            VideoCropCanvas.Children.Add(_videoCropOverlays[i]);
        }
        _videoCropSelection = new Rectangle
        {
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#42A5F5")),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = Brushes.Transparent
        };
        VideoCropCanvas.Children.Add(_videoCropSelection);
    }

    private void VideoCrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isVideoCropDragging = true;
        _videoCropStart = e.GetPosition(VideoCropCanvas);
        VideoCropCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void VideoCrop_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isVideoCropDragging) return;
        var pos = e.GetPosition(VideoCropCanvas);
        double x = Math.Min(_videoCropStart.X, pos.X);
        double y = Math.Min(_videoCropStart.Y, pos.Y);
        double w = Math.Abs(pos.X - _videoCropStart.X);
        double h = Math.Abs(pos.Y - _videoCropStart.Y);

        // Clamp to canvas
        double canvasW = VideoCropCanvas.ActualWidth;
        double canvasH = VideoCropCanvas.ActualHeight;
        x = Math.Max(0, x); y = Math.Max(0, y);
        if (x + w > canvasW) w = canvasW - x;
        if (y + h > canvasH) h = canvasH - y;

        _videoCropRect = new Rect(x, y, w, h);
        UpdateVideoCropVisuals();
    }

    private void VideoCrop_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isVideoCropDragging = false;
        VideoCropCanvas.ReleaseMouseCapture();
    }

    private void UpdateVideoCropVisuals()
    {
        if (_videoCropSelection == null || _videoCropRect.IsEmpty) return;
        var r = _videoCropRect;
        double cw = VideoCropCanvas.ActualWidth;
        double ch = VideoCropCanvas.ActualHeight;

        Canvas.SetLeft(_videoCropSelection, r.X);
        Canvas.SetTop(_videoCropSelection, r.Y);
        _videoCropSelection.Width = Math.Max(0, r.Width);
        _videoCropSelection.Height = Math.Max(0, r.Height);

        // Overlay darkening
        Canvas.SetLeft(_videoCropOverlays[0], 0); Canvas.SetTop(_videoCropOverlays[0], 0);
        _videoCropOverlays[0].Width = cw; _videoCropOverlays[0].Height = Math.Max(0, r.Y);

        Canvas.SetLeft(_videoCropOverlays[1], 0); Canvas.SetTop(_videoCropOverlays[1], r.Bottom);
        _videoCropOverlays[1].Width = cw; _videoCropOverlays[1].Height = Math.Max(0, ch - r.Bottom);

        Canvas.SetLeft(_videoCropOverlays[2], 0); Canvas.SetTop(_videoCropOverlays[2], r.Y);
        _videoCropOverlays[2].Width = Math.Max(0, r.X); _videoCropOverlays[2].Height = Math.Max(0, r.Height);

        Canvas.SetLeft(_videoCropOverlays[3], r.Right); Canvas.SetTop(_videoCropOverlays[3], r.Y);
        _videoCropOverlays[3].Width = Math.Max(0, cw - r.Right); _videoCropOverlays[3].Height = Math.Max(0, r.Height);

        // Convert to video pixel coordinates
        double videoAspect = (double)_videoW / _videoH;
        double canvasAspect = cw / ch;
        double displayW, displayH, offsetX, offsetY;
        if (videoAspect > canvasAspect)
        {
            displayW = cw; displayH = cw / videoAspect;
            offsetX = 0; offsetY = (ch - displayH) / 2;
        }
        else
        {
            displayH = ch; displayW = ch * videoAspect;
            offsetX = (cw - displayW) / 2; offsetY = 0;
        }

        double scaleX = _videoW / displayW;
        double scaleY = _videoH / displayH;
        int pixX = Math.Max(0, (int)Math.Round((r.X - offsetX) * scaleX));
        int pixY = Math.Max(0, (int)Math.Round((r.Y - offsetY) * scaleY));
        int pixW = Math.Min((int)Math.Round(r.Width * scaleX), _videoW - pixX);
        int pixH = Math.Min((int)Math.Round(r.Height * scaleY), _videoH - pixY);

        TxtVideoCropInfo.Text = pixW > 0 && pixH > 0 ? $"{pixW}x{pixH} at {pixX},{pixY}" : "";
        TxtVideoCropX.Text = pixX.ToString();
        TxtVideoCropY.Text = pixY.ToString();
        TxtVideoCropW.Text = Math.Max(0, pixW).ToString();
        TxtVideoCropH.Text = Math.Max(0, pixH).ToString();
    }

    // =====================================================================
    //  18. Extract Audio
    // =====================================================================

    private string? _extAudioOutputFolder;

    private void ExtractAudio_SelectFiles(object sender, RoutedEventArgs e)
    {
        if (!CheckFFmpeg()) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Video Files|*.mp4;*.avi;*.mov;*.mkv;*.wmv;*.webm;*.m4v|All Files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        AddVideosToExtractList(dlg.FileNames);
        ShowConfigState("extract_audio");
    }

    private void ExtractAudio_SelectDrop(object sender, DragEventArgs e)
    {
        if (!CheckFFmpeg()) return;
        var files = GetDroppedFiles(e, IsVideoFile);
        if (files.Length == 0) return;
        AddVideosToExtractList(files);
        ShowConfigState("extract_audio");
    }

    private void ExtractAudio_AddMoreBtn(object sender, RoutedEventArgs e)
    {
        if (!CheckFFmpeg()) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Video Files|*.mp4;*.avi;*.mov;*.mkv;*.wmv;*.webm;*.m4v|All Files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() == true)
            AddVideosToExtractList(dlg.FileNames);
    }

    private void ExtractAudio_AddFolder(object sender, RoutedEventArgs e)
    {
        if (!CheckFFmpeg()) return;
        var fbd = new System.Windows.Forms.FolderBrowserDialog { Description = "Select folder with video files" };
        if (fbd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var videoExts = new[] { ".mp4", ".avi", ".mov", ".mkv", ".wmv", ".webm", ".m4v" };
        var files = Directory.GetFiles(fbd.SelectedPath)
            .Where(f => videoExts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))
            .ToArray();
        if (files.Length > 0) AddVideosToExtractList(files);
    }

    private void ExtractAudio_Remove(object sender, RoutedEventArgs e)
    {
        RemoveFromList(_extractAudioFiles, sender);
        UpdateExtAudioFooter();
    }

    private void ExtractAudio_BrowseFolder(object sender, RoutedEventArgs e)
    {
        var fbd = new System.Windows.Forms.FolderBrowserDialog { Description = "Select output folder" };
        if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _extAudioOutputFolder = fbd.SelectedPath;
            TxtExtAudioOutputFolder.Text = _extAudioOutputFolder;
        }
    }

    private async void AddVideosToExtractList(string[] paths)
    {
        // Set default output folder
        if (_extAudioOutputFolder == null && paths.Length > 0)
        {
            _extAudioOutputFolder = System.IO.Path.GetDirectoryName(paths[0]);
            TxtExtAudioOutputFolder.Text = _extAudioOutputFolder;
        }

        foreach (var path in paths)
        {
            if (_extractAudioFiles.Any(f => f.FilePath == path)) continue;
            var info = new FileInfo(path);
            string resolution = "";
            string durText = "";
            try
            {
                var (duration, w, h, codec) = await FileToolsService.GetVideoInfoAsync(path);
                resolution = $"{Math.Min(w, h)}p";
                if (h > w) resolution = $"{w}p"; // portrait
                durText = FileToolsService.FormatTimeSpan(duration);
            }
            catch { }
            _extractAudioFiles.Add(new FileItem
            {
                Index = _extractAudioFiles.Count + 1,
                FilePath = path,
                FileName = System.IO.Path.GetFileName(path),
                FileSize = FileToolsService.FormatFileSize(info.Length),
                Extra = resolution,
                DurationText = durText
            });
        }
        UpdateExtAudioFooter();
    }

    private void UpdateExtAudioFooter()
    {
        TxtExtAudioFileCount.Text = $"{_extractAudioFiles.Count} files selected";
        long total = 0;
        foreach (var f in _extractAudioFiles)
        {
            try { total += new FileInfo(f.FilePath).Length; } catch { }
        }
        TxtExtAudioTotalSize.Text = FileToolsService.FormatFileSize(total);
    }

    private async void ExtractAudio_Execute(object sender, RoutedEventArgs e)
    {
        if (_extractAudioFiles.Count == 0) return;
        var format = (CmbExtAudioFormat.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLowerInvariant() ?? "mp3";
        string ext = format switch { "wav" => ".wav", "aac" => ".aac", "flac" => ".flac", _ => ".mp3" };
        bool embedThumb = ChkExtAudioThumb.IsChecked == true;
        bool overwrite = RbExtAudioOverwrite.IsChecked == true;

        string? outputDir = overwrite ? null : _extAudioOutputFolder;
        if (!overwrite && string.IsNullOrEmpty(outputDir))
        {
            MessageBox.Show("Select an output folder first.", "No Output Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ShowProcessing("Extracting audio...");
        int count = _extractAudioFiles.Count;
        long totalSize = 0;
        string? lastFile = null;

        try
        {
            for (int i = 0; i < count; i++)
            {
                var file = _extractAudioFiles[i];
                string outPath;
                if (overwrite)
                {
                    string dir = System.IO.Path.GetDirectoryName(file.FilePath)!;
                    string name = System.IO.Path.GetFileNameWithoutExtension(file.FilePath);
                    outPath = System.IO.Path.Combine(dir, name + ext);
                }
                else
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(file.FilePath);
                    outPath = System.IO.Path.Combine(outputDir!, name + ext);
                }

                if (embedThumb)
                    await FileToolsService.ExtractAudioWithThumbnailAsync(file.FilePath, outPath, CreateProgress());
                else
                    await FileToolsService.ExtractAudioAsync(file.FilePath, outPath, CreateProgress());

                totalSize += new FileInfo(outPath).Length;
                lastFile = outPath;
                MainProgress.Value = (int)((i + 1) * 100.0 / count);
                TxtProgressPct.Text = $"{(int)((i + 1) * 100.0 / count)}%";
            }

            string detail = $"{count} file(s) \u2014 {FileToolsService.FormatFileSize(totalSize)}";
            string folder = overwrite
                ? System.IO.Path.GetDirectoryName(_extractAudioFiles[0].FilePath)!
                : outputDir!;
            ShowComplete($"{count} audio track(s) extracted!", detail,
                filePath: count == 1 ? lastFile : null, folderPath: folder);
        }
        catch (Exception ex) { ProcessingOverlay.Visibility = Visibility.Collapsed; System.Windows.MessageBox.Show(ex.Message); }
    }

    // =====================================================================
    //  Trim Audio (multi-file with per-item waveform)
    // =====================================================================

    private readonly ObservableCollection<FileItem> _trimAudioFiles = new();
    private readonly List<AudioTrimItem> _trimAudioItems = new();
    private enum WfDragMode { None, Left, Right, Seek }
    private WfDragMode _wfDragMode = WfDragMode.None;
    private AudioTrimItem? _activeDragItem;
    private DispatcherTimer? _audioPlayTimer;
    private AudioTrimItem? _playingAudioItem;
    private string? _trimOutputFolder;

    private void TrimAudio_SelectFiles(object sender, RoutedEventArgs e)
    {
        if (!CheckFFmpeg()) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.wav;*.flac;*.aac;*.m4a;*.ogg;*.wma;*.opus|All Files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        AddTrimAudioFiles(dlg.FileNames);
        ShowConfigState("trim_audio");
    }

    private void TrimAudio_SelectDrop(object sender, DragEventArgs e)
    {
        if (!CheckFFmpeg()) return;
        var files = GetDroppedFiles(e, f => FileToolsService.IsAudioExtension(System.IO.Path.GetExtension(f)));
        if (files.Length == 0) return;
        AddTrimAudioFiles(files);
        ShowConfigState("trim_audio");
    }

    private void TrimAudio_AddMore(object sender, RoutedEventArgs e)
    {
        if (!CheckFFmpeg()) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.wav;*.flac;*.aac;*.m4a;*.ogg;*.wma;*.opus|All Files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() == true)
            AddTrimAudioFiles(dlg.FileNames);
    }

    private void TrimAudio_Remove(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string path) return;
        var fi = _trimAudioFiles.FirstOrDefault(f => f.FilePath == path);
        if (fi != null) _trimAudioFiles.Remove(fi);
        var item = _trimAudioItems.FirstOrDefault(a => a.FilePath == path);
        if (item != null)
        {
            // Stop player if playing
            if (_playingAudioItem == item) StopTrimPlayback();
            item.Player?.Stop();
            if (item.WaveformTempPath != null) try { File.Delete(item.WaveformTempPath); } catch { }
            if (item.WfRow != null) TrimWaveformPanel.Children.Remove(item.WfRow);
            _trimAudioItems.Remove(item);
        }
        RenumberList(_trimAudioFiles);
        UpdateTrimSidebarInfo();
    }

    private async void AddTrimAudioFiles(string[] paths)
    {
        // Set default output folder from first file
        if (_trimOutputFolder == null && paths.Length > 0)
        {
            _trimOutputFolder = System.IO.Path.GetDirectoryName(paths[0]);
            TxtTrimOutputFolder.Text = _trimOutputFolder;
        }

        foreach (var path in paths)
        {
            if (_trimAudioItems.Any(a => a.FilePath == path)) continue;

            var info = new FileInfo(path);
            string ext = System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            TimeSpan dur = TimeSpan.Zero;
            try { dur = await FileToolsService.GetAudioDurationAsync(path); } catch { }

            // Add to sidebar list
            _trimAudioFiles.Add(new FileItem
            {
                Index = _trimAudioFiles.Count + 1,
                FilePath = path,
                FileName = System.IO.Path.GetFileName(path),
                FileSize = FileToolsService.FormatTimeSpan(dur),
                Extra = $"{ext} \u2022 {FileToolsService.FormatFileSize(info.Length)}"
            });

            // Create trim item with waveform
            var item = new AudioTrimItem
            {
                FilePath = path,
                FileName = System.IO.Path.GetFileName(path),
                Duration = dur
            };
            _trimAudioItems.Add(item);

            // Build waveform row UI
            var row = BuildWaveformRow(item);
            item.WfRow = row;
            TrimWaveformPanel.Children.Add(row);

            // Generate waveform async
            _ = GenerateWaveformForItem(item);
        }
        UpdateTrimSidebarInfo();
    }

    private void UpdateTrimSidebarInfo()
    {
        TxtTrimFileCount.Text = $"Files ({_trimAudioItems.Count})";
        var total = TimeSpan.Zero;
        foreach (var item in _trimAudioItems) total += item.Duration;
        TxtTrimTotalDuration.Text = FileToolsService.FormatTimeSpan(total);
    }

    private async Task GenerateWaveformForItem(AudioTrimItem item)
    {
        try
        {
            item.WaveformTempPath = await FileToolsService.GenerateWaveformAsync(item.FilePath, 1200, 100);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(item.WaveformTempPath);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            if (item.WfImage != null) item.WfImage.Source = bmp;
            if (item.TxtLoading != null) item.TxtLoading.Visibility = Visibility.Collapsed;
        }
        catch
        {
            if (item.TxtLoading != null) item.TxtLoading.Text = "Could not generate waveform";
        }
    }

    private Border BuildWaveformRow(AudioTrimItem item)
    {
        const double WF_HEIGHT = 100;
        var teal = (Color)ColorConverter.ConvertFromString("#4DB6AC");
        var tealBrush = new SolidColorBrush(teal);

        // Outer border
        var rowBorder = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E22")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0)
        };

        var outerStack = new StackPanel();
        rowBorder.Child = outerStack;

        // Header: filename
        var header = new TextBlock
        {
            Text = item.FileName,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCCCCC")),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        outerStack.Children.Add(header);

        // Content: play + waveform + remaining
        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        item.WfContentGrid = contentGrid;
        outerStack.Children.Add(contentGrid);

        // Play button + duration
        var playStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var playBtn = new System.Windows.Controls.Button
        {
            Content = "\u25B6",
            FontSize = 18,
            Width = 36,
            Height = 36,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        playBtn.Click += (s, e) => ToggleTrimPlayback(item);
        item.BtnPlay = playBtn;
        playStack.Children.Add(playBtn);

        var durText = new TextBlock
        {
            Text = FileToolsService.FormatTimeSpan(item.Duration),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
            FontSize = 10,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        item.TxtPlayTime = durText;
        playStack.Children.Add(durText);
        Grid.SetColumn(playStack, 0);
        contentGrid.Children.Add(playStack);

        // Waveform area
        var wfBorder = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111111")),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Height = WF_HEIGHT,
            Margin = new Thickness(8, 0, 8, 0)
        };
        var wfGrid = new Grid();
        wfBorder.Child = wfGrid;

        // Waveform image
        var wfImage = new Image
        {
            Stretch = Stretch.Fill,
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(wfImage, BitmapScalingMode.HighQuality);
        item.WfImage = wfImage;
        wfGrid.Children.Add(wfImage);

        // Canvas for handles
        var canvas = new Canvas { Background = Brushes.Transparent };
        item.WfCanvas = canvas;
        wfGrid.Children.Add(canvas);

        // Selection
        var selection = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0x33, 0x4D, 0xB6, 0xAC)), Height = WF_HEIGHT };
        Canvas.SetTop(selection, 0);
        item.WfSelection = selection;
        canvas.Children.Add(selection);

        // Left handle
        var handleL = new Border
        {
            Width = 12, Height = WF_HEIGHT,
            Background = new SolidColorBrush(Color.FromArgb(0x88, 0x4D, 0xB6, 0xAC)),
            Cursor = System.Windows.Input.Cursors.SizeWE,
            BorderBrush = tealBrush, BorderThickness = new Thickness(2, 0, 0, 0),
            Child = new Border { Width = 4, Height = 24, Background = tealBrush, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center }
        };
        Canvas.SetTop(handleL, 0);
        item.WfHandleLeft = handleL;
        canvas.Children.Add(handleL);

        // Right handle
        var handleR = new Border
        {
            Width = 12, Height = WF_HEIGHT,
            Background = new SolidColorBrush(Color.FromArgb(0x88, 0x4D, 0xB6, 0xAC)),
            Cursor = System.Windows.Input.Cursors.SizeWE,
            BorderBrush = tealBrush, BorderThickness = new Thickness(0, 0, 2, 0),
            Child = new Border { Width = 4, Height = 24, Background = tealBrush, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center }
        };
        Canvas.SetTop(handleR, 0);
        item.WfHandleRight = handleR;
        canvas.Children.Add(handleR);

        // Playhead
        var playhead = new Border
        {
            Width = 2, Height = WF_HEIGHT,
            Background = Brushes.White, IsHitTestVisible = false
        };
        Canvas.SetTop(playhead, 0);
        item.WfPlayhead = playhead;
        canvas.Children.Add(playhead);

        // Darkening
        var darkenL = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0xAA, 0, 0, 0)), Height = WF_HEIGHT, IsHitTestVisible = false };
        Canvas.SetLeft(darkenL, 0); Canvas.SetTop(darkenL, 0);
        item.WfDarkenLeft = darkenL;
        canvas.Children.Add(darkenL);

        var darkenR = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0xAA, 0, 0, 0)), Height = WF_HEIGHT, IsHitTestVisible = false };
        Canvas.SetTop(darkenR, 0);
        item.WfDarkenRight = darkenR;
        canvas.Children.Add(darkenR);

        // Loading text
        var loadingTxt = new TextBlock
        {
            Text = "Generating waveform...",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
            FontSize = 11,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        item.TxtLoading = loadingTxt;
        wfGrid.Children.Add(loadingTxt);

        // Mouse handlers on canvas
        canvas.MouseLeftButtonDown += (s, ev) =>
        {
            double canvasW = canvas.ActualWidth;
            if (canvasW <= 0) return;
            var pos = ev.GetPosition(canvas);
            double leftX = item.TrimStartPct * canvasW;
            double rightX = item.TrimEndPct * canvasW;

            if (Math.Abs(pos.X - leftX) < 15)
                _wfDragMode = WfDragMode.Left;
            else if (Math.Abs(pos.X - rightX) < 15)
                _wfDragMode = WfDragMode.Right;
            else
            {
                _wfDragMode = WfDragMode.Seek;
                SeekTrimAudioToPosition(item, pos.X / canvasW);
            }
            _activeDragItem = item;
            canvas.CaptureMouse();
            ev.Handled = true;
        };
        canvas.MouseMove += (s, ev) =>
        {
            if (_wfDragMode == WfDragMode.None || _activeDragItem != item) return;
            double canvasW = canvas.ActualWidth;
            if (canvasW <= 0) return;
            double pct = Math.Max(0, Math.Min(1, ev.GetPosition(canvas).X / canvasW));
            if (_wfDragMode == WfDragMode.Left)
            {
                item.TrimStartPct = Math.Min(pct, item.TrimEndPct - 0.01);
                UpdateItemWaveformHandles(item);
            }
            else if (_wfDragMode == WfDragMode.Right)
            {
                item.TrimEndPct = Math.Max(pct, item.TrimStartPct + 0.01);
                UpdateItemWaveformHandles(item);
            }
            else if (_wfDragMode == WfDragMode.Seek)
                SeekTrimAudioToPosition(item, pct);
        };
        canvas.MouseLeftButtonUp += (s, ev) =>
        {
            _wfDragMode = WfDragMode.None;
            _activeDragItem = null;
            canvas.ReleaseMouseCapture();
        };

        Grid.SetColumn(wfBorder, 1);
        contentGrid.Children.Add(wfBorder);

        // Remaining duration (right side)
        var remainStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        var remainLabel = new TextBlock
        {
            Text = "Remaining",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
            FontSize = 10, HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        remainStack.Children.Add(remainLabel);
        var remainVal = new TextBlock
        {
            Text = FileToolsService.FormatTimeSpan(item.Duration),
            Foreground = tealBrush,
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        item.TxtRemaining = remainVal;
        remainStack.Children.Add(remainVal);
        Grid.SetColumn(remainStack, 2);
        contentGrid.Children.Add(remainStack);

        // Time labels below waveform
        var timeLabelGrid = new Grid { Margin = new Thickness(58, 2, 80, 0) };
        var startLabel = new TextBlock
        {
            Text = "00:00.000",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666666")),
            FontSize = 10, HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        item.TxtStartLabel = startLabel;
        timeLabelGrid.Children.Add(startLabel);
        var endLabel = new TextBlock
        {
            Text = FileToolsService.FormatTimeSpan(item.Duration),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666666")),
            FontSize = 10, HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        item.TxtEndLabel = endLabel;
        timeLabelGrid.Children.Add(endLabel);
        outerStack.Children.Add(timeLabelGrid);

        // Initial handle positions (after loaded)
        canvas.Loaded += (s, ev) => UpdateItemWaveformHandles(item);

        return rowBorder;
    }

    private void UpdateItemWaveformHandles(AudioTrimItem item)
    {
        if (item.WfCanvas == null) return;
        double canvasW = item.WfCanvas.ActualWidth;
        if (canvasW <= 0) return;

        double leftX = item.TrimStartPct * canvasW;
        double rightX = item.TrimEndPct * canvasW;

        if (item.WfSelection != null) { Canvas.SetLeft(item.WfSelection, leftX); item.WfSelection.Width = Math.Max(0, rightX - leftX); }
        if (item.WfHandleLeft != null) Canvas.SetLeft(item.WfHandleLeft, leftX - 6);
        if (item.WfHandleRight != null) Canvas.SetLeft(item.WfHandleRight, rightX - 6);
        if (item.WfDarkenLeft != null) { Canvas.SetLeft(item.WfDarkenLeft, 0); item.WfDarkenLeft.Width = Math.Max(0, leftX); }
        if (item.WfDarkenRight != null) { Canvas.SetLeft(item.WfDarkenRight, rightX); item.WfDarkenRight.Width = Math.Max(0, canvasW - rightX); }

        // Update time labels
        if (item.Duration.TotalSeconds > 0)
        {
            var startTs = TimeSpan.FromSeconds(item.TrimStartPct * item.Duration.TotalSeconds);
            var endTs = TimeSpan.FromSeconds(item.TrimEndPct * item.Duration.TotalSeconds);
            if (item.TxtStartLabel != null) item.TxtStartLabel.Text = FormatMs(startTs);
            if (item.TxtEndLabel != null) item.TxtEndLabel.Text = FormatMs(endTs);
            if (item.TxtRemaining != null) item.TxtRemaining.Text = FormatMs(endTs - startTs);
        }
    }

    private static string FormatMs(TimeSpan ts) => ts.TotalHours >= 1
        ? ts.ToString(@"h\:mm\:ss\.fff")
        : ts.ToString(@"mm\:ss\.fff");

    private void ToggleTrimPlayback(AudioTrimItem item)
    {
        // Stop any other playing item
        if (_playingAudioItem != null && _playingAudioItem != item)
            StopTrimPlayback();

        if (item.Player == null)
        {
            item.Player = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                Volume = 1,
                Visibility = Visibility.Collapsed
            };
            // Must be in visual tree
            if (item.WfContentGrid != null)
                item.WfContentGrid.Children.Add(item.Player);
            item.Player.Source = new Uri(item.FilePath);
            item.Player.Play();
            item.Player.Pause();
        }

        if (item.IsPlaying)
        {
            item.Player.Pause();
            _audioPlayTimer?.Stop();
            if (item.BtnPlay != null) item.BtnPlay.Content = "\u25B6";
            item.IsPlaying = false;
            _playingAudioItem = null;
        }
        else
        {
            // Seek to trim start if before it
            double currentPct = item.Duration.TotalSeconds > 0 ? item.Player.Position.TotalSeconds / item.Duration.TotalSeconds : 0;
            if (currentPct < item.TrimStartPct || currentPct >= item.TrimEndPct)
                item.Player.Position = TimeSpan.FromSeconds(item.TrimStartPct * item.Duration.TotalSeconds);

            item.Player.Play();
            _playingAudioItem = item;
            item.IsPlaying = true;
            if (item.BtnPlay != null) item.BtnPlay.Content = "\u23F8";

            _audioPlayTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _audioPlayTimer.Tick -= TrimAudioPlayTimer_Tick;
            _audioPlayTimer.Tick += TrimAudioPlayTimer_Tick;
            _audioPlayTimer.Start();
        }
    }

    private void StopTrimPlayback()
    {
        if (_playingAudioItem == null) return;
        _playingAudioItem.Player?.Pause();
        if (_playingAudioItem.BtnPlay != null) _playingAudioItem.BtnPlay.Content = "\u25B6";
        _playingAudioItem.IsPlaying = false;
        _audioPlayTimer?.Stop();
        _playingAudioItem = null;
    }

    private void TrimAudioPlayTimer_Tick(object? sender, EventArgs e)
    {
        var item = _playingAudioItem;
        if (item?.Player == null || item.Duration.TotalSeconds <= 0) return;
        double pct = item.Player.Position.TotalSeconds / item.Duration.TotalSeconds;
        if (item.WfCanvas != null)
        {
            double canvasW = item.WfCanvas.ActualWidth;
            if (canvasW > 0 && item.WfPlayhead != null)
                Canvas.SetLeft(item.WfPlayhead, pct * canvasW);
        }
        if (item.TxtPlayTime != null)
            item.TxtPlayTime.Text = FileToolsService.FormatTimeSpan(item.Player.Position);

        // Stop at trim end
        if (pct >= item.TrimEndPct)
        {
            item.Player.Pause();
            _audioPlayTimer?.Stop();
            item.IsPlaying = false;
            if (item.BtnPlay != null) item.BtnPlay.Content = "\u25B6";
            _playingAudioItem = null;
        }
    }

    private void SeekTrimAudioToPosition(AudioTrimItem item, double pct)
    {
        if (item.Player == null)
        {
            item.Player = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                Volume = 1,
                Visibility = Visibility.Collapsed
            };
            if (item.WfContentGrid != null)
                item.WfContentGrid.Children.Add(item.Player);
            item.Player.Source = new Uri(item.FilePath);
            item.Player.Play();
            item.Player.Pause();
        }
        if (item.Duration.TotalSeconds <= 0) return;
        item.Player.Position = TimeSpan.FromSeconds(pct * item.Duration.TotalSeconds);
        if (item.WfCanvas != null)
        {
            double canvasW = item.WfCanvas.ActualWidth;
            if (canvasW > 0 && item.WfPlayhead != null)
                Canvas.SetLeft(item.WfPlayhead, pct * canvasW);
        }
        if (item.TxtPlayTime != null)
            item.TxtPlayTime.Text = FileToolsService.FormatTimeSpan(item.Player.Position);
    }

    private void TrimAudio_ApplyToAll(object sender, RoutedEventArgs e)
    {
        if (!TimeSpan.TryParse(TxtGlobalTrimStart.Text.Trim(), out var cutStart)) cutStart = TimeSpan.Zero;
        if (!TimeSpan.TryParse(TxtGlobalTrimEnd.Text.Trim(), out var cutEnd)) cutEnd = TimeSpan.FromSeconds(1);

        foreach (var item in _trimAudioItems)
        {
            if (item.Duration.TotalSeconds <= 0) continue;
            item.TrimStartPct = Math.Min(cutStart.TotalSeconds / item.Duration.TotalSeconds, 0.99);
            // Cut from end: trim end = duration - cutEnd
            double endPct = Math.Max(0, (item.Duration.TotalSeconds - cutEnd.TotalSeconds) / item.Duration.TotalSeconds);
            item.TrimEndPct = Math.Max(endPct, item.TrimStartPct + 0.01);
            UpdateItemWaveformHandles(item);
        }
    }

    private void TrimAudio_Reset(object sender, RoutedEventArgs e)
    {
        foreach (var item in _trimAudioItems)
        {
            item.TrimStartPct = 0;
            item.TrimEndPct = 1;
            UpdateItemWaveformHandles(item);
        }
    }

    private void TrimAudio_BrowseFolder(object sender, RoutedEventArgs e)
    {
        var fbd = new System.Windows.Forms.FolderBrowserDialog { Description = "Select output folder" };
        if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _trimOutputFolder = fbd.SelectedPath;
            TxtTrimOutputFolder.Text = _trimOutputFolder;
        }
    }

    private async void TrimAudio_Execute(object sender, RoutedEventArgs e)
    {
        if (_trimAudioItems.Count == 0) return;

        string outputDir = _trimOutputFolder ?? System.IO.Path.GetDirectoryName(_trimAudioItems[0].FilePath)!;
        if (!Directory.Exists(outputDir))
        {
            MessageBox.Show("Select a valid output folder.", "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Determine output format
        string? formatOverride = null;
        if (CmbTrimAudioFormat.SelectedIndex > 0)
            formatOverride = (CmbTrimAudioFormat.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLowerInvariant();

        StopTrimPlayback();
        ShowProcessing("Trimming audio...");
        int count = _trimAudioItems.Count;
        long totalSize = 0;
        string? lastFile = null;

        try
        {
            for (int i = 0; i < count; i++)
            {
                var item = _trimAudioItems[i];
                if (item.Duration.TotalSeconds <= 0) continue;

                var start = TimeSpan.FromSeconds(item.TrimStartPct * item.Duration.TotalSeconds);
                var end = TimeSpan.FromSeconds(item.TrimEndPct * item.Duration.TotalSeconds);
                if (end <= start) continue;

                string ext = formatOverride != null ? $".{formatOverride}" : System.IO.Path.GetExtension(item.FilePath);
                string name = System.IO.Path.GetFileNameWithoutExtension(item.FilePath) + "_trimmed" + ext;
                string outPath = System.IO.Path.Combine(outputDir, name);

                await FileToolsService.TrimAudioAsync(item.FilePath, outPath, start, end, CreateProgress());
                totalSize += new FileInfo(outPath).Length;
                lastFile = outPath;
                MainProgress.Value = (int)((i + 1) * 100.0 / count);
                TxtProgressPct.Text = $"{(int)((i + 1) * 100.0 / count)}%";
            }

            string detail = $"{count} file(s) \u2014 {FileToolsService.FormatFileSize(totalSize)}";
            ShowComplete($"{count} audio file(s) trimmed!", detail,
                filePath: count == 1 ? lastFile : null, folderPath: outputDir);
        }
        catch (Exception ex) { ProcessingOverlay.Visibility = Visibility.Collapsed; MessageBox.Show(ex.Message); }
    }

    // =====================================================================
    //  YouTube Download
    // =====================================================================

    private async void Yt_Fetch(object sender, RoutedEventArgs e)
    {
        string url = TxtYtUrl.Text.Trim();
        if (string.IsNullOrEmpty(url)) { MessageBox.Show("Enter a YouTube URL."); return; }
        if (!FileToolsService.IsYtDlpAvailable())
        {
            MessageBox.Show("yt-dlp is required.\n\nInstall: https://github.com/yt-dlp/yt-dlp",
                "yt-dlp Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _ytUrl = url;
        _ytVideos.Clear();

        // Show spinner on select view
        YtSpinner.Visibility = Visibility.Visible;
        TxtYtSpinner.Text = "Fetching video info...";
        var spinAnim = new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(1))
        { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
        YtSpinnerRotate.BeginAnimation(System.Windows.Media.Animation.Storyboard.TargetPropertyProperty, null);
        YtSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, spinAnim);

        try
        {
            var videos = await FileToolsService.FetchYouTubeVideosAsync(url);

            // Stop spinner, show config
            YtSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            YtSpinner.Visibility = Visibility.Collapsed;
            ShowConfigState("youtube_dl");

            TxtYtTitle.Text = videos.Count == 1 ? videos[0].title : $"Playlist \u2014 {videos.Count} videos";
            TxtYtDetail.Text = $"{videos.Count} video{(videos.Count != 1 ? "s" : "")} found";

            foreach (var (title, duration, videoUrl, thumbnail) in videos)
            {
                var item = new YtVideoItem
                {
                    Title = title,
                    Duration = duration,
                    VideoUrl = videoUrl,
                    ThumbnailUrl = thumbnail
                };

                // Build thumbnail URL — use YouTube thumbnail from video ID if not provided
                string thumbUrl = thumbnail;
                if (string.IsNullOrEmpty(thumbUrl))
                {
                    var idMatch = System.Text.RegularExpressions.Regex.Match(
                        videoUrl, @"(?:v=|youtu\.be/|/embed/)([a-zA-Z0-9_-]{11})");
                    if (idMatch.Success)
                        thumbUrl = $"https://i.ytimg.com/vi/{idMatch.Groups[1].Value}/mqdefault.jpg";
                }
                if (!string.IsNullOrEmpty(thumbUrl))
                {
                    try
                    {
                        // Don't use CacheOption.OnLoad for remote URLs — let WPF download async
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(thumbUrl);
                        bmp.DecodePixelWidth = 160;
                        bmp.EndInit();
                        item.Thumbnail = bmp;
                    }
                    catch { }
                }

                _ytVideos.Add(item);
            }
        }
        catch (Exception ex)
        {
            YtSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            YtSpinner.Visibility = Visibility.Collapsed;
            ShowConfigState("youtube_dl");
            TxtYtTitle.Text = "Failed to fetch";
            TxtYtDetail.Text = ex.Message;
        }
    }

    private void YtMode_Changed(object sender, RoutedEventArgs e)
    {
        if (YtVideoOptions == null || ChkYtEmbedThumb == null) return;
        bool isVideo = RbYtVideo?.IsChecked == true;
        YtVideoOptions.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
        ChkYtEmbedThumb.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Yt_Download(object sender, RoutedEventArgs e)
    {
        var selected = _ytVideos.Where(v => v.IsSelected).ToList();
        if (selected.Count == 0) { MessageBox.Show("Select at least one video."); return; }

        var folderDlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select download folder" };
        if (folderDlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        BtnYtDownload.IsEnabled = false;
        BtnYtStop.Visibility = Visibility.Visible;
        _ytCancelSource = new CancellationTokenSource();

        bool isAudio = RbYtAudio.IsChecked == true;
        string quality = (CmbYtQuality.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLowerInvariant() ?? "best";
        bool embedThumb = ChkYtEmbedThumb.IsChecked == true;

        int total = selected.Count;
        int completed = 0;

        try
        {
            foreach (var video in selected)
            {
                if (_ytCancelSource.Token.IsCancellationRequested)
                {
                    video.Status = "Cancelled";
                    continue;
                }

                video.Status = "Downloading";
                video.Progress = 0;
                TxtYtOverallProgress.Text = $"Downloading {completed + 1} of {total}...";

                try
                {
                    var progress = new Progress<(int percent, string status)>(p =>
                    {
                        video.Progress = p.percent;
                    });

                    await FileToolsService.DownloadSingleVideoAsync(
                        video.VideoUrl, folderDlg.SelectedPath, quality, isAudio, embedThumb, progress);

                    video.Status = "Downloaded";
                    video.Progress = 100;
                    completed++;
                }
                catch
                {
                    video.Status = "Error";
                }
            }

            // Mark remaining as cancelled if stopped
            foreach (var v in selected.Where(v => v.Status == "Pending"))
                v.Status = "Cancelled";

            TxtYtOverallProgress.Text = $"Done \u2014 {completed} of {total} downloaded";
        }
        finally
        {
            BtnYtDownload.IsEnabled = true;
            BtnYtStop.Visibility = Visibility.Collapsed;
            _ytCancelSource = null;
        }
    }

    private void Yt_Stop(object sender, RoutedEventArgs e)
    {
        _ytCancelSource?.Cancel();
        TxtYtOverallProgress.Text = "Stopping...";
    }

    private void Yt_SelectAll(object sender, RoutedEventArgs e)
    {
        foreach (var v in _ytVideos) v.IsSelected = true;
    }

    private void Yt_DeselectAll(object sender, RoutedEventArgs e)
    {
        foreach (var v in _ytVideos) v.IsSelected = false;
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
    public string DurationText { get; set; } = "";
    public BitmapImage? Thumbnail { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// =========================================================================
//  Audio trim item — holds per-file trim state and UI references
// =========================================================================

public class AudioTrimItem
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public double TrimStartPct { get; set; } = 0;
    public double TrimEndPct { get; set; } = 1;
    public bool IsPlaying { get; set; }
    public string? WaveformTempPath { get; set; }

    // Programmatic UI refs
    public Border? WfRow { get; set; }
    public Grid? WfContentGrid { get; set; }
    public Image? WfImage { get; set; }
    public Canvas? WfCanvas { get; set; }
    public Rectangle? WfSelection { get; set; }
    public Border? WfHandleLeft { get; set; }
    public Border? WfHandleRight { get; set; }
    public Border? WfPlayhead { get; set; }
    public Rectangle? WfDarkenLeft { get; set; }
    public Rectangle? WfDarkenRight { get; set; }
    public TextBlock? TxtLoading { get; set; }
    public TextBlock? TxtStartLabel { get; set; }
    public TextBlock? TxtEndLabel { get; set; }
    public TextBlock? TxtRemaining { get; set; }
    public TextBlock? TxtPlayTime { get; set; }
    public System.Windows.Controls.Button? BtnPlay { get; set; }
    public MediaElement? Player { get; set; }
}

// =========================================================================
//  YouTube video item for download list
// =========================================================================

public class YtVideoItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _status = "Pending";
    private int _progress;

    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    public string Title { get; set; } = "";
    public string Duration { get; set; } = "";
    public string VideoUrl { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
    public BitmapImage? Thumbnail { get; set; }
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
    public int Progress { get => _progress; set { _progress = value; OnPropertyChanged(); } }

    // Status color for display
    public System.Windows.Media.Brush StatusColor => Status switch
    {
        "Downloaded" => new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#4CAF50")),
        "Downloading" => new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#42A5F5")),
        "Error" => new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF5350")),
        "Cancelled" => new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#888")),
        _ => new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#666"))
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(Status))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusColor)));
    }
}
