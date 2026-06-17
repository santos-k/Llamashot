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

    // which tool this workspace is currently showing
    private string _toolId = "split_pdf";
    private static readonly HashSet<string> Implemented = new() { "merge_pdf", "split_pdf", "compress_pdf", "pdf_to_images" };

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

    // merge settings / state (multi-file)
    private readonly List<string> _mergeFiles = new();
    private readonly Dictionary<string, int> _mergePages = new();
    private readonly Dictionary<string, string?> _mergePw = new();
    private TextBox _mergeNameBox = null!;

    // pdf-to-images settings
    private string _imgFormat = "png";
    private int _imgDpi = 150;
    private readonly Dictionary<string, Border> _fmtCards = new();
    private readonly Dictionary<string, Border> _dpiCards = new();

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
        ("watermark","Watermark PDF"), ("protect_pdf","Protect PDF"),
    };

    public ToolWorkspaceWindow(string toolId = "split_pdf")
    {
        InitializeComponent();
        _toolId = Implemented.Contains(toolId) ? toolId : "split_pdf";
        BuildRail();
        BuildSettings();
        BuildInfo();
        ApplyToolMeta();
        UpdateThemeGlyph();
        UpdateSelection();
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private void ApplyToolMeta()
    {
        var m = Meta(_toolId);
        TxtTitle.Text = m.title;
        TxtSubtitle.Text = m.sub;
        TxtTip.Text = m.tip;
        TxtProcess.Text = m.action;
        bool merge = _toolId == "merge_pdf";
        TxtDropTitle.Text = merge ? "Drag & drop PDFs here" : "Drag & drop a PDF here";
        BtnEmptySelect.Content = merge ? "Select PDFs" : "Select PDF";
    }

    /// <summary>Builds the per-tool settings slot for the active tool.</summary>
    private void BuildSettings()
    {
        SettingsHost.Children.Clear();
        _methodCards.Clear();
        _compressCards.Clear();
        _fmtCards.Clear();
        _dpiCards.Clear();
        if (_toolId == "merge_pdf") BuildMergeSettings();
        else if (_toolId == "compress_pdf") BuildCompressSettings();
        else if (_toolId == "pdf_to_images") BuildPdfToImagesSettings();
        else BuildSplitSettings();
    }

    private void SwitchTool(string id)
    {
        if (id == _toolId || !Implemented.Contains(id)) return;
        _toolId = id;
        BuildRail();
        BuildSettings();
        ApplyToolMeta();

        if (_pdfPath != null && _toolId == "split_pdf")
        {
            if (_pageCount <= 1)
                ConfirmDialog.Alert(this, "Can't Split PDF",
                    "This PDF has only 1 page, so there's nothing to split.");
            else { SetRange(1, _pageCount); SelectMethod(_method); }
        }

        if (_toolId == "merge_pdf") BuildMergeList();
        BuildInfo();
        UpdateSelection();
        ShowPreviewState();
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
        SettingsHost.Children.Add(SectionTitle("Compression Level"));
        SettingsHost.Children.Add(CompressOption("low", "Low Compression", "Best quality · larger file"));
        SettingsHost.Children.Add(CompressOption("rec", "Recommended", "Balanced quality and size"));
        SettingsHost.Children.Add(CompressOption("high", "High Compression", "Smallest file · lower quality"));

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

    private async System.Threading.Tasks.Task AddMergeFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
            if (_mergeFiles.Contains(path)) continue;

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

        BuildMergeList();
        BuildInfo();
        UpdateSelection();
        ShowPreviewState();
    }

    private void BuildMergeList()
    {
        MergeList.Items.Clear();
        for (int i = 0; i < _mergeFiles.Count; i++)
            MergeList.Items.Add(MergeRow(i));
        TxtMergeCount.Text = $"{_mergeFiles.Count} file{(_mergeFiles.Count == 1 ? "" : "s")}";
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

        var badge = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(9), Background = Hex("#F06262"),
            Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = new TextBlock
        {
            Text = (idx + 1).ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(badge, 0);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = fi.Name, Foreground = B("TextPrimaryBrush"), FontWeight = FontWeights.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
        info.Children.Add(new TextBlock { Text = $"{pages} page{(pages == 1 ? "" : "s")} · {FileToolsService.FormatFileSize(fi.Length)}", Foreground = B("TextMutedBrush"), FontSize = 12 });
        Grid.SetColumn(info, 1);

        var ctrls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        ctrls.Children.Add(MiniBtn("↑", () => MoveMerge(idx, -1), idx > 0));
        ctrls.Children.Add(MiniBtn("↓", () => MoveMerge(idx, +1), idx < _mergeFiles.Count - 1));
        ctrls.Children.Add(MiniBtn("✕", () => RemoveMerge(idx), true));
        Grid.SetColumn(ctrls, 2);

        grid.Children.Add(badge); grid.Children.Add(info); grid.Children.Add(ctrls);
        return new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 9),
            Background = B("SurfaceAltBrush"), BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1),
            Child = grid
        };
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
        BuildMergeList();
    }

    private void RemoveMerge(int idx)
    {
        if (idx < 0 || idx >= _mergeFiles.Count) return;
        string path = _mergeFiles[idx];
        _mergeFiles.RemoveAt(idx);
        _mergePages.Remove(path); _mergePw.Remove(path);
        BuildMergeList();
        BuildInfo();
        UpdateSelection();
        ShowPreviewState();
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

        if (_toolId == "merge_pdf")
        {
            if (_mergeFiles.Count == 0)
            {
                InfoRows.Children.Add(new TextBlock { Text = "No files added yet.", Foreground = B("TextMutedBrush"), FontSize = 13 });
                return;
            }
            int totalPages = _mergeFiles.Sum(p => _mergePages.TryGetValue(p, out var x) ? x : 0);
            long totalSize = _mergeFiles.Sum(p => new FileInfo(p).Length);
            AddInfoRow("Files", _mergeFiles.Count.ToString());
            AddInfoRow("Total Pages", totalPages.ToString());
            AddInfoRow("Combined Size", FileToolsService.FormatFileSize(totalSize));
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
        bool merge = _toolId == "merge_pdf";
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "PDF Files|*.pdf", Multiselect = merge };
        if (dlg.ShowDialog() == true)
        {
            if (merge) await AddMergeFiles(dlg.FileNames);
            else await LoadPdf(dlg.FileName);
        }
    }

    private void Preview_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Preview_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var pdfs = files.Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (pdfs.Length == 0) return;
            if (_toolId == "merge_pdf") await AddMergeFiles(pdfs);
            else await LoadPdf(pdfs[0]);
        }
    }

    private async System.Threading.Tasks.Task LoadPdf(string path)
    {
        // unlock if protected
        string? pw = await PasswordDialog.UnlockAsync(this, path);
        if (pw == null) return; // cancelled
        _password = string.IsNullOrEmpty(pw) ? null : pw;

        int pages;
        try { pages = await FileToolsService.GetPdfPageCountAsync(path, _password); }
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
        _pageCount = pages;
        _curPage = 1;
        _zoom = 1.0;

        TxtFileName.Text = Path.GetFileName(path);
        TxtTotalPages.Text = $"Total Pages: {pages}";
        ShowPreviewState();

        BuildInfo();
        UpdateSelection();
        if (_toolId == "split_pdf") { SetRange(1, pages); SelectMethod(_method); }
        await RenderCurrentPage();
        await BuildThumbnails();
    }

    private async System.Threading.Tasks.Task<BitmapImage?> RenderPageAsync(int page1, uint width)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(_pdfPath!);
            var doc = string.IsNullOrEmpty(_password)
                ? await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file)
                : await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file, _password);
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

    private async System.Threading.Tasks.Task RenderCurrentPage()
    {
        var bmp = await RenderPageAsync(_curPage, 900);
        if (bmp != null) PreviewImage.Source = bmp;
        PreviewImage.Width = 240 * _zoom;
        TxtPager.Text = $"{_curPage} / {_pageCount}";
        TxtZoom.Text = $"{(int)(_zoom * 100)}%";
    }

    private async System.Threading.Tasks.Task BuildThumbnails()
    {
        ThumbList.Items.Clear();
        for (int p = 1; p <= _pageCount; p++)
        {
            var bmp = await RenderPageAsync(p, 150);
            int pageNum = p;
            var card = new Border
            {
                Width = 74, Margin = new Thickness(0, 0, 10, 0), CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2), Cursor = Cursors.Hand, Tag = pageNum,
                BorderBrush = pageNum == _curPage ? B("AccentBrush") : B("BorderSoftBrush"),
                ClipToBounds = true
            };
            var stack = new StackPanel();
            stack.Children.Add(new Image { Source = bmp, Height = 96, Stretch = Stretch.UniformToFill });
            stack.Children.Add(new Border { Background = B("SurfaceAltBrush"), Child = new TextBlock { Text = $"Page {pageNum}", FontSize = 10, Foreground = B("TextSecondaryBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) } });
            card.Child = stack;
            card.MouseLeftButtonUp += async (_, _) => { _curPage = pageNum; await RenderCurrentPage(); HighlightThumb(); };
            ThumbList.Items.Add(card);
        }
    }

    private void HighlightThumb()
    {
        foreach (var item in ThumbList.Items)
            if (item is Border b && b.Tag is int p)
                b.BorderBrush = p == _curPage ? B("AccentBrush") : B("BorderSoftBrush");
    }

    private async void PrevPage_Click(object sender, RoutedEventArgs e)
    { if (_curPage > 1) { _curPage--; await RenderCurrentPage(); HighlightThumb(); } }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    { if (_curPage < _pageCount) { _curPage++; await RenderCurrentPage(); HighlightThumb(); } }

    private async void ZoomIn_Click(object sender, RoutedEventArgs e) { _zoom = Math.Min(3, _zoom + 0.25); await RenderCurrentPage(); }
    private async void ZoomOut_Click(object sender, RoutedEventArgs e) { _zoom = Math.Max(0.5, _zoom - 0.25); await RenderCurrentPage(); }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select output folder" };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) _folderBox.Text = dlg.SelectedPath;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _pdfPath = null; _password = null; _pageCount = 0; _curPage = 1; _zoom = 1.0;
        PreviewImage.Source = null;
        ThumbList.Items.Clear();
        _mergeFiles.Clear(); _mergePages.Clear(); _mergePw.Clear();
        if (MergeList != null) MergeList.Items.Clear();
        ShowPreviewState();
        BuildInfo();
        UpdateSelection();
    }

    /// <summary>Shows the correct preview region for the active tool and its current data.</summary>
    private void ShowPreviewState()
    {
        if (_toolId == "merge_pdf")
        {
            bool has = _mergeFiles.Count > 0;
            EmptyState.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            MergeListState.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            LoadedState.Visibility = Visibility.Collapsed;
        }
        else
        {
            bool has = _pdfPath != null;
            EmptyState.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            LoadedState.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            MergeListState.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateSelection()
    {
        if (_toolId == "merge_pdf")
            TxtSelection.Text = _mergeFiles.Count == 0 ? "No files selected" : $"{_mergeFiles.Count} files selected";
        else
            TxtSelection.Text = _pdfPath == null ? "No file selected" : "1 file selected";
    }

    // =====================================================================
    //  Process
    // =====================================================================
    private async void Process_Click(object sender, RoutedEventArgs e)
    {
        if (_toolId == "merge_pdf") { await ProcessMerge(); return; }

        if (_pdfPath == null)
        {
            ConfirmDialog.Alert(this, "No File Selected", "Add a PDF to get started.");
            return;
        }
        if (_toolId == "compress_pdf") await ProcessCompress();
        else if (_toolId == "pdf_to_images") await ProcessPdfToImages();
        else await ProcessSplit();
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
            MoveToNextTool();
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
            MoveToNextTool();
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
            MoveToNextTool();
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

        string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(_pdfPath!) + "_compressed.pdf");
        long original = new FileInfo(_pdfPath!).Length;

        BtnProcess.IsEnabled = false;
        TxtProcess.Text = "Compressing…";
        ShowBusy("Compressing PDF…", "Optimizing pages and images");
        try
        {
            var compressTask = FileToolsService.CompressPdfAsync(_pdfPath!, outPath, _quality, null, _password);
            await System.Threading.Tasks.Task.WhenAll(compressTask, System.Threading.Tasks.Task.Delay(650));
            long newSize = await compressTask;
            HideBusy();
            double pct = original > 0 ? (1 - (double)newSize / original) * 100 : 0;
            string change = pct >= 1 ? $"{pct:0}% smaller" : "already well optimized";
            ConfirmDialog.Alert(this, "Compression Complete",
                $"{FileToolsService.FormatFileSize(original)}  →  {FileToolsService.FormatFileSize(newSize)}   ({change})\n{outPath}",
                ConfirmDialog.AlertKind.Success, "Done");
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outPath}\""); } catch { }
            MoveToNextTool();
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
