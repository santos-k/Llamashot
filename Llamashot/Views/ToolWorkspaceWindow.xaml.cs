using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Llamashot.Core;
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Llamashot.Views;

/// <summary>
/// Reusable PDF-tool workspace shell (rail · preview · settings · info · action bar).
/// v1 hosts the Split PDF tool; other tools plug their own settings into SettingsHost.
/// </summary>
public partial class ToolWorkspaceWindow : Window
{
    // ---- state ----
    private string? _pdfPath;
    private string? _password;
    private int _pageCount;
    private int _curPage = 1;
    private double _zoom = 1.0;
    private const double PreviewBaseWidth = 540;

    // unified preview model: every page to show, in order (single file or concatenated multi-file)
    private readonly record struct PreviewPage(string Path, string? Password, int Page, bool IsImage);
    private readonly List<PreviewPage> _previewPages = new();

    // which tool this workspace is currently showing
    private string _toolId = "split_pdf";
    private static readonly HashSet<string> Implemented = new()
    {
        "merge_pdf", "split_pdf", "compress_pdf", "pdf_to_images", "images_to_pdf", "rotate_pdf",
        "extract_pages", "insert_pages", "page_numbers", "watermark", "protect_pdf"
    };

    // split settings controls
    private string _method = "custom";
    private readonly Dictionary<string, Border> _methodCards = new();
    private TextBox _fromBox = null!;
    private TextBox _toBox = null!;
    private TextBox _folderBox = null!;
    private readonly Dictionary<string, (Border box, bool on)> _opts = new();

    // compress settings controls
    private string _compressLevel = "rec";
    private int _quality = 65;
    private readonly Dictionary<string, Border> _compressCards = new();
    private bool _compressByTarget;
    private Border _segLevel = null!, _segTarget = null!;
    private StackPanel _levelPanel = null!, _targetPanel = null!;
    private TextBox _targetBox = null!;
    private ComboBox _targetUnitCombo = null!;

    // merge settings / state (multi-file)
    private readonly List<string> _mergeFiles = new();
    private readonly Dictionary<string, int> _mergePages = new();
    private readonly Dictionary<string, string?> _mergePw = new();
    private TextBox _mergeNameBox = null!;
    private int _previewRunId;

    // pdf-to-images settings
    private string _imgFormat = "png";
    private int _imgDpi = 150;
    private readonly Dictionary<string, Border> _fmtCards = new();
    private readonly Dictionary<string, Border> _dpiCards = new();

    // rotate settings
    private int _rotateDeg = 90;
    private readonly Dictionary<string, Border> _rotateCards = new();

    // extract-pages settings
    private TextBox _extractBox = null!;
    private TextBox _extractNameBox = null!;

    // page-numbers settings
    private string _pnPos = "bottom-center";
    private string _pnFont = "Segoe UI";
    private double _pnFontSize = 11;
    private string _pnColorHex = "#404040";
    private readonly Dictionary<string, Border> _pnCards = new();
    private readonly Dictionary<string, Border> _pnSizeCards = new();
    private readonly Dictionary<string, Border> _pnColorCards = new();

    // watermark settings
    private TextBox _wmTextBox = null!;
    private double _wmOpacity = 0.3;
    private int _wmFontSize = 48;
    private string _wmFont = "Segoe UI";
    private string _wmColorHex = "#808080";
    private string _wmOrient = "diagonal-up";
    private string _wmPos = "center";
    private readonly Dictionary<string, Border> _wmOpacityCards = new();
    private readonly Dictionary<string, Border> _wmSizeCards = new();
    private readonly Dictionary<string, Border> _wmOrientCards = new();
    private readonly Dictionary<string, Border> _wmColorCards = new();

    // basic font family choices shared by Page Numbers + Watermark
    private static readonly string[] BasicFonts =
        { "Segoe UI", "Arial", "Times New Roman", "Calibri", "Verdana", "Georgia", "Courier New" };
    // swatch palette (label -> hex)
    private static readonly (string name, string hex)[] BasicColors =
    {
        ("Black", "#000000"), ("Gray", "#666666"), ("Red", "#D7263D"), ("Blue", "#2563EB"),
        ("Green", "#2E7D32"), ("Orange", "#E07B00"), ("Purple", "#7C3AED"), ("White", "#FFFFFF")
    };

    // result chaining + panel collapse state
    private string? _chainPath;       // last single-PDF export, fed into the next tool the user opens
    private bool _leftCollapsed;
    private bool _infoCollapsed;
    private bool _tipCollapsed;
    private bool _settingsCollapsed;
    private bool _navCollapsed;
    private bool _rightCollapsed;
    private bool _picking;             // guards against re-entrant file-picker dialogs
    private double _dispW, _dispH;     // on-screen size of the current preview page (px)

    // insert-pages settings (items may be images or PDFs)
    private readonly List<string> _insertImages = new();
    private readonly Dictionary<string, string?> _insertPw = new();   // password per inserted PDF
    private readonly Dictionary<string, int> _insertItemPages = new(); // page count per inserted item
    private TextBox _insertAfterBox = null!;
    private TextBlock _insertCountText = null!;

    // protect settings
    private System.Windows.Controls.PasswordBox _protectPwBox = null!;
    private System.Windows.Controls.PasswordBox _protectConfirmBox = null!;
    private string _protectMode = "add";              // add | remove | break
    private string _breakCharsetKey = "digits";
    private int _breakMaxLen = 4;
    private bool _pdfLocked;                            // a break-mode file we couldn't open (no preview)
    private StackPanel _protectModeHost = null!;        // mode-specific settings slot
    private readonly Dictionary<string, Border> _protectModeCards = new();
    private readonly Dictionary<string, Border> _breakCharsetCards = new();
    private readonly Dictionary<string, Border> _breakLenCards = new();
    private System.Threading.CancellationTokenSource? _recoverCts;

    private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff" };

    /// <summary>True for tools that take a reorderable list of input files (Merge, Images→PDF).</summary>
    private bool IsMultiFile => _toolId == "merge_pdf" || _toolId == "images_to_pdf";

