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
        ("pdf_editor",     "PDF Editor",      "All-in-one PDF workspace",            "#00897B", "\u2630", "PDF Tools"),
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
        ("fill_sign",      "Fill & Sign",     "Fill forms, sign, stamp a PDF",       "#26A69A", "\u270d", "PDF Tools"),
        ("image_editor",   "Image Editor",    "All-in-one image editor",             "#7C4DFF", "\u2B1C", "Image Tools"),
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
    private bool _suppressUnsavedPrompt;

    // =====================================================================
    //  Observable collections
    // =====================================================================

    private string? _pePdfPath;
    private readonly List<PdfEditorPageEntry> _pePages = new();
    private string _pdfEditorActiveTool = "reorder";
    private readonly HashSet<PdfEditorPageEntry> _peSelectedPages = new();
    private double _peThumbSize = 150;

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
    private bool _ytGridView = true; // grid is the default view

    // Search results live on the search page; "Go to download"/"Open playlist" move to the download page.
    private enum YtScreen { Hero, SearchVideos, SearchPlaylists, Download }
    private YtScreen _ytScreen = YtScreen.Hero;
    private YtScreen _ytBackTarget = YtScreen.Hero;
    // Search filters (video search only): upload-date + sort drive YouTube's sp= token.
    private string _ytUploadDate = "any";          // any|today|week|month|year
    private int _ytSort;                            // 0 Relevance, 1 Upload date, 2 View count, 3 Rating
    private bool _ytFiltersReady;                   // suppress filter handlers during programmatic init
    private bool _ytSyncingSort;                    // guard while mirroring the two sort combos
    // Incremental keyword search: load results in batches of 10 (infinite scroll).
    private string _ytSearchQuery = "";             // current keyword for paging (video or playlist search)
    private bool _ytSearchIsPlaylist;               // current keyword search is a playlist search
    private int _ytLoadedCount;                     // results fetched so far for the current query
    private bool _ytLoadingMore;
    private bool _ytNoMore = true;                  // true until a keyword search starts
    private const int YtBatchSize = 12;   // 4 columns × 3 rows per batch
    private const int YtMaxResults = 120;
    // Cached search results + header text, so "Back" restores the search page without re-fetching.
    private readonly List<YtVideoItem> _ytSearchCache = new();
    private string _ytResultsTitle = "";
    private string _ytResultsDetail = "";
    // Snapshot of the download list's natural (playlist) order, for the "Playlist order" sort option.
    private readonly List<YtVideoItem> _ytDownloadOriginal = new();
    private bool _ytDownloadSortSyncing;

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
    //  Fill & Sign state
    // =====================================================================

    private string? _fsPdfPath;
    private int _fsCurrentPage;
    private int _fsPageCount;
    private const int FsDpi = 150;
    private readonly List<Llamashot.Models.FillElement> _fsElements = new();
    private readonly Dictionary<int, List<Llamashot.Core.DetectedRegion>> _fsRegionCache = new();
    private string _fsMode = "select";
    private string _fsFont = "Arial";
    private double _fsSize = 12;
    private bool _fsBold, _fsItalic;
    private string _fsColor = "#000000";

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
        YtVideoGrid.ItemsSource = _ytVideos;
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
        _toolPanels["pdf_editor"] = PanelPdfEditor;
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
        _toolPanels["fill_sign"] = PanelFillSign;
        _toolPanels["compress_image"] = PanelCompressImage;
        _toolPanels["resize_image"] = PanelResizeImage;
        _toolPanels["crop_image"] = PanelCropImage;
        _toolPanels["rotate_flip"] = PanelRotateFlip;
        _toolPanels["convert_format"] = PanelConvertFormat;
        _toolPanels["image_editor"] = PanelImageEditor;
        _toolPanels["compress_office"] = PanelCompressOffice;
        _toolPanels["video_tools"] = PanelVideoTools;
        _toolPanels["extract_audio"] = PanelExtractAudio;
        _toolPanels["trim_audio"] = PanelTrimAudio;
        _toolPanels["youtube_dl"] = PanelYouTubeDl;

        _selectViews["pdf_editor"] = PdfEditorSelectView;
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
        _selectViews["fill_sign"] = FillSignSelectView;
        _selectViews["compress_image"] = CompressImgSelectView;
        _selectViews["resize_image"] = ResizeSelectView;
        _selectViews["crop_image"] = CropSelectView;
        _selectViews["rotate_flip"] = RotateFlipSelectView;
        _selectViews["convert_format"] = ConvertSelectView;
        _selectViews["image_editor"] = ImgEditorSelectView;
        _selectViews["compress_office"] = CompressOfficeSelectView;
        _selectViews["video_tools"] = VideoSelectView;
        _selectViews["extract_audio"] = ExtractAudioSelectView;
        _selectViews["trim_audio"] = TrimAudioSelectView;
        _selectViews["youtube_dl"] = YtSelectView;

        _configViews["pdf_editor"] = PdfEditorConfigView;
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
        _configViews["image_editor"] = ImgEditorConfigView;
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

    private void ShowProcessing(string message, bool indeterminate = false)
    {
        TxtProcessing.Text = message;
        MainProgress.Value = 0;
        TxtProgressPct.Text = "0%";

        // Indeterminate work (searches) shows a spinner; measurable work keeps the % bar.
        MainSpinner.Visibility = indeterminate ? Visibility.Visible : Visibility.Collapsed;
        MainProgress.Visibility = indeterminate ? Visibility.Collapsed : Visibility.Visible;
        TxtProgressPct.Visibility = indeterminate ? Visibility.Collapsed : Visibility.Visible;
        if (indeterminate) StartMainSpinner(); else StopMainSpinner();

        FadeIn(ProcessingOverlay);
    }

    private void StartMainSpinner()
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
        { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
        MainSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    private void StopMainSpinner() => MainSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);

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
        _pePdfPath = null;
        _pePages.Clear();
        _peSelectedPages.Clear();
        PePageGrid.Children.Clear();
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
        _ieImagePath = null;
        _ieOriginalImage = null;
        _ieRotation = 0; _ieFlipH = false; _ieFlipV = false;
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
        foreach (var v in _ytVideos) v.PropertyChanged -= YtItem_PropertyChanged;
        _ytUrl = null;
        _ytVideos.Clear();
        _ytSearchCache.Clear();
        _ytScreen = YtScreen.Hero;
        _ytBackTarget = YtScreen.Hero;
        System.Windows.Data.CollectionViewSource.GetDefaultView(_ytVideos).Filter = null;
        _ytGridView = true;
        if (TxtYtSearch != null) TxtYtSearch.Text = "";
        if (TxtYtSearchPlaceholder != null) TxtYtSearchPlaceholder.Visibility = Visibility.Visible;
        if (TxtYtUrlTop != null) TxtYtUrlTop.Text = "";
        if (TxtYtDownloadFilter != null) TxtYtDownloadFilter.Text = "";
        if (TxtYtDownloadFilterPh != null) TxtYtDownloadFilterPh.Visibility = Visibility.Visible;
        _ytDownloadOriginal.Clear();
        ApplyYtViewMode();
        ApplyYtScreen();
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

        // Reset Fill & Sign
        _fsPdfPath = null;
        _fsElements.Clear();
        _fsRegionCache.Clear();
        _fsCurrentPage = 0;
        if (FillSignSelectView != null) FillSignSelectView.Visibility = Visibility.Visible;
        if (FillSignWorkspace != null) FillSignWorkspace.Visibility = Visibility.Collapsed;
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
        FadeOut(CompleteOverlay, 200, () =>
        {
            _suppressUnsavedPrompt = true;
            GoBack_Click(sender, new RoutedEventArgs());
            _suppressUnsavedPrompt = false;
        });
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

    private bool HasUnsavedWork() => _currentToolId switch
    {
        "pdf_editor"      => _pePdfPath != null,
        "merge_pdf"       => _mergePdfFiles.Count > 0,
        "split_pdf"       => _splitPdfPath != null,
        "compress_pdf"    => _compressPdfPath != null,
        "pdf_to_images"   => _pdfToImgPath != null,
        "images_to_pdf"   => _imgToPdfFiles.Count > 0,
        "rotate_pdf"      => _rotatePdfPath != null,
        "watermark"       => _watermarkPdfPath != null,
        "page_numbers"    => _pageNumPdfPath != null,
        "extract_pages"   => _extractPdfPath != null,
        "insert_pages"    => _insertBasePath != null,
        "image_editor"    => _ieImagePath != null,
        "compress_image"  => _compressImgFiles.Count > 0,
        "resize_image"    => _resizeSourcePath != null,
        "crop_image"      => _cropSourcePath != null,
        "rotate_flip"     => _rotateFlipSourcePath != null,
        "convert_format"  => _convertSourcePath != null,
        "compress_office" => _compressOfficeFiles.Count > 0,
        "video_tools"     => _videoToolsPath != null,
        "extract_audio"   => _extractAudioFiles.Count > 0,
        "trim_audio"      => _trimAudioFiles.Count > 0,
        "youtube_dl"      => _ytVideos.Count > 0 || _ytCancelSource != null,
        "fill_sign"       => _fsElements.Count > 0,
        _ => false,
    };

    private bool ConfirmDiscardWork()
    {
        if (_suppressUnsavedPrompt || CompleteOverlay.Visibility == Visibility.Visible || !HasUnsavedWork())
            return true;
        return ConfirmDialog.Show(this, "Unsaved Changes",
            "You have unsaved changes. If you leave now, your work will be lost.");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ConfirmDiscardWork())
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    private void GoBack_Click(object sender, RoutedEventArgs e)
    {
        // YouTube has internal navigation levels: Download -> Search -> Hero -> tool grid.
        if (_currentToolId == "youtube_dl" && YtTryGoBack())
            return;

        if (!ConfirmDiscardWork()) return;

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
    //  0. PDF Editor (single-document workspace)
    // =====================================================================

    private void PdfEditor_SelectFile(object sender, RoutedEventArgs e)
    {
        var dlg = CreatePdfOpenDialog(false);
        if (dlg.ShowDialog() != true) return;
        _ = PeLoadPdf(dlg.FileName);
        ShowConfigState("pdf_editor");
    }

    private void PdfEditor_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsPdfFile);
        if (files.Length == 0) return;
        _ = PeLoadPdf(files[0]);
        ShowConfigState("pdf_editor");
    }

    private async Task PeLoadPdf(string path)
    {
        _pePdfPath = path;
        _pePages.Clear();
        _peSelectedPages.Clear();
        PePageGrid.Children.Clear();

        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);

            for (uint i = 0; i < pdfDoc.PageCount; i++)
            {
                using var page = pdfDoc.GetPage(i);
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = 200 });
                stream.Seek(0);

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = stream.AsStreamForRead();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                _pePages.Add(new PdfEditorPageEntry
                {
                    OriginalIndex = (int)i,
                    GlobalIndex = (int)i + 1,
                    Thumbnail = bmp,
                    SourceFile = path
                });
            }

            PeRenderPageGrid();
            PeUpdateInfo();
            SelectPeTool("reorder");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load PDF: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PeRenderPageGrid()
    {
        PePageGrid.Children.Clear();
        double thumbW = _peThumbSize;

        for (int i = 0; i < _pePages.Count; i++)
        {
            var pg = _pePages[i];
            if (pg.IsDeleted && _pdfEditorActiveTool != "delete") continue;

            var thumbBorder = new Border
            {
                Width = thumbW, Margin = new Thickness(6),
                CornerRadius = new CornerRadius(6),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    _peSelectedPages.Contains(pg) ? "#E53935" : "#444")),
                BorderThickness = _peSelectedPages.Contains(pg) ? new Thickness(3) : new Thickness(1),
                Background = Brushes.White, ClipToBounds = true, Cursor = Cursors.Hand,
                Opacity = pg.IsDeleted ? 0.3 : 1.0,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                    { BlurRadius = 6, ShadowDepth = 2, Opacity = 0.2, Color = Colors.Black }
            };

            var img = new System.Windows.Controls.Image
            {
                Source = pg.Thumbnail, Stretch = Stretch.Uniform,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(pg.Rotation)
            };
            thumbBorder.Child = img;

            var outerStack = new StackPanel { Margin = new Thickness(2) };
            outerStack.Children.Add(thumbBorder);
            outerStack.Children.Add(new TextBlock
            {
                Text = (i + 1).ToString(),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888")),
                FontSize = 11, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            });

            pg.ThumbnailBorder = thumbBorder;
            var clickPg = pg;
            thumbBorder.MouseLeftButtonDown += (s, ev) => { PePageClick(clickPg); ev.Handled = true; };

            PePageGrid.Children.Add(outerStack);
        }

        TxtPeTotalPages.Text = _pePages.Count(p => !p.IsDeleted).ToString();
    }

    private void PePageClick(PdfEditorPageEntry pg)
    {
        switch (_pdfEditorActiveTool)
        {
            case "reorder":
                PeReorder_SelectPage(pg);
                break;
            case "extract":
            case "rotate":
            case "crop":
            case "insert":
            case "split":
            case "compress":
                PeToggleSelection(pg);
                break;
            case "delete":
                pg.IsDeleted = !pg.IsDeleted;
                if (pg.ThumbnailBorder != null) pg.ThumbnailBorder.Opacity = pg.IsDeleted ? 0.3 : 1.0;
                TxtPeDeleteCount.Text = $"{_pePages.Count(p => p.IsDeleted)} pages marked";
                PeUpdateInfo();
                break;
        }
    }

    private void PeToggleSelection(PdfEditorPageEntry pg)
    {
        if (pg.ThumbnailBorder == null) return;
        var sel = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E53935"));
        var unsel = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444"));

        if (_peSelectedPages.Contains(pg))
        {
            _peSelectedPages.Remove(pg);
            pg.ThumbnailBorder.BorderBrush = unsel;
            pg.ThumbnailBorder.BorderThickness = new Thickness(1);
        }
        else
        {
            _peSelectedPages.Add(pg);
            pg.ThumbnailBorder.BorderBrush = sel;
            pg.ThumbnailBorder.BorderThickness = new Thickness(3);
        }
        PeUpdateSelectionBar();

        if (_pdfEditorActiveTool == "extract")
            TxtPeExtractCount.Text = $"{_peSelectedPages.Count} pages selected";
    }

    private void PeUpdateSelectionBar()
    {
        if (_peSelectedPages.Count > 0)
        {
            PeSelectionBar.Visibility = Visibility.Visible;
            TxtPeSelectionCount.Text = $"{_peSelectedPages.Count} Pages Selected";
        }
        else
            PeSelectionBar.Visibility = Visibility.Collapsed;
    }

    private void PeUpdateInfo()
    {
        if (_pePdfPath == null) return;
        int activePages = _pePages.Count(p => !p.IsDeleted);
        TxtPeFileName.Text = System.IO.Path.GetFileName(_pePdfPath);
        TxtPeFileInfo.Text = $"{activePages} pages \u2022 {FileToolsService.FormatFileSize(new FileInfo(_pePdfPath).Length)}";
        TxtPeTotalPages.Text = activePages.ToString();
    }

    private void PeToolSelect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string tool)
            SelectPeTool(tool);
    }

    private void SelectPeTool(string tool)
    {
        _pdfEditorActiveTool = tool;
        _peSelectedPages.Clear();
        _peReorderSelected = null;

        System.Windows.Controls.Button[] sidebarButtons =
        {
            BtnPeTool_reorder, BtnPeTool_extract, BtnPeTool_insert,
            BtnPeTool_delete, BtnPeTool_rotate, BtnPeTool_crop,
            BtnPeTool_split, BtnPeTool_compress
        };
        foreach (var btn in sidebarButtons)
        {
            btn.Background = Brushes.Transparent;
            btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAA"));
        }

        var activeBtn = tool switch
        {
            "reorder" => BtnPeTool_reorder, "extract" => BtnPeTool_extract,
            "insert" => BtnPeTool_insert, "delete" => BtnPeTool_delete,
            "rotate" => BtnPeTool_rotate, "crop" => BtnPeTool_crop,
            "split" => BtnPeTool_split, "compress" => BtnPeTool_compress,
            _ => BtnPeTool_reorder
        };
        activeBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E53935"));
        activeBtn.Foreground = Brushes.White;

        var (title, subtitle) = tool switch
        {
            "reorder" => ("Reorder Pages", "Drag pages to reorder"),
            "extract" => ("Extract Pages", "Select pages and extract to new PDF"),
            "insert" => ("Insert Pages", "Add pages from PDF or image"),
            "delete" => ("Delete Pages", "Click pages to mark for deletion"),
            "rotate" => ("Rotate Pages", "Select pages, then use rotate buttons"),
            "crop" => ("Crop Pages", "Set crop margins and apply"),
            "split" => ("Split PDF", "Enter page range to split"),
            "compress" => ("Compress PDF", "Choose quality and compress"),
            _ => ("Reorder Pages", "Drag pages to reorder")
        };
        TxtPeToolTitle.Text = title;
        TxtPeToolSubtitle.Text = subtitle;

        bool showBar = tool is "extract" or "split" or "reorder" or "compress" or "delete";
        PeToolBar.Visibility = showBar ? Visibility.Visible : Visibility.Collapsed;
        PeBarExtract.Visibility = tool == "extract" ? Visibility.Visible : Visibility.Collapsed;
        PeBarSplit.Visibility = tool == "split" ? Visibility.Visible : Visibility.Collapsed;
        PeBarReorder.Visibility = tool == "reorder" ? Visibility.Visible : Visibility.Collapsed;
        PeBarCompress.Visibility = tool == "compress" ? Visibility.Visible : Visibility.Collapsed;
        PeBarDelete.Visibility = tool == "delete" ? Visibility.Visible : Visibility.Collapsed;
        PeSelectionBar.Visibility = Visibility.Collapsed;

        PeRenderPageGrid();
    }

    // Properties panel
    private void PeProp_RotateLeft(object sender, RoutedEventArgs e) => PeRotateTarget(270);
    private void PeProp_RotateRight(object sender, RoutedEventArgs e) => PeRotateTarget(90);
    private void PeProp_Rotate180(object sender, RoutedEventArgs e) => PeRotateTarget(180);

    private void PeRotateTarget(int degrees)
    {
        var targets = GetPeTargetPages();
        foreach (var pg in targets)
        {
            pg.Rotation = (pg.Rotation + degrees) % 360;
            if (pg.ThumbnailBorder?.Child is System.Windows.Controls.Image img)
                ((RotateTransform)img.RenderTransform).Angle = pg.Rotation;
        }
    }

    private List<PdfEditorPageEntry> GetPeTargetPages()
    {
        int scope = CmbPeApplyTo.SelectedIndex;
        return scope switch
        {
            1 => _peSelectedPages.ToList(),
            2 => _pePages.Where(p => !p.IsDeleted).ToList(),
            _ => _peSelectedPages.Count > 0 ? _peSelectedPages.Take(1).ToList() : new()
        };
    }

    private void PeProp_CropPage(object sender, RoutedEventArgs e)
    {
        var targets = GetPeTargetPages();
        if (targets.Count == 0)
        {
            MessageBox.Show("Select pages to crop.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        MessageBox.Show($"Crop will be applied to {targets.Count} page(s) when saving.", "Crop Pages", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // Selection bar quick actions
    private void PeSelection_Delete(object sender, RoutedEventArgs e)
    {
        foreach (var pg in _peSelectedPages.ToList())
        {
            pg.IsDeleted = true;
            if (pg.ThumbnailBorder != null) pg.ThumbnailBorder.Opacity = 0.3;
        }
        _peSelectedPages.Clear();
        PeUpdateSelectionBar();
        PeUpdateInfo();
    }

    private void PeSelection_Rotate(object sender, RoutedEventArgs e)
    {
        foreach (var pg in _peSelectedPages)
        {
            pg.Rotation = (pg.Rotation + 90) % 360;
            if (pg.ThumbnailBorder?.Child is System.Windows.Controls.Image img)
                ((RotateTransform)img.RenderTransform).Angle = pg.Rotation;
        }
    }

    private void PeSelection_Extract(object sender, RoutedEventArgs e)
    {
        if (_peSelectedPages.Count == 0) return;
        var saved = _peSelectedPages.ToList();
        SelectPeTool("extract");
        foreach (var pg in saved) PeToggleSelection(pg);
    }

    // Extract tool
    private void PeExtract_SelectAll(object sender, RoutedEventArgs e)
    {
        foreach (var pg in _pePages.Where(p => !p.IsDeleted))
            _peSelectedPages.Add(pg);
        PeRenderPageGrid();
        TxtPeExtractCount.Text = $"{_peSelectedPages.Count} pages selected";
    }

    private void PeExtract_DeselectAll(object sender, RoutedEventArgs e)
    {
        _peSelectedPages.Clear();
        PeRenderPageGrid();
        TxtPeExtractCount.Text = "0 pages selected";
    }

    // Reorder
    private PdfEditorPageEntry? _peReorderSelected;

    private void PeReorder_SelectPage(PdfEditorPageEntry pg)
    {
        if (_peReorderSelected?.ThumbnailBorder != null)
        {
            _peReorderSelected.ThumbnailBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444"));
            _peReorderSelected.ThumbnailBorder.BorderThickness = new Thickness(1);
        }
        _peReorderSelected = pg;
        if (pg.ThumbnailBorder != null)
        {
            pg.ThumbnailBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E53935"));
            pg.ThumbnailBorder.BorderThickness = new Thickness(3);
        }
        TxtPeReorderInfo.Text = $"Page {_pePages.IndexOf(pg) + 1} selected";
    }

    private void PeReorder_MoveLeft(object sender, RoutedEventArgs e) => PeReorderMove(-1);
    private void PeReorder_MoveRight(object sender, RoutedEventArgs e) => PeReorderMove(1);

    private void PeReorder_ToFront(object sender, RoutedEventArgs e)
    {
        if (_peReorderSelected == null) return;
        int idx = _pePages.IndexOf(_peReorderSelected);
        if (idx > 0) PeReorderMove(-idx);
    }

    private void PeReorder_ToBack(object sender, RoutedEventArgs e)
    {
        if (_peReorderSelected == null) return;
        int idx = _pePages.IndexOf(_peReorderSelected);
        if (idx >= 0 && idx < _pePages.Count - 1) PeReorderMove(_pePages.Count - 1 - idx);
    }

    private void PeReorderMove(int delta)
    {
        if (_peReorderSelected == null) return;
        int idx = _pePages.IndexOf(_peReorderSelected);
        int newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= _pePages.Count) return;
        _pePages.RemoveAt(idx);
        _pePages.Insert(newIdx, _peReorderSelected);
        PeRenderPageGrid();
        if (_peReorderSelected.ThumbnailBorder != null)
        {
            _peReorderSelected.ThumbnailBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E53935"));
            _peReorderSelected.ThumbnailBorder.BorderThickness = new Thickness(3);
        }
        TxtPeReorderInfo.Text = $"Moved to position {newIdx + 1}";
    }

    // Page navigation
    private void PeNav_First(object sender, RoutedEventArgs e) => TxtPeCurrentPage.Text = "1";
    private void PeNav_Prev(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TxtPeCurrentPage.Text, out int pg) && pg > 1) TxtPeCurrentPage.Text = (pg - 1).ToString();
    }
    private void PeNav_Next(object sender, RoutedEventArgs e)
    {
        int total = _pePages.Count(p => !p.IsDeleted);
        if (int.TryParse(TxtPeCurrentPage.Text, out int pg) && pg < total) TxtPeCurrentPage.Text = (pg + 1).ToString();
    }
    private void PeNav_Last(object sender, RoutedEventArgs e) => TxtPeCurrentPage.Text = _pePages.Count(p => !p.IsDeleted).ToString();

    // Add pages
    private void PeAdd_BlankPage(object sender, RoutedEventArgs e)
    {
        var rtb = new RenderTargetBitmap(200, 283, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 200, 283));
        rtb.Render(dv);
        rtb.Freeze();

        var bmpImg = new BitmapImage();
        using (var ms = new MemoryStream())
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            enc.Save(ms);
            ms.Position = 0;
            bmpImg.BeginInit();
            bmpImg.StreamSource = ms;
            bmpImg.CacheOption = BitmapCacheOption.OnLoad;
            bmpImg.EndInit();
            bmpImg.Freeze();
        }

        _pePages.Add(new PdfEditorPageEntry { OriginalIndex = -1, GlobalIndex = _pePages.Count + 1, Thumbnail = bmpImg, SourceFile = "" });
        PeRenderPageGrid();
        PeUpdateInfo();
    }

    private async void PeAdd_FromPdf(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "PDF Files|*.pdf" };
        if (dlg.ShowDialog() != true) return;
        await PeInsertPdfPages(dlg.FileName);
    }

    private void PeAdd_FromImage(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var imgPath in dlg.FileNames)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(imgPath);
                bmp.DecodePixelWidth = 200;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                _pePages.Add(new PdfEditorPageEntry { OriginalIndex = -1, GlobalIndex = _pePages.Count + 1, Thumbnail = bmp, SourceFile = imgPath });
            }
            catch { }
        }
        PeRenderPageGrid();
        PeUpdateInfo();
    }

    private async void PeInsertZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        foreach (var f in files)
        {
            var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
            if (ext == ".pdf") await PeInsertPdfPages(f);
            else if (ImageExtensions.Contains(ext))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(f);
                    bmp.DecodePixelWidth = 200;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    _pePages.Add(new PdfEditorPageEntry { OriginalIndex = -1, GlobalIndex = _pePages.Count + 1, Thumbnail = bmp, SourceFile = f });
                }
                catch { }
            }
        }
        PeRenderPageGrid();
        PeUpdateInfo();
    }

    private async Task PeInsertPdfPages(string path)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            for (uint i = 0; i < pdfDoc.PageCount; i++)
            {
                using var page = pdfDoc.GetPage(i);
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = 200 });
                stream.Seek(0);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = stream.AsStreamForRead();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                _pePages.Add(new PdfEditorPageEntry { OriginalIndex = (int)i, GlobalIndex = _pePages.Count + 1, Thumbnail = bmp, SourceFile = path });
            }
            PeRenderPageGrid();
            PeUpdateInfo();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PDF insert error: {ex.Message}");
        }
    }

    // Zoom
    private void PeZoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SldPeZoom == null) return;
        _peThumbSize = SldPeZoom.Value;
        if (_pePages.Count > 0) PeRenderPageGrid();
    }
    private void PeZoom_Out(object sender, RoutedEventArgs e) => SldPeZoom.Value = Math.Max(SldPeZoom.Minimum, SldPeZoom.Value - 20);
    private void PeZoom_In(object sender, RoutedEventArgs e) => SldPeZoom.Value = Math.Min(SldPeZoom.Maximum, SldPeZoom.Value + 20);

    // Undo/Redo stubs
    private void PeUndo_Click(object sender, RoutedEventArgs e) { }
    private void PeRedo_Click(object sender, RoutedEventArgs e) { }

    // Save
    private void PeSave_Click(object sender, RoutedEventArgs e)
    {
        if (_pePdfPath == null) return;
        _ = PeExecuteSave(_pePdfPath);
    }

    private void PeSaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF|*.pdf",
            FileName = _pePdfPath != null ? System.IO.Path.GetFileName(_pePdfPath) : "output.pdf"
        };
        if (dlg.ShowDialog() != true) return;
        _ = PeExecuteSave(dlg.FileName);
    }

    private async Task PeExecuteSave(string outputPath)
    {
        var activePages = _pePages.Where(p => !p.IsDeleted).ToList();

        if (_pdfEditorActiveTool == "extract")
        {
            activePages = _peSelectedPages.Where(p => !p.IsDeleted).ToList();
            if (activePages.Count == 0) { MessageBox.Show("Select pages to extract first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        }
        else if (_pdfEditorActiveTool == "split")
        {
            activePages = ParsePeSplitRange();
            if (activePages.Count == 0) { MessageBox.Show("Enter a valid page range.", "Invalid Range", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        }

        if (activePages.Count == 0) { MessageBox.Show("No pages to save.", "Info", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        int compressQuality = 95;
        if (_pdfEditorActiveTool == "compress")
            compressQuality = CmbPeCompressQuality.SelectedIndex switch { 0 => 90, 1 => 70, 2 => 50, 3 => 30, _ => 70 };

        int cropTop = 0, cropBottom = 0, cropLeft = 0, cropRight = 0;
        if (_pdfEditorActiveTool == "crop")
        {
            int.TryParse(TxtPeMarginTop.Text.Replace("mm", "").Trim(), out cropTop);
            int.TryParse(TxtPeMarginBottom.Text.Replace("mm", "").Trim(), out cropBottom);
            int.TryParse(TxtPeMarginLeft.Text.Replace("mm", "").Trim(), out cropLeft);
            int.TryParse(TxtPeMarginRight.Text.Replace("mm", "").Trim(), out cropRight);
            cropTop = (int)(cropTop * 3); cropBottom = (int)(cropBottom * 3);
            cropLeft = (int)(cropLeft * 3); cropRight = (int)(cropRight * 3);
        }

        ShowProcessing("Saving PDF...");
        try
        {
            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"llamashot_pe_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                var tempImages = new List<string>();
                int total = activePages.Count, done = 0;
                var progress = CreateProgress();

                foreach (var pg in activePages)
                {
                    BitmapSource srcBmp;
                    if (string.IsNullOrEmpty(pg.SourceFile))
                        srcBmp = pg.Thumbnail!;
                    else if (pg.OriginalIndex < 0 && !pg.SourceFile.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        var imgBmp = new BitmapImage();
                        imgBmp.BeginInit();
                        imgBmp.UriSource = new Uri(pg.SourceFile);
                        imgBmp.CacheOption = BitmapCacheOption.OnLoad;
                        imgBmp.EndInit();
                        imgBmp.Freeze();
                        srcBmp = imgBmp;
                    }
                    else
                    {
                        var sf = await Windows.Storage.StorageFile.GetFileFromPathAsync(pg.SourceFile);
                        var pd = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(sf);
                        using var page = pd.GetPage((uint)pg.OriginalIndex);
                        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                        await page.RenderToStreamAsync(stream, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = (uint)(page.Size.Width * 150 / 72) });
                        stream.Seek(0);
                        var bmpImg = new BitmapImage();
                        bmpImg.BeginInit();
                        bmpImg.StreamSource = stream.AsStreamForRead();
                        bmpImg.CacheOption = BitmapCacheOption.OnLoad;
                        bmpImg.EndInit();
                        bmpImg.Freeze();
                        srcBmp = bmpImg;
                    }

                    string tempFile = System.IO.Path.Combine(tempDir, $"page_{done:D5}.jpg");
                    int rot = pg.Rotation;
                    int cT = cropTop, cB = cropBottom, cL = cropLeft, cR = cropRight;

                    var thread = new System.Threading.Thread(() =>
                    {
                        BitmapSource finalBmp = srcBmp;
                        if (rot != 0) { finalBmp = new TransformedBitmap(srcBmp, new RotateTransform(rot)); finalBmp.Freeze(); }
                        if (cT > 0 || cB > 0 || cL > 0 || cR > 0)
                        {
                            int w = finalBmp.PixelWidth, h = finalBmp.PixelHeight;
                            int cx = Math.Max(0, cL), cy = Math.Max(0, cT);
                            int cw = Math.Max(1, w - cL - cR), ch = Math.Max(1, h - cT - cB);
                            if (cx + cw > w) cw = w - cx; if (cy + ch > h) ch = h - cy;
                            if (cw > 0 && ch > 0) { finalBmp = new CroppedBitmap(finalBmp, new Int32Rect(cx, cy, cw, ch)); finalBmp.Freeze(); }
                        }
                        var enc = new JpegBitmapEncoder { QualityLevel = compressQuality };
                        enc.Frames.Add(BitmapFrame.Create(finalBmp));
                        using var fs = new FileStream(tempFile, FileMode.Create);
                        enc.Save(fs);
                    });
                    thread.SetApartmentState(System.Threading.ApartmentState.STA);
                    thread.Start();
                    thread.Join();
                    tempImages.Add(tempFile);
                    done++;
                    progress.Report(done * 50 / total);
                }

                var imgProgress = new Progress<int>(v => progress.Report(50 + v / 2));
                await FileToolsService.ImagesToPdfAsync(tempImages.ToArray(), outputPath, imgProgress);
                var outputInfo = new FileInfo(outputPath);
                ShowComplete("PDF saved successfully!",
                    $"{System.IO.Path.GetFileName(outputPath)} \u2014 {activePages.Count} pages \u2014 {FileToolsService.FormatFileSize(outputInfo.Length)}",
                    outputPath);
            }
            finally { try { Directory.Delete(tempDir, true); } catch { } }
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private List<PdfEditorPageEntry> ParsePeSplitRange()
    {
        var result = new List<PdfEditorPageEntry>();
        var allPages = _pePages.Where(p => !p.IsDeleted).ToList();
        if (allPages.Count == 0) return result;
        string rangeText = TxtPeSplitRange.Text.Trim();
        if (string.IsNullOrEmpty(rangeText)) return result;
        foreach (var part in rangeText.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.Contains('-'))
            {
                var bounds = trimmed.Split('-');
                if (bounds.Length == 2 && int.TryParse(bounds[0].Trim(), out int from) && int.TryParse(bounds[1].Trim(), out int to))
                    for (int p = from; p <= to && p <= allPages.Count; p++) if (p >= 1) result.Add(allPages[p - 1]);
            }
            else if (int.TryParse(trimmed, out int single) && single >= 1 && single <= allPages.Count)
                result.Add(allPages[single - 1]);
        }
        return result;
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
    //  Fill & Sign — load handlers (Task 10)
    // =====================================================================

    private void FillSign_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "PDF files|*.pdf" };
        if (dlg.ShowDialog() == true) _ = FsLoadPdf(dlg.FileName);
    }

    private void FillSign_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        if (files.Length > 0) _ = FsLoadPdf(files[0]);
    }

    private async Task FsLoadPdf(string path)
    {
        _fsPdfPath = path;
        _fsElements.Clear();
        _fsRegionCache.Clear();
        _fsCurrentPage = 0;
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
        _fsPageCount = (int)pdf.PageCount;
        FillSignSelectView.Visibility = Visibility.Collapsed;
        FillSignWorkspace.Visibility = Visibility.Visible;
        await FsRenderCurrentPage();
        FsBuildThumbnails();
    }

    // =====================================================================
    //  Fill & Sign — render, navigation, thumbnails (Task 11)
    // =====================================================================

    private async Task FsRenderCurrentPage()
    {
        if (_fsPdfPath == null) return;
        using var bmp = await Llamashot.Core.FillSignRender.RenderPageToBitmapAsync(_fsPdfPath, _fsCurrentPage, FsDpi);
        var bi = new BitmapImage();
        using (var ms = new MemoryStream())
        {
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            bi.BeginInit(); bi.StreamSource = ms; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze();
        }
        FillSignPageImage.Source = bi;
        FillSignOverlay.Width = bi.PixelWidth;
        FillSignOverlay.Height = bi.PixelHeight;
        FsPageLabel.Text = $"Page {_fsCurrentPage + 1} / {_fsPageCount}";
        FsRenderOverlayElements();
        await FsEnsureRegions(_fsCurrentPage);
    }

    private void FsRenderOverlayElements()
    {
        // Remove element visuals but keep the hover rectangle (added in XAML).
        for (int i = FillSignOverlay.Children.Count - 1; i >= 0; i--)
            if (FillSignOverlay.Children[i] != FillSignHover)
                FillSignOverlay.Children.RemoveAt(i);
        // Rebuild visuals for the current page (Tasks 13/14/17).
        foreach (var el in _fsElements)
            if (el.Page == _fsCurrentPage)
                FsAddElementVisual(el, focus: false);
    }

    private async Task FsEnsureRegions(int page)
    {
        if (_fsRegionCache.ContainsKey(page) || _fsPdfPath == null) return;
        string path = _fsPdfPath;
        var regions = await Task.Run(async () =>
        {
            using var bmp = await Llamashot.Core.FillSignRender.RenderPageToBitmapAsync(path, page, FsDpi);
            return Llamashot.Core.FillSignDetector.DetectRegions(bmp);
        });
        _fsRegionCache[page] = regions;
    }

    private async void FsNextPage(object sender, RoutedEventArgs e)
    {
        if (_fsCurrentPage < _fsPageCount - 1) { _fsCurrentPage++; await FsRenderCurrentPage(); }
    }

    private async void FsPrevPage(object sender, RoutedEventArgs e)
    {
        if (_fsCurrentPage > 0) { _fsCurrentPage--; await FsRenderCurrentPage(); }
    }

    private async void FsBuildThumbnails()
    {
        FillSignThumbs.Children.Clear();
        if (_fsPdfPath == null) return;
        for (int i = 0; i < _fsPageCount; i++)
        {
            int pageIdx = i;
            using var bmp = await Llamashot.Core.FillSignRender.RenderPageToBitmapAsync(_fsPdfPath, i, 30);
            var bi = new BitmapImage();
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                bi.BeginInit(); bi.StreamSource = ms; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze();
            }
            var img = new System.Windows.Controls.Image { Source = bi, Margin = new Thickness(4), Cursor = Cursors.Hand };
            img.MouseLeftButtonDown += async (_, _) => { _fsCurrentPage = pageIdx; await FsRenderCurrentPage(); };
            FillSignThumbs.Children.Add(img);
        }
    }

    // =====================================================================
    //  Fill & Sign — hover highlight (Task 12)
    // =====================================================================

    private void FsOverlay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_fsRegionCache.TryGetValue(_fsCurrentPage, out var regions))
        { FillSignHover.Visibility = Visibility.Collapsed; return; }
        var p = e.GetPosition(FillSignOverlay);
        Llamashot.Core.DetectedRegion? hit = null;
        foreach (var r in regions)
            if (p.X >= r.X && p.X <= r.X + r.W && p.Y >= r.Y && p.Y <= r.Y + r.H) { hit = r; break; }
        if (hit is { } h)
        {
            Canvas.SetLeft(FillSignHover, h.X); Canvas.SetTop(FillSignHover, h.Y);
            FillSignHover.Width = h.W; FillSignHover.Height = h.H;
            FillSignHover.Visibility = Visibility.Visible;
        }
        else FillSignHover.Visibility = Visibility.Collapsed;
    }

    private void FsOverlay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_fsMode is "signature" or "stamp")
        {
            var pos = e.GetPosition(FillSignOverlay);
            string? imgPath = null;
            if (_fsMode == "signature")
            {
                var dlg = new SignatureDialog { Owner = this };
                if (dlg.ShowDialog() == true) imgPath = dlg.ResultPngPath;
            }
            else
            {
                var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg" };
                if (ofd.ShowDialog() == true) imgPath = ofd.FileName;
            }
            if (string.IsNullOrEmpty(imgPath) || !System.IO.File.Exists(imgPath)) return;

            // Default size: 160px wide, height from image aspect; positioned at click.
            double defW = 160, defH = 60;
            try
            {
                var probe = new System.Windows.Media.Imaging.BitmapImage();
                probe.BeginInit(); probe.UriSource = new Uri(imgPath); probe.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; probe.EndInit();
                if (probe.PixelWidth > 0) defH = defW * probe.PixelHeight / probe.PixelWidth;
            }
            catch { }

            var (sx, sy, sw, sh) = Llamashot.Core.FillSignGeometry.PixelRectToPointRect(pos.X, pos.Y, defW, defH, FsDpi);
            var sigEl = new Llamashot.Models.FillElement
            {
                Page = _fsCurrentPage,
                Type = _fsMode == "signature" ? Llamashot.Models.FillElementType.Signature : Llamashot.Models.FillElementType.Stamp,
                X = sx, Y = sy, Width = sw, Height = sh, ImagePath = imgPath
            };
            _fsElements.Add(sigEl);
            FsAddElementVisual(sigEl);
            return;
        }

        if (_fsMode is not ("text" or "datetime" or "check")) return;
        var p = e.GetPosition(FillSignOverlay);

        // Snap to a detected region if the cursor is over one.
        double x = p.X, y = p.Y, w = 0, h = 0;
        bool snapped = false;
        if (_fsRegionCache.TryGetValue(_fsCurrentPage, out var regions))
            foreach (var r in regions)
                if (p.X >= r.X && p.X <= r.X + r.W && p.Y >= r.Y && p.Y <= r.Y + r.H)
                { x = r.X; y = r.Y; w = r.W; h = r.H; snapped = true; break; }

        if (!snapped)
        {
            h = _fsSize * FsDpi / 72.0 * 1.4;
            w = _fsMode == "check" ? h : 160;
        }
        else { w = Math.Max(8, w); h = Math.Max(8, h); }

        var (ptX, ptY, ptW, ptH) = Llamashot.Core.FillSignGeometry.PixelRectToPointRect(x, y, w, h, FsDpi);
        var el = new Llamashot.Models.FillElement
        {
            Page = _fsCurrentPage,
            Type = _fsMode switch
            {
                "datetime" => Llamashot.Models.FillElementType.DateTime,
                "check"    => Llamashot.Models.FillElementType.Check,
                _          => Llamashot.Models.FillElementType.Text
            },
            X = ptX, Y = ptY, Width = ptW, Height = ptH,
            Text = _fsMode == "datetime" ? DateTime.Now.ToString("dd/MM/yyyy") : _fsMode == "check" ? "✓" : "",
            FontFamily = _fsFont, FontSize = _fsSize, Bold = _fsBold, Italic = _fsItalic, ColorHex = _fsColor
        };
        // For check, size font to the box height.
        if (_fsMode == "check") el.FontSize = Math.Max(8, Llamashot.Core.FillSignGeometry.PixelsToPoints(h, FsDpi));
        _fsElements.Add(el);
        FsAddElementVisual(el, focus: _fsMode != "check");
    }

    private void FsAddElementVisual(Llamashot.Models.FillElement el, bool focus = false)
    {
        double pxX = Llamashot.Core.FillSignGeometry.PointsToPixels(el.X, FsDpi);
        double pxY = Llamashot.Core.FillSignGeometry.PointsToPixels(el.Y, FsDpi);
        double pxW = Llamashot.Core.FillSignGeometry.PointsToPixels(el.Width, FsDpi);
        double pxH = Llamashot.Core.FillSignGeometry.PointsToPixels(el.Height, FsDpi);

        switch (el.Type)
        {
            case Llamashot.Models.FillElementType.Text:
            case Llamashot.Models.FillElementType.DateTime:
            case Llamashot.Models.FillElementType.Check:
            {
                var tb = new System.Windows.Controls.TextBox
                {
                    Text = el.Text,
                    Width = pxW > 0 ? pxW : 160,
                    MinHeight = pxH > 0 ? pxH : el.FontSize * FsDpi / 72.0 * 1.4,
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")),
                    Background = new SolidColorBrush(Color.FromArgb(20, 59, 130, 246)),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(el.ColorHex)),
                    FontFamily = new System.Windows.Media.FontFamily(el.FontFamily),
                    FontSize = el.FontSize * FsDpi / 72.0,
                    FontWeight = el.Bold ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = el.Italic ? FontStyles.Italic : FontStyles.Normal,
                    Padding = new Thickness(0), Tag = el,
                    TextAlignment = el.Type == Llamashot.Models.FillElementType.Check ? TextAlignment.Center : TextAlignment.Left
                };
                tb.TextChanged += (_, _) => el.Text = tb.Text;
                System.Windows.Controls.Canvas.SetLeft(tb, pxX);
                System.Windows.Controls.Canvas.SetTop(tb, pxY);
                FsEnableDrag(tb, el);
                FillSignOverlay.Children.Add(tb);
                if (focus) { tb.Focus(); tb.CaretIndex = tb.Text.Length; }
                break;
            }
            case Llamashot.Models.FillElementType.Signature:
            case Llamashot.Models.FillElementType.Stamp:
            {
                if (string.IsNullOrEmpty(el.ImagePath) || !System.IO.File.Exists(el.ImagePath)) break;
                var img = new System.Windows.Controls.Image
                {
                    Width = pxW > 0 ? pxW : 160,
                    Height = pxH > 0 ? pxH : 60,
                    Stretch = Stretch.Fill, Tag = el
                };
                var src = new System.Windows.Media.Imaging.BitmapImage();
                src.BeginInit(); src.UriSource = new Uri(el.ImagePath); src.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; src.EndInit(); src.Freeze();
                img.Source = src;

                var holder = new System.Windows.Controls.Grid { Width = img.Width, Height = img.Height, Tag = el };
                holder.Children.Add(img);
                var handle = new System.Windows.Shapes.Rectangle
                {
                    Width = 12, Height = 12, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                    Cursor = System.Windows.Input.Cursors.SizeNWSE
                };
                holder.Children.Add(handle);

                System.Windows.Controls.Canvas.SetLeft(holder, pxX);
                System.Windows.Controls.Canvas.SetTop(holder, pxY);
                FsEnableDrag(holder, el);

                bool resizing = false; System.Windows.Point rStart = default; double rW = 0, rH = 0;
                handle.PreviewMouseLeftButtonDown += (s, ev) =>
                {
                    resizing = true; rStart = ev.GetPosition(FillSignOverlay); rW = holder.Width; rH = holder.Height;
                    handle.CaptureMouse(); ev.Handled = true;
                };
                handle.PreviewMouseMove += (s, ev) =>
                {
                    if (!resizing) return;
                    var p = ev.GetPosition(FillSignOverlay);
                    double nw = Math.Max(16, rW + (p.X - rStart.X)), nh = Math.Max(8, rH + (p.Y - rStart.Y));
                    holder.Width = nw; holder.Height = nh; img.Width = nw; img.Height = nh;
                    el.Width = Llamashot.Core.FillSignGeometry.PixelsToPoints(nw, FsDpi);
                    el.Height = Llamashot.Core.FillSignGeometry.PixelsToPoints(nh, FsDpi);
                    ev.Handled = true;
                };
                handle.PreviewMouseLeftButtonUp += (s, ev) => { if (resizing) { resizing = false; handle.ReleaseMouseCapture(); ev.Handled = true; } };

                FillSignOverlay.Children.Add(holder);
                break;
            }
            default:
                break;
        }
    }

    private void FsEnableDrag(FrameworkElement fe, Llamashot.Models.FillElement el)
    {
        bool dragging = false; System.Windows.Point start = default; double ox = 0, oy = 0;
        fe.MouseRightButtonDown += (s, e) =>
        {
            FillSignOverlay.Children.Remove(fe);
            _fsElements.Remove(el);
            e.Handled = true;
        };
        fe.PreviewMouseLeftButtonDown += (s, e) =>
        {
            if (_fsMode != "select") return; // only drag in Select mode
            dragging = true;
            start = e.GetPosition(FillSignOverlay);
            ox = System.Windows.Controls.Canvas.GetLeft(fe);
            oy = System.Windows.Controls.Canvas.GetTop(fe);
            fe.CaptureMouse(); e.Handled = true;
        };
        fe.PreviewMouseMove += (s, e) =>
        {
            if (!dragging) return;
            var pos = e.GetPosition(FillSignOverlay);
            double nx = ox + (pos.X - start.X), ny = oy + (pos.Y - start.Y);
            System.Windows.Controls.Canvas.SetLeft(fe, nx);
            System.Windows.Controls.Canvas.SetTop(fe, ny);
            el.X = Llamashot.Core.FillSignGeometry.PixelsToPoints(nx, FsDpi);
            el.Y = Llamashot.Core.FillSignGeometry.PixelsToPoints(ny, FsDpi);
        };
        fe.PreviewMouseLeftButtonUp += (s, e) => { if (dragging) { dragging = false; fe.ReleaseMouseCapture(); } };
    }

    // =====================================================================
    //  Fill & Sign — toolbar handlers (Tasks 13/14/17)
    // =====================================================================

    private void FsMode_Click(object sender, RoutedEventArgs e)
    {
        _fsMode = (string)((FrameworkElement)sender).Tag;
    }

    private void FsFont_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (FsFontCombo.SelectedItem is System.Windows.Controls.ComboBoxItem it) _fsFont = (string)it.Content;
        else if (FsFontCombo.SelectedItem is string s) _fsFont = s;
    }

    private void FsSize_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        string? v = (FsSizeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string
                    ?? FsSizeCombo.SelectedItem as string;
        if (double.TryParse(v, out var d) && d > 0) _fsSize = d;
    }

    private void FsBold_Click(object sender, RoutedEventArgs e) => _fsBold = !_fsBold;
    private void FsItalic_Click(object sender, RoutedEventArgs e) => _fsItalic = !_fsItalic;

    private void FsColor_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.ColorDialog();
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var c = dlg.Color;
            _fsColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            FsColorSwatch.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        }
    }

    private async void FillSign_Save(object sender, RoutedEventArgs e)
    {
        if (_fsPdfPath == null || _fsElements.Count == 0) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF files|*.pdf",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_fsPdfPath) + "_filled.pdf"
        };
        if (dlg.ShowDialog() != true) return;
        string outPath = dlg.FileName;
        string src = _fsPdfPath;
        var elements = new List<Llamashot.Models.FillElement>(_fsElements);
        ShowProcessing("Saving filled PDF...");
        try
        {
            await Task.Run(() => Llamashot.Core.FillSignExporter.Export(src, elements, outPath));
            ShowComplete("Saved", System.IO.Path.GetFileName(outPath), outPath);
        }
        catch (Exception ex)
        {
            ProcessingOverlay.Visibility = Visibility.Collapsed;
            MessageBox.Show($"Failed to save: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
    //  Image Editor (unified single-image workspace)
    // =====================================================================

    private string? _ieImagePath;
    private BitmapImage? _ieOriginalImage;
    private BitmapSource? _ieWorkingImage;
    private BitmapSource? _ieAdjustBase; // snapshot before live adjust preview
    private BitmapSource? _ieFilterBase; // snapshot before filter was applied (so filters don't stack)
    private string _ieActiveTool = "crop";
    private double _ieRotation = 0;
    private bool _ieFlipH, _ieFlipV;
    private int _ieOrigW, _ieOrigH;
    private double _ieZoom = 0; // 0 = fit
    private bool _ieSuppressResize;
    private bool _ieSuppressAdjust;
    // Undo
    private readonly List<BitmapSource> _ieUndoStack = new();
    private readonly List<BitmapSource> _ieRedoStack = new();
    // Crop drag
    private bool _ieCropDragging;
    private Point _ieCropStart;
    private Rect _ieCropRect = Rect.Empty;
    private Rectangle? _ieCropSelection;
    private enum IeCropHandle { None, TL, TR, BL, BR, T, B, L, R, Move }
    private IeCropHandle _ieCropHandle = IeCropHandle.None;
    private Rect _ieCropRectOnDown;

    private void IePushUndo()
    {
        if (_ieWorkingImage != null) _ieUndoStack.Add(_ieWorkingImage);
        _ieRedoStack.Clear();
        if (_ieUndoStack.Count > 50) _ieUndoStack.RemoveAt(0);
    }

    private void IeUndo()
    {
        if (_ieUndoStack.Count == 0) return;
        _ieRedoStack.Add(_ieWorkingImage!);
        _ieWorkingImage = _ieUndoStack[^1];
        _ieUndoStack.RemoveAt(_ieUndoStack.Count - 1);
        IePreviewImage.Source = _ieWorkingImage;
        _ieZoom = 0; IeApplyZoom();
        IeUpdateInfo();
    }

    private void IeRedo()
    {
        if (_ieRedoStack.Count == 0) return;
        _ieUndoStack.Add(_ieWorkingImage!);
        _ieWorkingImage = _ieRedoStack[^1];
        _ieRedoStack.RemoveAt(_ieRedoStack.Count - 1);
        IePreviewImage.Source = _ieWorkingImage;
        _ieZoom = 0; IeApplyZoom();
        IeUpdateInfo();
    }

    private void IeUndo_Click(object sender, RoutedEventArgs e) => IeUndo();
    private void IeRedo_Click(object sender, RoutedEventArgs e) => IeRedo();

    private void ImgEditor_SelectFile(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif;*.webp|All|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        IeLoadImage(dlg.FileName);
        ShowConfigState("image_editor");
    }

    private void ImgEditor_SelectDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var imgFile = files.FirstOrDefault(f => ImageExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()));
        if (imgFile == null) return;
        IeLoadImage(imgFile);
        ShowConfigState("image_editor");
    }

    private void IeLoadImage(string path)
    {
        _ieImagePath = path;
        _ieRotation = 0; _ieFlipH = false; _ieFlipV = false;
        IeRotateTransform.Angle = 0;
        IeFlipTransform.ScaleX = 1; IeFlipTransform.ScaleY = 1;
        _ieUndoStack.Clear(); _ieRedoStack.Clear();

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        _ieOriginalImage = bmp;
        // Convert to Bgra32 for pixel operations
        _ieWorkingImage = IeEnsureBgra32(bmp);
        _ieAdjustBase = _ieWorkingImage;
        _ieFilterBase = _ieWorkingImage;
        _ieOrigW = bmp.PixelWidth;
        _ieOrigH = bmp.PixelHeight;

        IePreviewImage.Source = _ieWorkingImage;
        _ieZoom = 0;
        IeApplyZoom();
        IeUpdateInfo();

        _ieSuppressResize = true;
        _ieSuppressAdjust = true;
        TxtIeCropW.Text = _ieOrigW.ToString();
        TxtIeCropH.Text = _ieOrigH.ToString();
        TxtIeCropX.Text = "0"; TxtIeCropY.Text = "0";
        TxtIeResizeW.Text = _ieOrigW.ToString();
        TxtIeResizeH.Text = _ieOrigH.ToString();
        SldIeResizePct.Value = 100;
        SldIeBrightness.Value = 0; SldIeContrast.Value = 0; SldIeSaturation.Value = 0;
        SldIeExposure.Value = 0; SldIeHighlights.Value = 0; SldIeShadows.Value = 0; SldIeTemperature.Value = 0;
        _ieSuppressResize = false;
        _ieSuppressAdjust = false;

        _ieCropRect = Rect.Empty;
        IeClearCropVisuals();
        IeUpdateCompressEstimate();
        IeUpdateConvertEstimate();
        SelectIeTool("crop");
        // Delay crop init until layout is done
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            IeApplyZoom();
            if (_ieActiveTool == "crop") IeInitCropRect();
        });
    }

    private static BitmapSource IeEnsureBgra32(BitmapSource src)
    {
        if (src.Format == PixelFormats.Bgra32) return src;
        var converted = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private void IeUpdateInfo()
    {
        if (_ieImagePath == null || _ieWorkingImage == null) return;
        var info = new FileInfo(_ieImagePath);
        TxtIeFileName.Text = System.IO.Path.GetFileName(_ieImagePath);
        TxtIeFileInfo.Text = $"{_ieWorkingImage.PixelWidth} x {_ieWorkingImage.PixelHeight} \u2022 {FileToolsService.FormatFileSize(info.Length)}";
        TxtIeCurrentSize.Text = $"{_ieWorkingImage.PixelWidth} x {_ieWorkingImage.PixelHeight}";
    }

    // ---- Tool selection ----
    private void IeToolSelect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string tool)
            SelectIeTool(tool);
    }

    private void SelectIeTool(string tool)
    {
        _ieActiveTool = tool;
        System.Windows.Controls.Button[] sidebarBtns =
        {
            BtnIeTool_crop, BtnIeTool_resize, BtnIeTool_rotateflip, BtnIeTool_adjust,
            BtnIeTool_filters, BtnIeTool_watermark, BtnIeTool_compress, BtnIeTool_convert
        };
        foreach (var btn in sidebarBtns)
        {
            btn.Background = Brushes.Transparent;
            btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAA"));
        }
        var activeBtn = tool switch
        {
            "crop" => BtnIeTool_crop, "resize" => BtnIeTool_resize,
            "rotateflip" => BtnIeTool_rotateflip, "adjust" => BtnIeTool_adjust,
            "filters" => BtnIeTool_filters, "watermark" => BtnIeTool_watermark,
            "compress" => BtnIeTool_compress, "convert" => BtnIeTool_convert,
            _ => BtnIeTool_crop
        };
        activeBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C4DFF"));
        activeBtn.Foreground = Brushes.White;

        IeSecCrop.Visibility = tool == "crop" ? Visibility.Visible : Visibility.Collapsed;
        IeSecResize.Visibility = tool == "resize" ? Visibility.Visible : Visibility.Collapsed;
        IeSecRotateFlip.Visibility = tool == "rotateflip" ? Visibility.Visible : Visibility.Collapsed;
        IeSecAdjust.Visibility = tool == "adjust" ? Visibility.Visible : Visibility.Collapsed;
        IeSecFilters.Visibility = tool == "filters" ? Visibility.Visible : Visibility.Collapsed;
        IeSecWatermark.Visibility = tool == "watermark" ? Visibility.Visible : Visibility.Collapsed;
        IeSecCompress.Visibility = tool == "compress" ? Visibility.Visible : Visibility.Collapsed;
        IeSecConvert.Visibility = tool == "convert" ? Visibility.Visible : Visibility.Collapsed;

        if (tool != "crop") IeClearCropVisuals();
        else if (_ieWorkingImage != null) IeInitCropRect();

        if (tool == "adjust" && _ieWorkingImage != null)
        {
            _ieAdjustBase = _ieWorkingImage;
            _ieSuppressAdjust = true;
            SldIeBrightness.Value = 0; SldIeContrast.Value = 0; SldIeSaturation.Value = 0;
            SldIeExposure.Value = 0; SldIeHighlights.Value = 0; SldIeShadows.Value = 0; SldIeTemperature.Value = 0;
            _ieSuppressAdjust = false;
        }
        if (tool == "filters" && _ieWorkingImage != null) _ieFilterBase = _ieWorkingImage;
        if (tool == "compress") IeUpdateCompressEstimate();
        if (tool == "convert") IeUpdateConvertEstimate();
        if (tool == "resize" && _ieWorkingImage != null)
        {
            _ieSuppressResize = true;
            TxtIeResizeW.Text = _ieWorkingImage.PixelWidth.ToString();
            TxtIeResizeH.Text = _ieWorkingImage.PixelHeight.ToString();
            SldIeResizePct.Value = 100;
            _ieSuppressResize = false;
        }
    }

    // ---- Keyboard shortcuts ----
    public void IeHandleKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (_ieImagePath == null) return;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (ctrl && !shift && e.Key == Key.Z) { IeUndo(); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.Z) { IeRedo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { IeRedo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { IeSave_Click(this, new RoutedEventArgs()); e.Handled = true; }
    }

    // ---- Zoom ----
    private void IeApplyZoom()
    {
        if (_ieWorkingImage == null) return;
        double iw = _ieWorkingImage.PixelWidth, ih = _ieWorkingImage.PixelHeight;

        if (_ieZoom <= 0)
        {
            // Fit: scale to fit viewport
            double vw = IeScrollViewer.ActualWidth - 40;
            double vh = IeScrollViewer.ActualHeight - 40;
            if (vw <= 0 || vh <= 0) _ieZoom = 100;
            else
            {
                double fitScale = Math.Min(vw / iw, vh / ih);
                _ieZoom = Math.Max(5, (int)(fitScale * 100));
            }
        }

        double scale = _ieZoom / 100.0;
        // Set image to natural pixel size, use LayoutTransform for zoom
        IePreviewImage.Width = iw;
        IePreviewImage.Height = ih;
        IeCanvas.Width = iw;
        IeCanvas.Height = ih;
        IeZoomTransform.ScaleX = scale;
        IeZoomTransform.ScaleY = scale;

        TxtIeZoom.Text = $"{(int)_ieZoom}%";
    }

    private void IeZoom_Out(object sender, RoutedEventArgs e)
    {
        if (_ieZoom <= 0) IeApplyZoom(); // resolve fit
        _ieZoom = Math.Max(5, _ieZoom - 5);
        IeApplyZoom();
        if (_ieActiveTool == "crop") IeInitCropRect();
    }
    private void IeZoom_In(object sender, RoutedEventArgs e)
    {
        if (_ieZoom <= 0) IeApplyZoom(); // resolve fit
        _ieZoom = Math.Min(400, _ieZoom + 5);
        IeApplyZoom();
        if (_ieActiveTool == "crop") IeInitCropRect();
    }
    private void IeZoom_Fit(object sender, RoutedEventArgs e)
    {
        _ieZoom = 0;
        IeApplyZoom();
        if (_ieActiveTool == "crop") IeInitCropRect();
    }

    // ---- Interactive Crop ----
    private void IeInitCropRect()
    {
        if (_ieWorkingImage == null) return;
        // Canvas is at natural pixel size (zoom via LayoutTransform) — crop coords are pixel coords
        double dw = _ieWorkingImage.PixelWidth, dh = _ieWorkingImage.PixelHeight;
        _ieCropRect = new Rect(0, 0, dw, dh);
        IeUpdateCropVisuals();
    }

    private void IeClearCropVisuals()
    {
        var toRemove = IeCanvas.Children.OfType<System.Windows.Shapes.Shape>()
            .Concat<UIElement>(IeCanvas.Children.OfType<Border>().Where(b => b.Tag is string s && s == "ieCH"))
            .ToList();
        foreach (var el in toRemove) IeCanvas.Children.Remove(el);
        _ieCropSelection = null;
    }

    private void IeUpdateCropVisuals()
    {
        IeClearCropVisuals();
        if (_ieCropRect.IsEmpty || _ieWorkingImage == null) return;

        _ieCropSelection = new Rectangle
        {
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C4DFF")),
            StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = Brushes.Transparent, IsHitTestVisible = false
        };
        Canvas.SetLeft(_ieCropSelection, _ieCropRect.X);
        Canvas.SetTop(_ieCropSelection, _ieCropRect.Y);
        _ieCropSelection.Width = Math.Max(1, _ieCropRect.Width);
        _ieCropSelection.Height = Math.Max(1, _ieCropRect.Height);
        IeCanvas.Children.Add(_ieCropSelection);

        var pts = new (double x, double y)[]
        {
            (_ieCropRect.Left, _ieCropRect.Top), (_ieCropRect.Right, _ieCropRect.Top),
            (_ieCropRect.Left, _ieCropRect.Bottom), (_ieCropRect.Right, _ieCropRect.Bottom),
            ((_ieCropRect.Left+_ieCropRect.Right)/2, _ieCropRect.Top),
            ((_ieCropRect.Left+_ieCropRect.Right)/2, _ieCropRect.Bottom),
            (_ieCropRect.Left, (_ieCropRect.Top+_ieCropRect.Bottom)/2),
            (_ieCropRect.Right, (_ieCropRect.Top+_ieCropRect.Bottom)/2),
        };
        foreach (var (hx, hy) in pts)
        {
            var h = new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C4DFF")),
                BorderBrush = Brushes.White, BorderThickness = new Thickness(1),
                Tag = "ieCH", IsHitTestVisible = false
            };
            Canvas.SetLeft(h, hx - 5); Canvas.SetTop(h, hy - 5);
            IeCanvas.Children.Add(h);
        }

        // Coords are pixel values directly (LayoutTransform handles visual zoom)
        TxtIeCropX.Text = ((int)_ieCropRect.X).ToString();
        TxtIeCropY.Text = ((int)_ieCropRect.Y).ToString();
        TxtIeCropW.Text = ((int)_ieCropRect.Width).ToString();
        TxtIeCropH.Text = ((int)_ieCropRect.Height).ToString();
    }

    private IeCropHandle IeHitTestCropHandle(Point pt)
    {
        if (_ieCropRect.IsEmpty) return IeCropHandle.None;
        double tol = 8; var r = _ieCropRect;
        double mx = (r.Left+r.Right)/2, my = (r.Top+r.Bottom)/2;
        if (Math.Abs(pt.X-r.Left)<tol && Math.Abs(pt.Y-r.Top)<tol) return IeCropHandle.TL;
        if (Math.Abs(pt.X-r.Right)<tol && Math.Abs(pt.Y-r.Top)<tol) return IeCropHandle.TR;
        if (Math.Abs(pt.X-r.Left)<tol && Math.Abs(pt.Y-r.Bottom)<tol) return IeCropHandle.BL;
        if (Math.Abs(pt.X-r.Right)<tol && Math.Abs(pt.Y-r.Bottom)<tol) return IeCropHandle.BR;
        if (Math.Abs(pt.X-mx)<tol && Math.Abs(pt.Y-r.Top)<tol) return IeCropHandle.T;
        if (Math.Abs(pt.X-mx)<tol && Math.Abs(pt.Y-r.Bottom)<tol) return IeCropHandle.B;
        if (Math.Abs(pt.X-r.Left)<tol && Math.Abs(pt.Y-my)<tol) return IeCropHandle.L;
        if (Math.Abs(pt.X-r.Right)<tol && Math.Abs(pt.Y-my)<tol) return IeCropHandle.R;
        if (r.Contains(pt)) return IeCropHandle.Move;
        return IeCropHandle.None;
    }

    private void IeCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_ieActiveTool != "crop" || _ieWorkingImage == null) return;
        var pt = e.GetPosition(IeCanvas);
        _ieCropHandle = IeHitTestCropHandle(pt);
        if (_ieCropHandle == IeCropHandle.None)
        {
            _ieCropRect = new Rect(pt, new Size(0, 0));
            _ieCropHandle = IeCropHandle.BR;
        }
        _ieCropStart = pt; _ieCropRectOnDown = _ieCropRect;
        _ieCropDragging = true;
        IeCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void IeCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_ieCropDragging || _ieActiveTool != "crop") return;
        var pt = e.GetPosition(IeCanvas);
        double dx = pt.X - _ieCropStart.X, dy = pt.Y - _ieCropStart.Y;
        var r = _ieCropRectOnDown;
        switch (_ieCropHandle)
        {
            case IeCropHandle.Move: _ieCropRect = new Rect(r.X+dx, r.Y+dy, r.Width, r.Height); break;
            case IeCropHandle.BR: _ieCropRect = new Rect(r.X, r.Y, Math.Max(10,r.Width+dx), Math.Max(10,r.Height+dy)); break;
            case IeCropHandle.BL: _ieCropRect = new Rect(r.X+dx, r.Y, Math.Max(10,r.Width-dx), Math.Max(10,r.Height+dy)); break;
            case IeCropHandle.TR: _ieCropRect = new Rect(r.X, r.Y+dy, Math.Max(10,r.Width+dx), Math.Max(10,r.Height-dy)); break;
            case IeCropHandle.TL: _ieCropRect = new Rect(r.X+dx, r.Y+dy, Math.Max(10,r.Width-dx), Math.Max(10,r.Height-dy)); break;
            case IeCropHandle.T: _ieCropRect = new Rect(r.X, r.Y+dy, r.Width, Math.Max(10,r.Height-dy)); break;
            case IeCropHandle.B: _ieCropRect = new Rect(r.X, r.Y, r.Width, Math.Max(10,r.Height+dy)); break;
            case IeCropHandle.L: _ieCropRect = new Rect(r.X+dx, r.Y, Math.Max(10,r.Width-dx), r.Height); break;
            case IeCropHandle.R: _ieCropRect = new Rect(r.X, r.Y, Math.Max(10,r.Width+dx), r.Height); break;
        }
        // Clamp crop rect to image pixel bounds
        double imgW = _ieWorkingImage?.PixelWidth ?? 100;
        double imgH = _ieWorkingImage?.PixelHeight ?? 100;
        double cx = Math.Max(0, _ieCropRect.X), cy = Math.Max(0, _ieCropRect.Y);
        double cr = Math.Min(imgW, _ieCropRect.Right), cb = Math.Min(imgH, _ieCropRect.Bottom);
        if (cr - cx > 5 && cb - cy > 5) _ieCropRect = new Rect(cx, cy, cr - cx, cb - cy);
        IeUpdateCropVisuals();
    }

    private void IeCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _ieCropDragging = false; _ieCropHandle = IeCropHandle.None;
        IeCanvas.ReleaseMouseCapture();
    }

    private void IeCropRatio_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_ieWorkingImage == null || CmbIeCropRatio.SelectedIndex <= 0) return;
        double ratio = CmbIeCropRatio.SelectedIndex switch { 1=>1.0, 2=>4.0/3, 3=>16.0/9, 4=>3.0/2, _=>0 };
        if (ratio <= 0) return;
        double iw = _ieWorkingImage.PixelWidth, ih = _ieWorkingImage.PixelHeight;
        double cw = iw, ch = cw / ratio;
        if (ch > ih) { ch = ih; cw = ch * ratio; }
        _ieCropRect = new Rect((iw-cw)/2, (ih-ch)/2, cw, ch);
        IeUpdateCropVisuals();
    }

    private void IeCrop_Apply(object sender, RoutedEventArgs e)
    {
        if (_ieWorkingImage == null) return;
        // Crop rect is in pixel coords directly
        int cx = Math.Max(0, (int)_ieCropRect.X);
        int cy = Math.Max(0, (int)_ieCropRect.Y);
        int cw = (int)_ieCropRect.Width; int ch = (int)_ieCropRect.Height;
        if (cx+cw > _ieWorkingImage.PixelWidth) cw = _ieWorkingImage.PixelWidth-cx;
        if (cy+ch > _ieWorkingImage.PixelHeight) ch = _ieWorkingImage.PixelHeight-cy;
        if (cw <= 0 || ch <= 0) return;

        IePushUndo();
        var cropped = new CroppedBitmap(_ieWorkingImage, new Int32Rect(cx, cy, cw, ch));
        cropped.Freeze();
        _ieWorkingImage = IeEnsureBgra32(cropped);
        _ieAdjustBase = _ieWorkingImage;
        IePreviewImage.Source = _ieWorkingImage;
        _ieZoom = 0; IeApplyZoom();
        _ieCropRect = Rect.Empty; IeClearCropVisuals(); IeInitCropRect();
        IeUpdateInfo();
    }

    // ---- Resize ----
    private void IeResizeW_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ieSuppressResize || _ieWorkingImage == null || ChkIeResizeLock?.IsChecked != true) return;
        if (!int.TryParse(TxtIeResizeW.Text, out int w) || w <= 0) return;
        double ratio = (double)_ieWorkingImage.PixelHeight / _ieWorkingImage.PixelWidth;
        _ieSuppressResize = true;
        TxtIeResizeH.Text = ((int)(w * ratio)).ToString();
        _ieSuppressResize = false;
    }
    private void IeResizeH_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ieSuppressResize || _ieWorkingImage == null || ChkIeResizeLock?.IsChecked != true) return;
        if (!int.TryParse(TxtIeResizeH.Text, out int h) || h <= 0) return;
        double ratio = (double)_ieWorkingImage.PixelWidth / _ieWorkingImage.PixelHeight;
        _ieSuppressResize = true;
        TxtIeResizeW.Text = ((int)(h * ratio)).ToString();
        _ieSuppressResize = false;
    }
    private void IeResizePct_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_ieSuppressResize || _ieWorkingImage == null || SldIeResizePct == null) return;
        double pct = SldIeResizePct.Value / 100.0;
        _ieSuppressResize = true;
        TxtIeResizeW.Text = ((int)(_ieWorkingImage.PixelWidth * pct)).ToString();
        TxtIeResizeH.Text = ((int)(_ieWorkingImage.PixelHeight * pct)).ToString();
        if (TxtIeResizePct != null) TxtIeResizePct.Text = $"{(int)SldIeResizePct.Value}%";
        _ieSuppressResize = false;
    }
    private void IeResize_Apply(object sender, RoutedEventArgs e)
    {
        if (_ieWorkingImage == null) return;
        if (!int.TryParse(TxtIeResizeW.Text, out int tw) || !int.TryParse(TxtIeResizeH.Text, out int th) || tw<=0 || th<=0) return;
        IePushUndo();
        var scaled = new TransformedBitmap(_ieWorkingImage, new ScaleTransform((double)tw/_ieWorkingImage.PixelWidth, (double)th/_ieWorkingImage.PixelHeight));
        scaled.Freeze();
        _ieWorkingImage = IeEnsureBgra32(scaled);
        _ieAdjustBase = _ieWorkingImage;
        IePreviewImage.Source = _ieWorkingImage;
        _ieZoom = 0; IeApplyZoom(); IeUpdateInfo();
    }

    // ---- Rotate & Flip ----
    private void IeRotate_Left(object sender, RoutedEventArgs e) { _ieRotation = (_ieRotation+270)%360; IeRotateTransform.Angle = _ieRotation; }
    private void IeRotate_Right(object sender, RoutedEventArgs e) { _ieRotation = (_ieRotation+90)%360; IeRotateTransform.Angle = _ieRotation; }
    private void IeFlip_H(object sender, RoutedEventArgs e) { _ieFlipH = !_ieFlipH; IeFlipTransform.ScaleX = _ieFlipH ? -1 : 1; }
    private void IeFlip_V(object sender, RoutedEventArgs e) { _ieFlipV = !_ieFlipV; IeFlipTransform.ScaleY = _ieFlipV ? -1 : 1; }

    // ---- Adjust (live preview) ----
    private DispatcherTimer? _ieAdjustDebounce;

    private void IeAdjust_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_ieSuppressAdjust) return;
        if (TxtIeBrightVal != null) TxtIeBrightVal.Text = ((int)SldIeBrightness.Value).ToString();
        if (TxtIeContrastVal != null) TxtIeContrastVal.Text = ((int)SldIeContrast.Value).ToString();
        if (TxtIeSatVal != null) TxtIeSatVal.Text = ((int)SldIeSaturation.Value).ToString();
        if (TxtIeExposureVal != null) TxtIeExposureVal.Text = ((int)SldIeExposure.Value).ToString();
        if (TxtIeHighlightsVal != null) TxtIeHighlightsVal.Text = ((int)SldIeHighlights.Value).ToString();
        if (TxtIeShadowsVal != null) TxtIeShadowsVal.Text = ((int)SldIeShadows.Value).ToString();
        if (TxtIeTempVal != null) TxtIeTempVal.Text = ((int)SldIeTemperature.Value).ToString();

        // Debounce live preview
        _ieAdjustDebounce?.Stop();
        _ieAdjustDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _ieAdjustDebounce.Tick += (s, _) => { _ieAdjustDebounce.Stop(); IeApplyAdjustPreview(); };
        _ieAdjustDebounce.Start();
    }

    private async void IeApplyAdjustPreview()
    {
        if (_ieAdjustBase == null) return;
        var src = IeEnsureBgra32(_ieAdjustBase);
        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        byte[] px = new byte[h * stride];
        src.CopyPixels(px, stride, 0);

        double bright = SldIeBrightness.Value / 100.0;
        double contrast = SldIeContrast.Value / 100.0;
        double sat = SldIeSaturation.Value / 100.0;
        double exposure = SldIeExposure.Value / 100.0;
        double highlights = SldIeHighlights.Value / 100.0;
        double shadows = SldIeShadows.Value / 100.0;
        double temp = SldIeTemperature.Value / 100.0;

        await Task.Run(() =>
        {
            double cF = (1 + contrast); cF *= cF;
            double expMul = Math.Pow(2, exposure);

            for (int i = 0; i < px.Length; i += 4)
            {
                double b = px[i]/255.0, g = px[i+1]/255.0, r = px[i+2]/255.0;
                r *= expMul; g *= expMul; b *= expMul;
                r += bright; g += bright; b += bright;
                r = ((r-0.5)*cF)+0.5; g = ((g-0.5)*cF)+0.5; b = ((b-0.5)*cF)+0.5;
                double lum = 0.2126*r + 0.7152*g + 0.0722*b;
                if (lum > 0.5) { double f = highlights * (lum-0.5)*2; r+=f; g+=f; b+=f; }
                else { double f = shadows * (0.5-lum)*2; r+=f; g+=f; b+=f; }
                double gray = 0.2126*r + 0.7152*g + 0.0722*b;
                r = gray + (r-gray)*(1+sat); g = gray + (g-gray)*(1+sat); b = gray + (b-gray)*(1+sat);
                r += temp * 0.15; b -= temp * 0.15;
                px[i] = (byte)Math.Clamp((int)(b*255), 0, 255);
                px[i+1] = (byte)Math.Clamp((int)(g*255), 0, 255);
                px[i+2] = (byte)Math.Clamp((int)(r*255), 0, 255);
            }
        });

        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, w, h), px, stride, 0);
        wb.Freeze();
        IePreviewImage.Source = wb;
    }

    private void IeAdjust_Apply(object sender, RoutedEventArgs e)
    {
        if (_ieAdjustBase == null) return;
        IeApplyAdjustPreview(); // ensure latest
        IePushUndo();
        _ieWorkingImage = IePreviewImage.Source as BitmapSource ?? _ieWorkingImage;
        _ieAdjustBase = _ieWorkingImage;
        _ieSuppressAdjust = true;
        SldIeBrightness.Value = 0; SldIeContrast.Value = 0; SldIeSaturation.Value = 0;
        SldIeExposure.Value = 0; SldIeHighlights.Value = 0; SldIeShadows.Value = 0; SldIeTemperature.Value = 0;
        _ieSuppressAdjust = false;
        IeUpdateInfo();
    }

    // ---- Filters ----
    private async void IeFilter_Click(object sender, RoutedEventArgs e)
    {
        if (_ieWorkingImage == null || sender is not System.Windows.Controls.Button btn || btn.Tag is not string filter) return;
        // Apply filter on the pre-filter base so filters don't stack
        var src = IeEnsureBgra32(_ieFilterBase ?? _ieWorkingImage);
        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        byte[] px = new byte[h * stride];
        src.CopyPixels(px, stride, 0);

        await Task.Run(() => { for (int i = 0; i < px.Length; i += 4)
        {
            double b = px[i], g = px[i+1], r = px[i+2];
            switch (filter)
            {
                case "grayscale":
                    double gray = 0.2126*r + 0.7152*g + 0.0722*b;
                    r = g = b = gray; break;
                case "sepia":
                    double sr = r*0.393+g*0.769+b*0.189;
                    double sg = r*0.349+g*0.686+b*0.168;
                    double sb = r*0.272+g*0.534+b*0.131;
                    r=sr; g=sg; b=sb; break;
                case "invert": r=255-r; g=255-g; b=255-b; break;
                case "warm": r=Math.Min(255,r+20); g=Math.Min(255,g+10); b=Math.Max(0,b-15); break;
                case "cool": r=Math.Max(0,r-15); g=Math.Min(255,g+5); b=Math.Min(255,b+20); break;
                case "vintage":
                    r=r*0.5+g*0.35+b*0.15+30; g=r*0.2+g*0.55+b*0.15+15; b=r*0.1+g*0.2+b*0.4+10; break;
                case "hdr":
                    double l2 = (r+g+b)/3/255.0;
                    double boost = l2 < 0.5 ? 1.3 : 0.85;
                    r*=boost; g*=boost; b*=boost; break;
                case "bw_high_contrast":
                    double g2 = 0.2126*r+0.7152*g+0.0722*b;
                    g2 = ((g2/255.0-0.5)*2.5+0.5)*255;
                    r=g=b=g2; break;
            }
            px[i]=(byte)Math.Clamp((int)b,0,255);
            px[i+1]=(byte)Math.Clamp((int)g,0,255);
            px[i+2]=(byte)Math.Clamp((int)r,0,255);
        } });

        IePushUndo();
        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, w, h), px, stride, 0);
        wb.Freeze();
        _ieWorkingImage = wb;
        _ieAdjustBase = wb;
        IePreviewImage.Source = wb;
        IeUpdateInfo();
    }

    // ---- Watermark ----
    private void IeWatermark_Apply(object sender, RoutedEventArgs e)
    {
        if (_ieWorkingImage == null) return;
        string text = TxtIeWatermarkText.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        IePushUndo();
        double fontSize = SldIeWmFontSize.Value;
        double opacity = SldIeWmOpacity.Value / 100.0;
        int pos = CmbIeWmPosition.SelectedIndex;
        var wmColor = (CmbIeWmColor.SelectedIndex) switch
        {
            1 => System.Windows.Media.Color.FromArgb((byte)(opacity*255), 0, 0, 0),
            2 => System.Windows.Media.Color.FromArgb((byte)(opacity*255), 255, 0, 0),
            3 => System.Windows.Media.Color.FromArgb((byte)(opacity*255), 255, 255, 0),
            _ => System.Windows.Media.Color.FromArgb((byte)(opacity*255), 255, 255, 255),
        };

        int w = _ieWorkingImage.PixelWidth, h = _ieWorkingImage.PixelHeight;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(_ieWorkingImage, new Rect(0, 0, w, h));
            var tf = new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight, tf, fontSize, new SolidColorBrush(wmColor),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double tx = pos switch { 0 or 3 or 6 => 20, 1 or 4 or 7 => (w-ft.Width)/2, _ => w-ft.Width-20 };
            double ty = pos switch { 0 or 1 or 2 => 20, 3 or 4 or 5 => (h-ft.Height)/2, _ => h-ft.Height-20 };
            dc.DrawText(ft, new Point(tx, ty));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv); rtb.Freeze();
        _ieWorkingImage = IeEnsureBgra32(rtb);
        _ieAdjustBase = _ieWorkingImage;
        IePreviewImage.Source = _ieWorkingImage;
        IeUpdateInfo();
    }

    // ---- Compress ----
    private void IeCompress_QualityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtIeCompressQuality != null) TxtIeCompressQuality.Text = $"{(int)SldIeCompressQuality.Value}%";
        IeUpdateCompressEstimate();
    }

    private void IeUpdateCompressEstimate()
    {
        if (_ieWorkingImage == null || TxtIeCompressOrigSize == null) return;
        long origSize = _ieImagePath != null && File.Exists(_ieImagePath) ? new FileInfo(_ieImagePath).Length : 0;
        TxtIeCompressOrigSize.Text = FileToolsService.FormatFileSize(origSize);
        try
        {
            int quality = (int)SldIeCompressQuality.Value;
            using var ms = new MemoryStream();
            var enc = new JpegBitmapEncoder { QualityLevel = quality };
            enc.Frames.Add(BitmapFrame.Create(_ieWorkingImage));
            enc.Save(ms);
            TxtIeCompressEstSize.Text = FileToolsService.FormatFileSize(ms.Length);
        }
        catch { TxtIeCompressEstSize.Text = "N/A"; }
    }

    private void IeCompress_Apply(object sender, RoutedEventArgs e)
    {
        if (_ieWorkingImage == null) return;
        IePushUndo();
        int quality = (int)SldIeCompressQuality.Value;
        using var ms = new MemoryStream();
        var enc = new JpegBitmapEncoder { QualityLevel = quality };
        enc.Frames.Add(BitmapFrame.Create(_ieWorkingImage));
        enc.Save(ms); ms.Position = 0;
        var bmp = new BitmapImage();
        bmp.BeginInit(); bmp.StreamSource = ms; bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit(); bmp.Freeze();
        _ieWorkingImage = IeEnsureBgra32(bmp);
        _ieAdjustBase = _ieWorkingImage;
        IePreviewImage.Source = _ieWorkingImage;
        IeUpdateInfo(); IeUpdateCompressEstimate();
    }

    // ---- Convert ----
    private void IeConvertFormat_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => IeUpdateConvertEstimate();

    private void IeUpdateConvertEstimate()
    {
        if (_ieWorkingImage == null || TxtIeConvertEstSize == null) return;
        try
        {
            int fmt = CmbIeConvertFormat?.SelectedIndex ?? 0;
            int quality = (int)(SldIeConvertQuality?.Value ?? 90);
            using var ms = new MemoryStream();
            BitmapEncoder enc = fmt switch
            {
                1 => new JpegBitmapEncoder { QualityLevel = quality },
                2 => new BmpBitmapEncoder(),
                3 => new TiffBitmapEncoder(),
                4 => new GifBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };
            enc.Frames.Add(BitmapFrame.Create(_ieWorkingImage));
            enc.Save(ms);
            TxtIeConvertEstSize.Text = $"Estimated: {FileToolsService.FormatFileSize(ms.Length)}";
        }
        catch { TxtIeConvertEstSize.Text = ""; }
    }

    private void IeConvert_Save(object sender, RoutedEventArgs e) => IeSave_Click(sender, e);

    // ---- Compare ----
    private BitmapSource? _iePreCompareSource;
    private void IeCompare_Down(object sender, MouseButtonEventArgs e)
    {
        if (_ieOriginalImage == null) return;
        _iePreCompareSource = IePreviewImage.Source as BitmapSource;
        IePreviewImage.Source = _ieOriginalImage;
        IeRotateTransform.Angle = 0; IeFlipTransform.ScaleX = 1; IeFlipTransform.ScaleY = 1;
    }
    private void IeCompare_Up(object sender, MouseButtonEventArgs e)
    {
        if (_iePreCompareSource != null)
        {
            IePreviewImage.Source = _iePreCompareSource;
            IeRotateTransform.Angle = _ieRotation;
            IeFlipTransform.ScaleX = _ieFlipH ? -1 : 1; IeFlipTransform.ScaleY = _ieFlipV ? -1 : 1;
            _iePreCompareSource = null;
        }
    }

    // ---- Reset ----
    private void IeReset_Click(object sender, RoutedEventArgs e)
    {
        if (_ieImagePath == null) return;
        IeLoadImage(_ieImagePath);
    }

    private void IeShowOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (_ieOriginalImage == null) return;
        IePushUndo();
        _ieWorkingImage = IeEnsureBgra32(_ieOriginalImage);
        _ieAdjustBase = _ieWorkingImage;
        IePreviewImage.Source = _ieWorkingImage;
        _ieRotation = 0; _ieFlipH = false; _ieFlipV = false;
        IeRotateTransform.Angle = 0; IeFlipTransform.ScaleX = 1; IeFlipTransform.ScaleY = 1;
        _ieZoom = 0; IeApplyZoom(); IeUpdateInfo();
    }

    // ---- Save ----
    private void IeSave_Click(object sender, RoutedEventArgs e)
    {
        if (_ieWorkingImage == null) return;
        string ext = CmbIeConvertFormat?.SelectedIndex switch { 1=>".jpg", 2=>".bmp", 3=>".tiff", 4=>".gif", _=>".png" };
        string defaultName = _ieImagePath != null ? System.IO.Path.GetFileNameWithoutExtension(_ieImagePath)+ext : "image"+ext;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG|*.png|JPEG|*.jpg;*.jpeg|BMP|*.bmp|TIFF|*.tiff|GIF|*.gif|All|*.*",
            FileName = defaultName
        };
        if (dlg.ShowDialog() != true) return;

        ShowProcessing("Saving image...");
        try
        {
            // Apply any pending adjust preview to working image
            var working = (IePreviewImage.Source as BitmapSource) ?? _ieWorkingImage;
            int quality = (int)SldIeCompressQuality.Value;
            double rot = _ieRotation; bool fh = _ieFlipH, fv = _ieFlipV;

            var thread = new System.Threading.Thread(() =>
            {
                BitmapSource final2 = working;
                if (rot != 0) { final2 = new TransformedBitmap(final2, new RotateTransform(rot)); final2.Freeze(); }
                if (fh || fv) { final2 = new TransformedBitmap(final2, new ScaleTransform(fh?-1:1, fv?-1:1)); final2.Freeze(); }
                string outExt = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
                BitmapEncoder encoder = outExt switch
                {
                    ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = quality },
                    ".bmp" => new BmpBitmapEncoder(),
                    ".tiff" or ".tif" => new TiffBitmapEncoder(),
                    ".gif" => new GifBitmapEncoder(),
                    _ => new PngBitmapEncoder()
                };
                encoder.Frames.Add(BitmapFrame.Create(final2));
                using var fs = new FileStream(dlg.FileName, FileMode.Create);
                encoder.Save(fs);
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start(); thread.Join();
            var info = new FileInfo(dlg.FileName);
            ShowComplete("Image saved!", $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}", dlg.FileName);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static BitmapImage BitmapSourceToBitmapImage(BitmapSource src)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms); ms.Position = 0;
        var bmp = new BitmapImage();
        bmp.BeginInit(); bmp.StreamSource = ms; bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit(); bmp.Freeze();
        return bmp;
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

    // Treat input as a URL if it looks like one; otherwise it's a search query.
    private static bool YtLooksLikeUrl(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
        s.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        s.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

    private void Yt_Fetch(object sender, RoutedEventArgs e)
        => _ = RunYtFetchAsync(TxtYtUrl.Text.Trim(), RbYtModePlaylists?.IsChecked == true, fromHero: true);

    private void Yt_FetchTop(object sender, RoutedEventArgs e)
        => _ = RunYtFetchAsync(TxtYtUrlTop.Text.Trim(), RbYtModePlaylistsTop?.IsChecked == true, fromHero: false);

    private async Task RunYtFetchAsync(string url, bool playlistMode, bool fromHero)
    {
        if (string.IsNullOrEmpty(url)) { MessageBox.Show("Enter a YouTube URL or a search term."); return; }
        if (!FileToolsService.IsYtDlpAvailable())
        {
            MessageBox.Show("yt-dlp is required.\n\nInstall: https://github.com/yt-dlp/yt-dlp",
                "yt-dlp Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // If the input isn't a URL, treat it as a YouTube search query.
        // The Videos/Playlists radio chooses between a video search and a playlist search.
        bool isSearch = !YtLooksLikeUrl(url);
        bool playlistSearch = isSearch && playlistMode;
        string target;
        string? batchItems = null;
        if (!isSearch)
            target = url;
        else if (playlistSearch)
        {
            // Playlist keyword search: first batch of 12 (more on scroll).
            target = BuildPlaylistSearchTarget(url);
            batchItems = $"1-{YtBatchSize}";
        }
        else
        {
            // Video keyword search: first batch of 12 (more on scroll).
            target = BuildVideoSearchTarget(url, YtBatchSize);
            batchItems = $"1-{YtBatchSize}";
        }
        _ytFiltersReady = false; // suppress filter handlers while this fetch mutates UI

        // Reset paging state for this query (both video and playlist searches page).
        _ytSearchQuery = isSearch ? url : "";
        _ytSearchIsPlaylist = playlistSearch;
        _ytLoadedCount = 0;
        _ytLoadingMore = false;
        _ytNoMore = !isSearch;

        _ytUrl = url;
        _ytSearchCache.Clear();
        foreach (var v in _ytVideos) v.PropertyChanged -= YtItem_PropertyChanged;
        _ytVideos.Clear();
        TxtYtSearch.Text = "";
        System.Windows.Data.CollectionViewSource.GetDefaultView(_ytVideos).Filter = null;
        _ytGridView = true;
        TxtYtUrlTop.Text = url;
        // Keep the hero and top-bar mode radios in sync.
        RbYtModeVideos.IsChecked = !playlistMode; RbYtModePlaylists.IsChecked = playlistMode;
        RbYtModeVideosTop.IsChecked = !playlistMode; RbYtModePlaylistsTop.IsChecked = playlistMode;

        // Spinner: hero uses the big centered spinner; re-searching from the top bar uses the header line.
        if (fromHero)
        {
            YtSpinner.Visibility = Visibility.Visible;
            TxtYtSpinner.Text = playlistSearch ? "Searching playlists..." : isSearch ? "Searching YouTube..." : "Fetching video info...";
            var spinAnim = new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(1))
            { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
            YtSpinnerRotate.BeginAnimation(System.Windows.Media.Animation.Storyboard.TargetPropertyProperty, null);
            YtSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }
        else
        {
            TxtYtDetail.Text = playlistSearch ? "Searching playlists\u2026" : "Searching\u2026";
            ShowProcessing(playlistSearch ? "Searching playlists\u2026" : "Searching\u2026", indeterminate: true);
        }

        try
        {
            var videos = await FileToolsService.FetchYouTubeVideosAsync(target, batchItems);

            if (fromHero)
            {
                YtSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                YtSpinner.Visibility = Visibility.Collapsed;
                ShowConfigState("youtube_dl");
            }
            else
            {
                ProcessingOverlay.Visibility = Visibility.Collapsed;
                ProcessingOverlay.Opacity = 1;
            }

            string headerTitle = playlistSearch
                ? $"Playlist results \u2014 \u201c{url}\u201d"
                : isSearch
                    ? $"Search results \u2014 \u201c{url}\u201d"
                    : (videos.Count == 1 ? videos[0].title : $"Playlist \u2014 {videos.Count} videos");
            string headerDetail = playlistSearch
                ? $"{videos.Count} playlist{(videos.Count != 1 ? "s" : "")} found"
                : $"{videos.Count} video{(videos.Count != 1 ? "s" : "")} found";

            PopulateYtItems(videos);

            if (isSearch)
            {
                _ytLoadedCount = videos.Count;
                _ytNoMore = videos.Count < YtBatchSize;
            }

            if (isSearch)
            {
                // Stay on the search page; cache the results so "Back" can restore them.
                _ytScreen = playlistSearch ? YtScreen.SearchPlaylists : YtScreen.SearchVideos;
                _ytResultsTitle = headerTitle;
                _ytResultsDetail = headerDetail;
                _ytSearchCache.Clear();
                _ytSearchCache.AddRange(_ytVideos);
            }
            else
            {
                // A pasted URL goes straight to the download page. A single pasted
                // video is pre-checked (obvious intent); a pasted playlist's items
                // stay unchecked so the user picks which to download.
                _ytScreen = YtScreen.Download;
                _ytBackTarget = YtScreen.Hero;
                if (_ytVideos.Count == 1) _ytVideos[0].IsSelected = true;
                CaptureYtDownloadOrder();
            }

            TxtYtTitle.Text = headerTitle;
            TxtYtDetail.Text = headerDetail;

            ApplyYtViewMode();
            ApplyYtScreen();
            UpdateYtSelectedCount();
            ResetYtDownloadFilter();
            _ytFiltersReady = true; // re-enable filter/sort re-search now that the UI is settled
        }
        catch (Exception ex)
        {
            if (fromHero)
            {
                YtSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                YtSpinner.Visibility = Visibility.Collapsed;
                ShowConfigState("youtube_dl");
            }
            else
            {
                ProcessingOverlay.Visibility = Visibility.Collapsed;
                ProcessingOverlay.Opacity = 1;
            }
            TxtYtTitle.Text = "Failed to fetch";
            TxtYtDetail.Text = ex.Message;
        }
    }

    // Builds YtVideoItems (with thumbnails) from fetch results and adds them to the bound collection.
    private void PopulateYtItems(List<(string title, string duration, string url, string thumbnail, bool isPlaylist, string channel, string views, string age)> videos)
    {
        foreach (var (title, duration, videoUrl, thumbnail, isPlaylist, channel, views, age) in videos)
        {
            var item = new YtVideoItem
            {
                Title = title,
                Duration = duration,
                VideoUrl = videoUrl,
                ThumbnailUrl = thumbnail,
                IsPlaylist = isPlaylist,
                Channel = channel,
                Views = views,
                Age = age
            };

            // Derive thumbnail from video ID when not provided (videos only; playlists rely on the given URL).
            string thumbUrl = thumbnail;
            if (string.IsNullOrEmpty(thumbUrl) && !isPlaylist)
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
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(thumbUrl);
                    bmp.DecodePixelWidth = 160;
                    bmp.EndInit();
                    item.Thumbnail = bmp;
                }
                catch { }
            }

            item.PropertyChanged += YtItem_PropertyChanged;
            _ytVideos.Add(item);
        }
    }

    // Shows/hides controls based on the current screen.
    private void ApplyYtScreen()
    {
        if (YtDownloadControls == null) return;
        StopMainSpinner(); // results are rendering — kill any search spinner
        bool searchVideos = _ytScreen == YtScreen.SearchVideos;
        bool searchPlaylists = _ytScreen == YtScreen.SearchPlaylists;
        bool search = searchVideos || searchPlaylists;
        bool download = _ytScreen == YtScreen.Download;

        YtTopSearchBar.Visibility = search ? Visibility.Visible : Visibility.Collapsed;
        YtFiltersPanel.Visibility = searchVideos ? Visibility.Visible : Visibility.Collapsed; // filters: video search only
        YtBottomBar.Visibility = searchVideos ? Visibility.Visible : Visibility.Collapsed;
        YtDownloadControls.Visibility = download ? Visibility.Visible : Visibility.Collapsed;

        // The page title reflects the stage: searching vs. reviewing for download.
        if (_currentToolId == "youtube_dl")
            TxtToolTitle.Text = download ? "Review & Download" : "YouTube Download";

        // Multiple real (non-playlist) videos are loaded → the filter/sort helpers are useful.
        bool manyVideos = _ytVideos.Count(v => !v.IsPlaylist) > 1;

        // Header right-side controls.
        BtnYtOpenPlaylist.Visibility = searchPlaylists ? Visibility.Visible : Visibility.Collapsed;
        // A single global Back (top-left) drives every level — the second Back button was removed.
        ChkYtSelectAll.Visibility = (searchVideos || download) ? Visibility.Visible : Visibility.Collapsed;
        CmbYtSortHeader.Visibility = searchVideos ? Visibility.Visible : Visibility.Collapsed;
        YtDownloadFilterBox.Visibility = (download && manyVideos) ? Visibility.Visible : Visibility.Collapsed;
        CmbYtDownloadSort.Visibility = (download && manyVideos) ? Visibility.Visible : Visibility.Collapsed;

        // Playlists allow picking exactly one; videos allow multi-select checkboxes.
        var mode = searchPlaylists ? System.Windows.Controls.SelectionMode.Single
                                   : System.Windows.Controls.SelectionMode.Extended;
        YtVideoGrid.SelectionMode = mode;
        YtVideoList.SelectionMode = mode;

        UpdateYtGoDownloadEnabled();
    }

    private void UpdateYtGoDownloadEnabled()
    {
        bool any = _ytVideos.Any(v => v.IsSelected && !v.IsPlaylist);
        if (BtnYtGoDownload != null) BtnYtGoDownload.IsEnabled = any;
        if (BtnYtContinue != null) BtnYtContinue.IsEnabled = any;
    }

    // From video search results: carry the selected videos to the download page.
    private void Yt_GoToDownload(object sender, RoutedEventArgs e)
    {
        var selected = _ytVideos.Where(v => v.IsSelected).ToList();
        if (selected.Count == 0) { MessageBox.Show("Select at least one video."); return; }

        _ytSearchCache.Clear();
        _ytSearchCache.AddRange(_ytVideos);
        _ytBackTarget = YtScreen.SearchVideos;

        foreach (var v in _ytVideos) v.PropertyChanged -= YtItem_PropertyChanged;
        _ytVideos.Clear();
        System.Windows.Data.CollectionViewSource.GetDefaultView(_ytVideos).Filter = null;
        TxtYtSearch.Text = "";
        foreach (var v in selected) { v.PropertyChanged += YtItem_PropertyChanged; _ytVideos.Add(v); }

        _ytScreen = YtScreen.Download;
        TxtYtTitle.Text = $"Review & download — {selected.Count} selected";
        TxtYtDetail.Text = $"{selected.Count} video{(selected.Count != 1 ? "s" : "")} ready to download";
        CaptureYtDownloadOrder();

        ApplyYtViewMode();
        ApplyYtScreen();
        UpdateYtSelectedCount();
        ResetYtDownloadFilter();
    }

    // Clears the search results and returns to the hero entry screen.
    private void Yt_ClearResults(object sender, RoutedEventArgs e)
    {
        foreach (var v in _ytVideos) v.PropertyChanged -= YtItem_PropertyChanged;
        _ytVideos.Clear();
        _ytSearchCache.Clear();
        System.Windows.Data.CollectionViewSource.GetDefaultView(_ytVideos).Filter = null;
        TxtYtSearch.Text = "";
        _ytScreen = YtScreen.Hero;
        _ytBackTarget = YtScreen.Hero;
        TxtYtUrl.Text = "";
        TxtYtUrlTop.Text = "";

        // Reset filters to defaults for the next search.
        _ytFiltersReady = false;
        _ytUploadDate = "any";
        _ytSort = 0;
        if (CmbYtSort != null) CmbYtSort.SelectedIndex = 0;
        if (CmbYtSortHeader != null) CmbYtSortHeader.SelectedIndex = 0;
        if (YtUploadDateGroup != null)
            foreach (var c in YtUploadDateGroup.Children)
                if (c is System.Windows.Controls.RadioButton rb) rb.IsChecked = (rb.Tag as string) == "any";

        ShowSelectState("youtube_dl");
    }

    // Loads the videos of the highlighted playlist into the download page (drill-in).
    private async void Yt_OpenPlaylist(object sender, RoutedEventArgs e)
    {
        var lb = _ytGridView ? (System.Windows.Controls.ListBox)YtVideoGrid : YtVideoList;
        if (lb.SelectedItem is not YtVideoItem pl || !pl.IsPlaylist)
        {
            MessageBox.Show("Select a playlist to open."); return;
        }
        await YtOpenPlaylistAsync(pl);
    }

    private async Task YtOpenPlaylistAsync(YtVideoItem pl)
    {
        // Cache the playlist search results so "Back" can restore them.
        _ytSearchCache.Clear();
        _ytSearchCache.AddRange(_ytVideos);
        _ytBackTarget = YtScreen.SearchPlaylists;

        BtnYtOpenPlaylist.IsEnabled = false;
        TxtYtDetail.Text = "Loading playlist…";
        ShowProcessing("Loading playlist…", indeterminate: true);
        try
        {
            var videos = await FileToolsService.FetchYouTubeVideosAsync(pl.VideoUrl);

            foreach (var v in _ytVideos) v.PropertyChanged -= YtItem_PropertyChanged;
            _ytVideos.Clear();
            System.Windows.Data.CollectionViewSource.GetDefaultView(_ytVideos).Filter = null;
            TxtYtSearch.Text = "";
            PopulateYtItems(videos);

            _ytScreen = YtScreen.Download;
            TxtYtTitle.Text = pl.Title;
            TxtYtDetail.Text = $"{videos.Count} video{(videos.Count != 1 ? "s" : "")} in playlist";
            CaptureYtDownloadOrder();

            ApplyYtViewMode();
            ApplyYtScreen();
            UpdateYtSelectedCount();
            ResetYtDownloadFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load playlist: " + ex.Message);
            TxtYtDetail.Text = _ytResultsDetail;
        }
        finally
        {
            ProcessingOverlay.Visibility = Visibility.Collapsed;
            ProcessingOverlay.Opacity = 1;
            BtnYtOpenPlaylist.IsEnabled = true;
        }
    }

    // Returns from the download page to the cached search results (or hero for a pasted URL).
    private void Yt_BackToResults(object sender, RoutedEventArgs e)
    {
        if (_ytBackTarget == YtScreen.Hero)
        {
            Yt_ClearResults(sender, e);
            return;
        }

        ResetYtDownloadFilter();
        foreach (var v in _ytVideos) v.PropertyChanged -= YtItem_PropertyChanged;
        _ytVideos.Clear();
        System.Windows.Data.CollectionViewSource.GetDefaultView(_ytVideos).Filter = null;
        TxtYtSearch.Text = "";
        foreach (var v in _ytSearchCache)
        {
            v.PropertyChanged += YtItem_PropertyChanged;
            _ytVideos.Add(v);
        }
        _ytScreen = _ytBackTarget;
        TxtYtTitle.Text = _ytResultsTitle;
        TxtYtDetail.Text = _ytResultsDetail;

        ApplyYtViewMode();
        ApplyYtScreen();
        UpdateYtSelectedCount();
    }

    private void Yt_UrlTopKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; Yt_FetchTop(sender, e); }
    }

    // Handles the global Back button for the YouTube tool's internal levels.
    // Returns true when it consumed the back (so the caller doesn't return to the tool grid).
    private bool YtTryGoBack()
    {
        switch (_ytScreen)
        {
            case YtScreen.Download:
                Yt_BackToResults(this, new RoutedEventArgs());
                return true;
            case YtScreen.SearchVideos:
            case YtScreen.SearchPlaylists:
                Yt_ClearResults(this, new RoutedEventArgs());
                return true;
            default:
                return false; // Hero -> let the global Back return to the tool grid
        }
    }

    private void YtMode_Changed(object sender, RoutedEventArgs e)
    {
        if (YtVideoOptions == null || ChkYtEmbedThumb == null) return;
        bool isVideo = RbYtVideo?.IsChecked == true;
        YtVideoOptions.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
        ChkYtEmbedThumb.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
        UpdateYtSelectedCount();
    }

    private async void Yt_Download(object sender, RoutedEventArgs e)
    {
        var selected = _ytVideos.Where(v => v.IsSelected).ToList();
        if (selected.Count == 0) { MessageBox.Show("Select at least one video."); return; }

        bool confirmAudio = RbYtAudio.IsChecked == true;
        string confirmQuality = (CmbYtQuality.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Best";
        string confirmMsg = confirmAudio
            ? $"Download {selected.Count} video(s) as MP3 audio?"
            : $"Download {selected.Count} video(s) as Video — {confirmQuality}?";
        if (!ConfirmDialog.Show(this, "Confirm Download", confirmMsg, "Download", "Cancel"))
            return;

        var folderDlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select download folder" };
        if (folderDlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        BtnYtDownload.IsEnabled = false;
        BtnYtDownload.Content = "Downloading…";
        BtnYtStop.Visibility = Visibility.Visible;
        // Lock list-editing controls so the in-flight selection can't change mid-download.
        if (ChkYtSelectAll != null) ChkYtSelectAll.IsEnabled = false;
        if (CmbYtDownloadSort != null) CmbYtDownloadSort.IsEnabled = false;
        if (YtDownloadFilterBox != null) YtDownloadFilterBox.IsEnabled = false;
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
                TxtYtOverallProgress.Text = $"Downloading {completed + 1} / {total}…";

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
            BtnYtDownload.Content = "Download \u2192";
            BtnYtStop.Visibility = Visibility.Collapsed;
            if (ChkYtSelectAll != null) ChkYtSelectAll.IsEnabled = true;
            if (CmbYtDownloadSort != null) CmbYtDownloadSort.IsEnabled = true;
            if (YtDownloadFilterBox != null) YtDownloadFilterBox.IsEnabled = true;
            _ytCancelSource = null;
            UpdateYtSelectedCount();
        }
    }

    private void Yt_Stop(object sender, RoutedEventArgs e)
    {
        _ytCancelSource?.Cancel();
        TxtYtOverallProgress.Text = "Stopping...";
    }

    private void Yt_SelectAll(object sender, RoutedEventArgs e)
    {
        foreach (var v in System.Windows.Data.CollectionViewSource.GetDefaultView(_ytVideos).Cast<YtVideoItem>())
            v.IsSelected = true;
        UpdateYtSelectedCount();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_currentToolId == "image_editor") IeHandleKeyDown(e);

        if (_currentToolId == "fill_sign"
            && e.Key == Key.Z
            && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && _fsElements.Count > 0)
        {
            _fsElements.RemoveAt(_fsElements.Count - 1);
            _ = FsRenderCurrentPage();
            e.Handled = true;
            return;
        }
    }

    private void Yt_DeselectAll(object sender, RoutedEventArgs e)
    {
        foreach (var v in System.Windows.Data.CollectionViewSource.GetDefaultView(_ytVideos).Cast<YtVideoItem>())
            v.IsSelected = false;
        UpdateYtSelectedCount();
    }

    // Fetch when Enter is pressed in the URL box.
    private void Yt_UrlKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Yt_Fetch(sender, e);
        }
    }

    // Space toggles the check state of the highlighted item(s).
    // Arrow keys / clicks only move the highlight — they never check/uncheck.
    private void Yt_ListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox lb) return;

        // In playlist results, Enter opens the highlighted playlist; checks don't apply here.
        if (_ytScreen == YtScreen.SearchPlaylists)
        {
            if (e.Key == Key.Enter && lb.SelectedItem is YtVideoItem pl && pl.IsPlaylist)
            {
                e.Handled = true;
                _ = YtOpenPlaylistAsync(pl);
            }
        }
        else if (e.Key == Key.Space)
        {
            var items = lb.SelectedItems.Cast<YtVideoItem>().ToList();
            if (items.Count == 0) return;
            // If any highlighted item is unchecked, check them all; otherwise uncheck all.
            bool check = items.Any(v => !v.IsSelected);
            foreach (var v in items) v.IsSelected = check;
            UpdateYtSelectedCount();
            e.Handled = true;
            return;
        }

        // Left/Right move the highlight linearly across the whole list (reading order),
        // wrapping to the next/previous row automatically — not just within a grid row.
        if (e.Key == Key.Left || e.Key == Key.Right)
        {
            int count = lb.Items.Count;
            if (count == 0) return;
            int idx = lb.SelectedIndex;
            idx = e.Key == Key.Right
                ? (idx < 0 ? 0 : Math.Min(idx + 1, count - 1))
                : (idx < 0 ? count - 1 : Math.Max(idx - 1, 0));
            lb.SelectedIndex = idx;
            lb.ScrollIntoView(lb.SelectedItem);
            if (lb.ItemContainerGenerator.ContainerFromIndex(idx) is ListBoxItem lbi)
                lbi.Focus();
            e.Handled = true;
        }
    }

    // Double-click toggles the check state of a video, or opens a playlist.
    private async void Yt_ItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox lb || e.OriginalSource is not DependencyObject src) return;
        if (ItemsControl.ContainerFromElement(lb, src) is ListBoxItem { DataContext: YtVideoItem v })
        {
            e.Handled = true;
            if (v.IsPlaylist) { await YtOpenPlaylistAsync(v); return; }
            v.IsSelected = !v.IsSelected;
            UpdateYtSelectedCount();
        }
    }

    private void Yt_ListView(object sender, RoutedEventArgs e)
    {
        _ytGridView = false;
        ApplyYtViewMode();
    }

    private void Yt_GridView(object sender, RoutedEventArgs e)
    {
        _ytGridView = true;
        ApplyYtViewMode();
    }

    private void ApplyYtViewMode()
    {
        if (YtVideoList == null || YtVideoGrid == null) return;

        YtVideoList.Visibility = _ytGridView ? Visibility.Collapsed : Visibility.Visible;
        YtVideoGrid.Visibility = _ytGridView ? Visibility.Visible : Visibility.Collapsed;

        var active = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF0000"));
        var inactive = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333"));
        var activeFg = Brushes.White;
        var inactiveFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCC"));

        BtnYtGridView.Background = _ytGridView ? active : inactive;
        BtnYtListView.Background = _ytGridView ? inactive : active;
        BtnYtGridView.Foreground = _ytGridView ? activeFg : inactiveFg;
        BtnYtListView.Foreground = _ytGridView ? inactiveFg : activeFg;
    }

    private void Yt_SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtYtSearchPlaceholder != null)
            TxtYtSearchPlaceholder.Visibility =
                string.IsNullOrEmpty(TxtYtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;

        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_ytVideos);
        if (view == null) return;

        string q = TxtYtSearch.Text.Trim();
        view.Filter = string.IsNullOrEmpty(q)
            ? null
            : o => o is YtVideoItem v && v.Title.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    // Clears the download-page title filter and removes any active list filter.
    private void ResetYtDownloadFilter()
    {
        if (TxtYtDownloadFilter != null) TxtYtDownloadFilter.Text = "";
        if (TxtYtDownloadFilterPh != null) TxtYtDownloadFilterPh.Visibility = Visibility.Visible;
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_ytVideos);
        if (view != null) view.Filter = null;
    }

    // Download page: filter the loaded videos by title (does not re-query YouTube).
    private void Yt_DownloadFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtYtDownloadFilterPh != null)
            TxtYtDownloadFilterPh.Visibility =
                string.IsNullOrEmpty(TxtYtDownloadFilter.Text) ? Visibility.Visible : Visibility.Collapsed;

        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_ytVideos);
        if (view == null) return;

        string q = TxtYtDownloadFilter.Text.Trim();
        view.Filter = string.IsNullOrEmpty(q)
            ? null
            : o => o is YtVideoItem v && v.Title.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    // Captures the current order as the "natural" order and resets the sort combo.
    private void CaptureYtDownloadOrder()
    {
        _ytDownloadOriginal.Clear();
        _ytDownloadOriginal.AddRange(_ytVideos);
        if (CmbYtDownloadSort != null)
        {
            _ytDownloadSortSyncing = true;
            CmbYtDownloadSort.SelectedIndex = 0;
            _ytDownloadSortSyncing = false;
        }
    }

    private int YtOriginalIndex(YtVideoItem v)
    {
        int i = _ytDownloadOriginal.IndexOf(v);
        return i < 0 ? int.MaxValue : i;
    }

    // Download page: re-order the loaded list locally (selected first / title / longest).
    private void Yt_DownloadSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ytDownloadSortSyncing || _ytScreen != YtScreen.Download) return;
        ApplyYtDownloadSort();
    }

    private void ApplyYtDownloadSort()
    {
        if (CmbYtDownloadSort == null) return;
        List<YtVideoItem> ordered = CmbYtDownloadSort.SelectedIndex switch
        {
            1 => _ytVideos.OrderBy(v => v.IsSelected ? 0 : 1).ThenBy(YtOriginalIndex).ToList(), // selected first
            2 => _ytVideos.OrderBy(v => v.Title, StringComparer.OrdinalIgnoreCase).ToList(),     // title A–Z
            3 => _ytVideos.OrderByDescending(v => v.DurationMinutes).ThenBy(YtOriginalIndex).ToList(), // longest
            _ => _ytVideos.OrderBy(YtOriginalIndex).ToList(),                                    // playlist order
        };
        for (int i = 0; i < ordered.Count; i++)
        {
            int cur = _ytVideos.IndexOf(ordered[i]);
            if (cur != i) _ytVideos.Move(cur, i);
        }
    }

    private void YtItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(YtVideoItem.IsSelected))
            UpdateYtSelectedCount();
    }

    private void UpdateYtSelectedCount()
    {
        if (TxtYtSelectedCount == null) return;
        int sel = _ytVideos.Count(v => v.IsSelected && !v.IsPlaylist);
        int total = _ytVideos.Count(v => !v.IsPlaylist);
        TxtYtSelectedCount.Text = $"{sel} of {total} selected";

        if (TxtYtSelectedVideos != null) TxtYtSelectedVideos.Text = sel.ToString();
        if (TxtYtEstSize != null) TxtYtEstSize.Text = "~" + FormatYtSize(EstimateYtSizeMb());

        // On the review/download page the bottom bar is hidden, so surface the
        // live selected count + estimated size in the header subtitle instead.
        if (_ytScreen == YtScreen.Download && TxtYtDetail != null && _ytCancelSource == null)
            TxtYtDetail.Text = $"{sel} of {total} selected · ~{FormatYtSize(EstimateYtSizeMb())}";

        if (ChkYtSelectAll != null)
            ChkYtSelectAll.IsChecked = total == 0 ? false : (sel == total ? true : (sel == 0 ? false : (bool?)null));

        UpdateYtGoDownloadEnabled();
    }

    // Rough size estimate: duration × MB-per-minute for the active format/quality.
    private double EstimateYtSizeMb()
    {
        double rate = YtMbPerMin();
        double mb = 0;
        foreach (var v in _ytVideos)
            if (v.IsSelected && !v.IsPlaylist) mb += v.DurationMinutes * rate;
        return mb;
    }

    private double YtMbPerMin()
    {
        // On the review screen the chosen format/quality is known; otherwise assume 720p.
        if (_ytScreen == YtScreen.Download && RbYtAudio?.IsChecked == true) return 1.0;
        string q = (_ytScreen == YtScreen.Download && CmbYtQuality?.SelectedItem is ComboBoxItem ci)
            ? (ci.Content?.ToString() ?? "Best") : "Best";
        return q switch { "360p" => 5.0, "480p" => 8.0, "720p" => 15.0, _ => 15.0 };
    }

    private static string FormatYtSize(double mb)
    {
        if (mb <= 0) return "0 MB";
        return mb >= 1024 ? $"{mb / 1024.0:0.##} GB" : $"{mb:0} MB";
    }

    // Builds YouTube's sp= filter token from upload-date + sort. Returns "" for the
    // default (Any time + Relevance) so the caller can use the plain ytsearch path.
    // Protobuf: field 1 (0x08) = sort; field 2 (0x12) = filter group containing
    // sub-field 1 (0x08) = upload-date. YouTube sort values: relevance=0, rating=1,
    // upload-date=2, view-count=3.
    private static string BuildYtSp(string uploadDate, int sortIdx)
    {
        int dateVal = uploadDate switch { "today" => 2, "week" => 3, "month" => 4, "year" => 5, _ => 0 };
        int sortVal = sortIdx switch { 1 => 2, 2 => 3, 3 => 1, _ => 0 };
        if (dateVal == 0 && sortVal == 0) return "";
        var b = new List<byte>();
        if (sortVal != 0) { b.Add(0x08); b.Add((byte)sortVal); }
        if (dateVal != 0) { b.Add(0x12); b.Add(0x02); b.Add(0x08); b.Add((byte)dateVal); }
        return Convert.ToBase64String(b.ToArray());
    }

    private void Yt_CheckedFirst(object sender, RoutedEventArgs e)
    {
        var ordered = _ytVideos.Where(v => v.IsSelected)
            .Concat(_ytVideos.Where(v => !v.IsSelected))
            .ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            int cur = _ytVideos.IndexOf(ordered[i]);
            if (cur != i) _ytVideos.Move(cur, i);
        }
    }

    // Header "Select All" tri-state checkbox: check/uncheck every (non-playlist) item.
    private void Yt_SelectAllToggle(object sender, RoutedEventArgs e)
    {
        bool check = ChkYtSelectAll.IsChecked == true;
        foreach (var v in _ytVideos)
            if (!v.IsPlaylist) v.IsSelected = check;
        UpdateYtSelectedCount();
    }

    // Upload-date radio changed → re-query video search.
    private async void Yt_FilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_ytFiltersReady) return;
        if (sender is System.Windows.Controls.RadioButton rb && rb.Tag is string tag) _ytUploadDate = tag;
        await YtReSearchAsync();
    }

    // Sort combo changed (either sidebar or header) → mirror the other, then re-query.
    private async void Yt_SortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ytFiltersReady || _ytSyncingSort) return;
        int idx = (sender as ComboBox)?.SelectedIndex ?? 0;
        _ytSort = idx;
        _ytSyncingSort = true;
        if (CmbYtSort != null && CmbYtSort.SelectedIndex != idx) CmbYtSort.SelectedIndex = idx;
        if (CmbYtSortHeader != null && CmbYtSortHeader.SelectedIndex != idx) CmbYtSortHeader.SelectedIndex = idx;
        _ytSyncingSort = false;
        await YtReSearchAsync();
    }

    private async void Yt_ClearFilters(object sender, RoutedEventArgs e)
    {
        _ytFiltersReady = false;
        _ytUploadDate = "any";
        _ytSort = 0;
        if (CmbYtSort != null) CmbYtSort.SelectedIndex = 0;
        if (CmbYtSortHeader != null) CmbYtSortHeader.SelectedIndex = 0;
        if (YtUploadDateGroup != null)
            foreach (var c in YtUploadDateGroup.Children)
                if (c is System.Windows.Controls.RadioButton rb) rb.IsChecked = (rb.Tag as string) == "any";
        _ytFiltersReady = true;
        await YtReSearchAsync();
    }

    // Re-run the current video search with the active filters/sort.
    private async Task YtReSearchAsync()
    {
        if (_ytScreen != YtScreen.SearchVideos || string.IsNullOrWhiteSpace(_ytUrl)) return;
        await RunYtFetchAsync(_ytUrl, playlistMode: false, fromHero: false);
    }

    // Builds a video-search target for the first `end` results (ytsearch, or the
    // results page + sp= token when a filter/sort is active).
    private string BuildVideoSearchTarget(string query, int end)
    {
        string sp = BuildYtSp(_ytUploadDate, _ytSort);
        return string.IsNullOrEmpty(sp)
            ? $"ytsearch{end}:{query}"
            : $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}&sp={Uri.EscapeDataString(sp)}";
    }

    // Playlist search uses the results page with the "Playlist" type filter (sp=EgIQAw==).
    private static string BuildPlaylistSearchTarget(string query)
        => $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}&sp=EgIQAw%3D%3D";

    private string BuildSearchTarget(string query, int end)
        => _ytSearchIsPlaylist ? BuildPlaylistSearchTarget(query) : BuildVideoSearchTarget(query, end);

    private bool YtOnSearchScreen() => _ytScreen == YtScreen.SearchVideos || _ytScreen == YtScreen.SearchPlaylists;

    // Infinite scroll: fetch the next batch of 12 and append (selection preserved).
    private async Task YtLoadMoreAsync()
    {
        if (_ytLoadingMore || _ytNoMore) return;
        if (!YtOnSearchScreen() || string.IsNullOrWhiteSpace(_ytSearchQuery)) return;
        if (_ytLoadedCount >= YtMaxResults) { _ytNoMore = true; return; }

        _ytLoadingMore = true;
        if (YtLoadMoreBar != null) YtLoadMoreBar.Visibility = Visibility.Visible;
        try
        {
            int start = _ytLoadedCount + 1;
            int end = _ytLoadedCount + YtBatchSize;
            string target = BuildSearchTarget(_ytSearchQuery, end);
            var more = await FileToolsService.FetchYouTubeVideosAsync(target, $"{start}-{end}");

            // De-dupe against what's already loaded, then append.
            var existing = new HashSet<string>(_ytVideos.Select(v => v.VideoUrl), StringComparer.OrdinalIgnoreCase);
            var fresh = more.Where(m => !existing.Contains(m.url)).ToList();
            PopulateYtItems(fresh);

            _ytLoadedCount += more.Count;
            if (more.Count < YtBatchSize || _ytLoadedCount >= YtMaxResults) _ytNoMore = true;

            string noun = _ytSearchIsPlaylist ? "playlists" : "videos";
            _ytResultsDetail = $"{_ytVideos.Count} {noun} loaded";
            TxtYtDetail.Text = _ytResultsDetail;
            UpdateYtSelectedCount();
            _ytSearchCache.Clear();
            _ytSearchCache.AddRange(_ytVideos); // keep Back in sync
        }
        catch { /* keep what we already have */ }
        finally
        {
            if (YtLoadMoreBar != null) YtLoadMoreBar.Visibility = Visibility.Collapsed;
            _ytLoadingMore = false;
        }
    }

    // Load the next batch when the results are scrolled near the bottom.
    private async void Yt_GridScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!YtOnSearchScreen() || _ytNoMore || _ytLoadingMore) return;
        if (e.ExtentHeight <= 0 || e.VerticalChange <= 0) return;
        if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 250)
            await YtLoadMoreAsync();
    }

    // Card "⋯" menu: Open on YouTube / Copy link.
    private void Yt_CardMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not YtVideoItem item) return;
        var menu = new System.Windows.Controls.ContextMenu();
        var open = new System.Windows.Controls.MenuItem { Header = "Open on YouTube" };
        open.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.VideoUrl) { UseShellExecute = true }); }
            catch { }
        };
        var copy = new System.Windows.Controls.MenuItem { Header = "Copy link" };
        copy.Click += (_, _) => { try { Clipboard.SetText(item.VideoUrl); } catch { } };
        menu.Items.Add(open);
        menu.Items.Add(copy);
        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }

    // Review-screen quality changed → refresh the estimated size.
    private void Yt_QualityChanged(object sender, SelectionChangedEventArgs e) => UpdateYtSelectedCount();
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
//  PDF Editor data classes
// =========================================================================

