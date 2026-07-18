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
        ("merge_pdf",      "Merge PDF",       "Combine multiple PDFs into one",      "#FF8A8A", "\u229E", "PDF Tools"),
        ("split_pdf",      "Split PDF",       "Extract pages from PDF",              "#FFB088", "\u2016", "PDF Tools"),
        ("compress_pdf",   "Compress PDF",    "Reduce PDF file size",                "#FF9B9B", "\u2B07", "PDF Tools"),
        ("pdf_to_images",  "PDF to Images",   "Convert pages to JPG/PNG",            "#F58F8F", "\u29C9", "PDF Tools"),
        ("images_to_pdf",  "Images to PDF",   "Combine images into PDF",             "#F48FB1", "\u2B1C", "PDF Tools"),
        ("rotate_pdf",     "Rotate PDF",      "Rotate PDF pages",                    "#FFC777", "\u21BB", "PDF Tools"),
        ("watermark",      "Watermark PDF",   "Add text watermark",                  "#B79CFF", "\u2666", "PDF Tools"),
        ("page_numbers",   "Page Numbers",    "Add numbers to PDF",                  "#90A0F0", "#",      "PDF Tools"),
        ("extract_pages",  "Extract Pages",   "Pick specific pages from PDF",        "#FFB59B", "\u2398", "PDF Tools"),
        ("insert_pages",   "Insert Pages",    "Add new pages to a PDF",              "#C9B0A6", "\u2295", "PDF Tools"),
        ("protect_pdf",    "PDF Password",     "Add, remove, or recover a password",  "#8FB0F7", "\U0001F512", "PDF Tools"),
        ("pdf_editor",     "PDF Editor",      "Fill, sign, add text, checks & dates", "#7FD1B0", "\u270E", "PDF Tools"),
        ("image_editor",   "Image Editor",    "All-in-one image editor",             "#A78BFA", "\u2B1C", "Image Tools"),
        ("remove_bg",      "Remove Background", "Erase or replace photo backgrounds", "#9AE6C4", "\u2728", "Image Tools"),
        ("doc_scan",       "Document Scan",   "Crop a photo to a flat rectangle",    "#7FC8E6", "\u25a4", "Image Tools"),
        ("ocr",            "OCR \u2014 Extract Text", "Get selectable text from images & PDFs", "#6FD9C0", "\U0001F524", "Image Tools"),
        ("compress_image", "Compress Image",  "Reduce image file size",              "#6FD9E6", "\u2B07", "Image Tools"),
        ("resize_image",   "Resize Image",    "Change dimensions",                   "#6FD0C3", "\u2922", "Image Tools"),
        ("crop_image",     "Crop Image",      "Crop to selection",                   "#8FBEF7", "\u2702", "Image Tools"),
        ("rotate_flip",    "Rotate & Flip",   "Rotate or flip images",               "#C99BDB", "\u21BA", "Image Tools"),
        ("convert_format", "Convert Format",  "Change image format",                 "#F48FB1", "\u21C4", "Image Tools"),
        ("compress_office","Compress Office",  "Reduce DOCX/XLSX/PPTX",              "#A7B6C2", "\u2263", "Office Tools"),
        ("video_editor",  "Video Editor",     "Multi-clip timeline: join, trim, audio", "#F5A08F", "\U0001F39E", "Video & Audio"),
        ("video_tools",   "Video Tools",      "Trim, crop, rotate, flip & extract",  "#F58F8F", "\U0001F3AC", "Video & Audio"),
        ("extract_audio", "Extract Audio",    "Extract audio from video",            "#6FD3E6", "\u266B", "Video & Audio"),
        ("trim_audio",    "Trim Audio",       "Cut start and end of audio",          "#6FD0C3", "\u2702", "Video & Audio"),
        ("youtube_dl",    "YouTube Download", "Download video or audio from URL",    "#FF9B9B", "\u25B6", "Download"),
        ("link_dl",       "Link Downloader",  "Download any file or video from a link", "#7FB2F0", "\U0001F517", "Download"),
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

    private readonly ObservableCollection<FileItem> _mergePdfFiles = new();
    private readonly ObservableCollection<FileItem> _imgToPdfFiles = new();
    private readonly ObservableCollection<FileItem> _compressImgFiles = new();
    private readonly ObservableCollection<FileItem> _compressOfficeFiles = new();
    private readonly ObservableCollection<FileItem> _insertImages = new();

    // =====================================================================
    //  Single-file tool paths
    // =====================================================================

    private string? _splitPdfPath;
    private int _splitPageCount;
    private string? _compressPdfPath;
    private string? _pdfToImgPath;
    // Passwords for unlocked PDFs, keyed by file path (absence means no password needed).
    private readonly Dictionary<string, string> _pdfPasswords = new(StringComparer.OrdinalIgnoreCase);
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
    private bool _ytMusicMode;   // true when the YouTube/Music source toggle is on Music

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
        BuildCategoryChips();
        Core.ThemeManager.ThemeChanged += OnThemeChanged;
        UpdateThemeToggleGlyph();
        Closed += (s, e) => Core.ThemeManager.ThemeChanged -= OnThemeChanged;
        InitPanelMap();

        MergePdfList.ItemsSource = _mergePdfFiles;
        ImgToPdfList.ItemsSource = _imgToPdfFiles;
        CompressImgList.ItemsSource = _compressImgFiles;
        CompressOfficeList.ItemsSource = _compressOfficeFiles;
        InsertImageList.ItemsSource = _insertImages;
        ExtractAudioGrid.ItemsSource = _extractAudioFiles;
        ExtractAudioListView.ItemsSource = _extractAudioFiles;
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

    // =====================================================================
    //  Theme helpers
    // =====================================================================

    private static SolidColorBrush ThemeBrush(string key)
        => (SolidColorBrush)System.Windows.Application.Current.Resources[key];

    // For keys that may resolve to a gradient (e.g. SurfaceGradientBrush).
    private static Brush ThemeAnyBrush(string key)
        => (Brush)System.Windows.Application.Current.Resources[key];

    // =====================================================================
    //  Password-protected PDF support
    // =====================================================================

    /// <summary>The stored password for a PDF path, or null if none.</summary>
    private string? PwFor(string? path)
        => path != null && _pdfPasswords.TryGetValue(path, out var p) ? p : null;

    /// <summary>
    /// Prompts for a password if the PDF is locked. Returns true to proceed,
    /// false if the user cancelled (caller should abort loading that file).
    /// </summary>
    private async Task<bool> EnsurePdfUnlockedAsync(string path)
    {
        string? pw = await PasswordDialog.UnlockAsync(this, path);
        if (pw == null) return false;            // cancelled
        if (pw.Length > 0) _pdfPasswords[path] = pw;
        else _pdfPasswords.Remove(path);
        return true;
    }

    /// <summary>Loads a PDF for previews/thumbnails, using a stored password if present.</summary>
    private async Task<Windows.Data.Pdf.PdfDocument> LoadPdfDocAsync(string path)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        var pw = PwFor(path);
        return string.IsNullOrEmpty(pw)
            ? await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file)
            : await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file, pw);
    }

    private string _activeCategory = "All";

    private void BuildCategoryChips()
    {
        CategoryChips.Items.Clear();
        string[] cats = { "All", "PDF Tools", "Image Tools", "Office Tools", "Video & Audio", "Download" };
        foreach (var cat in cats)
        {
            bool active = cat == _activeCategory;
            var chip = new Border
            {
                Background = ThemeBrush(active ? "AccentBrush" : "SurfaceAltBrush"),
                CornerRadius = new CornerRadius(16), Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(14, 6, 14, 6), Cursor = Cursors.Hand, Tag = cat
            };
            chip.Child = new TextBlock
            {
                Text = cat, FontSize = 12, FontWeight = FontWeights.Medium,
                Foreground = ThemeBrush(active ? "AccentTextBrush" : "TextSecondaryBrush")
            };
            chip.MouseLeftButtonDown += (s, _) =>
            {
                _activeCategory = (string)((Border)s).Tag;
                RebuildCardPanel();
                BuildCategoryChips();
            };
            CategoryChips.Items.Add(chip);
        }
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e) => Core.ThemeManager.Toggle();

    private void OnThemeChanged()
    {
        UpdateThemeToggleGlyph();
        RebuildCardPanel();   // cards capture ThemeBrush() snapshots, so rebuild to repaint
        BuildCategoryChips(); // chips likewise
    }

    private void UpdateThemeToggleGlyph()
        => BtnThemeToggle.Content = Core.ThemeManager.Current == Core.ThemeManager.Dark ? "☀" : "\U0001F319";

    private Border CreateCard(string id, string title, string desc, string color, string icon)
    {
        var accentColor = (Color)ColorConverter.ConvertFromString(color);
        var accentBrush = new SolidColorBrush(accentColor);

        Brush surfaceBrush = ThemeAnyBrush("SurfaceGradientBrush");
        var hoverBrush = ThemeBrush("SurfaceHoverBrush");

        var card = new Border
        {
            MinWidth = 250, Height = 88, Margin = new Thickness(5),
            Background = surfaceBrush,
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x44, accentColor.R, accentColor.G, accentColor.B)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18),
            Cursor = Cursors.Hand, Tag = id,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.12, Color = (Color)ColorConverter.ConvertFromString("#3A4A8A") },
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        var grid = new Grid { Margin = new Thickness(14, 0, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconBorder = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(16),
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
        textStack.Children.Add(new TextBlock { Text = title, Foreground = ThemeBrush("TextPrimaryBrush"), FontSize = 15, FontWeight = FontWeights.SemiBold });
        textStack.Children.Add(new TextBlock
        {
            Text = desc, Foreground = ThemeBrush("TextSecondaryBrush"),
            FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        var arrow = new TextBlock
        {
            Text = "\u276F", FontSize = 18, Foreground = ThemeBrush("TextMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(arrow, 2);
        grid.Children.Add(arrow);
        card.Child = grid;

        card.MouseLeftButtonDown += Card_Click;
        card.MouseEnter += (s, _) =>
        {
            var b = (Border)s;
            b.Background = hoverBrush;
            b.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x88, accentColor.R, accentColor.G, accentColor.B));
        };
        card.MouseLeave += (s, _) =>
        {
            var b = (Border)s;
            b.Background = surfaceBrush;
            b.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x44, accentColor.R, accentColor.G, accentColor.B));
        };
        Core.Tilt.Attach(card);
        return card;
    }

    private void RebuildCardPanel()
    {
        CardPanel.Children.Clear();
        string filter = TxtToolSearch?.Text?.Trim().ToLowerInvariant() ?? "";
        int sortMode = CmbToolSort?.SelectedIndex ?? 0;

        var tools = ToolDefs.AsEnumerable();

        // Filter by active category chip
        if (_activeCategory != "All")
            tools = tools.Where(t => t.category == _activeCategory);

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
                        Foreground = ThemeBrush("TextMutedBrush"),
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
        _toolPanels["image_editor"] = PanelImageEditor;
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
        _selectViews["image_editor"] = ImgEditorSelectView;
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
        if (toolId == "extract_audio") ApplyExtAudioViewMode();
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
        foreach (var f in _extractAudioFiles) f.PropertyChanged -= ExtAudioItem_PropertyChanged;
        _extractAudioFiles.Clear();
        _extAudioOriginal.Clear();
        _extAudioOutputFolder = null;
        _extAudioGridView = true;
        _extAudioBusy = false;
        System.Windows.Data.CollectionViewSource.GetDefaultView(_extractAudioFiles).Filter = null;
        if (TxtExtAudioFilter != null) TxtExtAudioFilter.Text = "";
        if (TxtExtAudioFilterPh != null) TxtExtAudioFilterPh.Visibility = Visibility.Visible;
        if (CmbExtAudioSort != null) { _extAudioSyncingSort = true; CmbExtAudioSort.SelectedIndex = 0; _extAudioSyncingSort = false; }
        if (TxtExtAudioOverall != null) TxtExtAudioOverall.Text = "";
        if (TxtExtAudioOutputFolder != null) TxtExtAudioOutputFolder.Text = "";
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
        _activeTrimItem = null;
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

    /// <summary>
    /// Brings a just-opened non-modal window to the foreground. The card opens it on
    /// MouseLeftButtonDown, so the trailing MouseLeftButtonUp would otherwise re-activate
    /// this window — defer activation until the click is fully processed.
    /// </summary>
    private static void BringToFront(Window w)
    {
        w.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!w.IsLoaded) return;
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Maximized;
            w.Activate();
            w.Topmost = true;
            w.Topmost = false;
            w.Focus();
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        string id = (string)((Border)sender).Tag;

        // Unified interactive PDF editor (its own window — not the convert-and-save shell).
        if (id is "pdf_editor")
        {
            // Non-modal + no owner so the user can freely switch back to File Tools.
            var ed = new PdfMarkupWindow();
            ed.Show();
            BringToFront(ed);
            return;
        }

        // Images → PDF opens the interactive page composer (page size, margins,
        // multiple images per page, move / resize / 360° rotate).
        if (id == "images_to_pdf")
        {
            var comp = new ImagePdfComposerWindow();
            comp.Show();
            BringToFront(comp);
            return;
        }

        // Remove Background opens the interactive cut-out editor.
        if (id == "remove_bg")
        {
            var rb = new RemoveBackgroundWindow();
            rb.Show();
            BringToFront(rb);
            return;
        }

        // Document Scan opens the perspective-crop / scanner editor.
        if (id == "doc_scan")
        {
            var ds = new DocumentScanWindow();
            ds.Show();
            BringToFront(ds);
            return;
        }

        // OCR opens the text-extraction tool (images & PDFs → selectable text / searchable PDF).
        if (id == "ocr")
        {
            var ocr = new OcrToolWindow();
            ocr.Show();
            BringToFront(ocr);
            return;
        }

        // Video Editor opens the multi-clip timeline editor (join, trim, delete, add/remove audio).
        if (id == "video_editor")
        {
            var ve = new VideoEditorWindow();
            ve.Show();
            BringToFront(ve);
            return;
        }

        // Link Downloader opens the universal URL downloader (yt-dlp for media sites + direct file fetch).
        if (id == "link_dl")
        {
            var dl = new LinkDownloaderWindow();
            dl.Show();
            BringToFront(dl);
            return;
        }

        // New unified workspace — all PDF tools live here now.
        if (id is "merge_pdf" or "split_pdf" or "compress_pdf" or "pdf_to_images"
            or "rotate_pdf" or "extract_pages" or "insert_pages" or "page_numbers" or "watermark" or "protect_pdf")
        {
            // Non-modal + no owner so the user can freely switch back to File Tools.
            var ws = new ToolWorkspaceWindow(id);
            ws.Show();
            BringToFront(ws);
            return;
        }

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
        ConfirmDialog.Alert(this, "FFmpeg Required", "FFmpeg is required for video tools.\n\nInstall FFmpeg and add it to your system PATH:\nhttps://ffmpeg.org/download.html");
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
            if (!await EnsurePdfUnlockedAsync(path)) continue; // locked & cancelled — skip this file
            var info = new FileInfo(path);
            string extra = "";
            try
            {
                int pages = await FileToolsService.GetPdfPageCountAsync(path, PwFor(path));
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
        if (_mergePdfFiles.Count < 2)
        {
            ConfirmDialog.Alert(this, "Can't Merge PDF", "Add at least 2 PDF files to merge — a single file has nothing to combine.");
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PDF|*.pdf", FileName = "merged.pdf" };
        if (dlg.ShowDialog() != true) return;

        ShowProcessing("Merging PDF files...");
        try
        {
            var paths = _mergePdfFiles.Select(f => f.FilePath).ToArray();
            var passwords = paths.Select(PwFor).ToList();
            await FileToolsService.MergePdfsAsync(paths, dlg.FileName, CreateProgress(), passwords);
            var info = new FileInfo(dlg.FileName);
            ShowComplete("PDFs merged successfully!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
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
        if (!await EnsurePdfUnlockedAsync(path)) return; // locked & cancelled

        int pages;
        try
        {
            pages = await FileToolsService.GetPdfPageCountAsync(path, PwFor(path));
        }
        catch (Exception ex)
        {
            _splitPdfPath = null;
            ConfirmDialog.Alert(this, "Can't Open PDF", ex.Message, ConfirmDialog.AlertKind.Error);
            return; // stay on the select screen
        }

        // A single-page PDF can't be split — don't advance to the config screen.
        if (pages <= 1)
        {
            _splitPdfPath = null;
            _splitPageCount = 0;
            ConfirmDialog.Alert(this, "Can't Split PDF", "This PDF has only 1 page, so there's nothing to split.");
            return; // stay on the select screen
        }

        _splitPdfPath = path;
        _splitPageCount = pages;
        TxtSplitFileName.Text = System.IO.Path.GetFileName(path);
        TxtSplitInfo.Text = $"Total pages: {pages}";
        TxtSplitFrom.Text = "1";
        TxtSplitTo.Text = pages.ToString();
        ShowConfigState("split_pdf");
    }

    private async void SplitPdf_Execute(object sender, RoutedEventArgs e)
    {
        if (_splitPdfPath == null) return;

        if (_splitPageCount <= 1)
        {
            ConfirmDialog.Alert(this, "Can't Split PDF", "This PDF has only 1 page, so there's nothing to split.");
            return;
        }

        if (!int.TryParse(TxtSplitFrom.Text, out int from) || !int.TryParse(TxtSplitTo.Text, out int to)
            || from < 1 || to < from)
        {
            ConfirmDialog.Alert(this, "Invalid Input", "Enter a valid page range.");
            return;
        }

        if (to > _splitPageCount)
        {
            ConfirmDialog.Alert(this, "Page Range Too Large", $"This PDF has only {_splitPageCount} page(s). Choose a range within 1–{_splitPageCount}.");
            return;
        }

        var folderDlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select output folder" };
        if (folderDlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        ShowProcessing("Splitting PDF...");
        try
        {
            var results = await FileToolsService.SplitPdfAsync(_splitPdfPath, folderDlg.SelectedPath, from, to, CreateProgress(), PwFor(_splitPdfPath));
            ShowComplete($"Extracted {results.Length} page(s)!",
                $"Saved to {folderDlg.SelectedPath}",
                folderPath: folderDlg.SelectedPath);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
        }
    }

    // =====================================================================
    //  3. Compress PDF
    // =====================================================================

    private async void CompressPdf_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = CreatePdfOpenDialog(false);
        if (dlg.ShowDialog() != true) return;
        await LoadCompressPdf(dlg.FileName);
    }

    private async void CompressPdf_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsPdfFile);
        if (files.Length == 0) return;
        await LoadCompressPdf(files[0]);
    }

    private async Task LoadCompressPdf(string path)
    {
        if (!await EnsurePdfUnlockedAsync(path)) return; // locked & cancelled
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
            long result = await FileToolsService.CompressFileAsync(_compressPdfPath, dlg.FileName, quality, 0, CreateProgress(), PwFor(_compressPdfPath));
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
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
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
        if (!await EnsurePdfUnlockedAsync(path)) return; // locked & cancelled
        _pdfToImgPath = path;
        TxtPdfToImgFileName.Text = System.IO.Path.GetFileName(path);
        try
        {
            int pages = await FileToolsService.GetPdfPageCountAsync(path, PwFor(path));
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
            var results = await FileToolsService.PdfToImagesAsync(_pdfToImgPath, folderDlg.SelectedPath, format, dpi, CreateProgress(), PwFor(_pdfToImgPath));
            ShowComplete($"Converted {results.Length} pages!",
                $"Saved to {folderDlg.SelectedPath}",
                folderPath: folderDlg.SelectedPath);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
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
        if (_imgToPdfFiles.Count == 0)
        {
            ConfirmDialog.Alert(this, "No Images Added", "Add at least one image to create a PDF.");
            return;
        }

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
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
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
        if (!await EnsurePdfUnlockedAsync(path)) return; // locked & cancelled
        _rotatePdfPath = path;
        TxtRotatePdfFileName.Text = System.IO.Path.GetFileName(path);
        try
        {
            int pages = await FileToolsService.GetPdfPageCountAsync(path, PwFor(path));
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
            var pdfDoc = await LoadPdfDocAsync(pdfPath);
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
            await FileToolsService.RotatePdfAsync(_rotatePdfPath, dlg.FileName, (int)_rpAngle, CreateProgress(), PwFor(_rotatePdfPath));
            var info = new FileInfo(dlg.FileName);
            ShowComplete("PDF rotated!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            ProcessingOverlay.Visibility = Visibility.Collapsed;
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
        }
    }

    // =====================================================================
    //  7. Watermark PDF
    // =====================================================================

    private async void Watermark_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = CreatePdfOpenDialog(false);
        if (dlg.ShowDialog() != true) return;
        await LoadWatermarkPdf(dlg.FileName);
    }

    private async void Watermark_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsPdfFile);
        if (files.Length == 0) return;
        await LoadWatermarkPdf(files[0]);
    }

    private async Task LoadWatermarkPdf(string path)
    {
        if (!await EnsurePdfUnlockedAsync(path)) return; // locked & cancelled
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
            ConfirmDialog.Alert(this, "Input Required", "Enter watermark text.");
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
            await FileToolsService.WatermarkPdfAsync(_watermarkPdfPath, dlg.FileName, TxtWatermarkText.Text.Trim(), opacity, fontSize, CreateProgress(), PwFor(_watermarkPdfPath));
            var info = new FileInfo(dlg.FileName);
            ShowComplete("Watermark added successfully!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
        }
    }

    // =====================================================================
    //  8. Page Numbers
    // =====================================================================

    private async void PageNum_SelectFiles(object sender, RoutedEventArgs e)
    {
        var dlg = CreatePdfOpenDialog(false);
        if (dlg.ShowDialog() != true) return;
        await LoadPageNumPdf(dlg.FileName);
    }

    private async void PageNum_SelectDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e, IsPdfFile);
        if (files.Length == 0) return;
        await LoadPageNumPdf(files[0]);
    }

    private async Task LoadPageNumPdf(string path)
    {
        if (!await EnsurePdfUnlockedAsync(path)) return; // locked & cancelled
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
            await FileToolsService.AddPageNumbersAsync(_pageNumPdfPath, dlg.FileName, position, CreateProgress(), PwFor(_pageNumPdfPath));
            var info = new FileInfo(dlg.FileName);
            ShowComplete("Page numbers added!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
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
        if (!await EnsurePdfUnlockedAsync(path)) return; // locked & cancelled
        _extractPdfPath = path;
        TxtExtractFileName.Text = System.IO.Path.GetFileName(path);
        try
        {
            int pages = await FileToolsService.GetPdfPageCountAsync(path, PwFor(path));
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
            ConfirmDialog.Alert(this, "Extract Pages", "Select at least one page to extract.");
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
            await FileToolsService.ExtractPdfPagesAsync(_extractPdfPath, pageNumbers, dlg.FileName, CreateProgress(), PwFor(_extractPdfPath));
            var info = new FileInfo(dlg.FileName);
            ShowComplete("Pages extracted!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {pageNumbers.Length} pages \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            ProcessingOverlay.Visibility = Visibility.Collapsed;
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
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
            var pdfDoc = await LoadPdfDocAsync(pdfPath);
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
        if (!await EnsurePdfUnlockedAsync(dlg.FileName)) return; // locked & cancelled
        _insertBasePath = dlg.FileName;
        TxtInsertBaseName.Text = System.IO.Path.GetFileName(dlg.FileName);
        try
        {
            int pages = await FileToolsService.GetPdfPageCountAsync(dlg.FileName, PwFor(dlg.FileName));
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
            if (!await EnsurePdfUnlockedAsync(files[0])) return; // locked & cancelled
            _insertBasePath = files[0];
            TxtInsertBaseName.Text = System.IO.Path.GetFileName(files[0]);
            try
            {
                int pages = await FileToolsService.GetPdfPageCountAsync(files[0], PwFor(files[0]));
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
            var pdfDoc = await LoadPdfDocAsync(pdfPath);
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
        if (_insertBasePath == null)
        {
            ConfirmDialog.Alert(this, "No PDF Selected", "Select a base PDF to insert pages into.");
            return;
        }
        if (_insertImages.Count == 0)
        {
            ConfirmDialog.Alert(this, "Nothing to Insert", "Add at least one page (image) to insert.");
            return;
        }
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
            await FileToolsService.InsertPdfPagesAsync(_insertBasePath, imagePaths, afterPage, dlg.FileName, CreateProgress(), PwFor(_insertBasePath));
            var info = new FileInfo(dlg.FileName);
            ShowComplete("Pages inserted!",
                $"{System.IO.Path.GetFileName(dlg.FileName)} \u2014 {FileToolsService.FormatFileSize(info.Length)}",
                dlg.FileName);
        }
        catch (Exception ex)
        {
            ProcessingOverlay.Visibility = Visibility.Collapsed;
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
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

    private void CompressImg_ModeChanged(object sender, RoutedEventArgs e)
    {
        // XAML-initialized RadioButton can fire Checked before the rows exist.
        if (ImgQualityRow == null) return;
        bool byTarget = RbImgByTarget.IsChecked == true;
        ImgQualityRow.Visibility = byTarget ? Visibility.Collapsed : Visibility.Visible;
        ImgMaxDimRow.Visibility = byTarget ? Visibility.Collapsed : Visibility.Visible;
        ImgTargetRow.Visibility = byTarget ? Visibility.Visible : Visibility.Collapsed;
    }

    // Reads the target-size box + unit into bytes; null if invalid.
    private long? ImgTargetBytes()
    {
        if (!double.TryParse(TxtImgTarget.Text.Trim(), out double v) || v <= 0) return null;
        string unit = (CboImgTargetUnit.SelectedItem as ComboBoxItem)?.Content as string ?? "MB";
        double mult = unit == "KB" ? 1024 : 1024 * 1024;
        return (long)(v * mult);
    }

    private async void CompressImg_Execute(object sender, RoutedEventArgs e)
    {
        if (_compressImgFiles.Count == 0)
        {
            ConfirmDialog.Alert(this, "No Images Added", "Add at least one image to compress.");
            return;
        }

        bool byTarget = RbImgByTarget.IsChecked == true;
        long targetBytes = 0;
        if (byTarget)
        {
            var tb = ImgTargetBytes();
            if (tb == null)
            {
                ConfirmDialog.Alert(this, "Invalid Target Size", "Enter a target size greater than zero.", ConfirmDialog.AlertKind.Error);
                return;
            }
            targetBytes = tb.Value;
        }

        int quality = (int)SldImgQuality.Value;
        int maxDim = 0;
        if (!string.IsNullOrWhiteSpace(TxtImgMaxDim.Text))
            int.TryParse(TxtImgMaxDim.Text.Trim(), out maxDim);

        ShowProcessing("Compressing images...");

        long totalOriginal = 0, totalCompressed = 0;
        int count = _compressImgFiles.Count, missed = 0;

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

                if (byTarget)
                {
                    var res = await FileToolsService.CompressImageToTargetAsync(file.FilePath, outputPath, targetBytes);
                    totalCompressed += res.Bytes;
                    if (!res.TargetMet) missed++;
                }
                else
                {
                    long compressedSize = await FileToolsService.CompressImageAsync(file.FilePath, outputPath, quality, maxDim);
                    totalCompressed += compressedSize;
                }

                MainProgress.Value = (int)((i + 1) * 100.0 / count);
                TxtProgressPct.Text = $"{(int)((i + 1) * 100.0 / count)}%";
            }

            long saved = totalOriginal - totalCompressed;
            double pct = totalOriginal > 0 ? (saved * 100.0 / totalOriginal) : 0;
            string detail;
            if (byTarget)
            {
                detail = $"{FileToolsService.FormatFileSize(totalOriginal)} \u2192 {FileToolsService.FormatFileSize(totalCompressed)} " +
                         $"(target {FileToolsService.FormatFileSize(targetBytes)} each)";
                if (missed > 0)
                    detail += $"\n{missed} image(s) couldn't reach the target \u2014 smallest possible kept.";
            }
            else
            {
                detail = saved > 0
                    ? $"{FileToolsService.FormatFileSize(totalOriginal)} \u2192 {FileToolsService.FormatFileSize(totalCompressed)} ({pct:F1}% saved)"
                    : $"Compressed {count} image(s) \u2014 no size reduction";
            }
            string firstDir = System.IO.Path.GetDirectoryName(_compressImgFiles[0].FilePath)!;
            ShowComplete($"{count} image(s) compressed!", detail, folderPath: firstDir);
        }
        catch (Exception ex)
        {
            FadeOut(ProcessingOverlay);
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
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
            ConfirmDialog.Alert(this, "Invalid Input", "Enter valid dimensions.");
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
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
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

    private bool _cropSmartBusy;
    /// <summary>AI-detects the main subject and sets the crop selection to its bounding box.</summary>
    private async void CropSmart_Click(object sender, RoutedEventArgs e)
    {
        if (_cropSourcePath == null || _cropSmartBusy) return;
        _cropSmartBusy = true;
        string prevInfo = TxtCropInfo.Text;
        try
        {
            if (!AiMatting.ModelExists)
            {
                var prog = new Progress<double>(p => TxtCropInfo.Text = $"Downloading AI model… {p:0}%");
                await AiMatting.EnsureModelAsync(prog);
            }
            TxtCropInfo.Text = "Detecting subject…";

            var bi = new BitmapImage();
            bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(_cropSourcePath); bi.EndInit(); bi.Freeze();
            BitmapSource s = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
            int w = s.PixelWidth, h = s.PixelHeight;
            var px = new byte[w * h * 4];
            s.CopyPixels(px, w * 4, 0);

            var (minx, miny, maxx, maxy) = await Task.Run(() =>
            {
                var mask = AiMatting.ComputeMask(px, w, h);
                int x0 = w, y0 = h, x1 = -1, y1 = -1;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (mask[y * w + x] > 40)
                        {
                            if (x < x0) x0 = x; if (x > x1) x1 = x;
                            if (y < y0) y0 = y; if (y > y1) y1 = y;
                        }
                return (x0, y0, x1, y1);
            });

            if (maxx < 0) { TxtCropInfo.Text = "No clear subject found — drag a crop area manually."; return; }

            int pad = (int)(Math.Max(w, h) * 0.02);
            minx = Math.Max(0, minx - pad); miny = Math.Max(0, miny - pad);
            maxx = Math.Min(w - 1, maxx + pad); maxy = Math.Min(h - 1, maxy + pad);

            var disp = GetImageDisplayBounds();
            if (disp.Width <= 0 || disp.Height <= 0) { TxtCropInfo.Text = prevInfo; return; }
            double sx = disp.Width / w, sy = disp.Height / h;
            _cropSelectionRect = new Rect(disp.X + minx * sx, disp.Y + miny * sy, (maxx - minx) * sx, (maxy - miny) * sy);
            UpdateCropVisuals();
            TxtCropInfo.Text = "Smart crop set — adjust the handles or Crop & Save.";
        }
        catch (Exception ex)
        {
            ConfirmDialog.Alert(this, "Smart Crop Failed",
                $"{ex.Message}\n\nIf this is the first run, check your internet — the AI model downloads once.",
                ConfirmDialog.AlertKind.Error);
            TxtCropInfo.Text = prevInfo;
        }
        finally { _cropSmartBusy = false; }
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
            ConfirmDialog.Alert(this, "Input Required", "Load an image and draw a crop area first.");
            return;
        }

        var r = _cropSelectionRect;
        if (r.Width < 1 || r.Height < 1)
        {
            ConfirmDialog.Alert(this, "Input Required", "Draw a crop area on the image.");
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
            ConfirmDialog.Alert(this, "Error", "Invalid crop area.");
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
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
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
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
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

        // Block converting an image to the format it already is.
        string sourceExt = System.IO.Path.GetExtension(_convertSourcePath).ToLowerInvariant();
        if (sourceExt == ".jpeg") sourceExt = ".jpg";
        if (sourceExt == ".tif") sourceExt = ".tiff";
        if (sourceExt == ext)
        {
            ConfirmDialog.Alert(this, "Already This Format", $"This image is already in {targetFormat} format. Choose a different output format.");
            return;
        }

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
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
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
            BtnIeTool_filters, BtnIeTool_watermark, BtnIeTool_compress, BtnIeTool_convert,
            BtnIeTool_background
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
            "background" => BtnIeTool_background,
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
        IeSecBackground.Visibility = tool == "background" ? Visibility.Visible : Visibility.Collapsed;

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

    // ---- Background (AI matting) ----
    private bool _ieBgBusy;

    /// <summary>Pulls the working image to a BGRA buffer and computes the AI subject mask (downloads the
    /// model once). Returns (pixels, mask, w, h) or null on failure/cancel.</summary>
    private async Task<(byte[] px, byte[] mask, int w, int h)?> IeComputeMaskAsync()
    {
        if (_ieWorkingImage == null || _ieBgBusy) return null;
        _ieBgBusy = true;
        try
        {
            if (!AiMatting.ModelExists)
            {
                var prog = new Progress<double>(p => TxtIeCurrentSize.Text = $"Downloading AI model… {p:0}%");
                await AiMatting.EnsureModelAsync(prog);
            }
            TxtIeCurrentSize.Text = "Detecting subject…";
            var src = IeEnsureBgra32(_ieWorkingImage);
            int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
            var px = new byte[h * stride];
            src.CopyPixels(px, stride, 0);
            var mask = await Task.Run(() => AiMatting.ComputeMask(px, w, h));
            return (px, mask, w, h);
        }
        catch (Exception ex)
        {
            ConfirmDialog.Alert(this, "Background AI Failed",
                $"{ex.Message}\n\nIf this is the first run, check your internet — the model downloads once.",
                ConfirmDialog.AlertKind.Error);
            return null;
        }
        finally { _ieBgBusy = false; TxtIeCurrentSize.Text = ""; }
    }

    private void IeCommitBg(byte[] px, int w, int h)
    {
        IePushUndo();
        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, w, h), px, w * 4, 0);
        wb.Freeze();
        _ieWorkingImage = wb;
        _ieAdjustBase = wb; _ieFilterBase = wb;
        IePreviewImage.Source = wb;
        IeUpdateInfo();
    }

    private async void IeBgRemove_Click(object sender, RoutedEventArgs e)
    {
        var r = await IeComputeMaskAsync();
        if (r is not { } m) return;
        AiMatting.ApplyMask(m.px, m.mask);
        BackgroundRemover.SmoothAlpha(m.px, m.w, m.h, 2);
        IeCommitBg(m.px, m.w, m.h);
    }

    private async void IeBgBlur_Click(object sender, RoutedEventArgs e)
    {
        int radius = (int)(SldIeBgBlur?.Value ?? 14);
        var r = await IeComputeMaskAsync();
        if (r is not { } m) return;
        // Blur a copy, then composite the sharp subject over it using the mask as alpha.
        var blurred = (byte[])m.px.Clone();
        await Task.Run(() =>
        {
            BackgroundRemover.BoxBlur(blurred, m.w, m.h, radius);
            for (int p = 0; p < m.w * m.h; p++)
            {
                double a = m.mask[p] / 255.0;
                int i = p * 4;
                for (int c = 0; c < 3; c++)
                    blurred[i + c] = (byte)(m.px[i + c] * a + blurred[i + c] * (1 - a) + 0.5);
                blurred[i + 3] = 255;
            }
        });
        IeCommitBg(blurred, m.w, m.h);
    }

    private async void IeBgReplace_Click(object sender, RoutedEventArgs e)
    {
        byte br = 255, bg = 255, bb = 255;
        try { var c = (Color)System.Windows.Media.ColorConverter.ConvertFromString(TxtIeBgColor.Text.Trim()); br = c.R; bg = c.G; bb = c.B; }
        catch { ConfirmDialog.Alert(this, "Invalid Colour", "Enter a hex colour like #FFFFFF.", ConfirmDialog.AlertKind.Warning); return; }

        var r = await IeComputeMaskAsync();
        if (r is not { } m) return;
        await Task.Run(() =>
        {
            for (int p = 0; p < m.w * m.h; p++)
            {
                double a = m.mask[p] / 255.0;
                int i = p * 4;
                m.px[i]     = (byte)(m.px[i]     * a + bb * (1 - a) + 0.5);
                m.px[i + 1] = (byte)(m.px[i + 1] * a + bg * (1 - a) + 0.5);
                m.px[i + 2] = (byte)(m.px[i + 2] * a + br * (1 - a) + 0.5);
                m.px[i + 3] = 255;
            }
        });
        IeCommitBg(m.px, m.w, m.h);
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
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
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
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
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
            ConfirmDialog.Alert(this, "Export Failed", ex.Message, ConfirmDialog.AlertKind.Error);
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
    private bool _extAudioGridView = true;                 // card grid vs. row list
    private bool _extAudioSyncingSort;                     // guard while resetting the sort combo
    private bool _extAudioBusy;                            // an extraction run is in progress
    private readonly List<FileItem> _extAudioOriginal = new(); // added order, for the "Added order" sort
    private static readonly string[] ExtAudioVideoExts =
        { ".mp4", ".avi", ".mov", ".mkv", ".wmv", ".webm", ".m4v", ".flv", ".3gp", ".mpeg", ".mpg" };

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
        var files = CollectDroppedVideos(e);
        if (files.Length == 0) return;
        AddVideosToExtractList(files);
        ShowConfigState("extract_audio");
    }

    // Dropped items may be files or folders \u2014 gather all videos (folders scanned recursively).
    private static string[] CollectDroppedVideos(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return Array.Empty<string>();
        var dropped = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var result = new List<string>();
        foreach (var p in dropped)
        {
            if (Directory.Exists(p)) result.AddRange(ScanFolderForVideos(p));
            else if (File.Exists(p) && IsVideoFile(p)) result.Add(p);
        }
        return result.ToArray();
    }

    private static string[] ScanFolderForVideos(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => ExtAudioVideoExts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))
                .ToArray();
        }
        catch { return Array.Empty<string>(); }
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
        var fbd = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select a folder \u2014 videos in it and its subfolders are added"
        };
        if (fbd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var files = ScanFolderForVideos(fbd.SelectedPath);
        if (files.Length > 0) AddVideosToExtractList(files);
        else ConfirmDialog.Alert(this, "Nothing to add", "No video files found in that folder.", ConfirmDialog.AlertKind.Info);
    }

    private void ExtractAudio_Remove(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string path)
        {
            var item = _extractAudioFiles.FirstOrDefault(f => f.FilePath == path);
            if (item != null)
            {
                item.PropertyChanged -= ExtAudioItem_PropertyChanged;
                _extractAudioFiles.Remove(item);
                _extAudioOriginal.Remove(item);
            }
            RenumberList(_extractAudioFiles);
        }
        UpdateExtAudioFooter();
    }

    private void ExtractAudio_BrowseFolder(object sender, RoutedEventArgs e)
    {
        var fbd = new System.Windows.Forms.FolderBrowserDialog { Description = "Select output folder" };
        if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _extAudioOutputFolder = fbd.SelectedPath;
            TxtExtAudioOutputFolder.Text = _extAudioOutputFolder;
            RbExtAudioNew.IsChecked = true;
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
            long size = 0;
            try { size = new FileInfo(path).Length; } catch { }
            var item = new FileItem
            {
                Index = _extractAudioFiles.Count + 1,
                FilePath = path,
                FileName = System.IO.Path.GetFileName(path),
                FileSize = FileToolsService.FormatFileSize(size),
                IsSelected = true,
            };
            item.PropertyChanged += ExtAudioItem_PropertyChanged;
            _extractAudioFiles.Add(item);
            _extAudioOriginal.Add(item);

            // Probe duration/resolution and grab a thumbnail in the background so the
            // grid fills in progressively instead of blocking on each file.
            _ = LoadExtAudioMetaAsync(item);
        }
        UpdateExtAudioFooter();
    }

    // Fills in duration, resolution and a frame-grab thumbnail for one item, asynchronously.
    private async Task LoadExtAudioMetaAsync(FileItem item)
    {
        try
        {
            var (duration, w, h, _) = await FileToolsService.GetVideoInfoAsync(item.FilePath);
            string resolution = h > w ? $"{w}p" : $"{Math.Min(w, h)}p";
            item.Extra = resolution;
            item.DurationText = FileToolsService.FormatTimeSpan(duration);
        }
        catch { }

        try
        {
            string? thumb = await FileToolsService.GenerateVideoThumbnailAsync(item.FilePath);
            if (thumb != null)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(thumb);
                bmp.EndInit();
                bmp.Freeze();
                item.Thumbnail = bmp; // notifying \u2192 card/list updates
            }
        }
        catch { }
    }

    private void ExtAudioItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileItem.IsSelected))
            UpdateExtAudioFooter();
    }

    private void UpdateExtAudioFooter()
    {
        int total = _extractAudioFiles.Count;
        int sel = _extractAudioFiles.Count(f => f.IsSelected);
        TxtExtAudioFileCount.Text = total == 0
            ? "No files added yet"
            : $"{sel} of {total} selected";

        if (ChkExtAudioSelectAll != null)
            ChkExtAudioSelectAll.IsChecked = total == 0 ? false : (sel == total ? true : (sel == 0 ? false : (bool?)null));

        long bytes = 0;
        foreach (var f in _extractAudioFiles.Where(f => f.IsSelected))
            try { bytes += new FileInfo(f.FilePath).Length; } catch { }
        if (TxtExtAudioTotalSize != null)
            TxtExtAudioTotalSize.Text = sel == 0 ? "0 selected" : $"{sel} selected \u00b7 {FileToolsService.FormatFileSize(bytes)}";

        if (BtnExtAudioExtract != null) BtnExtAudioExtract.IsEnabled = sel > 0 && !_extAudioBusy;
    }

    // Select-all tri-state checkbox toggles every item.
    private void ExtractAudio_SelectAllToggle(object sender, RoutedEventArgs e)
    {
        bool check = ChkExtAudioSelectAll.IsChecked == true;
        foreach (var f in _extractAudioFiles) f.IsSelected = check;
        UpdateExtAudioFooter();
    }

    private void ExtractAudio_FilterChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtExtAudioFilterPh != null)
            TxtExtAudioFilterPh.Visibility =
                string.IsNullOrEmpty(TxtExtAudioFilter.Text) ? Visibility.Visible : Visibility.Collapsed;

        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_extractAudioFiles);
        if (view == null) return;
        string q = TxtExtAudioFilter.Text.Trim();
        view.Filter = string.IsNullOrEmpty(q)
            ? null
            : o => o is FileItem v && v.FileName.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void ExtractAudio_SortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_extAudioSyncingSort || CmbExtAudioSort == null) return;
        int ExtOrig(FileItem v) { int i = _extAudioOriginal.IndexOf(v); return i < 0 ? int.MaxValue : i; }
        List<FileItem> ordered = CmbExtAudioSort.SelectedIndex switch
        {
            1 => _extractAudioFiles.OrderBy(v => v.IsSelected ? 0 : 1).ThenBy(ExtOrig).ToList(),  // selected first
            2 => _extractAudioFiles.OrderBy(v => v.FileName, StringComparer.OrdinalIgnoreCase).ToList(), // name
            3 => _extractAudioFiles.OrderByDescending(v => ExtAudioDurationSeconds(v)).ThenBy(ExtOrig).ToList(), // longest
            _ => _extractAudioFiles.OrderBy(ExtOrig).ToList(),                                     // added order
        };
        for (int i = 0; i < ordered.Count; i++)
        {
            int cur = _extractAudioFiles.IndexOf(ordered[i]);
            if (cur != i) _extractAudioFiles.Move(cur, i);
        }
        RenumberList(_extractAudioFiles);
    }

    // Parses the "H:MM:SS" / "M:SS" DurationText into seconds for sorting.
    private static int ExtAudioDurationSeconds(FileItem v)
    {
        if (string.IsNullOrWhiteSpace(v.DurationText)) return 0;
        var segs = v.DurationText.Split(':');
        try
        {
            if (segs.Length == 3) return int.Parse(segs[0]) * 3600 + int.Parse(segs[1]) * 60 + int.Parse(segs[2]);
            if (segs.Length == 2) return int.Parse(segs[0]) * 60 + int.Parse(segs[1]);
            if (segs.Length == 1 && int.TryParse(segs[0], out int s)) return s;
        }
        catch { return 0; }
        return 0;
    }

    private void ExtractAudio_GridView(object sender, RoutedEventArgs e) { _extAudioGridView = true; ApplyExtAudioViewMode(); }
    private void ExtractAudio_ListView(object sender, RoutedEventArgs e) { _extAudioGridView = false; ApplyExtAudioViewMode(); }

    private void ApplyExtAudioViewMode()
    {
        if (ExtractAudioGrid == null || ExtractAudioListView == null) return;
        ExtractAudioGrid.Visibility = _extAudioGridView ? Visibility.Visible : Visibility.Collapsed;
        ExtractAudioListView.Visibility = _extAudioGridView ? Visibility.Collapsed : Visibility.Visible;

        var active = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00BCD4"));
        var inactive = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333"));
        var activeFg = Brushes.White;
        var inactiveFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCC"));
        BtnExtAudioGridView.Background = _extAudioGridView ? active : inactive;
        BtnExtAudioListView.Background = _extAudioGridView ? inactive : active;
        BtnExtAudioGridView.Foreground = _extAudioGridView ? activeFg : inactiveFg;
        BtnExtAudioListView.Foreground = _extAudioGridView ? inactiveFg : activeFg;
    }

    // Double-click toggles the check state of a card/row.
    private void ExtractAudio_ItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox lb || e.OriginalSource is not DependencyObject src) return;
        if (ItemsControl.ContainerFromElement(lb, src) is ListBoxItem { DataContext: FileItem v })
        {
            e.Handled = true;
            v.IsSelected = !v.IsSelected;
            UpdateExtAudioFooter();
        }
    }

    private async void ExtractAudio_Execute(object sender, RoutedEventArgs e)
    {
        var selected = _extractAudioFiles.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) { ConfirmDialog.Alert(this, "No File Selected", "Select at least one file."); return; }

        var format = (CmbExtAudioFormat.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLowerInvariant() ?? "mp3";
        string ext = format switch { "wav" => ".wav", "aac" => ".aac", "flac" => ".flac", _ => ".mp3" };
        bool embedThumb = ChkExtAudioThumb.IsChecked == true;
        bool overwrite = RbExtAudioOverwrite.IsChecked == true;

        string? outputDir = overwrite ? null : _extAudioOutputFolder;
        if (!overwrite && string.IsNullOrEmpty(outputDir))
        {
            ConfirmDialog.Alert(this, "No Output Folder", "Select an output folder first.");
            return;
        }

        // Lock the page controls while extracting; reset per-card progress.
        _extAudioBusy = true;
        BtnExtAudioExtract.IsEnabled = false;
        ChkExtAudioSelectAll.IsEnabled = false;
        CmbExtAudioSort.IsEnabled = false;
        foreach (var f in selected) { f.Status = "Pending"; f.Progress = 0; }

        int count = selected.Count;
        int completed = 0;
        long totalSize = 0;
        string? lastFile = null;

        try
        {
            for (int i = 0; i < count; i++)
            {
                var file = selected[i];
                string name = System.IO.Path.GetFileNameWithoutExtension(file.FilePath);
                string dir = overwrite ? System.IO.Path.GetDirectoryName(file.FilePath)! : outputDir!;
                string outPath = System.IO.Path.Combine(dir, name + ext);

                file.Status = "Extracting";
                file.Progress = 0;
                TxtExtAudioOverall.Text = $"Extracting {i + 1} / {count}\u2026";

                try
                {
                    var progress = new Progress<int>(p => file.Progress = p);
                    if (embedThumb)
                        await FileToolsService.ExtractAudioWithThumbnailAsync(file.FilePath, outPath, progress);
                    else
                        await FileToolsService.ExtractAudioAsync(file.FilePath, outPath, progress);

                    file.Status = "Done";
                    file.Progress = 100;
                    totalSize += new FileInfo(outPath).Length;
                    lastFile = outPath;
                    completed++;
                }
                catch
                {
                    file.Status = "Error";
                }
            }

            TxtExtAudioOverall.Text = $"Done \u2014 {completed} of {count} extracted";
            string detail = $"{completed} file(s) \u2014 {FileToolsService.FormatFileSize(totalSize)}";
            string folder = overwrite ? System.IO.Path.GetDirectoryName(selected[0].FilePath)! : outputDir!;
            if (completed > 0)
                ShowComplete($"{completed} audio track(s) extracted!", detail,
                    filePath: completed == 1 ? lastFile : null, folderPath: folder);
        }
        catch (Exception ex) { ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error); }
        finally
        {
            _extAudioBusy = false;
            ChkExtAudioSelectAll.IsEnabled = true;
            CmbExtAudioSort.IsEnabled = true;
            UpdateExtAudioFooter();
        }
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
    private AudioTrimItem? _activeTrimItem;          // the file currently being worked on (highlighted)
    private bool _suppressTrimFileSelect;            // guard while syncing sidebar selection
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
        // Ensure a working file is always highlighted.
        if (_activeTrimItem == null && _trimAudioItems.Count > 0)
            SetActiveTrimItem(_trimAudioItems[0]);
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

        // Header: filename (left) + zoom controls (right)
        var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

        var zoomPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(zoomPanel, Dock.Right);
        System.Windows.Controls.Button MakeZoomBtn(string glyph, Action act)
        {
            var b = new System.Windows.Controls.Button
            {
                Content = glyph, Width = 24, Height = 22, FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2E")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCC")),
                BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(2, 0, 2, 0)
            };
            b.Click += (s, e) => act();
            return b;
        }
        var zoomLabel = new TextBlock
        {
            Text = "1x", FontSize = 11, MinWidth = 26, TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888")),
            VerticalAlignment = VerticalAlignment.Center
        };
        item.TxtZoom = zoomLabel;
        var zoomTxt = new TextBlock
        {
            Text = "Zoom", FontSize = 11, Margin = new Thickness(0, 0, 6, 0),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666")),
            VerticalAlignment = VerticalAlignment.Center
        };
        zoomPanel.Children.Add(zoomTxt);
        zoomPanel.Children.Add(MakeZoomBtn("−", () => TrimZoom(item, 0.5)));   // −
        zoomPanel.Children.Add(zoomLabel);
        zoomPanel.Children.Add(MakeZoomBtn("+", () => TrimZoom(item, 2.0)));        // +
        headerDock.Children.Add(zoomPanel);

        var header = new TextBlock
        {
            Text = item.FileName,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCCCCC")),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        headerDock.Children.Add(header);
        outerStack.Children.Add(headerDock);

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

        // Waveform area — a horizontal ScrollViewer holds the (zoomable) waveform grid.
        var wfBorder = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111111")),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Height = WF_HEIGHT,
            Margin = new Thickness(8, 0, 8, 0)
        };
        var wfScroll = new System.Windows.Controls.ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
            Padding = new Thickness(0)
        };
        item.WfScroll = wfScroll;
        var wfGrid = new Grid { HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        item.WfZoomGrid = wfGrid;
        wfScroll.Content = wfGrid;
        wfBorder.Child = wfScroll;
        wfScroll.SizeChanged += (s, ev) => ApplyTrimZoom(item);

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
            SetActiveTrimItem(item);
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

        // Initial sizing/handle positions (after loaded), and keep them aligned on resize.
        canvas.Loaded += (s, ev) => ApplyTrimZoom(item);
        canvas.SizeChanged += (s, ev) => UpdateItemWaveformHandles(item);

        return rowBorder;
    }

    // Sets the per-wave zoom (clamped 1x..16x) and re-fits the waveform grid width.
    private void TrimZoom(AudioTrimItem item, double factor)
    {
        item.Zoom = Math.Clamp(item.Zoom * factor, 1, 16);
        SetActiveTrimItem(item);
        ApplyTrimZoom(item);
    }

    // Widens the waveform grid to viewport×zoom so the horizontal scrollbar appears,
    // giving finer control when placing cut points; 1x exactly fits the viewport.
    private void ApplyTrimZoom(AudioTrimItem item)
    {
        if (item.WfScroll == null || item.WfZoomGrid == null) return;
        double vw = item.WfScroll.ViewportWidth;
        if (vw <= 0) vw = item.WfScroll.ActualWidth;
        if (vw <= 0) return;
        item.WfZoomGrid.Width = Math.Max(vw, vw * item.Zoom);
        if (item.TxtZoom != null) item.TxtZoom.Text = item.Zoom <= 1.0 ? "1x" : $"{item.Zoom:0}x";
        UpdateItemWaveformHandles(item);
    }

    // Highlights the row + sidebar entry of the file currently being worked on.
    private void SetActiveTrimItem(AudioTrimItem item)
    {
        _activeTrimItem = item;
        foreach (var it in _trimAudioItems)
        {
            if (it.WfRow == null) continue;
            bool active = it == item;
            it.WfRow.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#22343A" : "#1E1E22"));
            it.WfRow.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#4DB6AC" : "#333333"));
            it.WfRow.BorderThickness = active ? new Thickness(3, 0, 0, 1) : new Thickness(0, 0, 0, 1);
        }
        var fi = _trimAudioFiles.FirstOrDefault(f => f.FilePath == item.FilePath);
        if (fi != null && !ReferenceEquals(TrimAudioFileList.SelectedItem, fi))
        {
            _suppressTrimFileSelect = true;
            TrimAudioFileList.SelectedItem = fi;
            _suppressTrimFileSelect = false;
        }
    }

    // Clicking a file in the sidebar makes it the active/working file and scrolls to it.
    private void TrimAudio_FileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTrimFileSelect) return;
        if (TrimAudioFileList.SelectedItem is FileItem fi)
        {
            var item = _trimAudioItems.FirstOrDefault(a => a.FilePath == fi.FilePath);
            if (item != null)
            {
                SetActiveTrimItem(item);
                item.WfRow?.BringIntoView();
            }
        }
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

        RenderLowPeakMarkers(item, canvasW);

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

    // Draws an amber marker (guide line + down-triangle) at each detected low peak.
    private void RenderLowPeakMarkers(AudioTrimItem item, double canvasW)
    {
        if (item.WfCanvas == null) return;
        foreach (var el in item.MarkerEls) item.WfCanvas.Children.Remove(el);
        item.MarkerEls.Clear();
        if (canvasW <= 0 || item.LowPeaks.Count == 0) return;

        double h = item.WfCanvas.ActualHeight > 0 ? item.WfCanvas.ActualHeight : 100;
        var fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFC107"));
        var lineFill = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xC1, 0x07));
        foreach (var frac in item.LowPeaks)
        {
            double x = Math.Clamp(frac, 0, 1) * canvasW;
            var line = new Rectangle { Width = 1, Height = h, Fill = lineFill, IsHitTestVisible = false };
            Canvas.SetLeft(line, x); Canvas.SetTop(line, 0);
            item.WfCanvas.Children.Add(line); item.MarkerEls.Add(line);

            var tri = new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection { new Point(x - 5, 0), new Point(x + 5, 0), new Point(x, 9) },
                Fill = fill, IsHitTestVisible = false
            };
            item.WfCanvas.Children.Add(tri); item.MarkerEls.Add(tri);
        }
    }

    private static string FormatMs(TimeSpan ts) => ts.TotalHours >= 1
        ? ts.ToString(@"h\:mm\:ss\.fff")
        : ts.ToString(@"mm\:ss\.fff");

    private void ToggleTrimPlayback(AudioTrimItem item)
    {
        SetActiveTrimItem(item);
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

    // Copies the active (highlighted) file's cut points to every file: the same number
    // of seconds is cut from the start and from the end of each track.
    private void TrimAudio_ApplyToAll(object sender, RoutedEventArgs e)
    {
        var src = _activeTrimItem ?? (_trimAudioItems.Count > 0 ? _trimAudioItems[0] : null);
        if (src == null || src.Duration.TotalSeconds <= 0)
        {
            ConfirmDialog.Alert(this, "No active file", "Set the cut points on a file first (drag its handles or use Suggest trims).", ConfirmDialog.AlertKind.Info);
            return;
        }

        double cutStartSec = src.TrimStartPct * src.Duration.TotalSeconds;
        double cutEndSec = (1.0 - src.TrimEndPct) * src.Duration.TotalSeconds;

        foreach (var item in _trimAudioItems)
        {
            double dur = item.Duration.TotalSeconds;
            if (dur <= 0) continue;
            double startPct = Math.Clamp(cutStartSec / dur, 0, 0.98);
            double endPct = Math.Clamp((dur - cutEndSec) / dur, startPct + 0.01, 1);
            item.TrimStartPct = startPct;
            item.TrimEndPct = endPct;
            UpdateItemWaveformHandles(item);
        }
    }

    private void TrimAudio_Reset(object sender, RoutedEventArgs e)
    {
        foreach (var item in _trimAudioItems)
        {
            item.TrimStartPct = 0;
            item.TrimEndPct = 1;
            item.LowPeaks = new List<double>();   // clear detected markers
            UpdateItemWaveformHandles(item);
        }
    }

    // Auto-detect each track's quiet dips ("low peaks"), mark them on the waveform, and
    // snap the trim handles past the quiet intro/outro (relative to the track's loudness).
    private async void TrimAudio_SuggestTrims(object sender, RoutedEventArgs e)
    {
        if (_trimAudioItems.Count == 0) return;
        StopTrimPlayback();
        BtnTrimSuggest.IsEnabled = false;
        ShowProcessing("Analyzing levels…", indeterminate: true);
        int adjusted = 0;
        try
        {
            foreach (var item in _trimAudioItems)
            {
                if (item.Duration.TotalSeconds <= 0) continue;
                try
                {
                    var (lowPeaks, startFrac, endFrac) = await FileToolsService.AnalyzeAudioLowPeaksAsync(item.FilePath);
                    item.LowPeaks = lowPeaks;
                    double startPct = Math.Clamp(startFrac, 0, 0.98);
                    double endPct = Math.Clamp(endFrac, startPct + 0.01, 1);
                    // Count it as a real suggestion if some intro/outro was trimmed.
                    if (startPct > 0.001 || endPct < 0.999) adjusted++;
                    item.TrimStartPct = startPct;
                    item.TrimEndPct = endPct;
                    UpdateItemWaveformHandles(item);
                }
                catch { /* leave this item's handles untouched */ }
            }
        }
        finally
        {
            ProcessingOverlay.Visibility = Visibility.Collapsed;
            ProcessingOverlay.Opacity = 1;
            StopMainSpinner();
            BtnTrimSuggest.IsEnabled = true;
        }

        if (adjusted == 0)
            ConfirmDialog.Alert(this, "Nothing to snap", "No quiet intro/outro was detected — handles left unchanged. Low-peak markers (if any) are shown on the waveform.", ConfirmDialog.AlertKind.Info);
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
            ConfirmDialog.Alert(this, "Invalid Folder", "Select a valid output folder.");
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
        catch (Exception ex) { ProcessingOverlay.Visibility = Visibility.Collapsed; ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error); }
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
        if (string.IsNullOrEmpty(url)) { ConfirmDialog.Alert(this, "URL Required", "Enter a YouTube URL or a search term."); return; }
        if (!FileToolsService.IsYtDlpAvailable())
        {
            ConfirmDialog.Alert(this, "yt-dlp Required", "yt-dlp is required.\n\nInstall: https://github.com/yt-dlp/yt-dlp");
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
        if (selected.Count == 0) { ConfirmDialog.Alert(this, "No Videos Selected", "Select at least one video."); return; }

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
            ConfirmDialog.Alert(this, "No Playlist Selected", "Select a playlist to open."); return;
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
            ConfirmDialog.Alert(this, "Error", "Failed to load playlist: " + ex.Message, ConfirmDialog.AlertKind.Error);
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
        if (YtVideoOptions == null || YtAudioOptions == null) return;
        bool isVideo = RbYtVideo?.IsChecked == true;
        YtVideoOptions.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
        YtAudioOptions.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
        UpdateYtSelectedCount();
    }

    private async void Yt_Download(object sender, RoutedEventArgs e)
    {
        var selected = _ytVideos.Where(v => v.IsSelected).ToList();
        if (selected.Count == 0) { ConfirmDialog.Alert(this, "No Videos Selected", "Select at least one video."); return; }

        bool confirmAudio = RbYtAudio.IsChecked == true;
        string confirmQuality = (CmbYtQuality.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Best";
        string confirmFormat = (CmbYtAudioFormat.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "M4A";
        string confirmMsg = confirmAudio
            ? $"Download {selected.Count} song(s) as {confirmFormat}?"
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
        string audioFormat = (CmbYtAudioFormat.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLowerInvariant() ?? "m4a";

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
                        video.VideoUrl, folderDlg.SelectedPath, quality, isAudio, audioFormat, progress);

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

    // Source toggle (YouTube / Music). Music routes keyword searches to YouTube Music songs;
    // playlist search doesn't apply there, so that toggle is hidden while Music is active.
    private void YtSource_Changed(object sender, RoutedEventArgs e)
    {
        _ytMusicMode = RbYtSourceMusic?.IsChecked == true;
        if (RbYtModePlaylists != null)
            RbYtModePlaylists.Visibility = _ytMusicMode ? Visibility.Collapsed : Visibility.Visible;
        if (_ytMusicMode && RbYtModeVideos != null) RbYtModeVideos.IsChecked = true;
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

    // Builds a video-search target for the first `end` results. In Music mode, routes to the
    // YouTube Music songs search; otherwise ytsearch (or the results page + sp= token when a
    // filter/sort is active).
    private string BuildVideoSearchTarget(string query, int end)
    {
        if (_ytMusicMode)
            return FileToolsService.BuildMusicSearchUrl(query);
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

    private string _extra = "";
    public string Extra { get => _extra; set { _extra = value; OnPropertyChanged(); OnPropertyChanged(nameof(MetaLine)); } }

    private string _durationText = "";
    public string DurationText { get => _durationText; set { _durationText = value; OnPropertyChanged(); } }

    private BitmapImage? _thumbnail;
    public BitmapImage? Thumbnail { get => _thumbnail; set { _thumbnail = value; OnPropertyChanged(); } }

    // Selection + per-file progress (used by the Extract Audio card grid; ignored elsewhere).
    private bool _isSelected = true;
    private string _status = "Pending";
    private int _progress;
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusColor)); } }
    public int Progress { get => _progress; set { _progress = value; OnPropertyChanged(); } }

    // "1080p · 12.3 MB" style meta line for the card footer.
    public string MetaLine =>
        (string.IsNullOrEmpty(Extra), string.IsNullOrEmpty(FileSize)) switch
        {
            (false, false) => $"{Extra} · {FileSize}",
            (false, true) => Extra,
            (true, false) => FileSize,
            _ => ""
        };

    public System.Windows.Media.Brush StatusColor => Status switch
    {
        "Done" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4CAF50")),
        "Extracting" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#26C6DA")),
        "Error" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF5350")),
        "Cancelled" => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#888")),
        _ => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#888")),
    };

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

    // Auto-detected quiet dips ("low peaks") as fractions 0..1, plus their drawn markers.
    public List<double> LowPeaks { get; set; } = new();
    public List<UIElement> MarkerEls { get; } = new();

    // Per-wave horizontal zoom (1x = fit; higher widens the wave for finer cut placement).
    public double Zoom { get; set; } = 1;
    public System.Windows.Controls.ScrollViewer? WfScroll { get; set; }
    public Grid? WfZoomGrid { get; set; }
    public TextBlock? TxtZoom { get; set; }

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