    private bool AcceptsFile(string path) => _toolId == "images_to_pdf"
        ? ImageExts.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase))
        : path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private static int QualityFor(string key) => key switch { "low" => 85, "high" => 40, _ => 65 };

    /// <summary>Per-tool title/subtitle/tip/action-button text.</summary>
    private static (string title, string sub, string tip, string action) Meta(string id) => id switch
    {
        "merge_pdf" => ("Merge PDF", "Combine multiple PDFs into one",
            "Add two or more PDFs, then drag with ↑ ↓ to set the order they'll be combined in.",
            "Merge PDF  →"),
        "pdf_to_images" => ("PDF to Images", "Convert each page to an image",
            "PNG keeps quality and transparency; JPG makes smaller files. Higher resolution looks sharper but takes more space.",
            "Convert  →"),
        "images_to_pdf" => ("Images to PDF", "Combine images into one PDF",
            "Each image becomes one page, in the order shown. Use ↑ ↓ to rearrange before creating the PDF.",
            "Create PDF  →"),
        "rotate_pdf" => ("Rotate PDF", "Turn every page by a fixed angle",
            "The preview shows how pages will look after rotating. The rotation is applied to all pages in the document.",
            "Rotate PDF  →"),
        "extract_pages" => ("Extract Pages", "Pull specific pages into a new PDF",
            "List the pages you want with commas and ranges, e.g. 1, 3, 5-8. Pages keep the order you enter.",
            "Extract  →"),
        "insert_pages" => ("Insert Pages", "Insert images or PDF pages into a PDF",
            "Add images or PDFs to insert, then choose which page they go after. Images add one page; PDFs add all their pages.",
            "Insert  →"),
        "page_numbers" => ("Page Numbers", "Stamp page numbers onto every page",
            "Choose where the number sits on each page. Numbering starts at 1 on the first page.",
            "Add Numbers  →"),
        "watermark" => ("Watermark PDF", "Overlay text across every page",
            "Enter your watermark text and tune the size and opacity. It's drawn diagonally across each page.",
            "Add Watermark  →"),
        "protect_pdf" => ("PDF Password", "Add, remove, or recover a password",
            "Add a password to lock a PDF, remove a known password, or recover an unknown one. Choose what to do on the right.",
            "Protect  →"),
        "compress_pdf" => ("Compress PDF", "Reduce file size while keeping quality",
            "Higher compression means a smaller file but lower image quality. Recommended works well for most documents.",
            "Compress PDF  →"),
        _ => ("Split PDF", "Split PDF pages into multiple files",
            "Choose a split method, set your page range, and pick an output folder.",
            "Split PDF  →"),
    };

    private static readonly (string id, string title)[] RailToolList =
    {
        ("merge_pdf","Merge PDF"), ("split_pdf","Split PDF"), ("compress_pdf","Compress PDF"),
        ("pdf_to_images","PDF to Images"), ("images_to_pdf","Images to PDF"), ("rotate_pdf","Rotate PDF"),
        ("extract_pages","Extract Pages"), ("insert_pages","Insert Pages"), ("page_numbers","Page Numbers"),
        ("watermark","Watermark PDF"), ("protect_pdf","PDF Password"),
    };

    public ToolWorkspaceWindow(string toolId = "split_pdf")
    {
        InitializeComponent();
        _toolId = Implemented.Contains(toolId) ? toolId : "split_pdf";
        BuildRail();
        BuildSettings();
        ApplyToolMeta();
        RefreshAll();
        UpdateThemeGlyph();
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private void ApplyToolMeta()
    {
        var m = Meta(_toolId);
        TxtTitle.Text = m.title;
        TxtSubtitle.Text = m.sub;
        TxtTip.Text = m.tip;
        TxtProcess.Text = _toolId == "protect_pdf"
            ? _protectMode switch { "remove" => "Remove Password  →", "break" => "Break Password  →", _ => "Protect  →" }
            : m.action;

        bool multi = IsMultiFile;
        TxtLeftTitle.Text = multi ? (_toolId == "images_to_pdf" ? "Images" : "Uploaded Files") : "Source File";
        TxtLeftSub.Text = multi ? "Use ↑ ↓ to reorder · drop more to add" : "The PDF you want to work on";
        BtnAddMore.Content = multi ? "+ Add More" : (_pdfPath == null ? "Open" : "Replace");
        TxtCenterEmptySub.Text = multi
            ? (_toolId == "images_to_pdf" ? "Add images on the left to preview the PDF" : "Add PDFs on the left to preview the merge")
            : "Add a file on the left to see it here";
        TxtPreviewTitle.Text = _toolId == "merge_pdf" ? "Merged Preview"
            : _toolId == "images_to_pdf" ? "PDF Preview" : "Preview";
    }

    /// <summary>Builds the per-tool settings slot for the active tool.</summary>
    private void BuildSettings()
    {
        SettingsHost.Children.Clear();
        _methodCards.Clear();
        _compressCards.Clear();
        _fmtCards.Clear();
        _dpiCards.Clear();
        _rotateCards.Clear();
        _pnCards.Clear();
        _pnSizeCards.Clear();
        _pnColorCards.Clear();
        _wmOpacityCards.Clear();
        _wmSizeCards.Clear();
        _wmOrientCards.Clear();
        _wmColorCards.Clear();
        if (_toolId == "merge_pdf") BuildMergeSettings();
        else if (_toolId == "images_to_pdf") BuildImagesToPdfSettings();
        else if (_toolId == "compress_pdf") BuildCompressSettings();
        else if (_toolId == "pdf_to_images") BuildPdfToImagesSettings();
        else if (_toolId == "rotate_pdf") BuildRotateSettings();
        else if (_toolId == "extract_pages") BuildExtractSettings();
        else if (_toolId == "insert_pages") BuildInsertSettings();
        else if (_toolId == "page_numbers") BuildPageNumbersSettings();
        else if (_toolId == "watermark") BuildWatermarkSettings();
        else if (_toolId == "protect_pdf") BuildProtectSettings();
        else BuildSplitSettings();
    }

    private void SwitchTool(string id)
    {
        if (id == _toolId || !Implemented.Contains(id)) return;
        _toolId = id;
        BuildRail();
        BuildSettings();
        ApplyToolMeta();

        // Carry the last exported PDF into the tool the user just opened (chaining).
        if (_chainPath != null && File.Exists(_chainPath) && !IsMultiFile && _chainPath != _pdfPath && AcceptsFile(_chainPath))
        {
            _ = LoadPdf(_chainPath);
            return;
        }

        if (_pdfPath != null && _toolId == "split_pdf")
        {
            if (_pageCount <= 1)
                ConfirmDialog.Alert(this, "Can't Split PDF",
                    "This PDF has only 1 page, so there's nothing to split.");
            else { SetRange(1, _pageCount); SelectMethod(_method); }
        }

        RefreshAll();
    }

    /// <summary>Returns the next implemented tool after the current one (wraps around).</summary>
    private string NextImplementedTool()
    {
        var ids = RailToolList.Select(t => t.id).ToList();
        int idx = ids.IndexOf(_toolId);
        for (int i = 1; i <= ids.Count; i++)
        {
            var cand = ids[(idx + i) % ids.Count];
            if (Implemented.Contains(cand) && cand != _toolId) return cand;
        }
        return _toolId;
    }

    private void MoveToNextTool()
    {
        var next = NextImplementedTool();
        if (next != _toolId) SwitchTool(next);
    }

    /// <summary>Remembers a freshly-exported single PDF so the next tool the user opens starts from it.</summary>
    private void RememberChain(string path)
    {
        if (!string.IsNullOrEmpty(path) && path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            _chainPath = path;
    }

    private static SolidColorBrush B(string key) => (SolidColorBrush)Application.Current.Resources[key];

    // ---- busy / spinner overlay ----
    private System.Windows.Media.Animation.Storyboard? _spin;

    private void ShowBusy(string title, string sub)
    {
        TxtBusyTitle.Text = title;
        TxtBusySub.Text = sub;
        BusyOverlay.Visibility = Visibility.Visible;
        if (_spin == null)
        {
            var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 360,
                new Duration(TimeSpan.FromSeconds(0.85)))
            { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
            _spin = new System.Windows.Media.Animation.Storyboard();
            _spin.Children.Add(anim);
            System.Windows.Media.Animation.Storyboard.SetTarget(anim, SpinnerRotate);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(anim,
                new PropertyPath(RotateTransform.AngleProperty));
        }
        _spin.Begin();
    }

    private void HideBusy()
    {
        _spin?.Stop();
        BusyOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnThemeChanged()
    {
        UpdateThemeGlyph();
        BuildRail();
        BuildSettings();
        BuildInfo();
        BuildLeftPanel();
        _ = BuildFilmstrip();
    }

    private void UpdateThemeGlyph()
        => TxtTheme.Text = ThemeManager.Current == ThemeManager.Dark ? "☀  Theme" : "🌙  Theme";

    private void Theme_Click(object sender, RoutedEventArgs e) => ThemeManager.Toggle();

    // =====================================================================
    //  Rail
    // =====================================================================
    private void BuildRail()
    {
        RailTools.Children.Clear();
        foreach (var (id, title) in RailToolList)
        {
            bool active = id == _toolId;
            var bd = new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(11, 9, 11, 9),
                Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand, Tag = id,
                Background = active ? B("AccentBrush") : Brushes.Transparent,
                Opacity = active ? 1 : 0.82
            };
            bd.Child = new TextBlock
            {
                Text = title, FontSize = 13.5,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = active ? B("AccentTextBrush") : B("TextSecondaryBrush")
            };
            bd.MouseLeftButtonUp += Rail_Click;
            RailTools.Children.Add(bd);
        }
    }

    private void Rail_Click(object sender, MouseButtonEventArgs e)
    {
        string id = (string)((Border)sender).Tag;
        if (id == _toolId) return;
        if (!Implemented.Contains(id))
        {
            ConfirmDialog.Alert(this, "Coming Soon",
                "This tool is moving into the new workspace next. Split PDF and Compress PDF are available here today.",
                ConfirmDialog.AlertKind.Info);
            return;
        }
        SwitchTool(id);
    }

    // =====================================================================
    //  Split settings (the per-tool slot)
    // =====================================================================
    private void BuildSplitSettings()
    {
        SettingsHost.Children.Clear();
        _methodCards.Clear();

        SettingsHost.Children.Add(SectionTitle("Split Method"));
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        AddMethod(grid, 0, 0, "custom", "Custom Range", "Split specific pages");
        AddMethod(grid, 0, 1, "every", "Every Page", "Extract every page");
        AddMethod(grid, 1, 0, "fixed", "Fixed Size", "Split by page count");
        AddMethod(grid, 1, 1, "extract", "Extract Pages", "Select specific pages");
        SettingsHost.Children.Add(grid);

        SettingsHost.Children.Add(SectionTitle("Page Range", 22));
        var rangeGrid = new Grid();
        rangeGrid.ColumnDefinitions.Add(new ColumnDefinition());
        rangeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        rangeGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var fromStack = LabeledField("FROM PAGE", out _fromBox, "1");
        var toStack = LabeledField("TO PAGE", out _toBox, "1");
        Grid.SetColumn(fromStack, 0); Grid.SetColumn(toStack, 2);
        rangeGrid.Children.Add(fromStack); rangeGrid.Children.Add(toStack);
        SettingsHost.Children.Add(rangeGrid);

        SettingsHost.Children.Add(MutedLabel("QUICK SELECT", 14));
        var pills = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        pills.Children.Add(QuickPill("All Pages", () => SetRange(1, _pageCount)));
        pills.Children.Add(QuickPill("First Half", () => SetRange(1, Math.Max(1, _pageCount / 2))));
        pills.Children.Add(QuickPill("Second Half", () => SetRange(_pageCount / 2 + 1, _pageCount)));
        SettingsHost.Children.Add(pills);

        SettingsHost.Children.Add(SectionTitle("Output Options", 22));
        var checks = new StackPanel();
        checks.Children.Add(MakeCheck("separate", "Create separate files for each page", true));
        checks.Children.Add(MakeCheck("reverse", "Extract pages in reverse order", false));
        checks.Children.Add(MakeCheck("metadata", "Preserve document properties", true));
        checks.Children.Add(MakeCheck("namerange", "Add page range to file name", true));
        SettingsHost.Children.Add(checks);

        SettingsHost.Children.Add(MutedLabel("OUTPUT FOLDER", 22));
        var folderGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        folderGrid.ColumnDefinitions.Add(new ColumnDefinition());
        folderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string defFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", "Split");
        _folderBox = new TextBox
        {
            Text = defFolder, FontSize = 12, Padding = new Thickness(11), Margin = new Thickness(0, 0, 8, 0),
            Background = B("SurfaceAltBrush"), Foreground = B("TextSecondaryBrush"),
            BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var browse = SoftButton("Browse", Browse_Click);
        Grid.SetColumn(browse, 1);
        folderGrid.Children.Add(_folderBox); folderGrid.Children.Add(browse);
        SettingsHost.Children.Add(folderGrid);

        SelectMethod(_method);
    }

    private void AddMethod(Grid grid, int row, int col, string key, string title, string desc)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Cursor = Cursors.Hand,
            Margin = new Thickness(col == 0 ? 0 : 6, row == 0 ? 0 : 12, col == 0 ? 6 : 0, 0),
            BorderThickness = new Thickness(1), Tag = key
        };
        var inner = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(2), Margin = new Thickness(0, 1, 11, 0), VerticalAlignment = VerticalAlignment.Top };
        var txt = new StackPanel();
        txt.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = B("TextPrimaryBrush") });
        txt.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = B("TextMutedBrush") });
        inner.Children.Add(dot); inner.Children.Add(txt);
        card.Child = inner;
        card.MouseLeftButtonUp += (_, _) => SelectMethod(key);
        Grid.SetRow(card, row); Grid.SetColumn(card, col);
        grid.Children.Add(card);
        _methodCards[key] = card;
    }

    private void SelectMethod(string key)
    {
        if (key is "fixed" or "extract")
        {
            ConfirmDialog.Alert(this, "Coming Soon",
                "Custom Range and Every Page are available now. Fixed Size and Extract land next.",
                ConfirmDialog.AlertKind.Info);
            key = _method; // revert
        }
        _method = key;
        foreach (var (k, card) in _methodCards)
        {
            bool on = k == key;
            card.BorderBrush = on ? B("DangerBrush") /*orange-ish via danger? use accent*/ : B("BorderSoftBrush");
            card.BorderBrush = on ? B("AccentBrush") : B("BorderSoftBrush");
            card.Background = on ? Tint("AccentBrush") : B("SurfaceBrush");
            var dot = (Border)((StackPanel)card.Child).Children[0];
            dot.BorderBrush = on ? B("AccentBrush") : B("BorderSoftBrush");
            dot.Child = on ? new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = B("AccentBrush") } : null;
        }
        bool isEvery = key == "every";
        _fromBox.IsEnabled = !isEvery; _toBox.IsEnabled = !isEvery;
        if (isEvery && _pageCount > 0) SetRange(1, _pageCount);
    }

    private void SetRange(int from, int to)
    {
        if (_pageCount <= 0) return;
        from = Math.Max(1, Math.Min(from, _pageCount));
        to = Math.Max(from, Math.Min(to, _pageCount));
        _fromBox.Text = from.ToString();
        _toBox.Text = to.ToString();
    }

    // =====================================================================
    //  Compress settings (the per-tool slot)
    // =====================================================================
    private void BuildCompressSettings()
    {
        SettingsHost.Children.Add(SectionTitle("Compression Mode"));

        // Segmented toggle: by quality level vs by target size.
        var seg = new Border
        {
            CornerRadius = new CornerRadius(10), Background = B("SurfaceAltBrush"),
            BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1),
            Padding = new Thickness(3), Margin = new Thickness(0, 0, 0, 16)
        };
        var segGrid = new Grid();
        segGrid.ColumnDefinitions.Add(new ColumnDefinition());
        segGrid.ColumnDefinitions.Add(new ColumnDefinition());
        _segLevel = CompressSegItem("By level", false);
        _segTarget = CompressSegItem("By target size", true);
        Grid.SetColumn(_segTarget, 1);
        segGrid.Children.Add(_segLevel); segGrid.Children.Add(_segTarget);
        seg.Child = segGrid;
        SettingsHost.Children.Add(seg);

        // Quality-level panel.
        _levelPanel = new StackPanel();
        _levelPanel.Children.Add(CompressOption("low", "Low Compression", "Best quality · larger file"));
        _levelPanel.Children.Add(CompressOption("rec", "Recommended", "Balanced quality and size"));
        _levelPanel.Children.Add(CompressOption("high", "High Compression", "Smallest file · lower quality"));
        SettingsHost.Children.Add(_levelPanel);

        // Target-size panel.
        _targetPanel = new StackPanel { Visibility = Visibility.Collapsed };
        _targetPanel.Children.Add(new TextBlock
        {
            Text = "Aim for a maximum file size. Llamashot lowers quality and, if needed, resolution to get under it.",
            FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = B("TextMutedBrush"), Margin = new Thickness(0, 0, 0, 12)
        });
        var tg = new Grid();
        tg.ColumnDefinitions.Add(new ColumnDefinition());
        tg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _targetBox = new TextBox
        {
            Text = "1", FontSize = 14, Padding = new Thickness(11), Margin = new Thickness(0, 0, 8, 0),
            Background = B("SurfaceAltBrush"), Foreground = B("TextPrimaryBrush"),
            BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _targetUnitCombo = new ComboBox { Width = 78, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
        _targetUnitCombo.Items.Add("KB"); _targetUnitCombo.Items.Add("MB");
        _targetUnitCombo.SelectedIndex = 1;
        Grid.SetColumn(_targetUnitCombo, 1);
        tg.Children.Add(_targetBox); tg.Children.Add(_targetUnitCombo);
        _targetPanel.Children.Add(tg);
        SettingsHost.Children.Add(_targetPanel);

        SettingsHost.Children.Add(MutedLabel("OUTPUT FOLDER", 22));
        var folderGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        folderGrid.ColumnDefinitions.Add(new ColumnDefinition());
        folderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string defFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", "Compressed");
        _folderBox = new TextBox
        {
            Text = defFolder, FontSize = 12, Padding = new Thickness(11), Margin = new Thickness(0, 0, 8, 0),
            Background = B("SurfaceAltBrush"), Foreground = B("TextSecondaryBrush"),
            BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var browse = SoftButton("Browse", Browse_Click);
        Grid.SetColumn(browse, 1);
        folderGrid.Children.Add(_folderBox); folderGrid.Children.Add(browse);
        SettingsHost.Children.Add(folderGrid);

        SelectQuality(_compressLevel);
        SetCompressMode(_compressByTarget);
    }

    private Border CompressSegItem(string text, bool isTarget)
    {
        var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(0, 9, 0, 9), Cursor = Cursors.Hand };
        b.Child = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 13, FontWeight = FontWeights.SemiBold };
        b.MouseLeftButtonUp += (_, _) => SetCompressMode(isTarget);
        return b;
    }

    private void SetCompressMode(bool byTarget)
    {
        _compressByTarget = byTarget;
        _levelPanel.Visibility = byTarget ? Visibility.Collapsed : Visibility.Visible;
        _targetPanel.Visibility = byTarget ? Visibility.Visible : Visibility.Collapsed;
        void Style(Border seg, bool on)
        {
            seg.Background = on ? B("AccentBrush") : Brushes.Transparent;
            ((TextBlock)seg.Child).Foreground = on ? B("AccentTextBrush") : B("TextSecondaryBrush");
        }
        Style(_segLevel, !byTarget);
        Style(_segTarget, byTarget);
    }

    // Parses the target-size box + unit into bytes; null if the input is invalid.
    private long? GetTargetBytes()
    {
        if (!double.TryParse(_targetBox.Text.Trim(), out double v) || v <= 0) return null;
        double mult = (string?)_targetUnitCombo.SelectedItem == "KB" ? 1024 : 1024 * 1024;
        return (long)(v * mult);
    }

    private Border CompressOption(string key, string title, string desc)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1), Tag = key
        };
        var inner = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(2), Margin = new Thickness(0, 1, 13, 0), VerticalAlignment = VerticalAlignment.Top };
        var txt = new StackPanel();
        txt.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = B("TextPrimaryBrush") });
        txt.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = B("TextMutedBrush"), Margin = new Thickness(0, 2, 0, 0) });
        inner.Children.Add(dot); inner.Children.Add(txt);
        card.Child = inner;
        card.MouseLeftButtonUp += (_, _) => SelectQuality(key);
        _compressCards[key] = card;
        return card;
    }

    private void SelectQuality(string key)
    {
        _compressLevel = key;
        _quality = QualityFor(key);
        foreach (var (k, card) in _compressCards)
        {
            bool on = k == key;
            card.BorderBrush = on ? B("AccentBrush") : B("BorderSoftBrush");
            card.Background = on ? Tint("AccentBrush") : B("SurfaceBrush");
            var dot = (Border)((StackPanel)card.Child).Children[0];
            dot.BorderBrush = on ? B("AccentBrush") : B("BorderSoftBrush");
            dot.Child = on ? new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = B("AccentBrush") } : null;
        }
    }

    // =====================================================================
    //  PDF to Images settings (the per-tool slot)
    // =====================================================================
    private void BuildPdfToImagesSettings()
    {
        SettingsHost.Children.Add(SectionTitle("Image Format"));
        SettingsHost.Children.Add(RadioCard(_fmtCards, "png", "PNG", "Lossless · supports transparency", () => SelectFormat("png")));
        SettingsHost.Children.Add(RadioCard(_fmtCards, "jpg", "JPG", "Smaller files · best for photos", () => SelectFormat("jpg")));

        SettingsHost.Children.Add(SectionTitle("Resolution", 22));
        SettingsHost.Children.Add(RadioCard(_dpiCards, "96", "Screen", "96 DPI · web & previews", () => SelectDpi("96")));
        SettingsHost.Children.Add(RadioCard(_dpiCards, "150", "Standard", "150 DPI · balanced (recommended)", () => SelectDpi("150")));
        SettingsHost.Children.Add(RadioCard(_dpiCards, "300", "High", "300 DPI · print quality", () => SelectDpi("300")));

        SettingsHost.Children.Add(MutedLabel("OUTPUT FOLDER", 22));
        SettingsHost.Children.Add(BuildFolderRow(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", "Images")));

        SelectFormat(_imgFormat);
        SelectDpi(_imgDpi.ToString());
    }

    private void SelectFormat(string key) { _imgFormat = key; HighlightRadio(_fmtCards, key); }
    private void SelectDpi(string key) { _imgDpi = int.Parse(key); HighlightRadio(_dpiCards, key); }

    // =====================================================================
    //  Rotate settings (the per-tool slot)
    // =====================================================================
    private void BuildRotateSettings()
    {
        SettingsHost.Children.Add(SectionTitle("Rotation"));
        SettingsHost.Children.Add(RadioCard(_rotateCards, "90", "Rotate 90° right", "Clockwise quarter turn", () => SelectRotation("90")));
        SettingsHost.Children.Add(RadioCard(_rotateCards, "180", "Rotate 180°", "Flip upside down", () => SelectRotation("180")));
        SettingsHost.Children.Add(RadioCard(_rotateCards, "270", "Rotate 90° left", "Counter-clockwise quarter turn", () => SelectRotation("270")));

        SettingsHost.Children.Add(MutedLabel("OUTPUT FOLDER", 22));
        SettingsHost.Children.Add(BuildFolderRow(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", "Rotated")));

        SelectRotation(_rotateDeg.ToString());
    }

    private void SelectRotation(string key)
    {
        _rotateDeg = int.Parse(key);
        HighlightRadio(_rotateCards, key);
        UpdateRotatePreview();
    }

    /// <summary>Rotates the on-screen preview to show how pages will look after rotating.</summary>
    private void UpdateRotatePreview()
        => PreviewImage.LayoutTransform = _toolId == "rotate_pdf"
            ? new RotateTransform(_rotateDeg)
            : Transform.Identity;

    private TextBox ThemedBox(string text) => new()
    {
        Text = text, FontSize = 14, Padding = new Thickness(11), Margin = new Thickness(0, 8, 0, 0),
        Background = B("SurfaceAltBrush"), Foreground = B("TextPrimaryBrush"),
        BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1)
    };

    private TextBlock Hint(string text) => new()
    {
        Text = text, FontSize = 11.5, Foreground = B("TextMutedBrush"),
        Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap
    };

    // =====================================================================
    //  Extract Pages settings
    // =====================================================================
    private void BuildExtractSettings()
    {
        SettingsHost.Children.Add(SectionTitle("Pages to Extract"));
        SettingsHost.Children.Add(MutedLabel("PAGE SELECTION"));
        _extractBox = ThemedBox(_pageCount > 0 ? $"1-{_pageCount}" : "");
        SettingsHost.Children.Add(_extractBox);
        SettingsHost.Children.Add(Hint("Use commas and ranges, e.g. 1, 3, 5-8. Pages keep the order you enter."));

        SettingsHost.Children.Add(MutedLabel("QUICK SELECT", 14));
        var pills = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        pills.Children.Add(QuickPill("All Pages", () => { if (_pageCount > 0) _extractBox.Text = $"1-{_pageCount}"; }));
        pills.Children.Add(QuickPill("First Half", () => { if (_pageCount > 0) _extractBox.Text = $"1-{Math.Max(1, _pageCount / 2)}"; }));
        pills.Children.Add(QuickPill("Second Half", () => { if (_pageCount > 0) _extractBox.Text = $"{_pageCount / 2 + 1}-{_pageCount}"; }));
        SettingsHost.Children.Add(pills);

        SettingsHost.Children.Add(SectionTitle("Output File", 22));
        SettingsHost.Children.Add(MutedLabel("FILE NAME"));
        _extractNameBox = ThemedBox("extracted.pdf");
        SettingsHost.Children.Add(_extractNameBox);

        SettingsHost.Children.Add(MutedLabel("OUTPUT FOLDER", 22));
        SettingsHost.Children.Add(BuildFolderRow(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", "Extracted")));
    }

    /// <summary>Parses "1, 3, 5-8" into an ordered, de-duplicated page list. Null if malformed.</summary>
    private static List<int>? ParsePageSpec(string spec)
    {
        var result = new List<int>();
        foreach (var raw in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim();
            if (token.Contains('-'))
            {
                var parts = token.Split('-');
                if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), out int a) || !int.TryParse(parts[1].Trim(), out int b)) return null;
                if (a > b) (a, b) = (b, a);
                for (int p = a; p <= b; p++) result.Add(p);
            }
            else
            {
                if (!int.TryParse(token, out int p)) return null;
                result.Add(p);
            }
        }
        var seen = new HashSet<int>();
        var ordered = new List<int>();
        foreach (var p in result) if (seen.Add(p)) ordered.Add(p);
        return ordered;
    }

    // =====================================================================
    //  Insert Pages settings
    // =====================================================================
    private void BuildInsertSettings()
    {
        SettingsHost.Children.Add(SectionTitle("Insert Position"));
        SettingsHost.Children.Add(MutedLabel("INSERT AFTER PAGE"));
        _insertAfterBox = ThemedBox(_pageCount > 0 ? _pageCount.ToString() : "0");
        _insertAfterBox.TextChanged += (_, _) => { RebuildPreviewModel(); _ = RenderPreviewUi(); };
        SettingsHost.Children.Add(_insertAfterBox);
        SettingsHost.Children.Add(Hint("0 places the inserts before page 1. Images add one page; PDFs add all their pages. Add and reorder them on the left."));

        SettingsHost.Children.Add(MutedLabel("OUTPUT FOLDER", 22));
        SettingsHost.Children.Add(BuildFolderRow(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", "Inserted")));
    }

    private async void InsertChooseImages_Click(object sender, RoutedEventArgs e)
    {
        if (_picking) return;
        _picking = true;
        string[]? chosen = null;
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Images & PDF|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff;*.pdf"
            };
            if (dlg.ShowDialog() == true) chosen = dlg.FileNames;
        }
        finally { _picking = false; }

        if (chosen != null) await AddInsertItems(chosen);
    }

    /// <summary>Adds images and/or PDFs to the insert list, unlocking protected PDFs and counting pages.</summary>
    private async System.Threading.Tasks.Task AddInsertItems(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (_insertImages.Contains(path)) continue;
            bool isPdf = path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
            bool isImg = ImageExts.Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase));
            if (!isPdf && !isImg) continue;

            if (isPdf)
            {
                string? pw = await PasswordDialog.UnlockAsync(this, path);
                if (pw == null) continue; // cancelled
                string? realPw = string.IsNullOrEmpty(pw) ? null : pw;
                int pages;
                try { pages = await FileToolsService.GetPdfPageCountAsync(path, realPw); }
                catch (Exception ex)
                {
                    ConfirmDialog.Alert(this, "Can't Open PDF", $"{Path.GetFileName(path)}:\n{ex.Message}", ConfirmDialog.AlertKind.Error);
                    continue;
                }
                _insertImages.Add(path); _insertPw[path] = realPw; _insertItemPages[path] = pages;
            }
            else
            {
                _insertImages.Add(path); _insertPw[path] = null; _insertItemPages[path] = 1;
            }
        }
        RefreshAll();
    }

    private void UpdateInsertCount()
    {
        if (_insertCountText == null) return;
        _insertCountText.Text = _insertImages.Count == 0
            ? "No images chosen yet."
            : $"{_insertImages.Count} image{(_insertImages.Count == 1 ? "" : "s")} ready to insert";
    }

    // =====================================================================
    //  Page Numbers settings
    // =====================================================================
    private void BuildPageNumbersSettings()
    {
        SettingsHost.Children.Add(SectionTitle("Number Position"));
        SettingsHost.Children.Add(PositionGrid(_pnCards, SelectPnPos, includeCenter: false));

        SettingsHost.Children.Add(SectionTitle("Font", 22));
        SettingsHost.Children.Add(FontCombo(_pnFont, f => { _pnFont = f; UpdatePreviewOverlay(); }));

        SettingsHost.Children.Add(SectionTitle("Font Size", 22));
        var pnSizes = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        pnSizes.ColumnDefinitions.Add(new ColumnDefinition());
        pnSizes.ColumnDefinitions.Add(new ColumnDefinition());
        pnSizes.ColumnDefinitions.Add(new ColumnDefinition());
        AddChip(pnSizes, 0, _pnSizeCards, "sm", "Small", () => SelectPnSize("sm"));
        AddChip(pnSizes, 1, _pnSizeCards, "md", "Medium", () => SelectPnSize("md"));
        AddChip(pnSizes, 2, _pnSizeCards, "lg", "Large", () => SelectPnSize("lg"));
        SettingsHost.Children.Add(pnSizes);

        SettingsHost.Children.Add(SectionTitle("Color", 22));
        SettingsHost.Children.Add(ColorSwatches(_pnColorCards, _pnColorHex, hex => { _pnColorHex = hex; HighlightSwatches(_pnColorCards, hex); UpdatePreviewOverlay(); }));

        SettingsHost.Children.Add(MutedLabel("OUTPUT FOLDER", 22));
        SettingsHost.Children.Add(BuildFolderRow(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", "Numbered")));

        SelectPnPos(_pnPos);
        SelectPnSize(_pnFontSize <= 9 ? "sm" : _pnFontSize >= 16 ? "lg" : "md");
        HighlightSwatches(_pnColorCards, _pnColorHex);
    }

    private void SelectPnPos(string key) { _pnPos = key; HighlightPos(_pnCards, key); UpdatePreviewOverlay(); }
    private void SelectPnSize(string key)
    {
        _pnFontSize = key switch { "sm" => 9, "lg" => 18, _ => 12 };
        HighlightChips(_pnSizeCards, key);
        UpdatePreviewOverlay();
    }

    // =====================================================================
    //  Watermark settings
    // =====================================================================
    private static double WmOpacityFor(string k) => k switch { "light" => 0.15, "strong" => 0.5, _ => 0.3 };
    private static int WmSizeFor(string k) => k switch { "small" => 32, "large" => 72, _ => 48 };

    private void BuildWatermarkSettings()
    {
        SettingsHost.Children.Add(SectionTitle("Watermark Text"));
        _wmTextBox = ThemedBox("CONFIDENTIAL");
        _wmTextBox.Margin = new Thickness(0, 0, 0, 0);
        _wmTextBox.TextChanged += (_, _) => UpdatePreviewOverlay();
        SettingsHost.Children.Add(_wmTextBox);

        SettingsHost.Children.Add(SectionTitle("Orientation", 22));
        var orient = new Grid();
        orient.ColumnDefinitions.Add(new ColumnDefinition());
        orient.ColumnDefinitions.Add(new ColumnDefinition());
        orient.ColumnDefinitions.Add(new ColumnDefinition());
        orient.ColumnDefinitions.Add(new ColumnDefinition());
        AddChip(orient, 0, _wmOrientCards, "horizontal", "Horizontal", () => SelectWmOrient("horizontal"));
        AddChip(orient, 1, _wmOrientCards, "vertical", "Vertical", () => SelectWmOrient("vertical"));
        AddChip(orient, 2, _wmOrientCards, "diagonal-up", "Tilt /", () => SelectWmOrient("diagonal-up"));
        AddChip(orient, 3, _wmOrientCards, "diagonal-down", "Tilt \\", () => SelectWmOrient("diagonal-down"));
        SettingsHost.Children.Add(orient);

        SettingsHost.Children.Add(SectionTitle("Position", 22));
        SettingsHost.Children.Add(PositionGrid(_wmPosCards, SelectWmPos, includeCenter: true));

        SettingsHost.Children.Add(SectionTitle("Font", 22));
        SettingsHost.Children.Add(FontCombo(_wmFont, f => { _wmFont = f; UpdatePreviewOverlay(); }));

        SettingsHost.Children.Add(SectionTitle("Font Size", 22));
        var wmSizes = new Grid();
        wmSizes.ColumnDefinitions.Add(new ColumnDefinition());
        wmSizes.ColumnDefinitions.Add(new ColumnDefinition());
        wmSizes.ColumnDefinitions.Add(new ColumnDefinition());
        AddChip(wmSizes, 0, _wmSizeCards, "small", "32 pt", () => SelectWmSize("small"));
        AddChip(wmSizes, 1, _wmSizeCards, "medium", "48 pt", () => SelectWmSize("medium"));
        AddChip(wmSizes, 2, _wmSizeCards, "large", "72 pt", () => SelectWmSize("large"));
        SettingsHost.Children.Add(wmSizes);

        SettingsHost.Children.Add(SectionTitle("Opacity", 22));
        var wmOpac = new Grid();
        wmOpac.ColumnDefinitions.Add(new ColumnDefinition());
        wmOpac.ColumnDefinitions.Add(new ColumnDefinition());
        wmOpac.ColumnDefinitions.Add(new ColumnDefinition());
        AddChip(wmOpac, 0, _wmOpacityCards, "light", "Light", () => SelectWmOpacity("light"));
        AddChip(wmOpac, 1, _wmOpacityCards, "medium", "Medium", () => SelectWmOpacity("medium"));
        AddChip(wmOpac, 2, _wmOpacityCards, "strong", "Strong", () => SelectWmOpacity("strong"));
        SettingsHost.Children.Add(wmOpac);

        SettingsHost.Children.Add(SectionTitle("Color", 22));
        SettingsHost.Children.Add(ColorSwatches(_wmColorCards, _wmColorHex, hex => { _wmColorHex = hex; HighlightSwatches(_wmColorCards, hex); UpdatePreviewOverlay(); }));

        SettingsHost.Children.Add(MutedLabel("OUTPUT FOLDER", 22));
        SettingsHost.Children.Add(BuildFolderRow(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", "Watermarked")));

        SelectWmOrient(_wmOrient);
        SelectWmPos(_wmPos);
        SelectWmOpacity(_wmOpacity <= 0.15 ? "light" : _wmOpacity >= 0.5 ? "strong" : "medium");
        SelectWmSize(_wmFontSize <= 32 ? "small" : _wmFontSize >= 72 ? "large" : "medium");
        HighlightSwatches(_wmColorCards, _wmColorHex);
    }

    // watermark position uses its own card dictionary
    private readonly Dictionary<string, Border> _wmPosCards = new();

    private void SelectWmOpacity(string key) { _wmOpacity = WmOpacityFor(key); HighlightChips(_wmOpacityCards, key); UpdatePreviewOverlay(); }
    private void SelectWmSize(string key) { _wmFontSize = WmSizeFor(key); HighlightChips(_wmSizeCards, key); UpdatePreviewOverlay(); }
    private void SelectWmOrient(string key) { _wmOrient = key; HighlightChips(_wmOrientCards, key); UpdatePreviewOverlay(); }
    private void SelectWmPos(string key) { _wmPos = key; HighlightPos(_wmPosCards, key); UpdatePreviewOverlay(); }

    // =====================================================================
    //  Protect settings
    // =====================================================================
    private void BuildProtectSettings()
    {
        _protectModeCards.Clear();
        SettingsHost.Children.Add(SectionTitle("What do you want to do?"));
        SettingsHost.Children.Add(RadioCard(_protectModeCards, "add", "Add password", "Lock the PDF so a password is required to open it", () => SelectProtectMode("add")));
        SettingsHost.Children.Add(RadioCard(_protectModeCards, "remove", "Remove password", "Save an unprotected copy (you must know the password)", () => SelectProtectMode("remove")));
        SettingsHost.Children.Add(RadioCard(_protectModeCards, "break", "Break password", "Recover an unknown open-password by trying combinations", () => SelectProtectMode("break")));
        HighlightRadio(_protectModeCards, _protectMode);

        _protectModeHost = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        SettingsHost.Children.Add(_protectModeHost);
        BuildProtectModeBody();
    }

    private void SelectProtectMode(string mode)
    {
        if (_protectMode == mode) return;
        _protectMode = mode;
        HighlightRadio(_protectModeCards, mode);
        BuildProtectModeBody();
        ApplyToolMeta(); // refresh the action-button label
    }

    private void BuildProtectModeBody()
    {
        _protectModeHost.Children.Clear();
        _breakCharsetCards.Clear();
        _breakLenCards.Clear();

        if (_protectMode == "add")
        {
            _protectModeHost.Children.Add(MutedLabel("PASSWORD"));
            _protectPwBox = ThemedPasswordBox();
            _protectModeHost.Children.Add(_protectPwBox);
            _protectModeHost.Children.Add(MutedLabel("CONFIRM PASSWORD", 16));
            _protectConfirmBox = ThemedPasswordBox();
            _protectModeHost.Children.Add(_protectConfirmBox);
            _protectModeHost.Children.Add(Hint("This password will be required to open the PDF. It can't be recovered if lost."));
        }
        else if (_protectMode == "remove")
        {
            _protectModeHost.Children.Add(Hint(_pdfPath == null
                ? "Open the protected PDF — you'll be asked for its password. Then a decrypted copy is saved."
                : "A decrypted copy will be saved with no password required to open it."));
        }
        else // break
        {
            _protectModeHost.Children.Add(MutedLabel("CHARACTERS TO TRY"));
            var csGrid = new Grid();
            for (int i = 0; i < 3; i++) csGrid.ColumnDefinitions.Add(new ColumnDefinition());
            int c = 0;
            foreach (var (key, label, _) in FileToolsService.RecoveryCharsets)
                AddChip(csGrid, c++, _breakCharsetCards, key, label, () => { _breakCharsetKey = key; HighlightChips(_breakCharsetCards, key); });
            _protectModeHost.Children.Add(csGrid);
            HighlightChips(_breakCharsetCards, _breakCharsetKey);

            _protectModeHost.Children.Add(MutedLabel("MAX LENGTH", 16));
            var lenGrid = new Grid();
            for (int i = 0; i < 4; i++) lenGrid.ColumnDefinitions.Add(new ColumnDefinition());
            int col = 0;
            foreach (var n in new[] { 3, 4, 5, 6 })
            {
                int len = n;
                AddChip(lenGrid, col++, _breakLenCards, n.ToString(), n.ToString(), () => { _breakMaxLen = len; HighlightChips(_breakLenCards, len.ToString()); });
            }
            _protectModeHost.Children.Add(lenGrid);
            HighlightChips(_breakLenCards, _breakMaxLen.ToString());

            _protectModeHost.Children.Add(Hint("Tries common passwords first, then every combination up to the chosen length. Longer passwords can take a very long time — press Stop anytime."));
        }

        _protectModeHost.Children.Add(MutedLabel("OUTPUT FOLDER", 22));
        _protectModeHost.Children.Add(BuildFolderRow(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", "Protected")));
    }

    private System.Windows.Controls.PasswordBox ThemedPasswordBox() => new()
    {
        FontSize = 14, Padding = new Thickness(11), Margin = new Thickness(0, 8, 0, 0),
        Background = B("SurfaceAltBrush"), Foreground = B("TextPrimaryBrush"),
        BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1)
    };

    /// <summary>Generic radio-style settings card (dot + title + description).</summary>
    private Border RadioCard(Dictionary<string, Border> dict, string key, string title, string desc, Action onClick)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1), Tag = key
        };
        var inner = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(2), Margin = new Thickness(0, 1, 13, 0), VerticalAlignment = VerticalAlignment.Top };
        var txt = new StackPanel();
        txt.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = B("TextPrimaryBrush") });
        txt.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = B("TextMutedBrush"), Margin = new Thickness(0, 2, 0, 0) });
        inner.Children.Add(dot); inner.Children.Add(txt);
        card.Child = inner;
        card.MouseLeftButtonUp += (_, _) => onClick();
        dict[key] = card;
        return card;
    }

    private void HighlightRadio(Dictionary<string, Border> dict, string key)
    {
        foreach (var (k, card) in dict)
        {
            bool on = k == key;
            card.BorderBrush = on ? B("AccentBrush") : B("BorderSoftBrush");
            card.Background = on ? Tint("AccentBrush") : B("SurfaceBrush");
            var dot = (Border)((StackPanel)card.Child).Children[0];
            dot.BorderBrush = on ? B("AccentBrush") : B("BorderSoftBrush");
            dot.Child = on ? new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = B("AccentBrush") } : null;
        }
    }

    // ---- compact shared controls for Page Numbers / Watermark ----

    /// <summary>A small selectable chip placed in a single grid column.</summary>
    private void AddChip(Grid grid, int col, Dictionary<string, Border> dict, string key, string label, Action onClick)
    {
        var chip = new Border
        {
            Margin = new Thickness(col == 0 ? 0 : 6, 0, 0, 0), CornerRadius = new CornerRadius(9), Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1), BorderBrush = B("BorderSoftBrush"), Background = B("SurfaceBrush"),
            Padding = new Thickness(4, 9, 4, 9)
        };
        chip.Child = new TextBlock { Text = label, FontSize = 12.5, Foreground = B("TextSecondaryBrush"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
        chip.MouseLeftButtonUp += (_, _) => onClick();
        Grid.SetColumn(chip, col);
        grid.Children.Add(chip);
        dict[key] = chip;
    }

    private void HighlightChips(Dictionary<string, Border> dict, string key)
    {
        foreach (var (k, chip) in dict)
        {
            bool on = k == key;
            chip.Background = on ? Tint("AccentBrush") : B("SurfaceBrush");
            chip.BorderBrush = on ? B("AccentBrush") : B("BorderSoftBrush");
            if (chip.Child is TextBlock t) { t.Foreground = on ? B("AccentBrush") : B("TextSecondaryBrush"); t.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal; }
        }
    }

    /// <summary>A 3×3 position picker (corners + edges, optionally center).</summary>
    private UIElement PositionGrid(Dictionary<string, Border> dict, Action<string> onSelect, bool includeCenter)
    {
        dict.Clear();
        var outer = new Border
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = B("BorderSoftBrush"),
            Background = B("SurfaceAltBrush"), Padding = new Thickness(8)
        };
        var g = new Grid();
        for (int c = 0; c < 3; c++) g.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < 3; r++) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

        void Cell(int r, int c, string key)
        {
            var cell = new Border
            {
                Margin = new Thickness(3), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1), BorderBrush = B("BorderSoftBrush"), Background = B("SurfaceBrush")
            };
            cell.Child = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = B("TextMutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            cell.MouseLeftButtonUp += (_, _) => onSelect(key);
            Grid.SetRow(cell, r); Grid.SetColumn(cell, c);
            g.Children.Add(cell);
            dict[key] = cell;
        }
        Cell(0, 0, "top-left"); Cell(0, 1, "top-center"); Cell(0, 2, "top-right");
        if (includeCenter) Cell(1, 1, "center");
        Cell(2, 0, "bottom-left"); Cell(2, 1, "bottom-center"); Cell(2, 2, "bottom-right");
        outer.Child = g;
        return outer;
    }

    private void HighlightPos(Dictionary<string, Border> dict, string key)
    {
        foreach (var (k, cell) in dict)
        {
            bool on = k == key;
            cell.Background = on ? B("AccentBrush") : B("SurfaceBrush");
            cell.BorderBrush = on ? B("AccentBrush") : B("BorderSoftBrush");
            if (cell.Child is Border dot) dot.Background = on ? B("AccentTextBrush") : B("TextMutedBrush");
        }
    }

    private ComboBox FontCombo(string current, Action<string> onPick)
    {
        var cb = new ComboBox
        {
            FontSize = 13, Style = (Style)Resources["ThemedCombo"]
        };
        foreach (var f in BasicFonts) cb.Items.Add(new ComboBoxItem { Content = f, FontFamily = new FontFamily(f) });
        cb.SelectedIndex = Math.Max(0, Array.IndexOf(BasicFonts, current));
        cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is ComboBoxItem ci) onPick(ci.Content!.ToString()!); };
        return cb;
    }

    private UIElement ColorSwatches(Dictionary<string, Border> dict, string current, Action<string> onPick)
    {
        dict.Clear();
        var wrap = new WrapPanel();
        foreach (var (name, hex) in BasicColors)
        {
            var sw = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 8),
                Background = Hex(hex), BorderThickness = new Thickness(2), BorderBrush = B("BorderSoftBrush"),
                Cursor = Cursors.Hand, ToolTip = name
            };
            string h = hex;
            sw.MouseLeftButtonUp += (_, _) => onPick(h);
            dict[hex] = sw;
            wrap.Children.Add(sw);
        }
        return wrap;
    }

    private void HighlightSwatches(Dictionary<string, Border> dict, string hex)
    {
        foreach (var (k, sw) in dict)
            sw.BorderBrush = string.Equals(k, hex, StringComparison.OrdinalIgnoreCase) ? B("AccentBrush") : B("BorderSoftBrush");
    }

    // ---- live page-number / watermark overlay on the preview page ----
    private TextBlock OverlayText(string text, string font, double px, string hex, double opacity) => new()
    {
        Text = text, FontFamily = new FontFamily(font), FontSize = Math.Max(6, px),
        Foreground = Hex(hex), Opacity = opacity, TextWrapping = TextWrapping.NoWrap
    };

    private static (double x, double y) PlacePoint(string pos, double w, double h, double tw, double th, double margin) => pos switch
    {
        "top-left" => (margin, margin),
        "top-center" => ((w - tw) / 2, margin),
        "top-right" => (w - tw - margin, margin),
        "center" => ((w - tw) / 2, (h - th) / 2),
        "bottom-left" => (margin, h - th - margin),
        "bottom-right" => (w - tw - margin, h - th - margin),
        _ => ((w - tw) / 2, h - th - margin),
    };

    private void PlaceOverlay(TextBlock tb, string pos, double w, double h, double margin, double angle)
    {
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tw = tb.DesiredSize.Width, th = tb.DesiredSize.Height;
        if (angle != 0) { tb.RenderTransformOrigin = new Point(0.5, 0.5); tb.RenderTransform = new RotateTransform(angle); }
        var (x, y) = PlacePoint(pos, w, h, tw, th, margin);
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        OverlayCanvas.Children.Add(tb);
    }

    private void UpdatePreviewOverlay()
    {
        if (OverlayCanvas == null) return;
        OverlayCanvas.Children.Clear();
        if (_dispW <= 0 || _dispH <= 0 || _previewPages.Count == 0) return;
        double w = _dispW, h = _dispH;

        if (_toolId == "page_numbers")
        {
            var tb = OverlayText($"Page {_curPage} of {_previewPages.Count}", _pnFont, _pnFontSize * w / 612.0, _pnColorHex, 1.0);
            PlaceOverlay(tb, _pnPos, w, h, w * 0.04, 0);
        }
        else if (_toolId == "watermark")
        {
            string text = _wmTextBox?.Text?.Trim() ?? "";
            if (text.Length == 0) return;
            double angle = _wmOrient switch { "horizontal" => 0, "vertical" => -90, "diagonal-down" => 45, _ => -45 };
            var tb = OverlayText(text, _wmFont, _wmFontSize * w / 612.0, _wmColorHex, _wmOpacity);
            PlaceOverlay(tb, _wmPos, w, h, w * 0.05, angle);
        }
    }

    // =====================================================================
    //  Merge settings + file list (multi-file slot)
    // =====================================================================
    private void BuildMergeSettings()
    {
        SettingsHost.Children.Add(SectionTitle("Output File"));
        SettingsHost.Children.Add(MutedLabel("FILE NAME"));
        _mergeNameBox = new TextBox
        {
            Text = "merged.pdf", FontSize = 14, Padding = new Thickness(11), Margin = new Thickness(0, 8, 0, 0),
            Background = B("SurfaceAltBrush"), Foreground = B("TextPrimaryBrush"),
            BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1)
        };
        SettingsHost.Children.Add(_mergeNameBox);

        SettingsHost.Children.Add(MutedLabel("OUTPUT FOLDER", 22));
        SettingsHost.Children.Add(BuildFolderRow(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", "Merged")));
    }

    private void BuildImagesToPdfSettings()
    {
        SettingsHost.Children.Add(SectionTitle("Output File"));
        SettingsHost.Children.Add(MutedLabel("FILE NAME"));
        _mergeNameBox = new TextBox
        {
            Text = "images.pdf", FontSize = 14, Padding = new Thickness(11), Margin = new Thickness(0, 8, 0, 0),
            Background = B("SurfaceAltBrush"), Foreground = B("TextPrimaryBrush"),
            BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1)
        };
        SettingsHost.Children.Add(_mergeNameBox);

        SettingsHost.Children.Add(MutedLabel("OUTPUT FOLDER", 22));
        SettingsHost.Children.Add(BuildFolderRow(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", "PDF")));
    }

    private async System.Threading.Tasks.Task AddMergeFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!AcceptsFile(path)) continue;
            if (_mergeFiles.Contains(path)) continue;

            if (_toolId == "images_to_pdf")
            {
                _mergeFiles.Add(path);
                _mergePages[path] = 1; // one image = one page
                _mergePw[path] = null;
                continue;
            }

            // PDF (merge): unlock if protected, then count pages
            string? pw = await PasswordDialog.UnlockAsync(this, path);
            if (pw == null) continue; // user cancelled this file's unlock
            string? realPw = string.IsNullOrEmpty(pw) ? null : pw;

            int pages;
            try { pages = await FileToolsService.GetPdfPageCountAsync(path, realPw); }
            catch (Exception ex)
            {
                ConfirmDialog.Alert(this, "Can't Open PDF", $"{Path.GetFileName(path)}:\n{ex.Message}", ConfirmDialog.AlertKind.Error);
                continue;
            }

            _mergeFiles.Add(path);
            _mergePages[path] = pages;
            _mergePw[path] = realPw;
        }

        RefreshAll();
    }

    // =====================================================================
    //  Left panel (files management)
    // =====================================================================
    private void BuildLeftPanel()
    {
        if (LeftHost == null) return;
        LeftHost.Children.Clear();
        if (IsMultiFile)
        {
            for (int i = 0; i < _mergeFiles.Count; i++) LeftHost.Children.Add(MergeRow(i));
            LeftHost.Children.Add(DropCard());
            LeftHost.Children.Add(TipsCard());
        }
        else if (_toolId == "insert_pages")
        {
            LeftHost.Children.Add(_pdfPath != null ? SingleFileCard() : DropCard());
            LeftHost.Children.Add(InsertImagesSection());
            LeftHost.Children.Add(TipsCard());
        }
        else
        {
            LeftHost.Children.Add(_pdfPath != null ? SingleFileCard() : DropCard());
            LeftHost.Children.Add(TipsCard());
        }
    }

    /// <summary>Left-panel "Images to Insert" list with add / reorder / remove + drag-drop.</summary>
    private UIElement InsertImagesSection()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 12) };
        var header = new Grid { Margin = new Thickness(2, 0, 2, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = new TextBlock { Text = "Files to Insert", FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = B("TextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(t, 0);
        var add = SoftButton("+ Add More", InsertChooseImages_Click);
        add.Padding = new Thickness(12, 7, 12, 7);
        Grid.SetColumn(add, 1);
        header.Children.Add(t); header.Children.Add(add);
        sp.Children.Add(header);

        if (_insertImages.Count == 0)
        {
            sp.Children.Add(new TextBlock
            {
                Text = "No files yet. Click “+ Add More” or drop images / PDFs here.",
                Foreground = B("TextMutedBrush"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 0)
            });
        }
        else
        {
            for (int i = 0; i < _insertImages.Count; i++) sp.Children.Add(InsertImageRow(i));
        }
        return sp;
    }

    private Border InsertImageRow(int idx)
    {
        string path = _insertImages[idx];
        var fi = new FileInfo(path);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        bool isPdf = path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        var thumb = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true, Background = Hex(isPdf ? "#F06262" : "#5BB98B")
        };
        var im = isPdf ? null : SafeImage(path);
        thumb.Child = im != null
            ? new Image { Source = im, Stretch = Stretch.UniformToFill }
            : new TextBlock { Text = isPdf ? "PDF" : "🖼", Foreground = Brushes.White, FontSize = isPdf ? 9.5 : 14, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(thumb, 0);

        int itemPages = _insertItemPages.TryGetValue(path, out var ipc) ? ipc : 1;
        string meta = isPdf
            ? $"#{idx + 1} · PDF · {itemPages} page{(itemPages == 1 ? "" : "s")} · {FileToolsService.FormatFileSize(fi.Length)}"
            : $"#{idx + 1} · {fi.Extension.TrimStart('.').ToUpperInvariant()} · {FileToolsService.FormatFileSize(fi.Length)}";
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = fi.Name, Foreground = B("TextPrimaryBrush"), FontWeight = FontWeights.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
        info.Children.Add(new TextBlock { Text = meta, Foreground = B("TextMutedBrush"), FontSize = 12 });
        Grid.SetColumn(info, 1);

        var ctrls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        ctrls.Children.Add(MiniBtn("↑", () => MoveInsert(idx, -1), idx > 0));
        ctrls.Children.Add(MiniBtn("↓", () => MoveInsert(idx, +1), idx < _insertImages.Count - 1));
        ctrls.Children.Add(MiniBtn("✕", () => RemoveInsert(idx), true));
        Grid.SetColumn(ctrls, 2);

        grid.Children.Add(thumb); grid.Children.Add(info); grid.Children.Add(ctrls);
        var row = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 9),
            Background = B("SurfaceAltBrush"), BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1), Child = grid
        };
        EnableRowReorder(row, idx, (f, to) => { MoveInList(_insertImages, f, to); RefreshAll(); });
        return row;
    }

    private void MoveInsert(int idx, int dir)
    {
        int j = idx + dir;
        if (j < 0 || j >= _insertImages.Count) return;
        (_insertImages[idx], _insertImages[j]) = (_insertImages[j], _insertImages[idx]);
        RefreshAll();
    }

    private void RemoveInsert(int idx)
    {
        if (idx < 0 || idx >= _insertImages.Count) return;
        string path = _insertImages[idx];
        _insertImages.RemoveAt(idx);
        _insertPw.Remove(path); _insertItemPages.Remove(path);
        RefreshAll();
    }

    private Border DropCard()
    {
        bool img = _toolId == "images_to_pdf";
        var card = new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 22, 16, 22), Margin = new Thickness(0, 2, 0, 12),
            BorderThickness = new Thickness(1), BorderBrush = B("BorderSoftBrush"), Background = B("SurfaceAltBrush"), Cursor = Cursors.Hand
        };
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = img ? "🖼" : "📄", FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.8 });
        sp.Children.Add(new TextBlock
        {
            Text = IsMultiFile ? (img ? "Drag & drop more images here" : "Drag & drop more PDFs here") : "Drag & drop a PDF here",
            Foreground = B("TextSecondaryBrush"), FontSize = 13, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 2), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap
        });
        sp.Children.Add(new TextBlock { Text = "or click to browse", Foreground = B("TextMutedBrush"), FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center });
        card.Child = sp;
        card.MouseLeftButtonUp += (_, _) => AddFiles_Click(card, new RoutedEventArgs());
        return card;
    }

    private Border SingleFileCard()
    {
        var fi = new FileInfo(_pdfPath!);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Background = Hex("#F06262"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        badge.Child = new TextBlock { Text = "PDF", Foreground = Brushes.White, FontSize = 10.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(badge, 0);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = fi.Name, Foreground = B("TextPrimaryBrush"), FontWeight = FontWeights.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
        info.Children.Add(new TextBlock { Text = $"{_pageCount} page{(_pageCount == 1 ? "" : "s")} · {FileToolsService.FormatFileSize(fi.Length)}", Foreground = B("TextMutedBrush"), FontSize = 12 });
        Grid.SetColumn(info, 1);
        var remove = MiniBtn("✕", () => Clear_Click(this, new RoutedEventArgs()), true);
        Grid.SetColumn(remove, 2);
        grid.Children.Add(badge); grid.Children.Add(info); grid.Children.Add(remove);
        return new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12),
            Background = B("SurfaceAltBrush"), BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1), Child = grid
        };
    }

    private Border TipsCard()
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "💡 Tips", FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = B("AccentBrush"), Margin = new Thickness(0, 0, 0, 6) });
        sp.Children.Add(new TextBlock
        {
            Text = IsMultiFile
                ? "Use ↑ ↓ or drag a file to change the order. They are combined in this sequence."
                : _toolId == "insert_pages"
                    ? "Add images or PDFs to insert. Use ↑ ↓ or drag to reorder; the preview shows where they land after your chosen page."
                    : "Use the preview to check the pages before processing.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = B("TextSecondaryBrush")
        });
        return new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Background = B("SurfaceAltBrush"), BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1), Child = sp };
    }

    // =====================================================================
    //  Unified preview (center): model + render + filmstrip
    // =====================================================================
    private void RebuildPreviewModel()
    {
        _previewPages.Clear();
        if (IsMultiFile)
        {
            bool images = _toolId == "images_to_pdf";
            foreach (var path in _mergeFiles)
            {
                if (images) { _previewPages.Add(new PreviewPage(path, null, 1, true)); continue; }
                string? pw = _mergePw.TryGetValue(path, out var p) ? p : null;
                int n = _mergePages.TryGetValue(path, out var pc) ? pc : 0;
                for (int pg = 1; pg <= n; pg++) _previewPages.Add(new PreviewPage(path, pw, pg, false));
            }
        }
        else if (_toolId == "insert_pages" && _pdfPath != null)
        {
            int after = 0;
            int.TryParse(_insertAfterBox?.Text?.Trim(), out after);
            after = Math.Max(0, Math.Min(after, _pageCount));
            for (int pg = 1; pg <= after; pg++) _previewPages.Add(new PreviewPage(_pdfPath, _password, pg, false));
            foreach (var item in _insertImages)
            {
                bool isImg = ImageExts.Any(x => item.EndsWith(x, StringComparison.OrdinalIgnoreCase));
                if (isImg) { _previewPages.Add(new PreviewPage(item, null, 1, true)); continue; }
                string? ipw = _insertPw.TryGetValue(item, out var p) ? p : null;
                int n = _insertItemPages.TryGetValue(item, out var c) ? c : 0;
                for (int pg = 1; pg <= n; pg++) _previewPages.Add(new PreviewPage(item, ipw, pg, false));
            }
            for (int pg = after + 1; pg <= _pageCount; pg++) _previewPages.Add(new PreviewPage(_pdfPath, _password, pg, false));
        }
        else if (_pdfPath != null)
        {
            for (int pg = 1; pg <= _pageCount; pg++) _previewPages.Add(new PreviewPage(_pdfPath, _password, pg, false));
        }
        if (_curPage < 1) _curPage = 1;
        if (_curPage > _previewPages.Count) _curPage = Math.Max(1, _previewPages.Count);
    }

    /// <summary>Full UI refresh: model, left panel, info, action bar, preview, filmstrip.</summary>
    private void RefreshAll()
    {
        RebuildPreviewModel();
        BuildLeftPanel();
        BuildInfo();
        UpdateSelection();
        ShowPreviewState();
        _ = RenderPreviewUi();
    }

    private async System.Threading.Tasks.Task RenderPreviewUi()
    {
        if (_previewPages.Count > 0) { await RenderCurrentPage(); await BuildFilmstrip(); }
        else { ThumbList.Items.Clear(); PreviewImage.Source = null; }
    }

    private async System.Threading.Tasks.Task BuildFilmstrip()
    {
        if (ThumbList == null) return;
        int run = ++_previewRunId;
        ThumbList.Items.Clear();
        for (int i = 0; i < _previewPages.Count; i++)
        {
            int idx = i;
            var pp = _previewPages[i];
            var card = new Border
            {
                Width = 58, Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(2), Cursor = Cursors.Hand, Tag = idx, ClipToBounds = true,
                BorderBrush = (idx + 1) == _curPage ? B("AccentBrush") : B("BorderSoftBrush"), Background = B("SurfaceBrush")
            };
            var stack = new StackPanel();
            var img = new Image { Height = 72, Stretch = Stretch.UniformToFill };
            stack.Children.Add(img);
            stack.Children.Add(new TextBlock { Text = (idx + 1).ToString(), FontSize = 9.5, Foreground = B("TextMutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) });
            card.Child = stack;
            card.MouseLeftButtonUp += async (_, _) => { _curPage = idx + 1; await RenderCurrentPage(); };
            ThumbList.Items.Add(card);

            var bmp = pp.IsImage ? SafeImage(pp.Path) : await RenderThumbAsync(pp.Path, pp.Password, pp.Page, 150);
            if (run != _previewRunId) return;
            img.Source = bmp;
        }
    }

    private void HighlightFilmstrip()
    {
        foreach (var item in ThumbList.Items)
            if (item is Border b && b.Tag is int i)
                b.BorderBrush = (i + 1) == _curPage ? B("AccentBrush") : B("BorderSoftBrush");
    }

    private static BitmapImage? SafeImage(string path)
    {
        try { var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new System.Uri(path); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 200; bi.EndInit(); bi.Freeze(); return bi; }
        catch { return null; }
    }

    private async System.Threading.Tasks.Task<BitmapImage?> RenderThumbAsync(string path, string? pw, int page1, uint width)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var doc = string.IsNullOrEmpty(pw)
                ? await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file)
                : await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file, pw);
            if (page1 < 1 || page1 > (int)doc.PageCount) return null;
            using var pg = doc.GetPage((uint)(page1 - 1));
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await pg.RenderToStreamAsync(stream, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = width });
            stream.Seek(0);
            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.StreamSource = stream.AsStreamForRead(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit(); bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private Border MergeRow(int idx)
    {
        string path = _mergeFiles[idx];
        var fi = new FileInfo(path);
        int pages = _mergePages.TryGetValue(path, out var pg) ? pg : 0;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        bool isImg = _toolId == "images_to_pdf";
        var badge = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(9), Background = Hex(isImg ? "#5BB98B" : "#F06262"),
            Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = new TextBlock
        {
            Text = (idx + 1).ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(badge, 0);

        string meta = isImg
            ? $"{fi.Extension.TrimStart('.').ToUpperInvariant()} · {FileToolsService.FormatFileSize(fi.Length)}"
            : $"{pages} page{(pages == 1 ? "" : "s")} · {FileToolsService.FormatFileSize(fi.Length)}";
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = fi.Name, Foreground = B("TextPrimaryBrush"), FontWeight = FontWeights.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
        info.Children.Add(new TextBlock { Text = meta, Foreground = B("TextMutedBrush"), FontSize = 12 });
        Grid.SetColumn(info, 1);

        var ctrls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        ctrls.Children.Add(MiniBtn("↑", () => MoveMerge(idx, -1), idx > 0));
        ctrls.Children.Add(MiniBtn("↓", () => MoveMerge(idx, +1), idx < _mergeFiles.Count - 1));
        ctrls.Children.Add(MiniBtn("✕", () => RemoveMerge(idx), true));
        Grid.SetColumn(ctrls, 2);

        grid.Children.Add(badge); grid.Children.Add(info); grid.Children.Add(ctrls);
        var row = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 9),
            Background = B("SurfaceAltBrush"), BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1),
            Child = grid
        };
        EnableRowReorder(row, idx, (f, to) => { MoveInList(_mergeFiles, f, to); RefreshAll(); });
        return row;
    }

    private Button MiniBtn(string glyph, Action onClick, bool enabled)
    {
        var btn = new Button
        {
            Content = glyph, Width = 30, Height = 30, Margin = new Thickness(5, 0, 0, 0), Cursor = Cursors.Hand, FontSize = 13,
            Background = B("SurfaceBrush"), Foreground = B("TextSecondaryBrush"), IsEnabled = enabled, Opacity = enabled ? 1 : 0.35,
            Template = (ControlTemplate)Resources["SoftBtn"]
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private void MoveMerge(int idx, int dir)
    {
        int j = idx + dir;
        if (j < 0 || j >= _mergeFiles.Count) return;
        (_mergeFiles[idx], _mergeFiles[j]) = (_mergeFiles[j], _mergeFiles[idx]);
        RefreshAll();
    }

    private static void MoveInList<T>(List<T> list, int from, int to)
    {
        if (from < 0 || from >= list.Count || to < 0 || to >= list.Count || from == to) return;
        var item = list[from];
        list.RemoveAt(from);
        list.Insert(to, item);
    }

    /// <summary>Wires button-free drag-and-drop reordering onto a left-panel row.</summary>
    private void EnableRowReorder(Border row, int index, Action<int, int> move)
    {
        const string Fmt = "llama-reorder";
        row.AllowDrop = true;
        Point start = default;
        bool down = false;
        row.PreviewMouseLeftButtonDown += (_, e) => { start = e.GetPosition(row); down = true; };
        row.PreviewMouseLeftButtonUp += (_, _) => down = false;
        row.MouseMove += (_, e) =>
        {
            if (!down) return;
            var p = e.GetPosition(row);
            if (Math.Abs(p.Y - start.Y) < 8 && Math.Abs(p.X - start.X) < 8) return;
            down = false;
            try { System.Windows.DragDrop.DoDragDrop(row, new System.Windows.DataObject(Fmt, index), DragDropEffects.Move); } catch { }
        };
        row.DragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent(Fmt)) { e.Effects = DragDropEffects.Move; e.Handled = true; row.BorderBrush = B("AccentBrush"); }
        };
        row.DragLeave += (_, _) => row.BorderBrush = B("BorderSoftBrush");
        row.Drop += (_, e) =>
        {
            row.BorderBrush = B("BorderSoftBrush");
            if (!e.Data.GetDataPresent(Fmt)) return;
            int from = (int)e.Data.GetData(Fmt)!;
            move(from, index);
            e.Handled = true;
        };
    }

    private void RemoveMerge(int idx)
    {
        if (idx < 0 || idx >= _mergeFiles.Count) return;
        string path = _mergeFiles[idx];
        _mergeFiles.RemoveAt(idx);
        _mergePages.Remove(path); _mergePw.Remove(path);
        RefreshAll();
    }

    private UIElement BuildFolderRow(string defaultFolder)
    {
        var folderGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        folderGrid.ColumnDefinitions.Add(new ColumnDefinition());
        folderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _folderBox = new TextBox
        {
            Text = defaultFolder, FontSize = 12, Padding = new Thickness(11), Margin = new Thickness(0, 0, 8, 0),
            Background = B("SurfaceAltBrush"), Foreground = B("TextSecondaryBrush"),
            BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var browse = SoftButton("Browse", Browse_Click);
        Grid.SetColumn(browse, 1);
        folderGrid.Children.Add(_folderBox); folderGrid.Children.Add(browse);
        return folderGrid;
    }

    private static SolidColorBrush Hex(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    // =====================================================================
    //  Info panel
    // =====================================================================
    private void BuildInfo()
    {
        InfoRows.Children.Clear();

        if (IsMultiFile)
        {
            if (_mergeFiles.Count == 0)
            {
                string what = _toolId == "images_to_pdf" ? "images" : "files";
                InfoRows.Children.Add(new TextBlock { Text = $"No {what} added yet.", Foreground = B("TextMutedBrush"), FontSize = 13 });
                return;
            }
            long totalSize = _mergeFiles.Sum(p => new FileInfo(p).Length);
            if (_toolId == "images_to_pdf")
            {
                AddInfoRow("Images", _mergeFiles.Count.ToString());
                AddInfoRow("Output Pages", _mergeFiles.Count.ToString());
                AddInfoRow("Combined Size", FileToolsService.FormatFileSize(totalSize));
            }
            else
            {
                int totalPages = _mergeFiles.Sum(p => _mergePages.TryGetValue(p, out var x) ? x : 0);
                AddInfoRow("Files", _mergeFiles.Count.ToString());
                AddInfoRow("Total Pages", totalPages.ToString());
                AddInfoRow("Combined Size", FileToolsService.FormatFileSize(totalSize));
            }
            return;
        }

        if (_pdfPath == null)
        {
            InfoRows.Children.Add(new TextBlock { Text = "No file loaded yet.", Foreground = B("TextMutedBrush"), FontSize = 13 });
            return;
        }
        var fi = new FileInfo(_pdfPath);
        AddInfoRow("File Name", fi.Name);
        AddInfoRow("File Size", FileToolsService.FormatFileSize(fi.Length));
        AddInfoRow("Total Pages", _pageCount.ToString());
        AddInfoRow("Modified", fi.LastWriteTime.ToString("MMM d, yyyy"));
    }

    private void AddInfoRow(string k, string v)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var kt = new TextBlock { Text = k, Foreground = B("TextMutedBrush"), FontSize = 13, Margin = new Thickness(0, 9, 0, 9) };
        var vt = new TextBlock { Text = v, Foreground = B("TextPrimaryBrush"), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 9, 0, 9), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 160 };
        Grid.SetColumn(vt, 1);
        var border = new Border { BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
        g.Children.Add(kt); g.Children.Add(vt); border.Child = g;
        InfoRows.Children.Add(border);
    }

    // =====================================================================
    //  File loading + preview
    // =====================================================================
    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_picking) return;
        _picking = true;
        string[]? chosen = null;
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = IsMultiFile,
                Filter = _toolId == "images_to_pdf"
                    ? "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff"
                    : "PDF Files|*.pdf"
            };
            if (dlg.ShowDialog() == true) chosen = dlg.FileNames;
        }
        finally { _picking = false; }

        if (chosen == null) return;
        if (IsMultiFile) await AddMergeFiles(chosen);
        else await LoadPdf(chosen[0]);
    }

    private void Preview_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Preview_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        if (_toolId == "insert_pages")
        {
            var imgs = files.Where(f => ImageExts.Any(x => f.EndsWith(x, StringComparison.OrdinalIgnoreCase))).ToList();
            var pdfs = files.Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();

            // With no base PDF yet, the first dropped PDF becomes the source; the rest are inserts.
            string? source = null;
            if (_pdfPath == null && pdfs.Count > 0) { source = pdfs[0]; pdfs.RemoveAt(0); }

            var inserts = new List<string>(pdfs);
            inserts.AddRange(imgs);
            if (source != null) await LoadPdf(source);
            if (inserts.Count > 0) await AddInsertItems(inserts);
            return;
        }

        var accepted = files.Where(AcceptsFile).ToArray();
        if (accepted.Length == 0) return;
        if (IsMultiFile) await AddMergeFiles(accepted);
        else await LoadPdf(accepted[0]);
    }

    /// <summary>Registers an encrypted file we can't open yet (Break mode) — no preview, no page count.</summary>
    private void LoadLocked(string path)
    {
        _pdfPath = path; _password = null; _pageCount = 0; _curPage = 1; _zoom = 1.0;
        _chainPath = path; _pdfLocked = true;
        RefreshAll();
        BuildProtectModeBody(); // refresh the hint now that a file is loaded
    }

    private async System.Threading.Tasks.Task LoadPdf(string path)
    {
        bool inProtect = _toolId == "protect_pdf";

        // Break mode already chosen: accept a protected file we can't open yet — no prompt, no preview.
        if (inProtect && _protectMode == "break" && await FileToolsService.IsPdfEncryptedAsync(path))
        {
            LoadLocked(path);
            return;
        }
        _pdfLocked = false;

        // unlock if protected — in the Protect tool, offer a "recover" escape for unknown passwords
        string? pw = await PasswordDialog.UnlockAsync(this, path, allowRecover: inProtect);
        if (pw == null) return; // cancelled
        if (pw == PasswordDialog.RecoverSentinel)
        {
            SelectProtectMode("break");   // switch the right panel to Break mode
            LoadLocked(path);             // load the file in its locked state
            return;
        }
        string? realPw = string.IsNullOrEmpty(pw) ? null : pw;

        int pages;
        try { pages = await FileToolsService.GetPdfPageCountAsync(path, realPw); }
        catch (Exception ex)
        {
            ConfirmDialog.Alert(this, "Can't Open PDF", ex.Message, ConfirmDialog.AlertKind.Error);
            return;
        }

        if (_toolId == "split_pdf" && pages <= 1)
        {
            ConfirmDialog.Alert(this, "Can't Split PDF", "This PDF has only 1 page, so there's nothing to split.");
            return; // stay on empty state
        }

        _pdfPath = path;
        _password = realPw;
        _pageCount = pages;
        _curPage = 1;
        _zoom = 1.0;
        _chainPath = path; // keep the working file as the carry-over for the next tool

        if (_toolId == "split_pdf") { SetRange(1, pages); SelectMethod(_method); }
        else if (_toolId == "extract_pages" && string.IsNullOrWhiteSpace(_extractBox.Text)) _extractBox.Text = $"1-{pages}";
        else if (_toolId == "insert_pages") _insertAfterBox.Text = pages.ToString();

        BtnAddMore.Content = IsMultiFile ? "+ Add More" : "Replace";
        RefreshAll();
    }

    private async System.Threading.Tasks.Task RenderCurrentPage()
    {
        if (_previewPages.Count == 0) return;
        if (_curPage < 1) _curPage = 1;
        if (_curPage > _previewPages.Count) _curPage = _previewPages.Count;

        var pp = _previewPages[_curPage - 1];
        var bmp = pp.IsImage ? SafeImage(pp.Path) : await RenderThumbAsync(pp.Path, pp.Password, pp.Page, 1100);
        double dw = PreviewBaseWidth * _zoom;
        if (bmp != null)
        {
            PreviewImage.Source = bmp;
            double dh = bmp.PixelWidth > 0 ? dw * bmp.PixelHeight / bmp.PixelWidth : dw * 1.3;
            PreviewImage.Width = dw; PreviewImage.Height = dh;
            OverlayCanvas.Width = dw; OverlayCanvas.Height = dh;
            _dispW = dw; _dispH = dh;
        }
        else { PreviewImage.Width = dw; }
        TxtPager.Text = $"{_curPage} / {_previewPages.Count}";
        TxtPreviewSub.Text = $"Total {_previewPages.Count} page{(_previewPages.Count == 1 ? "" : "s")}";
        TxtZoom.Text = $"{(int)(_zoom * 100)}%";
        UpdateRotatePreview();
        UpdatePreviewOverlay();
        HighlightFilmstrip();
        ScrollThumbIntoView();
    }

    private void ScrollThumbIntoView()
    {
        if (ThumbScroller == null) return;
        double cardW = 66; // 58 width + 8 margin
        double target = (_curPage - 1) * cardW - 100;
        ThumbScroller.ScrollToHorizontalOffset(Math.Max(0, target));
    }

    private async void PrevPage_Click(object sender, RoutedEventArgs e)
    { if (_curPage > 1) { _curPage--; await RenderCurrentPage(); } }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    { if (_curPage < _previewPages.Count) { _curPage++; await RenderCurrentPage(); } }

    private async void ZoomIn_Click(object sender, RoutedEventArgs e) { _zoom = Math.Min(3, _zoom + 0.2); await RenderCurrentPage(); }
    private async void ZoomOut_Click(object sender, RoutedEventArgs e) { _zoom = Math.Max(0.4, _zoom - 0.2); await RenderCurrentPage(); }
    private void FitZoom_Click(object sender, RoutedEventArgs e) => OpenFullscreenViewer();

    private void OpenFullscreenViewer()
    {
        if (_previewPages.Count == 0) return;

        int fsPage = _curPage;

        var img = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(40, 70, 40, 70)
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        var pager = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 16, 0), MinWidth = 70, TextAlignment = TextAlignment.Center
        };

        var root = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x10, 0x12, 0x16)) };

        var fs = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowState = WindowState.Maximized,
            AllowsTransparency = false,
            ShowInTaskbar = false,
            Owner = this,
            Background = Brushes.Black,
            Content = root
        };

        async System.Threading.Tasks.Task Show()
        {
            pager.Text = $"{fsPage} / {_previewPages.Count}";
            var pp = _previewPages[fsPage - 1];
            img.Source = pp.IsImage ? SafeImage(pp.Path) : await RenderThumbAsync(pp.Path, pp.Password, pp.Page, 1900);
        }

        Button NavBtn(string glyph, Action onClick)
        {
            var b = new Button
            {
                Content = glyph, Width = 46, Height = 46, FontSize = 22, Cursor = Cursors.Hand,
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center
            };
            b.Template = (ControlTemplate)Resources["SoftBtn"] ?? b.Template;
            b.Click += (_, _) => onClick();
            return b;
        }

        var prev = NavBtn("‹", async () => { if (fsPage > 1) { fsPage--; await Show(); } });
        prev.HorizontalAlignment = HorizontalAlignment.Left;
        prev.Margin = new Thickness(18, 0, 0, 0);
        var next = NavBtn("›", async () => { if (fsPage < _previewPages.Count) { fsPage++; await Show(); } });
        next.HorizontalAlignment = HorizontalAlignment.Right;
        next.Margin = new Thickness(0, 0, 18, 0);

        var close = new Button
        {
            Content = "✕  Close  (Esc)", Height = 34, Padding = new Thickness(14, 0, 14, 0), FontSize = 13,
            Cursor = Cursors.Hand, Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF))
        };
        close.Click += (_, _) => fs.Close();

        var topBar = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 16, 0, 0)
        };
        topBar.Children.Add(pager);
        topBar.Children.Add(close);

        root.Children.Add(img);
        root.Children.Add(prev);
        root.Children.Add(next);
        root.Children.Add(topBar);

        fs.KeyDown += async (_, ke) =>
        {
            if (ke.Key == System.Windows.Input.Key.Escape) fs.Close();
            else if (ke.Key == System.Windows.Input.Key.Left && fsPage > 1) { fsPage--; await Show(); }
            else if (ke.Key == System.Windows.Input.Key.Right && fsPage < _previewPages.Count) { fsPage++; await Show(); }
        };

        fs.Loaded += async (_, _) => { _curPage = fsPage; await Show(); };
        fs.Closed += async (_, _) => { _curPage = fsPage; await RenderCurrentPage(); };
        fs.Show();
    }

    private void FilmPrev_Click(object sender, RoutedEventArgs e)
        => ThumbScroller?.ScrollToHorizontalOffset(Math.Max(0, ThumbScroller.HorizontalOffset - 264));
    private void FilmNext_Click(object sender, RoutedEventArgs e)
        => ThumbScroller?.ScrollToHorizontalOffset(ThumbScroller.HorizontalOffset + 264);

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select output folder" };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) _folderBox.Text = dlg.SelectedPath;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _pdfPath = null; _password = null; _pageCount = 0; _curPage = 1; _zoom = 1.0; _chainPath = null;
        _mergeFiles.Clear(); _mergePages.Clear(); _mergePw.Clear();
        _insertImages.Clear(); _insertPw.Clear(); _insertItemPages.Clear();
        if (_toolId == "insert_pages") UpdateInsertCount();
        BtnAddMore.Content = IsMultiFile ? "+ Add More" : "Open";
        RefreshAll();
    }

    // ---- panel collapse (left files + right file-info) ----
    private void CollapseLeft_Click(object sender, RoutedEventArgs e) => SetLeftCollapsed(true);
    private void ExpandLeft_Click(object sender, MouseButtonEventArgs e) => SetLeftCollapsed(false);

    private void SetLeftCollapsed(bool collapsed)
    {
        _leftCollapsed = collapsed;
        LeftPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        LeftRail.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        ColFiles.Width = collapsed ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
    }

    private void CollapseInfo_Click(object sender, RoutedEventArgs e)
    {
        _infoCollapsed = !_infoCollapsed;
        InfoRows.Visibility = _infoCollapsed ? Visibility.Collapsed : Visibility.Visible;
        BtnInfoToggle.Content = _infoCollapsed ? "▸" : "▾";
        BtnInfoToggle.ToolTip = _infoCollapsed ? "Expand" : "Collapse";
    }

    private void CollapseTip_Click(object sender, RoutedEventArgs e)
    {
        _tipCollapsed = !_tipCollapsed;
        TxtTip.Visibility = _tipCollapsed ? Visibility.Collapsed : Visibility.Visible;
        BtnTipToggle.Content = _tipCollapsed ? "▸" : "▾";
        BtnTipToggle.ToolTip = _tipCollapsed ? "Expand" : "Collapse";
    }

    private void CollapseSettings_Click(object sender, RoutedEventArgs e)
    {
        _settingsCollapsed = !_settingsCollapsed;
        SettingsScroller.Visibility = _settingsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        BtnSettingsToggle.Content = _settingsCollapsed ? "▸" : "▾";
        BtnSettingsToggle.ToolTip = _settingsCollapsed ? "Expand" : "Collapse";
    }

    // ---- sidebar (left navigation) collapse ----
    private void CollapseNav_Click(object sender, RoutedEventArgs e) => SetNavCollapsed(true);
    private void ExpandNav_Click(object sender, RoutedEventArgs e) => SetNavCollapsed(false);
    private void ExpandNav_Click(object sender, MouseButtonEventArgs e) => SetNavCollapsed(false);

    private void SetNavCollapsed(bool collapsed)
    {
        _navCollapsed = collapsed;
        NavPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        NavRail.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        ColNav.Width = collapsed ? GridLength.Auto : new GridLength(240);
    }

    // ---- right info/settings panel collapse (to a rail, like the left panel) ----
    private void CollapseRight_Click(object sender, RoutedEventArgs e) => SetRightCollapsed(true);
    private void ExpandRight_Click(object sender, MouseButtonEventArgs e) => SetRightCollapsed(false);

    private void SetRightCollapsed(bool collapsed)
    {
        _rightCollapsed = collapsed;
        RightPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        RightRail.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        ColInfo.Width = collapsed ? GridLength.Auto : new GridLength(0.95, GridUnitType.Star);
    }

    private void ShowPreviewState()
    {
        bool has = _previewPages.Count > 0;
        CenterEmpty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        PreviewState.Visibility = has ? Visibility.Visible : Visibility.Collapsed;

        if (_pdfLocked)
        {
            TxtCenterEmptyIcon.Text = "🔒";
            TxtCenterEmptyTitle.Text = "Locked PDF — preview unavailable";
            TxtCenterEmptySub.Text = "Pick a character set and length on the right, then click Break Password.";
        }
        else
        {
            TxtCenterEmptyIcon.Text = "📄";
            TxtCenterEmptyTitle.Text = "Nothing to preview yet";
        }
    }

    private void UpdateSelection()
    {
        if (IsMultiFile)
        {
            string noun = _toolId == "images_to_pdf" ? "image" : "file";
            if (_mergeFiles.Count == 0) { TxtSelection.Text = $"No {noun}s selected"; return; }
            long size = _mergeFiles.Sum(p => new FileInfo(p).Length);
            TxtSelection.Text = $"{_mergeFiles.Count} {noun}{(_mergeFiles.Count == 1 ? "" : "s")} selected · {_previewPages.Count} page{(_previewPages.Count == 1 ? "" : "s")} · {FileToolsService.FormatFileSize(size)}";
        }
        else
        {
            if (_pdfPath == null) { TxtSelection.Text = "No file selected"; return; }
            if (_pdfLocked) { TxtSelection.Text = $"{Path.GetFileName(_pdfPath)} · 🔒 locked · {FileToolsService.FormatFileSize(new FileInfo(_pdfPath).Length)}"; return; }
            TxtSelection.Text = $"{Path.GetFileName(_pdfPath)} · {_pageCount} page{(_pageCount == 1 ? "" : "s")} · {FileToolsService.FormatFileSize(new FileInfo(_pdfPath).Length)}";
        }
    }

    // =====================================================================
    //  Process
    // =====================================================================
    private async void Process_Click(object sender, RoutedEventArgs e)
    {
        if (_toolId == "merge_pdf") { await ProcessMerge(); return; }
        if (_toolId == "images_to_pdf") { await ProcessImagesToPdf(); return; }

        if (_pdfPath == null)
        {
            ConfirmDialog.Alert(this, "No File Selected", "Add a PDF to get started.");
            return;
        }
        if (_toolId == "compress_pdf") await ProcessCompress();
        else if (_toolId == "pdf_to_images") await ProcessPdfToImages();
        else if (_toolId == "rotate_pdf") await ProcessRotate();
        else if (_toolId == "extract_pages") await ProcessExtract();
        else if (_toolId == "insert_pages") await ProcessInsert();
        else if (_toolId == "page_numbers") await ProcessPageNumbers();
        else if (_toolId == "watermark") await ProcessWatermark();
        else if (_toolId == "protect_pdf") await ProcessProtect();
        else await ProcessSplit();
    }

    private async System.Threading.Tasks.Task ProcessExtract()
    {
        var pages = ParsePageSpec(_extractBox.Text);
        if (pages == null || pages.Count == 0)
        {
            ConfirmDialog.Alert(this, "Invalid Pages", "Enter the pages to extract, e.g. 1, 3, 5-8.");
            return;
        }
        if (pages.Any(p => p < 1 || p > _pageCount))
        {
            ConfirmDialog.Alert(this, "Page Out of Range", $"This PDF has {_pageCount} page(s). Use values within 1–{_pageCount}.");
            return;
        }

        string outDir = _folderBox.Text.Trim();
        try { Directory.CreateDirectory(outDir); }
        catch { ConfirmDialog.Alert(this, "Invalid Folder", "Choose a valid output folder.", ConfirmDialog.AlertKind.Error); return; }

        string name = _extractNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) name = "extracted.pdf";
        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name += ".pdf";
        string outPath = Path.Combine(outDir, name);

        await RunJob("Extracting pages…", $"Pulling {pages.Count} page(s)", "Creating…",
            () => FileToolsService.ExtractPdfPagesAsync(_pdfPath!, pages.ToArray(), outPath, null, _password),
            "Extraction Complete", $"Extracted {pages.Count} page(s) to:\n{outPath}", outPath, select: true);
    }

    private async System.Threading.Tasks.Task ProcessInsert()
    {
        if (_insertImages.Count == 0)
        {
            ConfirmDialog.Alert(this, "Nothing to Insert", "Add at least one image or PDF to insert.");
            return;
        }
        if (!int.TryParse(_insertAfterBox.Text.Trim(), out int afterPage) || afterPage < 0 || afterPage > _pageCount)
        {
            ConfirmDialog.Alert(this, "Invalid Page", $"\"Insert after page\" must be between 0 and {_pageCount}.");
            return;
        }

        string outDir = _folderBox.Text.Trim();
        try { Directory.CreateDirectory(outDir); }
        catch { ConfirmDialog.Alert(this, "Invalid Folder", "Choose a valid output folder.", ConfirmDialog.AlertKind.Error); return; }

        string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(_pdfPath!) + "_inserted.pdf");
        var insertPws = _insertImages.Select(p => _insertPw.TryGetValue(p, out var pw) ? pw : null).ToList();

        await RunJob("Inserting pages…", $"Adding {_insertImages.Count} item(s)", "Inserting…",
            () => FileToolsService.InsertPdfPagesAsync(_pdfPath!, _insertImages.ToArray(), afterPage, outPath, null, _password, insertPws),
            "Insert Complete", $"Inserted {_insertImages.Count} item(s) into:\n{outPath}", outPath, select: true);
    }

    private async System.Threading.Tasks.Task ProcessPageNumbers()
    {
        string outDir = _folderBox.Text.Trim();
        try { Directory.CreateDirectory(outDir); }
        catch { ConfirmDialog.Alert(this, "Invalid Folder", "Choose a valid output folder.", ConfirmDialog.AlertKind.Error); return; }

        string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(_pdfPath!) + "_numbered.pdf");

        await RunJob("Adding page numbers…", $"Stamping {_pageCount} page(s)", "Numbering…",
            () => FileToolsService.AddPageNumbersAsync(_pdfPath!, outPath, _pnPos, null, _password, _pnFont, _pnFontSize, _pnColorHex),
            "Page Numbers Added", $"Numbered {_pageCount} page(s) and saved to:\n{outPath}", outPath, select: true);
    }

    private async System.Threading.Tasks.Task ProcessWatermark()
    {
        string text = _wmTextBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            ConfirmDialog.Alert(this, "No Text", "Enter the watermark text to overlay.");
            return;
        }

        string outDir = _folderBox.Text.Trim();
        try { Directory.CreateDirectory(outDir); }
        catch { ConfirmDialog.Alert(this, "Invalid Folder", "Choose a valid output folder.", ConfirmDialog.AlertKind.Error); return; }

        string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(_pdfPath!) + "_watermarked.pdf");

        await RunJob("Adding watermark…", $"Overlaying “{text}”", "Stamping…",
            () => FileToolsService.WatermarkPdfAsync(_pdfPath!, outPath, text, _wmOpacity, _wmFontSize, null, _password, _wmFont, _wmColorHex, _wmOrient, _wmPos),
            "Watermark Added", $"Watermarked {_pageCount} page(s) and saved to:\n{outPath}", outPath, select: true);
    }

    private async System.Threading.Tasks.Task ProcessProtect()
    {
        if (_pdfPath == null) { ConfirmDialog.Alert(this, "No File", "Open a PDF first."); return; }

        string outDir = _folderBox.Text.Trim();
        try { Directory.CreateDirectory(outDir); }
        catch { ConfirmDialog.Alert(this, "Invalid Folder", "Choose a valid output folder.", ConfirmDialog.AlertKind.Error); return; }

        if (_protectMode == "remove") { await ProcessRemovePassword(outDir); return; }
        if (_protectMode == "break") { await ProcessBreakPassword(outDir); return; }

        // ---- add password ----
        string pw = _protectPwBox.Password;
        if (string.IsNullOrEmpty(pw))
        {
            ConfirmDialog.Alert(this, "No Password", "Enter a password to protect the PDF.");
            return;
        }
        if (pw != _protectConfirmBox.Password)
        {
            ConfirmDialog.Alert(this, "Passwords Don't Match", "The password and confirmation don't match.");
            return;
        }

        string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(_pdfPath!) + "_protected.pdf");
        await RunJob("Protecting PDF…", "Encrypting with your password", "Protecting…",
            () => FileToolsService.ProtectPdfAsync(_pdfPath!, outPath, pw, _password),
            "PDF Protected", $"Saved a password-protected copy to:\n{outPath}", outPath, select: true);
    }

    private async System.Threading.Tasks.Task ProcessRemovePassword(string outDir)
    {
        string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(_pdfPath!) + "_unlocked.pdf");
        await RunJob("Removing password…", "Saving a decrypted copy", "Removing…",
            () => FileToolsService.RemovePdfPasswordAsync(_pdfPath!, outPath, _password),
            "Password Removed", $"Saved an unprotected copy to:\n{outPath}", outPath, select: true);
    }

    private async System.Threading.Tasks.Task ProcessBreakPassword(string outDir)
    {
        string charset = FileToolsService.RecoveryCharsets.First(c => c.key == _breakCharsetKey).chars;

        _recoverCts = new System.Threading.CancellationTokenSource();
        var token = _recoverCts.Token;
        var progress = new Progress<(long tried, long total, string current)>(p =>
        {
            TxtBusySub.Text = p.total > 0
                ? $"Tried {p.tried:N0} of {p.total:N0}…  ({p.current})"
                : $"Trying common passwords…  ({p.current})";
        });

        BtnProcess.IsEnabled = false;
        TxtProcess.Text = "Breaking…";
        ShowBusy("Recovering password…", "Trying common passwords…");
        BtnBusyCancel.Visibility = Visibility.Visible;
        string? found = null;
        bool cancelled = false;
        try
        {
            found = await FileToolsService.RecoverPdfPasswordAsync(_pdfPath!, charset, _breakMaxLen, progress, token);
        }
        catch (OperationCanceledException) { cancelled = true; }
        catch (Exception ex) { HideBusy(); BtnBusyCancel.Visibility = Visibility.Collapsed; ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error); BtnProcess.IsEnabled = true; ApplyToolMeta(); return; }

        HideBusy();
        BtnBusyCancel.Visibility = Visibility.Collapsed;
        BtnProcess.IsEnabled = true;
        ApplyToolMeta();
        _recoverCts.Dispose(); _recoverCts = null;

        if (cancelled) { ConfirmDialog.Alert(this, "Stopped", "Password recovery was stopped."); return; }
        if (found == null)
        {
            ConfirmDialog.Alert(this, "Not Found",
                "Couldn't recover the password with these settings. Try a larger character set or a longer max length.",
                ConfirmDialog.AlertKind.Info);
            return;
        }

        // found it — unlock the file in-app and save a decrypted copy
        _password = string.IsNullOrEmpty(found) ? null : found;
        _pdfLocked = false;
        string shown = string.IsNullOrEmpty(found) ? "(no open password — owner-protected only)" : found;
        string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(_pdfPath!) + "_unlocked.pdf");
        try
        {
            await FileToolsService.RemovePdfPasswordAsync(_pdfPath!, outPath, _password);
            RememberChain(outPath);
            ConfirmDialog.Alert(this, "Password Recovered",
                $"Password: {shown}\n\nSaved an unprotected copy to:\n{outPath}", ConfirmDialog.AlertKind.Success, "Done");
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outPath}\""); } catch { }
        }
        catch (Exception ex)
        {
            ConfirmDialog.Alert(this, "Password Recovered",
                $"Password: {shown}\n\nBut saving an unprotected copy failed: {ex.Message}", ConfirmDialog.AlertKind.Info);
        }
        // try to render the now-unlocked file
        try { _pageCount = await FileToolsService.GetPdfPageCountAsync(_pdfPath!, _password); RefreshAll(); } catch { }
    }

    private void BtnBusyCancel_Click(object sender, RoutedEventArgs e) => _recoverCts?.Cancel();

    /// <summary>Shared run wrapper: spinner + min-dwell, themed success, open result, advance.</summary>
    private async System.Threading.Tasks.Task RunJob(
        string busyTitle, string busySub, string buttonText,
        Func<System.Threading.Tasks.Task> work,
        string okTitle, string okMessage, string resultPath, bool select)
    {
        BtnProcess.IsEnabled = false;
        TxtProcess.Text = buttonText;
        ShowBusy(busyTitle, busySub);
        try
        {
            await System.Threading.Tasks.Task.WhenAll(work(), System.Threading.Tasks.Task.Delay(650));
            HideBusy();
            RememberChain(resultPath);
            ConfirmDialog.Alert(this, okTitle, okMessage, ConfirmDialog.AlertKind.Success, "Done");
            try { System.Diagnostics.Process.Start("explorer.exe", select ? $"/select,\"{resultPath}\"" : resultPath); } catch { }
        }
        catch (Exception ex)
        {
            HideBusy();
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
        }
        finally
        {
            BtnProcess.IsEnabled = true;
            ApplyToolMeta();
        }
    }

    private async System.Threading.Tasks.Task ProcessRotate()
    {
        string outDir = _folderBox.Text.Trim();
        try { Directory.CreateDirectory(outDir); }
        catch { ConfirmDialog.Alert(this, "Invalid Folder", "Choose a valid output folder.", ConfirmDialog.AlertKind.Error); return; }

        string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(_pdfPath!) + "_rotated.pdf");

        BtnProcess.IsEnabled = false;
        TxtProcess.Text = "Rotating…";
        ShowBusy("Rotating PDF…", $"Turning {_pageCount} page(s) by {_rotateDeg}°");
        try
        {
            var task = FileToolsService.RotatePdfAsync(_pdfPath!, outPath, _rotateDeg, null, _password);
            await System.Threading.Tasks.Task.WhenAll(task, System.Threading.Tasks.Task.Delay(650));
            await task;
            HideBusy();
            ConfirmDialog.Alert(this, "Rotation Complete",
                $"Rotated {_pageCount} page(s) by {_rotateDeg}° and saved to:\n{outPath}",
                ConfirmDialog.AlertKind.Success, "Done");
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outPath}\""); } catch { }
            RememberChain(outPath);
        }
        catch (Exception ex)
        {
            HideBusy();
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
        }
        finally
        {
            BtnProcess.IsEnabled = true;
            ApplyToolMeta();
        }
    }

    private async System.Threading.Tasks.Task ProcessPdfToImages()
    {
        string outDir = _folderBox.Text.Trim();
        try { Directory.CreateDirectory(outDir); }
        catch { ConfirmDialog.Alert(this, "Invalid Folder", "Choose a valid output folder.", ConfirmDialog.AlertKind.Error); return; }

        BtnProcess.IsEnabled = false;
        TxtProcess.Text = "Converting…";
        ShowBusy("Converting to images…", $"Rendering {_pageCount} page(s) as {_imgFormat.ToUpperInvariant()}");
        try
        {
            var task = FileToolsService.PdfToImagesAsync(_pdfPath!, outDir, _imgFormat, _imgDpi, null, _password);
            await System.Threading.Tasks.Task.WhenAll(task, System.Threading.Tasks.Task.Delay(650));
            var results = await task;
            HideBusy();
            ConfirmDialog.Alert(this, "Conversion Complete",
                $"Saved {results.Length} {_imgFormat.ToUpperInvariant()} image(s) to:\n{outDir}",
                ConfirmDialog.AlertKind.Success, "Done");
            try { System.Diagnostics.Process.Start("explorer.exe", outDir); } catch { }
        }
        catch (Exception ex)
        {
            HideBusy();
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
        }
        finally
        {
            BtnProcess.IsEnabled = true;
            ApplyToolMeta();
        }
    }

    private async System.Threading.Tasks.Task ProcessMerge()
    {
        if (_mergeFiles.Count < 2)
        {
            ConfirmDialog.Alert(this, "Can't Merge PDF",
                "Add at least 2 PDF files to merge — a single file has nothing to combine.");
            return;
        }

        string outDir = _folderBox.Text.Trim();
        try { Directory.CreateDirectory(outDir); }
        catch { ConfirmDialog.Alert(this, "Invalid Folder", "Choose a valid output folder.", ConfirmDialog.AlertKind.Error); return; }

        string name = _mergeNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) name = "merged.pdf";
        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name += ".pdf";
        string outPath = Path.Combine(outDir, name);

        BtnProcess.IsEnabled = false;
        TxtProcess.Text = "Merging…";
        ShowBusy("Merging PDFs…", $"Combining {_mergeFiles.Count} files");
        try
        {
            var passwords = _mergeFiles.Select(p => _mergePw.TryGetValue(p, out var pw) ? pw : null).ToList();
            var mergeTask = FileToolsService.MergePdfsAsync(_mergeFiles.ToArray(), outPath, null, passwords);
            await System.Threading.Tasks.Task.WhenAll(mergeTask, System.Threading.Tasks.Task.Delay(650));
            await mergeTask;
            HideBusy();
            long size = new FileInfo(outPath).Length;
            ConfirmDialog.Alert(this, "Merge Complete",
                $"Combined {_mergeFiles.Count} files ({FileToolsService.FormatFileSize(size)}) into:\n{outPath}",
                ConfirmDialog.AlertKind.Success, "Done");
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outPath}\""); } catch { }
            RememberChain(outPath);
        }
        catch (Exception ex)
        {
            HideBusy();
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
        }
        finally
        {
            BtnProcess.IsEnabled = true;
            ApplyToolMeta();
        }
    }

    private async System.Threading.Tasks.Task ProcessImagesToPdf()
    {
        if (_mergeFiles.Count < 1)
        {
            ConfirmDialog.Alert(this, "No Images", "Add at least one image to create a PDF.");
            return;
        }

        string outDir = _folderBox.Text.Trim();
        try { Directory.CreateDirectory(outDir); }
        catch { ConfirmDialog.Alert(this, "Invalid Folder", "Choose a valid output folder.", ConfirmDialog.AlertKind.Error); return; }

        string name = _mergeNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) name = "images.pdf";
        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name += ".pdf";
        string outPath = Path.Combine(outDir, name);

        BtnProcess.IsEnabled = false;
        TxtProcess.Text = "Creating…";
        ShowBusy("Creating PDF…", $"Adding {_mergeFiles.Count} image(s)");
        try
        {
            var task = FileToolsService.ImagesToPdfAsync(_mergeFiles.ToArray(), outPath, null);
            await System.Threading.Tasks.Task.WhenAll(task, System.Threading.Tasks.Task.Delay(650));
            await task;
            HideBusy();
            long size = new FileInfo(outPath).Length;
            ConfirmDialog.Alert(this, "PDF Created",
                $"Combined {_mergeFiles.Count} image(s) ({FileToolsService.FormatFileSize(size)}) into:\n{outPath}",
                ConfirmDialog.AlertKind.Success, "Done");
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outPath}\""); } catch { }
            RememberChain(outPath);
        }
        catch (Exception ex)
        {
            HideBusy();
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
        }
        finally
        {
            BtnProcess.IsEnabled = true;
            ApplyToolMeta();
        }
    }

    private async System.Threading.Tasks.Task ProcessSplit()
    {
        if (!int.TryParse(_fromBox.Text, out int from) || !int.TryParse(_toBox.Text, out int to) || from < 1 || to < from)
        {
            ConfirmDialog.Alert(this, "Invalid Input", "Enter a valid page range.");
            return;
        }
        if (to > _pageCount)
        {
            ConfirmDialog.Alert(this, "Page Range Too Large", $"This PDF has only {_pageCount} page(s). Choose a range within 1–{_pageCount}.");
            return;
        }

        string outDir = _folderBox.Text.Trim();
        try { Directory.CreateDirectory(outDir); }
        catch { ConfirmDialog.Alert(this, "Invalid Folder", "Choose a valid output folder.", ConfirmDialog.AlertKind.Error); return; }

        BtnProcess.IsEnabled = false;
        TxtProcess.Text = "Splitting…";
        ShowBusy("Splitting PDF…", $"Extracting pages {from}–{to}");
        try
        {
            // run the split; keep the spinner visible at least briefly so it reads as work, not a flash
            var splitTask = FileToolsService.SplitPdfAsync(_pdfPath!, outDir, from, to, null, _password);
            await System.Threading.Tasks.Task.WhenAll(splitTask, System.Threading.Tasks.Task.Delay(650));
            var results = await splitTask;
            HideBusy();
            ConfirmDialog.Alert(this, "Split Complete",
                $"Extracted {results.Length} page(s) to:\n{outDir}", ConfirmDialog.AlertKind.Success, "Done");
            try { System.Diagnostics.Process.Start("explorer.exe", outDir); } catch { }
        }
        catch (Exception ex)
        {
            HideBusy();
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
        }
        finally
        {
            BtnProcess.IsEnabled = true;
            ApplyToolMeta();
        }
    }

    private async System.Threading.Tasks.Task ProcessCompress()
    {
        string outDir = _folderBox.Text.Trim();
        try { Directory.CreateDirectory(outDir); }
        catch { ConfirmDialog.Alert(this, "Invalid Folder", "Choose a valid output folder.", ConfirmDialog.AlertKind.Error); return; }

        long? targetBytes = null;
        if (_compressByTarget)
        {
            targetBytes = GetTargetBytes();
            if (targetBytes == null)
            {
                ConfirmDialog.Alert(this, "Invalid Target Size", "Enter a target size greater than zero.", ConfirmDialog.AlertKind.Error);
                return;
            }
        }

        string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(_pdfPath!) + "_compressed.pdf");
        long original = new FileInfo(_pdfPath!).Length;

        BtnProcess.IsEnabled = false;
        TxtProcess.Text = "Compressing…";
        ShowBusy("Compressing PDF…", "Optimizing pages and images");
        try
        {
            long newSize;
            string change;
            if (targetBytes != null)
            {
                var task = FileToolsService.CompressPdfToTargetAsync(_pdfPath!, outPath, targetBytes.Value, null, _password);
                await System.Threading.Tasks.Task.WhenAll(task, System.Threading.Tasks.Task.Delay(650));
                var res = await task;
                newSize = res.Bytes;
                change = res.TargetMet
                    ? $"target {FileToolsService.FormatFileSize(targetBytes.Value)} ✓"
                    : $"couldn't reach {FileToolsService.FormatFileSize(targetBytes.Value)} — smallest possible";
            }
            else
            {
                var compressTask = FileToolsService.CompressPdfAsync(_pdfPath!, outPath, _quality, null, _password);
                await System.Threading.Tasks.Task.WhenAll(compressTask, System.Threading.Tasks.Task.Delay(650));
                newSize = await compressTask;
                double pct = original > 0 ? (1 - (double)newSize / original) * 100 : 0;
                change = pct >= 1 ? $"{pct:0}% smaller" : "already well optimized";
            }
            HideBusy();
            ConfirmDialog.Alert(this, "Compression Complete",
                $"{FileToolsService.FormatFileSize(original)}  →  {FileToolsService.FormatFileSize(newSize)}   ({change})\n{outPath}",
                ConfirmDialog.AlertKind.Success, "Done");
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outPath}\""); } catch { }
            RememberChain(outPath);
        }
        catch (Exception ex)
        {
            HideBusy();
            ConfirmDialog.Alert(this, "Error", ex.Message, ConfirmDialog.AlertKind.Error);
        }
        finally
        {
            BtnProcess.IsEnabled = true;
            ApplyToolMeta();
        }
    }

    // =====================================================================
    //  Small UI builders
    // =====================================================================
    private TextBlock SectionTitle(string text, double topMargin = 0)
        => new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 16, Foreground = B("TextPrimaryBrush"), Margin = new Thickness(0, topMargin, 0, 12) };

    private TextBlock MutedLabel(string text, double topMargin = 0)
        => new() { Text = text, FontSize = 11, Foreground = B("TextMutedBrush"), Margin = new Thickness(0, topMargin, 0, 0) };

    private StackPanel LabeledField(string label, out TextBox box, string val)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = B("TextMutedBrush"), Margin = new Thickness(0, 0, 0, 7) });
        box = new TextBox
        {
            Text = val, FontSize = 14, Padding = new Thickness(11), Background = B("SurfaceAltBrush"),
            Foreground = B("TextPrimaryBrush"), BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1)
        };
        sp.Children.Add(box);
        return sp;
    }

    private Button QuickPill(string text, Action onClick)
    {
        var btn = new Button
        {
            Content = text, Margin = new Thickness(0, 0, 9, 0), Padding = new Thickness(14, 7, 14, 7),
            FontSize = 12.5, Cursor = Cursors.Hand, BorderThickness = new Thickness(0),
            Background = B("SurfaceAltBrush"), Foreground = B("TextSecondaryBrush"),
            Template = (ControlTemplate)Resources["Pill"]
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private Border MakeCheck(string key, string label, bool on)
    {
        var row = new Border { Padding = new Thickness(0, 6, 0, 6), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var box = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 11, 0),
            BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center
        };
        var tb = new TextBlock { FontSize = 13.5, Foreground = B("TextSecondaryBrush"), Text = label, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(box); sp.Children.Add(tb);
        row.Child = sp;
        _opts[key] = (box, on);
        ApplyCheck(key);
        row.MouseLeftButtonUp += (_, _) => { var v = _opts[key]; _opts[key] = (v.box, !v.on); ApplyCheck(key); };
        return row;
    }

    private void ApplyCheck(string key)
    {
        var (box, on) = _opts[key];
        box.Background = on ? B("AccentBrush") : B("SurfaceAltBrush");
        box.BorderBrush = on ? B("AccentBrush") : B("BorderSoftBrush");
        box.Child = on ? new TextBlock { Text = "✓", Foreground = B("AccentTextBrush"), FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null;
    }

    private Button SoftButton(string text, RoutedEventHandler onClick)
    {
        var btn = new Button
        {
            Content = text, Padding = new Thickness(16, 0, 16, 0), Cursor = Cursors.Hand, FontSize = 13,
            Background = B("SurfaceAltBrush"), Foreground = B("TextSecondaryBrush"), BorderThickness = new Thickness(0),
            Template = (ControlTemplate)Resources["SoftBtn"]
        };
        btn.Click += onClick;
        return btn;
    }

    private static SolidColorBrush Tint(string key)
    {
        var c = B(key).Color;
        return new SolidColorBrush(Color.FromArgb(0x22, c.R, c.G, c.B));
    }
}