public class PdfEditorPageEntry
{
    public int OriginalIndex { get; set; }  // 0-based in source
    public int GlobalIndex { get; set; }     // 1-based across all files
    public BitmapImage? Thumbnail { get; set; }
    public int Rotation { get; set; }        // 0, 90, 180, 270
    public bool IsDeleted { get; set; }
    public string SourceFile { get; set; } = "";
    public Border? ThumbnailBorder { get; set; }
}

// =========================================================================
//  YouTube video item for download list
// =========================================================================

public class YtVideoItem : INotifyPropertyChanged
{
    private bool _isSelected = false;   // results start unchecked; the user opts in
    private string _status = "Pending";
    private int _progress;

    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    public bool IsPlaylist { get; set; }
    public string Title { get; set; } = "";
    public string Duration { get; set; } = "";
    public string VideoUrl { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Views { get; set; } = "";
    public string Age { get; set; } = "";

    // "12M views · 2 years ago" — drops the missing half (age is often absent in flat search).
    public string ViewsAge =>
        (string.IsNullOrEmpty(Views), string.IsNullOrEmpty(Age)) switch
        {
            (false, false) => $"{Views} · {Age}",
            (false, true) => Views,
            (true, false) => Age,
            _ => ""
        };

    // Approximate duration in minutes parsed from the "H:MM:SS" / "M:SS" duration string.
    public double DurationMinutes
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Duration)) return 0;
            var segs = Duration.Split(':');
            double mins = 0;
            try
            {
                if (segs.Length == 3) mins = int.Parse(segs[0]) * 60 + int.Parse(segs[1]) + int.Parse(segs[2]) / 60.0;
                else if (segs.Length == 2) mins = int.Parse(segs[0]) + int.Parse(segs[1]) / 60.0;
                else if (segs.Length == 1 && int.TryParse(segs[0], out int s)) mins = s / 60.0;
            }
            catch { return 0; }
            return mins;
        }
    }
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
