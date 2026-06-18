using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Llamashot.Core;
using Llamashot.Models;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Llamashot.Views;

/// <summary>
/// Unified interactive PDF editor — fill, sign, add text, checks, dates, times,
/// initials, signatures and images, with a contextual Properties panel.
/// Replaces the former Fill &amp; Sign / Edit split. Places <see cref="FillElement"/>
/// annotations over rendered pages and exports via FillSignExporter.
/// </summary>
public class PdfMarkupWindow : Window
{
    private const int DisplayDpi = 120;

    private string? _pdfPath;
    private string? _pdfPw;
    private int _pageCount;
    private int _curPage;             // 0-based
    private double _pageWpt, _pageHpt;
    private double _zoom = 1.0;

    private readonly List<FillElement> _elements = new();
    private readonly Dictionary<FillElement, Border> _chips = new();
    private FillElement? _selected;
    private string _tool = "select";

    private Image _pageImg = null!;
    private Canvas _overlay = null!;
    private Border _pageHost = null!;
    private TextBlock _txtPager = null!;
    private TextBlock _txtItems = null!;
    private TextBlock _zoomLabel = null!;
    private Button _btnSave = null!;
    private Button _btnUndo = null!;
    private Button _btnRedo = null!;
    private readonly Stack<List<FillElement>> _undo = new();
    private readonly Stack<List<FillElement>> _redo = new();
    private List<FillElement>? _preEdit;   // snapshot taken when an element is selected (for property edits)
    private List<FillElement>? _editSnap;  // snapshot taken when text editing begins
    private string _editStartText = "";
    private List<FillElement>? _dragSnap;  // snapshot taken when a move-drag begins
    private Border _dropZone = null!;
    private string _dropBorderBrush = "BorderSoftBrush";
    private StackPanel _actionControls = null!;
    private StackPanel _pagerPanel = null!;
    private readonly Dictionary<string, Border> _toolButtons = new();

    // panels / collapse
    private Border _propsPanel = null!;
    private Border _propsRail = null!;
    private StackPanel _propHost = null!;
    private TextBlock _propCtx = null!;
    private ColumnDefinition _colProps = null!;
    private ColumnDefinition _colRail = null!;
    private Border _railPanel = null!;
    private bool _propsCollapsed;
    private bool _focusMode;

    private TextBlock _subTitle = null!;

    // default style for the next placed item (editable in the panel while a tool is active)
    private readonly FillElement _def = new() { FontFamily = "Segoe UI", FontSize = 12, ColorHex = "#1C3FAA", Align = "left" };
    private string _redactColor = "#FFFFFF";
    private int _blurStrength = 14;

    // drag state (press-and-drag: move past threshold = move, click without move = edit)
    private bool _pendingDrag;
    private bool _dragMoved;
    private Point _dragStart;
    private double _origLeft, _origTop;
    private Border? _dragChip;

    // region (blur / cover) rubber-band drawing
    private bool _drawingRegion;
    private Point _regionStart;
    private System.Windows.Shapes.Rectangle? _regionPreview;

    // live cursor ghost — previews what the active tool will place, centered on the pointer
    private TextBlock? _ghost;

    // selection handles (resize + rotate) per element
    private readonly Dictionary<FillElement, List<UIElement>> _handles = new();
    // resize-grip drag state
    private FillElement? _resizeEl;
    private Border? _resizeChip;
    private FrameworkElement? _resizeContent;
    private Image? _resizeBlur;
    private Point _resizeStart;
    private double _resizeW0, _resizeH0;
    private List<FillElement>? _resizeSnap;
    // rotate-grip drag state
    private FillElement? _rotEl;
    private Border? _rotChip;
    private List<FillElement>? _rotSnap;

    // palettes
    private static readonly string[] Fonts = { "Segoe UI", "Arial", "Helvetica", "Times New Roman", "Calibri", "Verdana", "Georgia", "Courier New" };
    private static readonly (string name, string hex)[] Swatches =
    {
        ("Black","#222222"), ("Ink Blue","#1C3FAA"), ("Green","#137A3F"), ("Red","#C0392B"),
        ("Amber","#B8860B"), ("Purple","#6D28D9"), ("Teal","#0E7490"), ("Gray","#555555")
    };
    private static readonly (string id, string glyph)[] MarkStyles =
    {
        ("check","✓"), ("cross","✗"), ("radio","●"), ("box","■")
    };

    public PdfMarkupWindow(string mode = "editor")
    {
        Title = "Llamashot - PDF Editor";
        Width = 1320; Height = 880;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowState = WindowState.Maximized;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        BuildUi();
        PreviewKeyDown += OnKeyDown;
        PreviewMouseWheel += OnCanvasWheel;

        AllowDrop = true;
        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => HighlightDrop(false);
        Drop += OnDrop;
    }

    private static bool HasPdf(System.Windows.IDataObject d) =>
        d.GetDataPresent(System.Windows.DataFormats.FileDrop)
        && d.GetData(System.Windows.DataFormats.FileDrop) is string[] f
        && f.Any(p => string.Equals(Path.GetExtension(p), ".pdf", System.StringComparison.OrdinalIgnoreCase));

    private void OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        bool ok = HasPdf(e.Data);
        e.Effects = ok ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        if (_pdfPath == null) HighlightDrop(ok);
        e.Handled = true;
    }

    private async void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        HighlightDrop(false);
        if (!HasPdf(e.Data)) return;
        var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
        var pdf = files.First(p => string.Equals(Path.GetExtension(p), ".pdf", System.StringComparison.OrdinalIgnoreCase));
        await LoadPdfFile(pdf);
    }

    private void HighlightDrop(bool on)
    {
        if (_dropZone == null) return;
        if (on) { _dropZone.BorderBrush = B("AccentBrush"); _dropZone.SetResourceReference(Border.BackgroundProperty, "SurfaceHoverBrush"); }
        else { _dropZone.SetResourceReference(Border.BorderBrushProperty, _dropBorderBrush); _dropZone.SetResourceReference(Border.BackgroundProperty, "HeaderBrush"); }
    }

    private static SolidColorBrush B(string key) => (SolidColorBrush)Application.Current.Resources[key];
    private static void Dyn(FrameworkElement el, DependencyProperty p, string key) => el.SetResourceReference(p, key);

    // =====================================================================
    //  UI scaffold
    // =====================================================================
    private void BuildUi()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(BuildTopBar());

        // body: rail | canvas | properties (+ collapsed props rail)
        var body = new Grid();
        _colRail = new ColumnDefinition { Width = new GridLength(218) };
        var colCanvas = new ColumnDefinition();
        _colProps = new ColumnDefinition { Width = new GridLength(286) };
        body.ColumnDefinitions.Add(_colRail);
        body.ColumnDefinitions.Add(colCanvas);
        body.ColumnDefinitions.Add(_colProps);

        _railPanel = BuildRail();
        Grid.SetColumn(_railPanel, 0);
        body.Children.Add(_railPanel);

        var canvas = BuildCanvas();
        Grid.SetColumn(canvas, 1);
        body.Children.Add(canvas);

        _propsPanel = BuildProps();
        Grid.SetColumn(_propsPanel, 2);
        body.Children.Add(_propsPanel);

        _propsRail = BuildPropsRail();
        Grid.SetColumn(_propsRail, 2);
        body.Children.Add(_propsRail);

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        root.Children.Add(BuildActionBar());

        Content = root;
        HighlightTool();
        RefreshProps();
    }

    private Border BuildTopBar()
    {
        var top = new Border { Padding = new Thickness(22, 14, 22, 14), BorderThickness = new Thickness(0, 0, 0, 1) };
        Dyn(top, Border.BorderBrushProperty, "BorderSoftBrush");
        var g = new Grid();

        var titleStack = new StackPanel();
        var t = new TextBlock { Text = "PDF Editor", FontSize = 22, FontWeight = FontWeights.Bold };
        Dyn(t, TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var sub = new TextBlock { Text = "Add text, checks, dates, signatures & more — then save", FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0) };
        Dyn(sub, TextBlock.ForegroundProperty, "TextMutedBrush");
        _subTitle = sub;
        titleStack.Children.Add(t); titleStack.Children.Add(sub);
        g.Children.Add(titleStack);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _btnUndo = IconButton("↶", "Undo (Ctrl+Z)"); _btnUndo.Click += (_, _) => Undo(); _btnUndo.Margin = new Thickness(0, 0, 6, 0);
        _btnRedo = IconButton("↷", "Redo (Ctrl+Y)"); _btnRedo.Click += (_, _) => Redo(); _btnRedo.Margin = new Thickness(0, 0, 12, 0);
        var focus = IconButton("⛶", "Focus mode (hide panels)"); focus.Click += (_, _) => ToggleFocus();
        var open = ChromeButton("\U0001F4C2  Open PDF", true); open.Margin = new Thickness(10, 0, 0, 0); open.Click += (_, _) => OpenPdf();
        actions.Children.Add(_btnUndo);
        actions.Children.Add(_btnRedo);
        actions.Children.Add(focus);
        actions.Children.Add(open);
        g.Children.Add(actions);
        UpdateUndoButtons();

        top.Child = g;
        Grid.SetRow(top, 0);
        return top;
    }

    // =====================================================================
    //  Left tool rail
    // =====================================================================
    private Border BuildRail()
    {
        var rail = new Border { BorderThickness = new Thickness(0, 0, 1, 0) };
        Dyn(rail, Border.BackgroundProperty, "HeaderBrush");
        Dyn(rail, Border.BorderBrushProperty, "BorderSoftBrush");

        var panel = new StackPanel { Margin = new Thickness(12, 16, 12, 16) };

        panel.Children.Add(RailLabel("SELECT", 2));
        panel.Children.Add(ToolButton("select", "↖", "Select & move"));

        panel.Children.Add(RailLabel("FIELDS"));
        panel.Children.Add(ToolButton("text", "\U0001F54A", "Text"));
        panel.Children.Add(ToolButton("check", "✓", "Check mark"));
        panel.Children.Add(ToolButton("cross", "✗", "Cross"));
        panel.Children.Add(ToolButton("radio", "◉", "Radio / dot"));

        panel.Children.Add(RailLabel("DATE & TIME"));
        panel.Children.Add(ToolButton("date", "\U0001F4C5", "Date"));
        panel.Children.Add(ToolButton("time", "\U0001F550", "Time"));

        panel.Children.Add(RailLabel("SIGN"));
        panel.Children.Add(ToolButton("signature", "✍", "Signature"));
        panel.Children.Add(ToolButton("initials", "\U0001F170", "Initials"));
        panel.Children.Add(ToolButton("image", "\U0001F5BC", "Image / stamp"));

        panel.Children.Add(RailLabel("HIDE / REDACT"));
        panel.Children.Add(ToolButton("redact", "▮", "Cover (color box)"));
        panel.Children.Add(ToolButton("blur", "\U0001F532", "Blur area"));

        panel.Children.Add(RailLabel("ASSIST"));
        var detect = ChromeButton("✨  Auto-detect fields", false);
        detect.Margin = new Thickness(0, 2, 0, 4); detect.HorizontalAlignment = HorizontalAlignment.Stretch;
        detect.Click += async (_, _) => await AutoDetect();
        panel.Children.Add(detect);
        var del = ChromeButton("\U0001F5D1  Delete selected", false);
        del.HorizontalAlignment = HorizontalAlignment.Stretch;
        del.Click += (_, _) => DeleteSelected();
        panel.Children.Add(del);

        rail.Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return rail;
    }

    private TextBlock RailLabel(string text, double top = 16) => new()
    {
        Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(6, top, 0, 7), Foreground = B("TextMutedBrush")
    };

    private Border ToolButton(string id, string glyph, string label)
    {
        var bd = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(11, 9, 11, 9), Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand, Tag = id };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = glyph, FontSize = 15, Width = 22, TextAlignment = TextAlignment.Center });
        row.Children.Add(new TextBlock { Text = label, FontSize = 13.5, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        bd.Child = row;
        bd.MouseLeftButtonUp += (_, _) => { _tool = id; Select(null); HighlightTool(); };
        _toolButtons[id] = bd;
        return bd;
    }

    private void HighlightTool()
    {
        foreach (var (id, bd) in _toolButtons)
        {
            bool on = id == _tool;
            bd.Background = on ? B("AccentBrush") : Brushes.Transparent;
            foreach (var tb in ((StackPanel)bd.Child).Children.OfType<TextBlock>())
            {
                tb.Foreground = on ? B("AccentTextBrush") : B("TextSecondaryBrush");
                tb.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
        if (_overlay != null)
            _overlay.Cursor = _tool == "select" ? Cursors.Arrow : Cursors.Cross;
        UpdateGhost(null); // hide until the pointer moves over the page again
    }

    private static bool IsGhostTool(string t) =>
        t is "text" or "initials" or "check" or "cross" or "radio" or "date" or "time";

    // =====================================================================
    //  Center canvas
    // =====================================================================
    private Grid BuildCanvas()
    {
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(28)
        };
        var pageStack = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };

        _pageHost = new Border { Background = Brushes.White, SnapsToDevicePixels = true, Visibility = Visibility.Collapsed };
        _pageHost.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 26, ShadowDepth = 0, Opacity = 0.28, Color = Colors.Black };

        _pageImg = new Image { Stretch = Stretch.None, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(_pageImg, BitmapScalingMode.HighQuality);
        _overlay = new Canvas { Background = Brushes.Transparent };
        _overlay.MouseLeftButtonDown += Overlay_MouseDown;
        _overlay.MouseMove += Overlay_MouseMove;
        _overlay.MouseLeftButtonUp += Overlay_MouseUp;
        _overlay.MouseLeave += (_, _) => UpdateGhost(null);

        var imgHost = new Grid();
        imgHost.Children.Add(_pageImg);
        imgHost.Children.Add(_overlay);
        _pageHost.Child = imgHost;
        pageStack.Children.Add(_pageHost);
        scroller.Content = pageStack;

        // drop zone overlays the canvas area so it stays centered regardless of scroll content
        var area = new Grid();
        area.Children.Add(scroller);
        _dropZone = BuildDropZone();
        area.Children.Add(_dropZone);
        return area;
    }

    private Border BuildDropZone()
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(2), Padding = new Thickness(54, 46, 54, 46),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand
        };
        Dyn(card, Border.BackgroundProperty, "HeaderBrush");
        _dropBorderBrush = "BorderSoftBrush";
        Dyn(card, Border.BorderBrushProperty, _dropBorderBrush);

        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var icon = new TextBlock { Text = "\U0001F4C4", FontSize = 52, HorizontalAlignment = HorizontalAlignment.Center };
        var t1 = new TextBlock { Text = "Drag & drop a PDF here", FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 4) };
        Dyn(t1, TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var t2 = new TextBlock { Text = "or click to browse for a file", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20) };
        Dyn(t2, TextBlock.ForegroundProperty, "TextMutedBrush");
        var open = ChromeButton("\U0001F4C2  Open PDF", true);
        open.HorizontalAlignment = HorizontalAlignment.Center; open.Padding = new Thickness(26, 11, 26, 11); open.FontWeight = FontWeights.SemiBold;
        open.Click += (_, _) => OpenPdf();
        sp.Children.Add(icon); sp.Children.Add(t1); sp.Children.Add(t2); sp.Children.Add(open);
        card.Child = sp;
        card.MouseLeftButtonUp += (_, _) => OpenPdf();
        return card;
    }

    // =====================================================================
    //  Right properties panel
    // =====================================================================
    private Border BuildProps()
    {
        var p = new Border { BorderThickness = new Thickness(1, 0, 0, 0) };
        Dyn(p, Border.BackgroundProperty, "HeaderBrush");
        Dyn(p, Border.BorderBrushProperty, "BorderSoftBrush");

        var outer = new DockPanel { Margin = new Thickness(0) };

        // header with collapse button
        var head = new Grid { Margin = new Thickness(16, 16, 12, 6) };
        var hs = new StackPanel();
        var h = new TextBlock { Text = "Properties", FontSize = 14, FontWeight = FontWeights.Bold };
        Dyn(h, TextBlock.ForegroundProperty, "TextPrimaryBrush");
        _propCtx = new TextBlock { Text = "Nothing selected", FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) };
        Dyn(_propCtx, TextBlock.ForegroundProperty, "TextMutedBrush");
        hs.Children.Add(h); hs.Children.Add(_propCtx);
        head.Children.Add(hs);
        var collapse = IconButton("⟩", "Collapse panel"); collapse.HorizontalAlignment = HorizontalAlignment.Right; collapse.VerticalAlignment = VerticalAlignment.Top;
        collapse.Click += (_, _) => SetPropsCollapsed(true);
        head.Children.Add(collapse);
        DockPanel.SetDock(head, Dock.Top);
        outer.Children.Add(head);

        _propHost = new StackPanel { Margin = new Thickness(16, 8, 16, 16) };
        outer.Children.Add(new ScrollViewer { Content = _propHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        p.Child = outer;
        return p;
    }

    private Border BuildPropsRail()
    {
        var rail = new Border { BorderThickness = new Thickness(1, 0, 0, 0), Visibility = Visibility.Collapsed, Cursor = Cursors.Hand };
        Dyn(rail, Border.BackgroundProperty, "HeaderBrush");
        Dyn(rail, Border.BorderBrushProperty, "BorderSoftBrush");
        var sp = new StackPanel { Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        var expand = IconButton("⟨", "Expand properties"); expand.Click += (_, _) => SetPropsCollapsed(false);
        sp.Children.Add(expand);
        var lbl = new TextBlock { Text = "Properties", FontSize = 11, Margin = new Thickness(0, 10, 0, 0), Foreground = B("TextMutedBrush") };
        lbl.LayoutTransform = new RotateTransform(90);
        sp.Children.Add(lbl);
        rail.Child = sp;
        return rail;
    }

    private void SetPropsCollapsed(bool on)
    {
        _propsCollapsed = on;
        _propsPanel.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        _propsRail.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _colProps.Width = on ? new GridLength(46) : new GridLength(286);
    }

    private void ToggleFocus()
    {
        _focusMode = !_focusMode;
        _railPanel.Visibility = _focusMode ? Visibility.Collapsed : Visibility.Visible;
        _colRail.Width = _focusMode ? new GridLength(0) : new GridLength(218);
        if (_focusMode) { _propsPanel.Visibility = Visibility.Collapsed; _propsRail.Visibility = Visibility.Collapsed; _colProps.Width = new GridLength(0); }
        else SetPropsCollapsed(_propsCollapsed);
    }

    private static readonly Dictionary<string, string> ToolNames = new()
    {
        ["text"] = "Text", ["check"] = "Check mark", ["cross"] = "Cross", ["radio"] = "Radio / dot",
        ["date"] = "Date", ["time"] = "Time", ["initials"] = "Initials", ["signature"] = "Signature",
        ["image"] = "Image / stamp", ["redact"] = "Cover", ["blur"] = "Blur"
    };

    // ---- properties: contextual rebuild ----
    private void RefreshProps()
    {
        _propHost.Children.Clear();

        // 1) An element is selected → edit it.
        if (_selected != null) { BuildElementProps(_selected); return; }

        // 2) A placement tool is active → edit the defaults for the next item.
        if (_tool != "select") { BuildToolDefaults(_tool); return; }

        // 3) Idle.
        _propCtx.Text = "Nothing selected";
        _propHost.Children.Add(Hint("Pick a tool on the left, then click the page to place it. Select any item to edit its style here."));
    }

    private void BuildToolDefaults(string tool)
    {
        _propCtx.Text = (ToolNames.TryGetValue(tool, out var n) ? n : "Tool") + " — defaults";

        if (tool is "signature" or "image")
        {
            _propHost.Children.Add(Hint(tool == "signature"
                ? "Click the page to draw or import your signature."
                : "Click the page, then choose an image to place."));
            return;
        }
        if (tool == "redact")
        {
            _propHost.Children.Add(SectionLabel("COVER COLOR"));
            _propHost.Children.Add(ColorSwatchesWhite(_redactColor, hex => _redactColor = hex));
            _propHost.Children.Add(Hint("Drag a rectangle on the page to cover an area with this color."));
            return;
        }
        if (tool == "blur")
        {
            _propHost.Children.Add(SectionLabel("BLUR STRENGTH"));
            _propHost.Children.Add(SizeStepper(_blurStrength, v => _blurStrength = System.Math.Clamp(v, 2, 60), 2, 60, 2));
            _propHost.Children.Add(Hint("Drag a rectangle on the page to blur whatever is underneath."));
            return;
        }

        bool isMark = tool is "check" or "cross" or "radio";
        if (!isMark)
        {
            _propHost.Children.Add(SectionLabel("FONT"));
            _propHost.Children.Add(FontCombo(_def, () => { }));
        }
        _propHost.Children.Add(SectionLabel("SIZE"));
        _propHost.Children.Add(SizeStepper((int)System.Math.Round(_def.FontSize), v => _def.FontSize = System.Math.Max(6, v), 6, 96, 1));
        if (!isMark)
        {
            _propHost.Children.Add(SectionLabel("STYLE"));
            _propHost.Children.Add(StyleRow(_def, () => { }));
            _propHost.Children.Add(SectionLabel("ALIGN"));
            _propHost.Children.Add(AlignRow(_def, () => { }));
        }
        _propHost.Children.Add(SectionLabel("COLOR"));
        _propHost.Children.Add(ColorSwatches(_def, () => { }));
        _propHost.Children.Add(Hint("Click the page to place. These settings apply to the next item."));
    }

    private void BuildElementProps(FillElement e)
    {
        bool isImage = e.Type is FillElementType.Signature or FillElementType.Stamp or FillElementType.Blur;
        bool isMark = e.Type == FillElementType.Check;
        bool isRedact = e.Type == FillElementType.Redact;
        _propCtx.Text = e.Type switch
        {
            FillElementType.Blur => "Blur area selected",
            FillElementType.Redact => "Cover box selected",
            FillElementType.Signature or FillElementType.Stamp => "Image selected",
            FillElementType.Check => "Mark selected",
            FillElementType.DateTime => "Date / time selected",
            _ => "Text field selected"
        };

        if (isRedact)
        {
            _propHost.Children.Add(SectionLabel("COVER COLOR"));
            _propHost.Children.Add(ColorSwatchesWhite(e.ColorHex, hex => { e.ColorHex = hex; EditCommit(e); }));
            _propHost.Children.Add(SectionLabel("ROTATION (°)"));
            _propHost.Children.Add(RotationStepper(e));
            _propHost.Children.Add(SectionLabel("OPACITY (%)"));
            _propHost.Children.Add(OpacityStepper(e));
            _propHost.Children.Add(Hint("Drag the box to move it, the corner grip to resize, the top dot to rotate."));
            return;
        }

        if (isImage)
        {
            if (e.Type != FillElementType.Blur)
            {
                _propHost.Children.Add(SectionLabel("IMAGE"));
                var replace = ChromeButton("\U0001F504  Replace image", false);
                replace.HorizontalAlignment = HorizontalAlignment.Stretch; replace.Margin = new Thickness(0, 0, 0, 12);
                replace.Click += (_, _) => ReplaceImage(e);
                _propHost.Children.Add(replace);
            }
            _propHost.Children.Add(SectionLabel("WIDTH"));
            _propHost.Children.Add(SizeStepper((int)e.Width, v =>
            {
                double ratio = e.Height / System.Math.Max(1, e.Width);
                e.Width = System.Math.Max(20, v);
                if (e.Type != FillElementType.Blur) e.Height = e.Width * ratio; // keep aspect for images
                EditCommit(e);
            }, 20, 600, 10));
            if (e.Type == FillElementType.Blur)
            {
                _propHost.Children.Add(SectionLabel("HEIGHT"));
                _propHost.Children.Add(SizeStepper((int)e.Height, v => { e.Height = System.Math.Max(10, v); EditCommit(e); }, 10, 600, 10));
                _propHost.Children.Add(SectionLabel("BLUR STRENGTH"));
                _propHost.Children.Add(SizeStepper((int)e.FontSize, v => { e.FontSize = System.Math.Clamp(v, 2, 60); EditCommit(e); }, 2, 60, 2));
                _propHost.Children.Add(SectionLabel("OPACITY (%)"));
                _propHost.Children.Add(OpacityStepper(e));
            }
            _propHost.Children.Add(SectionLabel("ROTATION (°)"));
            _propHost.Children.Add(RotationStepper(e));
            _propHost.Children.Add(Hint("Drag to move, the corner grip to resize, the top dot to rotate (hold Shift to snap 15°)."));
            return;
        }

        if (isMark)
        {
            _propHost.Children.Add(SectionLabel("MARK STYLE"));
            _propHost.Children.Add(MarkStylePicker(e));
        }
        else
        {
            _propHost.Children.Add(SectionLabel("CONTENT"));
            _propHost.Children.Add(ContentBox(e));
            _propHost.Children.Add(SectionLabel("FONT"));
            _propHost.Children.Add(FontCombo(e, () => EditCommit(e)));
        }

        _propHost.Children.Add(SectionLabel("SIZE"));
        _propHost.Children.Add(SizeStepper((int)System.Math.Round(e.FontSize), v => { e.FontSize = System.Math.Max(6, v); EditCommit(e); }, 6, 96, 1));

        _propHost.Children.Add(SectionLabel("STYLE"));
        _propHost.Children.Add(StyleRow(e, () => EditCommit(e)));

        if (!isMark)
        {
            _propHost.Children.Add(SectionLabel("ALIGN"));
            _propHost.Children.Add(AlignRow(e, () => EditCommit(e)));
        }

        _propHost.Children.Add(SectionLabel("COLOR"));
        _propHost.Children.Add(ColorSwatches(e, () => EditCommit(e)));
    }

    private TextBlock SectionLabel(string t) => new()
    {
        Text = t, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 14, 0, 7),
        Foreground = B("TextMutedBrush")
    };

    private Border Hint(string t)
    {
        var b = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 8, 0, 0) };
        Dyn(b, Border.BackgroundProperty, "SurfaceBrush");
        Dyn(b, Border.BorderBrushProperty, "BorderSoftBrush"); b.BorderThickness = new Thickness(1);
        var tb = new TextBlock { Text = t, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, LineHeight = 17 };
        Dyn(tb, TextBlock.ForegroundProperty, "TextMutedBrush");
        b.Child = tb; return b;
    }

    private TextBox ContentBox(FillElement e)
    {
        var box = new TextBox
        {
            Text = e.Text, FontSize = 13, Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(1),
            AcceptsReturn = false
        };
        Dyn(box, System.Windows.Controls.Control.BackgroundProperty, "SurfaceBrush");
        Dyn(box, System.Windows.Controls.Control.ForegroundProperty, "TextPrimaryBrush");
        Dyn(box, System.Windows.Controls.Control.BorderBrushProperty, "BorderSoftBrush");
        box.GotFocus += (_, _) => { _editSnap = Snapshot(); _editStartText = box.Text; };
        box.LostFocus += (_, _) =>
        {
            if (_editSnap != null && box.Text != _editStartText) PushSnap(_editSnap);
            _editSnap = null;
        };
        box.TextChanged += (_, _) =>
        {
            e.Text = box.Text;
            if (_chips.TryGetValue(e, out var chip) && chip.Child is TextBox t) t.Text = box.Text;
        };
        return box;
    }

    private ComboBox FontCombo(FillElement e, System.Action apply)
    {
        var cb = new ComboBox { Style = (Style)Application.Current.Resources["ThemedCombo"] };
        foreach (var f in Fonts) cb.Items.Add(new ComboBoxItem { Content = f });
        cb.SelectedIndex = System.Math.Max(0, System.Array.IndexOf(Fonts, e.FontFamily));
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is ComboBoxItem ci) { e.FontFamily = (string)ci.Content; apply(); }
        };
        return cb;
    }

    private Grid SizeStepper(int value, System.Action<int> apply, int min, int max, int step)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox { Text = value.ToString(), FontSize = 13, Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center };
        Dyn(box, System.Windows.Controls.Control.BackgroundProperty, "SurfaceBrush");
        Dyn(box, System.Windows.Controls.Control.ForegroundProperty, "TextPrimaryBrush");
        Dyn(box, System.Windows.Controls.Control.BorderBrushProperty, "BorderSoftBrush");
        void Commit(int v) { v = System.Math.Clamp(v, min, max); box.Text = v.ToString(); apply(v); }
        box.LostFocus += (_, _) => { if (int.TryParse(box.Text, out var v)) Commit(v); else box.Text = value.ToString(); };
        box.KeyDown += (_, ev) => { if (ev.Key == Key.Enter && int.TryParse(box.Text, out var v)) Commit(v); };
        Grid.SetColumn(box, 0); g.Children.Add(box);

        var stepWrap = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        var minus = PillButton("−", 38); minus.Click += (_, _) => { if (int.TryParse(box.Text, out var v)) Commit(v - step); };
        var plus = PillButton("+", 38); plus.Margin = new Thickness(6, 0, 0, 0); plus.Click += (_, _) => { if (int.TryParse(box.Text, out var v)) Commit(v + step); };
        stepWrap.Children.Add(minus); stepWrap.Children.Add(plus);
        Grid.SetColumn(stepWrap, 1); g.Children.Add(stepWrap);
        return g;
    }

    private Grid RotationStepper(FillElement e) =>
        SizeStepper((int)System.Math.Round(e.Rotation), v => { e.Rotation = ((v % 360) + 360) % 360; EditCommit(e); }, 0, 360, 15);

    private Grid OpacityStepper(FillElement e) =>
        SizeStepper((int)System.Math.Round(e.Opacity), v => { e.Opacity = System.Math.Clamp(v, 1, 100); EditCommit(e); }, 1, 100, 5);

    private StackPanel StyleRow(FillElement e, System.Action apply)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var b = TogglePill("B", e.Bold, FontWeights.Bold, FontStyles.Normal, false);
        var i = TogglePill("I", e.Italic, FontWeights.Normal, FontStyles.Italic, false);
        var u = TogglePill("U", e.Underline, FontWeights.Normal, FontStyles.Normal, true);
        b.Margin = new Thickness(0, 0, 6, 0); i.Margin = new Thickness(0, 0, 6, 0);
        b.MouseLeftButtonUp += (_, _) => { e.Bold = !e.Bold; SetPillOn(b, e.Bold); apply(); };
        i.MouseLeftButtonUp += (_, _) => { e.Italic = !e.Italic; SetPillOn(i, e.Italic); apply(); };
        u.MouseLeftButtonUp += (_, _) => { e.Underline = !e.Underline; SetPillOn(u, e.Underline); apply(); };
        sp.Children.Add(b); sp.Children.Add(i); sp.Children.Add(u);
        return sp;
    }

    private StackPanel AlignRow(FillElement e, System.Action apply)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var items = new (string id, string glyph)[] { ("left", "←"), ("center", "↔"), ("right", "→") };
        var pills = new List<(string id, Border pill)>();
        foreach (var (id, glyph) in items)
        {
            var pill = Pill(glyph, 52); pill.Margin = new Thickness(0, 0, 6, 0);
            SetPillOn(pill, e.Align == id);
            pills.Add((id, pill));
            pill.MouseLeftButtonUp += (_, _) =>
            {
                e.Align = id;
                foreach (var (pid, pb) in pills) SetPillOn(pb, pid == id);
                apply();
            };
            sp.Children.Add(pill);
        }
        return sp;
    }

    private WrapPanel MarkStylePicker(FillElement e)
    {
        var wp = new WrapPanel();
        var pills = new List<(string g, Border pill)>();
        foreach (var (id, glyph) in MarkStyles)
        {
            var pill = Pill(glyph, 52); pill.Margin = new Thickness(0, 0, 6, 6);
            ((TextBlock)pill.Child).FontSize = 18;
            SetPillOn(pill, e.Text == glyph);
            pills.Add((glyph, pill));
            pill.MouseLeftButtonUp += (_, _) =>
            {
                e.Text = glyph;
                foreach (var (g, pb) in pills) SetPillOn(pb, g == glyph);
                EditCommit(e);
            };
            wp.Children.Add(pill);
        }
        return wp;
    }

    private WrapPanel ColorSwatches(FillElement e, System.Action apply) =>
        SwatchGrid(Swatches, e.ColorHex, hex => { e.ColorHex = hex; apply(); });

    // includes white first — for the Cover/redact tool
    private WrapPanel ColorSwatchesWhite(string current, System.Action<string> set)
    {
        var list = new (string name, string hex)[]
        {
            ("White","#FFFFFF"), ("Black","#222222"), ("Gray","#9AA0B0"),
            ("Ink Blue","#1C3FAA"), ("Red","#C0392B"), ("Green","#137A3F"),
            ("Amber","#B8860B"), ("Teal","#0E7490")
        };
        return SwatchGrid(list, current, set);
    }

    private WrapPanel SwatchGrid((string name, string hex)[] colors, string current, System.Action<string> set)
    {
        var wp = new WrapPanel();
        var sws = new List<(string hex, Border sw)>();
        foreach (var (name, hex) in colors)
        {
            var sw = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 8, 8),
                Background = HexBrush(hex), Cursor = Cursors.Hand, BorderThickness = new Thickness(2),
                BorderBrush = string.Equals(current, hex, System.StringComparison.OrdinalIgnoreCase) ? B("AccentBrush") : B("BorderSoftBrush"),
                ToolTip = name
            };
            sws.Add((hex, sw));
            sw.MouseLeftButtonUp += (_, _) =>
            {
                foreach (var (h, s) in sws) s.BorderBrush = h == hex ? B("AccentBrush") : B("BorderSoftBrush");
                set(hex);
            };
            wp.Children.Add(sw);
        }
        return wp;
    }

    // ---- small pill helpers ----
    private Border Pill(string glyph, double width)
    {
        var b = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Width = width, Height = 34 };
        Dyn(b, Border.BackgroundProperty, "SurfaceBrush");
        Dyn(b, Border.BorderBrushProperty, "BorderSoftBrush");
        var tb = new TextBlock { Text = glyph, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Dyn(tb, TextBlock.ForegroundProperty, "TextSecondaryBrush");
        b.Child = tb;
        return b;
    }

    private Border TogglePill(string glyph, bool on, FontWeight fw, System.Windows.FontStyle fs, bool underline)
    {
        var b = Pill(glyph, 52);
        var tb = (TextBlock)b.Child;
        tb.FontWeight = fw; tb.FontStyle = fs;
        if (underline) tb.TextDecorations = TextDecorations.Underline;
        SetPillOn(b, on);
        return b;
    }

    private void SetPillOn(Border pill, bool on)
    {
        pill.Background = on ? B("AccentBrush") : B("SurfaceBrush");
        pill.BorderBrush = on ? Brushes.Transparent : B("BorderSoftBrush");
        ((TextBlock)pill.Child).Foreground = on ? B("AccentTextBrush") : B("TextSecondaryBrush");
    }

    private Button PillButton(string text, double width)
    {
        var btn = new Button { Content = text, Width = width, Height = 34, Cursor = Cursors.Hand, FontSize = 15, BorderThickness = new Thickness(1), Template = RoundedTemplate() };
        Dyn(btn, BackgroundProperty, "SurfaceBrush"); Dyn(btn, ForegroundProperty, "TextSecondaryBrush"); Dyn(btn, BorderBrushProperty, "BorderSoftBrush");
        return btn;
    }

    // =====================================================================
    //  Bottom action bar
    // =====================================================================
    private Border BuildActionBar()
    {
        var bar = new Border { Padding = new Thickness(22, 12, 22, 12), BorderThickness = new Thickness(0, 1, 0, 0) };
        Dyn(bar, Border.BackgroundProperty, "HeaderBrush");
        Dyn(bar, Border.BorderBrushProperty, "BorderSoftBrush");
        var g = new Grid();

        _txtItems = new TextBlock { Text = "0 items placed", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
        Dyn(_txtItems, TextBlock.ForegroundProperty, "TextMutedBrush");
        g.Children.Add(_txtItems);

        var pager = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
        var prev = IconButton("‹", "Previous page"); prev.Click += (_, _) => Page(-1);
        var next = IconButton("›", "Next page"); next.Click += (_, _) => Page(+1);
        _txtPager = new TextBlock { Text = "– / –", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), FontSize = 13 };
        Dyn(_txtPager, TextBlock.ForegroundProperty, "TextSecondaryBrush");
        pager.Children.Add(prev); pager.Children.Add(_txtPager); pager.Children.Add(next);
        _pagerPanel = pager;
        g.Children.Add(pager);

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Visibility = Visibility.Collapsed };
        _actionControls = right;
        var zout = IconButton("−", "Zoom out"); zout.Click += (_, _) => Zoom(-0.1);
        _zoomLabel = new TextBlock { Text = "100%", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), FontSize = 12.5, MinWidth = 38, TextAlignment = TextAlignment.Center };
        Dyn(_zoomLabel, TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var zin = IconButton("+", "Zoom in"); zin.Click += (_, _) => Zoom(+0.1);
        var save = ChromeButton("Save PDF  →", true);
        save.Margin = new Thickness(16, 0, 0, 0); save.Padding = new Thickness(26, 10, 26, 10); save.FontWeight = FontWeights.Bold;
        save.Click += async (_, _) => await Save();
        _btnSave = save;
        right.Children.Add(zout); right.Children.Add(_zoomLabel); right.Children.Add(zin); right.Children.Add(save);
        g.Children.Add(right);

        bar.Child = g;
        Grid.SetRow(bar, 2);
        return bar;
    }

    private void Zoom(double delta)
    {
        _zoom = System.Math.Clamp(_zoom + delta, 0.5, 3.0);
        _pageHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        _zoomLabel.Text = $"{(int)System.Math.Round(_zoom * 100)}%";
    }

    // =====================================================================
    //  Buttons
    // =====================================================================
    private Button IconButton(string text, string tip)
    {
        var btn = new Button { Content = text, Width = 36, Height = 36, Cursor = Cursors.Hand, FontSize = 15, BorderThickness = new Thickness(1), Template = RoundedTemplate(), ToolTip = tip };
        Dyn(btn, BackgroundProperty, "SurfaceBrush"); Dyn(btn, ForegroundProperty, "TextSecondaryBrush"); Dyn(btn, BorderBrushProperty, "BorderSoftBrush");
        return btn;
    }

    private Button ChromeButton(string text, bool accent)
    {
        var btn = new Button { Content = text, Cursor = Cursors.Hand, FontSize = 13, BorderThickness = new Thickness(accent ? 0 : 1), Padding = new Thickness(16, 9, 16, 9), Template = RoundedTemplate() };
        if (accent) { Dyn(btn, BackgroundProperty, "AccentBrush"); Dyn(btn, ForegroundProperty, "AccentTextBrush"); }
        else { Dyn(btn, BackgroundProperty, "SurfaceBrush"); Dyn(btn, ForegroundProperty, "TextSecondaryBrush"); Dyn(btn, BorderBrushProperty, "BorderSoftBrush"); }
        return btn;
    }

    private static ControlTemplate RoundedTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        t.VisualTree = bd;
        return t;
    }

    // =====================================================================
    //  Load + render
    // =====================================================================
    private async void OpenPdf()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "PDF Files|*.pdf" };
        if (dlg.ShowDialog() != true) return;
        await LoadPdfFile(dlg.FileName);
    }

    private async System.Threading.Tasks.Task LoadPdfFile(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".pdf", System.StringComparison.OrdinalIgnoreCase))
        {
            ConfirmDialog.Alert(this, "Not a PDF", "Please choose a .pdf file.", ConfirmDialog.AlertKind.Info);
            return;
        }
        string? pw = await PasswordDialog.UnlockAsync(this, path);
        if (pw == null) return;

        try
        {
            _pdfPath = path;
            _pdfPw = string.IsNullOrEmpty(pw) ? null : pw;
            _pageCount = await FileToolsService.GetPdfPageCountAsync(_pdfPath, _pdfPw);
            _elements.Clear();
            _selected = null;
            ClearHistory();
            _curPage = 0;
            _zoom = 1.0; _pageHost.LayoutTransform = null; _zoomLabel.Text = "100%";
            _dropZone.Visibility = Visibility.Collapsed;
            _pageHost.Visibility = Visibility.Visible;
            _txtItems.Visibility = Visibility.Visible;
            _pagerPanel.Visibility = Visibility.Visible;
            _actionControls.Visibility = Visibility.Visible;
            await RenderPage();
            UpdateDocInfo();
            RefreshProps();
            UpdateItemCount();
        }
        catch (System.Exception ex)
        {
            ConfirmDialog.Alert(this, "Can't Open PDF", ex.Message, ConfirmDialog.AlertKind.Error);
        }
    }

    private async System.Threading.Tasks.Task RenderPage()
    {
        if (_pdfPath == null) return;
        var (wpt, hpt, _) = await FillSignRender.GetPageSizeAsync(_pdfPath, _curPage);
        _pageWpt = wpt; _pageHpt = hpt;
        using var bmp = await FillSignRender.RenderPageToBitmapAsync(_pdfPath, _curPage, DisplayDpi);
        _pageImg.Source = BitmapToSource(bmp);

        double wpx = FillSignGeometry.PointsToPixels(wpt, DisplayDpi);
        double hpx = FillSignGeometry.PointsToPixels(hpt, DisplayDpi);
        _pageImg.Width = wpx; _pageImg.Height = hpx;
        _overlay.Width = wpx; _overlay.Height = hpx;

        LayoutChips();
        _txtPager.Text = $"{_curPage + 1} / {_pageCount}";
    }

    private static BitmapSource BitmapToSource(System.Drawing.Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit(); bi.StreamSource = ms; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze();
        return bi;
    }

    private async void Page(int delta)
    {
        if (_pdfPath == null) return;
        int p = _curPage + delta;
        if (p < 0 || p >= _pageCount) return;
        _curPage = p;
        _selected = null;
        await RenderPage();
        RefreshProps();
    }

    private void UpdateItemCount() => _txtItems.Text = $"{_elements.Count} item{(_elements.Count == 1 ? "" : "s")} placed";

    private void UpdateDocInfo()
    {
        if (_pdfPath == null) return;
        string name = Path.GetFileName(_pdfPath);
        long bytes = 0; try { bytes = new FileInfo(_pdfPath).Length; } catch { }
        string size = bytes > 0 ? FormatBytes(bytes) : "";
        Title = $"Llamashot - PDF Editor — {name}";
        _subTitle.Text = $"{name}   ·   {_pageCount} page{(_pageCount == 1 ? "" : "s")}   ·   {(int)System.Math.Round(_pageWpt)}×{(int)System.Math.Round(_pageHpt)} pt"
                       + (size.Length > 0 ? $"   ·   {size}" : "");
    }

    private static string FormatBytes(long b) =>
        b >= 1048576 ? $"{b / 1048576.0:0.#} MB" : b >= 1024 ? $"{b / 1024.0:0.#} KB" : $"{b} B";

    // =====================================================================
    //  Chips
    // =====================================================================
    private void LayoutChips()
    {
        _overlay.Children.Clear();
        _chips.Clear();
        _handles.Clear();
        foreach (var e in _elements)
            if (e.Page == _curPage) AddChip(e);
    }

    private void RebuildChip(FillElement e)
    {
        if (_chips.TryGetValue(e, out var old)) { _overlay.Children.Remove(old); _chips.Remove(e); }
        AddChip(e);
        Select(e);
    }

    // =====================================================================
    //  Undo / Redo  (snapshot the element list before each mutation)
    // =====================================================================
    private static FillElement Clone(FillElement e) => new()
    {
        Page = e.Page, Type = e.Type, X = e.X, Y = e.Y, Width = e.Width, Height = e.Height,
        Text = e.Text, FontFamily = e.FontFamily, FontSize = e.FontSize, Bold = e.Bold,
        Italic = e.Italic, Underline = e.Underline, Align = e.Align, ColorHex = e.ColorHex, ImagePath = e.ImagePath,
        Rotation = e.Rotation, Opacity = e.Opacity
    };

    private List<FillElement> Snapshot() => _elements.Select(Clone).ToList();

    private void PushUndo() { _undo.Push(Snapshot()); _redo.Clear(); UpdateUndoButtons(); }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(Snapshot());
        RestoreFrom(_undo.Pop());
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(Snapshot());
        RestoreFrom(_redo.Pop());
    }

    private void RestoreFrom(List<FillElement> snap)
    {
        _elements.Clear();
        _elements.AddRange(snap);
        _selected = null;
        LayoutChips();
        UpdateItemCount();
        RefreshProps();
        UpdateUndoButtons();
    }

    private void UpdateUndoButtons()
    {
        if (_btnUndo == null) return;
        _btnUndo.IsEnabled = _undo.Count > 0;
        _btnRedo.IsEnabled = _redo.Count > 0;
        _btnUndo.Opacity = _undo.Count > 0 ? 1 : 0.4;
        _btnRedo.Opacity = _redo.Count > 0 ? 1 : 0.4;
    }

    private void ClearHistory() { _undo.Clear(); _redo.Clear(); _preEdit = _editSnap = _dragSnap = null; UpdateUndoButtons(); }

    private void PushSnap(List<FillElement>? snap)
    {
        if (snap == null) return;
        _undo.Push(snap); _redo.Clear(); UpdateUndoButtons();
    }

    /// <summary>Commit a property change on a selected element: record the pre-edit snapshot, then redraw.</summary>
    private void EditCommit(FillElement e)
    {
        PushSnap(_preEdit); _preEdit = null;
        RebuildChip(e); // re-selects e, which refreshes _preEdit for the next edit
    }

    private void AddChip(FillElement e)
    {
        double wpx = FillSignGeometry.PointsToPixels(e.Width, DisplayDpi);
        double hpx = FillSignGeometry.PointsToPixels(e.Height, DisplayDpi);
        FrameworkElement content;
        TextBox? textBox = null;
        Image? blurImg = null;
        bool resizable = e.Type is FillElementType.Signature or FillElementType.Stamp
                                 or FillElementType.Blur or FillElementType.Redact;

        if (e.Type is FillElementType.Signature or FillElementType.Stamp)
        {
            content = new Image
            {
                Source = e.ImagePath != null && File.Exists(e.ImagePath) ? new BitmapImage(new System.Uri(e.ImagePath)) : null,
                Width = wpx, Height = hpx, Stretch = Stretch.Fill
            };
        }
        else if (e.Type == FillElementType.Redact)
        {
            content = new Border { Width = wpx, Height = hpx, Background = HexBrush(e.ColorHex) };
        }
        else if (e.Type == FillElementType.Blur)
        {
            var img = new Image { Width = wpx, Height = hpx, Stretch = Stretch.Fill, Source = CropPage(e) };
            img.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = System.Math.Max(2, e.FontSize) };
            content = img; blurImg = img;
        }
        else
        {
            var tb = new TextBox
            {
                Text = e.Text, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                Foreground = HexBrush(e.ColorHex), FontSize = FillSignGeometry.PointsToPixels(e.FontSize, DisplayDpi),
                FontWeight = e.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = e.Italic ? FontStyles.Italic : FontStyles.Normal,
                TextDecorations = e.Underline ? TextDecorations.Underline : null,
                TextAlignment = e.Align switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left },
                IsReadOnly = true, Padding = new Thickness(2, 0, 2, 0),
                MinWidth = 24, FontFamily = new System.Windows.Media.FontFamily(e.FontFamily),
                CaretBrush = HexBrush(e.ColorHex)
            };
            tb.TextChanged += (_, _) => e.Text = tb.Text;
            content = tb; textBox = tb;
        }

        var host = new Grid();
        host.Children.Add(content);

        var chip = new Border
        {
            Child = host, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent,
            CornerRadius = new CornerRadius(3), Cursor = Cursors.SizeAll, Tag = e, Background = Brushes.Transparent
        };
        chip.Opacity = System.Math.Clamp(e.Opacity / 100.0, 0.05, 1.0);
        chip.RenderTransformOrigin = new Point(0.5, 0.5);
        chip.RenderTransform = new RotateTransform(e.Rotation);
        Canvas.SetLeft(chip, FillSignGeometry.PointsToPixels(e.X, DisplayDpi));
        Canvas.SetTop(chip, FillSignGeometry.PointsToPixels(e.Y, DisplayDpi));

        chip.MouseLeftButtonDown += (s, ev) =>
        {
            // already editing this text → let the textbox place the caret
            if (textBox != null && !textBox.IsReadOnly) { Select(e); return; }
            Select(e);
            _dragChip = chip; _pendingDrag = true; _dragMoved = false;
            _dragStart = ev.GetPosition(_overlay);
            _origLeft = Canvas.GetLeft(chip); _origTop = Canvas.GetTop(chip);
            chip.CaptureMouse();
            ev.Handled = true;
        };
        chip.MouseMove += (s, ev) =>
        {
            if (!_pendingDrag || _dragChip != chip) return;
            var p = ev.GetPosition(_overlay);
            if (!_dragMoved && (System.Math.Abs(p.X - _dragStart.X) > 4 || System.Math.Abs(p.Y - _dragStart.Y) > 4))
            {
                _dragMoved = true; _dragSnap = Snapshot(); // snapshot before the move
                if (textBox != null) { textBox.IsReadOnly = true; } // a drag is a move, never an edit
            }
            if (_dragMoved) MoveChipTo(chip, p);
        };
        chip.MouseLeftButtonUp += (s, ev) =>
        {
            if (_dragChip != chip) return;
            chip.ReleaseMouseCapture();
            bool moved = _dragMoved;
            _pendingDrag = false; _dragMoved = false; _dragChip = null;
            if (moved)
            {
                PushSnap(_dragSnap); _dragSnap = null;
                e.X = FillSignGeometry.PixelsToPoints(Canvas.GetLeft(chip), DisplayDpi);
                e.Y = FillSignGeometry.PixelsToPoints(Canvas.GetTop(chip), DisplayDpi);
            }
            else if (textBox != null)
            {
                EnterEdit(textBox); // a click (no drag) on text → edit
            }
            ev.Handled = true;
        };

        if (textBox != null)
            textBox.LostFocus += (_, _) =>
            {
                textBox.IsReadOnly = true; textBox.Background = Brushes.Transparent;
                if (_editSnap != null && textBox.Text != _editStartText) PushSnap(_editSnap);
                _editSnap = null;
            };

        if (resizable)
        {
            var handles = new List<UIElement>();

            var grip = new Border
            {
                Width = 13, Height = 13, CornerRadius = new CornerRadius(2),
                Background = B("AccentBrush"), BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -7, -7), Cursor = Cursors.SizeNWSE, Visibility = Visibility.Collapsed
            };
            WireResizeGrip(grip, chip, e, content, blurImg);
            host.Children.Add(grip); handles.Add(grip);

            var rot = new Border
            {
                Width = 15, Height = 15, CornerRadius = new CornerRadius(8),
                Background = B("AccentBrush"), BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -30, 0, 0), Cursor = Cursors.Hand, Visibility = Visibility.Collapsed,
                ToolTip = "Drag to rotate"
            };
            WireRotateGrip(rot, chip, e);
            host.Children.Add(rot); handles.Add(rot);

            _handles[e] = handles;
        }

        _overlay.Children.Add(chip);
        _chips[e] = chip;
    }

    private void WireResizeGrip(Border grip, Border chip, FillElement e, FrameworkElement content, Image? blurImg)
    {
        grip.MouseLeftButtonDown += (s, ev) =>
        {
            _resizeEl = e; _resizeChip = chip; _resizeContent = content; _resizeBlur = blurImg;
            _resizeStart = ev.GetPosition(_overlay);
            _resizeW0 = e.Width; _resizeH0 = e.Height; _resizeSnap = Snapshot();
            grip.CaptureMouse(); ev.Handled = true;
        };
        grip.MouseMove += (s, ev) =>
        {
            if (_resizeEl != e) return;
            var p = ev.GetPosition(_overlay);
            double dxPx = p.X - _resizeStart.X, dyPx = p.Y - _resizeStart.Y;
            // project the screen delta onto the element's local (rotated) axes
            double th = e.Rotation * System.Math.PI / 180.0;
            double ldx = dxPx * System.Math.Cos(th) + dyPx * System.Math.Sin(th);
            double ldy = -dxPx * System.Math.Sin(th) + dyPx * System.Math.Cos(th);
            double newW = System.Math.Max(14, _resizeW0 + FillSignGeometry.PixelsToPoints(ldx, DisplayDpi));
            double newH;
            if (e.Type is FillElementType.Signature or FillElementType.Stamp)
            { double ratio = _resizeH0 / System.Math.Max(1, _resizeW0); newH = newW * ratio; }
            else newH = System.Math.Max(10, _resizeH0 + FillSignGeometry.PixelsToPoints(ldy, DisplayDpi));
            e.Width = newW; e.Height = newH;
            double wpx = FillSignGeometry.PointsToPixels(newW, DisplayDpi);
            double hpx = FillSignGeometry.PointsToPixels(newH, DisplayDpi);
            if (content is FrameworkElement fe) { fe.Width = wpx; fe.Height = hpx; }
            ev.Handled = true;
        };
        grip.MouseLeftButtonUp += (s, ev) =>
        {
            if (_resizeEl != e) return;
            grip.ReleaseMouseCapture();
            PushSnap(_resizeSnap); _resizeSnap = null; _resizeEl = null;
            if (blurImg != null) blurImg.Source = CropPage(e); // re-crop for an accurate preview
            ev.Handled = true;
        };
    }

    private void WireRotateGrip(Border rot, Border chip, FillElement e)
    {
        rot.MouseLeftButtonDown += (s, ev) =>
        {
            _rotEl = e; _rotChip = chip; _rotSnap = Snapshot();
            rot.CaptureMouse(); ev.Handled = true;
        };
        rot.MouseMove += (s, ev) =>
        {
            if (_rotEl != e) return;
            var p = ev.GetPosition(_overlay);
            double cx = Canvas.GetLeft(chip) + chip.ActualWidth / 2;
            double cy = Canvas.GetTop(chip) + chip.ActualHeight / 2;
            double ang = System.Math.Atan2(p.Y - cy, p.X - cx) * 180.0 / System.Math.PI + 90.0;
            ang = (ang % 360 + 360) % 360;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                ang = System.Math.Round(ang / 15.0) * 15.0; // Shift snaps to 15°
            e.Rotation = ang;
            ((RotateTransform)chip.RenderTransform).Angle = ang;
            ev.Handled = true;
        };
        rot.MouseLeftButtonUp += (s, ev) =>
        {
            if (_rotEl != e) return;
            rot.ReleaseMouseCapture();
            PushSnap(_rotSnap); _rotSnap = null; _rotEl = null;
            RefreshProps(); // reflect the new angle in the panel
            ev.Handled = true;
        };
    }

    /// <summary>Puts a text chip into editable mode with a reliable, deferred focus + caret.</summary>
    private void EnterEdit(TextBox tb)
    {
        _editSnap = Snapshot(); _editStartText = tb.Text;  // capture pre-edit state for undo
        tb.IsReadOnly = false;
        tb.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 124, 156, 255));
        UpdateGhost(null); // don't show the placement ghost while typing
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            tb.Focus(); Keyboard.Focus(tb);
            tb.CaretIndex = tb.Text.Length;            // caret at end → smooth typing
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Crops the current rendered page bitmap to an element's rect (for the live blur preview).</summary>
    private BitmapSource? CropPage(FillElement e)
    {
        if (_pageImg.Source is not BitmapSource src) return null;
        int x = (int)FillSignGeometry.PointsToPixels(e.X, DisplayDpi);
        int y = (int)FillSignGeometry.PointsToPixels(e.Y, DisplayDpi);
        int w = (int)FillSignGeometry.PointsToPixels(e.Width, DisplayDpi);
        int h = (int)FillSignGeometry.PointsToPixels(e.Height, DisplayDpi);
        x = System.Math.Clamp(x, 0, src.PixelWidth - 1);
        y = System.Math.Clamp(y, 0, src.PixelHeight - 1);
        w = System.Math.Clamp(w, 1, src.PixelWidth - x);
        h = System.Math.Clamp(h, 1, src.PixelHeight - y);
        try { return new CroppedBitmap(src, new Int32Rect(x, y, w, h)); } catch { return null; }
    }

    private void MoveChipTo(Border chip, Point p)
    {
        double nl = _origLeft + (p.X - _dragStart.X);
        double nt = _origTop + (p.Y - _dragStart.Y);
        nl = System.Math.Max(0, System.Math.Min(nl, _overlay.Width - 4));
        nt = System.Math.Max(0, System.Math.Min(nt, _overlay.Height - 4));
        Canvas.SetLeft(chip, nl); Canvas.SetTop(chip, nt);
    }

    private void Select(FillElement? e)
    {
        _selected = e;
        _preEdit = e != null ? Snapshot() : null;
        foreach (var (el, chip) in _chips)
            chip.BorderBrush = el == e ? B("AccentBrush") : Brushes.Transparent;
        foreach (var (el, list) in _handles)
            foreach (var h in list) h.Visibility = el == e ? Visibility.Visible : Visibility.Collapsed;
        RefreshProps();
    }

    private void DeleteSelected()
    {
        if (_selected == null) return;
        PushSnap(Snapshot());
        _elements.Remove(_selected);
        if (_chips.TryGetValue(_selected, out var chip)) _overlay.Children.Remove(chip);
        _chips.Remove(_selected);
        _selected = null;
        RefreshProps();
        UpdateItemCount();
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        bool editing = System.Windows.Input.Keyboard.FocusedElement is TextBox tb && !tb.IsReadOnly;
        if (e.Key == Key.Escape)
        {
            if (editing) Keyboard.ClearFocus();   // commit the text edit
            else Select(null);                     // deselect
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && editing) { Keyboard.ClearFocus(); e.Handled = true; return; } // Enter commits text
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Key == Key.Z && !editing) { Undo(); e.Handled = true; return; }
            if (e.Key == Key.Y && !editing) { Redo(); e.Handled = true; return; }
            if (e.Key == Key.S) { _ = Save(); e.Handled = true; return; }                 // Ctrl+S saves
            if (e.Key == Key.OemPlus || e.Key == Key.Add) { Zoom(+0.1); e.Handled = true; return; }
            if (e.Key == Key.OemMinus || e.Key == Key.Subtract) { Zoom(-0.1); e.Handled = true; return; }
        }
        if ((e.Key == Key.Delete || e.Key == Key.Back) && _selected != null && !editing)
        {
            DeleteSelected();
            e.Handled = true;
            return;
        }
        // arrow keys nudge the selected item (Shift = 10 pt steps)
        if (!editing && _selected != null &&
            e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            double step = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 10 : 1;
            double dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
            double dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
            NudgeSelected(dx, dy);
            e.Handled = true;
        }
    }

    private void NudgeSelected(double dxPt, double dyPt)
    {
        if (_selected == null) return;
        PushSnap(Snapshot());
        _selected.X = System.Math.Max(0, _selected.X + dxPt);
        _selected.Y = System.Math.Max(0, _selected.Y + dyPt);
        if (_chips.TryGetValue(_selected, out var chip))
        {
            Canvas.SetLeft(chip, FillSignGeometry.PointsToPixels(_selected.X, DisplayDpi));
            Canvas.SetTop(chip, FillSignGeometry.PointsToPixels(_selected.Y, DisplayDpi));
        }
    }

    private void OnCanvasWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return; // plain scroll = pan
        Zoom(e.Delta > 0 ? +0.1 : -0.1);
        e.Handled = true;
    }

    // =====================================================================
    //  Placement
    // =====================================================================
    private async void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // a click on the empty page while editing text commits that edit first…
        if (Keyboard.FocusedElement is TextBox etb && !etb.IsReadOnly)
        {
            Keyboard.ClearFocus();
            // …then, if a placement tool is active, fall through and place at this point too
            if (_tool == "select") { Select(null); return; }
        }

        if (_pdfPath == null || _tool == "select") { Select(null); return; }

        // region tools draw a rubber-band rectangle
        if (_tool is "redact" or "blur") { BeginRegion(e.GetPosition(_overlay)); return; }

        var p = e.GetPosition(_overlay);
        // click point is treated as the CENTER of the item to place
        double cx = FillSignGeometry.PixelsToPoints(p.X, DisplayDpi);
        double cy = FillSignGeometry.PixelsToPoints(p.Y, DisplayDpi);

        switch (_tool)
        {
            case "text": PlaceText(cx, cy, "", FillElementType.Text); break;
            case "initials": PlaceText(cx, cy, "", FillElementType.Text); break;
            case "check": PlaceMark(cx, cy, "✓"); break;
            case "cross": PlaceMark(cx, cy, "✗"); break;
            case "radio": PlaceMark(cx, cy, "●"); break;
            case "date": PlaceText(cx, cy, System.DateTime.Now.ToString("MMM d, yyyy"), FillElementType.DateTime); break;
            case "time": PlaceText(cx, cy, System.DateTime.Now.ToString("h:mm tt"), FillElementType.DateTime); break;
            case "signature": await PlaceSignature(cx, cy); break;
            case "image": PlaceImage(cx, cy); break;
        }
    }

    private void Overlay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var p = e.GetPosition(_overlay);
        if (_drawingRegion) { UpdateRegion(p); return; }
        UpdateGhost(p);
    }

    // =====================================================================
    //  Cursor ghost (live placement preview centered on the pointer)
    // =====================================================================
    private void UpdateGhost(Point? at)
    {
        bool editing = Keyboard.FocusedElement is TextBox t && !t.IsReadOnly;
        bool show = at != null && _pdfPath != null && !_pendingDrag && !_drawingRegion
                    && !editing && IsGhostTool(_tool);
        if (!show)
        {
            if (_ghost != null) _ghost.Visibility = Visibility.Collapsed;
            return;
        }
        if (_ghost == null)
        {
            _ghost = new TextBlock { IsHitTestVisible = false, Opacity = 0.5, TextAlignment = TextAlignment.Center };
            _overlay.Children.Add(_ghost);
        }
        else if (_ghost.Parent != _overlay) // overlay was rebuilt (page change) — re-attach
        {
            _overlay.Children.Add(_ghost);
        }
        ConfigureGhost();
        _ghost.Visibility = Visibility.Visible;
        System.Windows.Controls.Panel.SetZIndex(_ghost, 1000);
        _ghost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // free text anchors its left edge at the cursor (where typing starts);
        // marks / date / time center on the cursor
        bool leftAnchor = _tool is "text" or "initials";
        double left = leftAnchor ? at!.Value.X : at!.Value.X - _ghost.DesiredSize.Width / 2;
        Canvas.SetLeft(_ghost, left);
        Canvas.SetTop(_ghost, at.Value.Y - _ghost.DesiredSize.Height / 2);
    }

    private void ConfigureGhost()
    {
        string glyph; string fam; double sizePt = _def.FontSize;
        switch (_tool)
        {
            case "check": glyph = "✓"; fam = "Segoe UI Symbol"; sizePt = _def.FontSize + 4; break;
            case "cross": glyph = "✗"; fam = "Segoe UI Symbol"; sizePt = _def.FontSize + 4; break;
            case "radio": glyph = "●"; fam = "Segoe UI Symbol"; sizePt = _def.FontSize + 4; break;
            case "date":  glyph = System.DateTime.Now.ToString("MMM d, yyyy"); fam = _def.FontFamily; break;
            case "time":  glyph = System.DateTime.Now.ToString("h:mm tt"); fam = _def.FontFamily; break;
            default:      glyph = "Text"; fam = _def.FontFamily; break; // text / initials
        }
        _ghost!.Text = glyph;
        _ghost.FontFamily = new System.Windows.Media.FontFamily(fam);
        _ghost.FontSize = FillSignGeometry.PointsToPixels(sizePt, DisplayDpi);
        _ghost.Foreground = HexBrush(_def.ColorHex);
        _ghost.FontWeight = _def.Bold ? FontWeights.Bold : FontWeights.Normal;
        _ghost.FontStyle = _def.Italic ? FontStyles.Italic : FontStyles.Normal;
    }

    private void Overlay_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_drawingRegion) EndRegion(e.GetPosition(_overlay));
    }

    // ---- region (cover / blur) rubber-band ----
    private void BeginRegion(Point start)
    {
        _drawingRegion = true;
        _regionStart = start;
        _regionPreview = new System.Windows.Shapes.Rectangle
        {
            Stroke = B("AccentBrush"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(_tool == "redact" ? (byte)60 : (byte)40, 124, 156, 255))
        };
        Canvas.SetLeft(_regionPreview, start.X); Canvas.SetTop(_regionPreview, start.Y);
        _overlay.Children.Add(_regionPreview);
        _overlay.CaptureMouse();
    }

    private void UpdateRegion(Point cur)
    {
        if (_regionPreview == null) return;
        double l = System.Math.Min(_regionStart.X, cur.X), t = System.Math.Min(_regionStart.Y, cur.Y);
        Canvas.SetLeft(_regionPreview, l); Canvas.SetTop(_regionPreview, t);
        _regionPreview.Width = System.Math.Abs(cur.X - _regionStart.X);
        _regionPreview.Height = System.Math.Abs(cur.Y - _regionStart.Y);
    }

    private void EndRegion(Point end)
    {
        _drawingRegion = false;
        _overlay.ReleaseMouseCapture();
        if (_regionPreview != null) { _overlay.Children.Remove(_regionPreview); _regionPreview = null; }

        double lpx = System.Math.Min(_regionStart.X, end.X), tpx = System.Math.Min(_regionStart.Y, end.Y);
        double wpx = System.Math.Abs(end.X - _regionStart.X), hpx = System.Math.Abs(end.Y - _regionStart.Y);
        if (wpx < 6 || hpx < 6) { _tool = "select"; HighlightTool(); return; } // ignore tiny drags

        PushSnap(Snapshot());
        var el = new FillElement
        {
            Page = _curPage,
            Type = _tool == "redact" ? FillElementType.Redact : FillElementType.Blur,
            X = FillSignGeometry.PixelsToPoints(lpx, DisplayDpi),
            Y = FillSignGeometry.PixelsToPoints(tpx, DisplayDpi),
            Width = FillSignGeometry.PixelsToPoints(wpx, DisplayDpi),
            Height = FillSignGeometry.PixelsToPoints(hpx, DisplayDpi),
            ColorHex = _redactColor,
            FontSize = _blurStrength // blur radius stored here
        };
        _elements.Add(el);
        AddChip(el);
        Select(el);
        UpdateItemCount();
    }

    // cx, cy = desired CENTER of the element (in points)
    private void PlaceText(double cx, double cy, string text, FillElementType type)
    {
        PushSnap(Snapshot());
        double h = _def.FontSize * 1.5;
        double w; double x;
        if (string.IsNullOrEmpty(text))
        {
            w = 220; x = cx;                 // empty field: anchor caret at the cursor, grows right
        }
        else
        {
            w = MeasureWidthPt(text, _def) + 10; x = cx - w / 2;   // known content: centered
        }
        var el = new FillElement
        {
            Page = _curPage, Type = type, X = x, Y = cy - h / 2, Width = w, Height = h,
            Text = text, FontFamily = _def.FontFamily, FontSize = _def.FontSize,
            Bold = _def.Bold, Italic = _def.Italic, Underline = _def.Underline, Align = _def.Align, ColorHex = _def.ColorHex
        };
        _elements.Add(el);
        AddChip(el);
        Select(el);
        UpdateItemCount();
        if (_chips[el].Child is TextBox tb && string.IsNullOrEmpty(text)) EnterEdit(tb);
    }

    private void PlaceMark(double cx, double cy, string glyph)
    {
        PushSnap(Snapshot());
        double w = _def.FontSize + 12, h = _def.FontSize + 8;
        var el = new FillElement
        {
            Page = _curPage, Type = FillElementType.Check, X = cx - w / 2, Y = cy - h / 2, Width = w, Height = h,
            Text = glyph, FontSize = _def.FontSize + 4, FontFamily = "Segoe UI Symbol", ColorHex = _def.ColorHex
        };
        _elements.Add(el);
        AddChip(el);
        Select(el);
        UpdateItemCount();
    }

    /// <summary>Measures a string's rendered width in PDF points for the given style.</summary>
    private double MeasureWidthPt(string text, FillElement style)
    {
        if (string.IsNullOrEmpty(text)) return 60;
        var tf = new Typeface(new System.Windows.Media.FontFamily(style.FontFamily),
            style.Italic ? FontStyles.Italic : FontStyles.Normal,
            style.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        double emDip = style.FontSize * 96.0 / 72.0;   // points → device-independent pixels
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight, tf, emDip, Brushes.Black, 1.0);
        return ft.WidthIncludingTrailingWhitespace * 72.0 / 96.0; // DIP → points
    }

    private async System.Threading.Tasks.Task PlaceSignature(double xPt, double yPt)
    {
        var dlg = new SignatureDialog { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultPngPath == null) return;
        AddImageElement(dlg.ResultPngPath, xPt, yPt, FillElementType.Signature, 180);
        BackToSelect(); // don't re-open the signature window on the next click
    }

    private void PlaceImage(double xPt, double yPt)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dlg.ShowDialog() != true) return;
        AddImageElement(dlg.FileName, xPt, yPt, FillElementType.Stamp, 220);
        BackToSelect();
    }

    private void BackToSelect() { _tool = "select"; HighlightTool(); }

    private void ReplaceImage(FillElement e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dlg.ShowDialog() != true) return;
        PushSnap(Snapshot());
        e.ImagePath = dlg.FileName;
        try { var bi = new BitmapImage(new System.Uri(dlg.FileName)); if (bi.PixelWidth > 0) e.Height = e.Width * bi.PixelHeight / bi.PixelWidth; } catch { }
        RebuildChip(e);
    }

    // cx, cy = desired CENTER of the image (in points)
    private void AddImageElement(string imagePath, double cx, double cy, FillElementType type, double widthPt)
    {
        PushSnap(Snapshot());
        double ratio = 0.4;
        try { var bi = new BitmapImage(new System.Uri(imagePath)); if (bi.PixelWidth > 0) ratio = (double)bi.PixelHeight / bi.PixelWidth; } catch { }
        double hPt = widthPt * ratio;
        var el = new FillElement
        {
            Page = _curPage, Type = type, X = cx - widthPt / 2, Y = cy - hPt / 2,
            Width = widthPt, Height = hPt, ImagePath = imagePath
        };
        _elements.Add(el);
        AddChip(el);
        Select(el);
        UpdateItemCount();
    }

    // =====================================================================
    //  Auto-detect
    // =====================================================================
    private async System.Threading.Tasks.Task AutoDetect()
    {
        if (_pdfPath == null) { ConfirmDialog.Alert(this, "No PDF", "Open a PDF first."); return; }
        try
        {
            using var bmp = await FillSignRender.RenderPageToBitmapAsync(_pdfPath, _curPage, DisplayDpi);
            var regions = FillSignDetector.DetectRegions(bmp);
            if (regions.Count > 0) PushSnap(Snapshot());
            int added = 0;
            foreach (var r in regions)
            {
                double xPt = FillSignGeometry.PixelsToPoints(r.X + 3, DisplayDpi);
                double yPt = FillSignGeometry.PixelsToPoints(r.Y, DisplayDpi);
                double hPt = FillSignGeometry.PixelsToPoints(r.H, DisplayDpi);
                bool check = r.Kind == RegionKind.Checkbox;
                var el = new FillElement
                {
                    Page = _curPage, Type = check ? FillElementType.Check : FillElementType.Text,
                    X = xPt, Y = yPt, Width = FillSignGeometry.PixelsToPoints(r.W, DisplayDpi),
                    Height = System.Math.Max(12, hPt), Text = check ? "✓" : "",
                    FontFamily = check ? "Segoe UI Symbol" : "Segoe UI",
                    ColorHex = check ? "#137A3F" : "#000000",
                    FontSize = System.Math.Max(9, System.Math.Min(16, hPt * 0.6))
                };
                _elements.Add(el);
                added++;
            }
            LayoutChips();
            UpdateItemCount();
            ConfirmDialog.Alert(this, "Fields Detected",
                added == 0 ? "No fillable fields were found on this page." : $"Added {added} field marker(s). Click one and type, or drag to reposition.",
                added == 0 ? ConfirmDialog.AlertKind.Info : ConfirmDialog.AlertKind.Success);
        }
        catch (System.Exception ex)
        {
            ConfirmDialog.Alert(this, "Detection Failed", ex.Message, ConfirmDialog.AlertKind.Error);
        }
    }

    // =====================================================================
    //  Save
    // =====================================================================
    private async System.Threading.Tasks.Task Save()
    {
        if (_pdfPath == null) { ConfirmDialog.Alert(this, "No PDF", "Open a PDF first."); return; }
        if (_elements.Count == 0) { ConfirmDialog.Alert(this, "Nothing to Save", "Add at least one item before saving."); return; }

        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", "Edited");
        Directory.CreateDirectory(folder);
        string outPath = Path.Combine(folder, Path.GetFileNameWithoutExtension(_pdfPath) + "_edited.pdf");

        _btnSave.IsEnabled = false;
        var prevText = _btnSave.Content;
        _btnSave.Content = "Saving…";
        string? blurDir = null;
        try
        {
            var src = _pdfPath;
            var els = new List<FillElement>(_elements);
            blurDir = await RenderBlursAsync(els);
            await System.Threading.Tasks.Task.Run(() => FillSignExporter.Export(src, els, outPath));
            ConfirmDialog.Alert(this, "Saved", $"Saved your PDF to:\n{outPath}", ConfirmDialog.AlertKind.Success, "Done");
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outPath}\""); } catch { }
        }
        catch (System.Exception ex)
        {
            ConfirmDialog.Alert(this, "Save Failed", ex.Message, ConfirmDialog.AlertKind.Error);
        }
        finally
        {
            if (blurDir != null) try { Directory.Delete(blurDir, true); } catch { }
            _btnSave.IsEnabled = true;
            _btnSave.Content = prevText;
        }
    }

    /// <summary>Rasterizes each Blur element's page region, blurs it, and swaps the element to a Blur image for export.</summary>
    private async System.Threading.Tasks.Task<string?> RenderBlursAsync(List<FillElement> els)
    {
        var blurs = els.Where(e => e.Type == FillElementType.Blur).ToList();
        if (blurs.Count == 0) return null;

        const int dpi = 200;
        string dir = Path.Combine(Path.GetTempPath(), "llama_blur_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        // group by page so each page renders once
        foreach (var pageGroup in blurs.GroupBy(b => b.Page))
        {
            using var bmp = await FillSignRender.RenderPageToBitmapAsync(_pdfPath!, pageGroup.Key, dpi);
            int idx = 0;
            foreach (var e in pageGroup)
            {
                int x = (int)FillSignGeometry.PointsToPixels(e.X, dpi);
                int y = (int)FillSignGeometry.PointsToPixels(e.Y, dpi);
                int w = (int)FillSignGeometry.PointsToPixels(e.Width, dpi);
                int h = (int)FillSignGeometry.PointsToPixels(e.Height, dpi);
                x = System.Math.Clamp(x, 0, bmp.Width - 1); y = System.Math.Clamp(y, 0, bmp.Height - 1);
                w = System.Math.Clamp(w, 1, bmp.Width - x); h = System.Math.Clamp(h, 1, bmp.Height - y);

                using var crop = bmp.Clone(new System.Drawing.Rectangle(x, y, w, h), bmp.PixelFormat);
                int radius = System.Math.Max(2, (int)(e.FontSize * dpi / DisplayDpi)); // scale on-screen radius to export dpi
                using var blurred = BoxBlur(crop, radius);
                string f = Path.Combine(dir, $"blur_{pageGroup.Key}_{idx++}.png");
                if (e.Opacity < 100)
                    using (var faded = ApplyAlpha(blurred, e.Opacity / 100.0)) faded.Save(f, System.Drawing.Imaging.ImageFormat.Png);
                else
                    blurred.Save(f, System.Drawing.Imaging.ImageFormat.Png);
                e.ImagePath = f; // exporter draws it as an image at the same rect
            }
        }
        return dir;
    }

    /// <summary>Returns a copy of the bitmap with a uniform alpha multiplier applied.</summary>
    private static System.Drawing.Bitmap ApplyAlpha(System.Drawing.Bitmap src, double alpha)
    {
        var outp = new System.Drawing.Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(outp);
        var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = (float)System.Math.Clamp(alpha, 0, 1) };
        using var ia = new System.Drawing.Imaging.ImageAttributes();
        ia.SetColorMatrix(cm);
        g.DrawImage(src, new System.Drawing.Rectangle(0, 0, src.Width, src.Height),
            0, 0, src.Width, src.Height, System.Drawing.GraphicsUnit.Pixel, ia);
        return outp;
    }

    /// <summary>Fast separable box blur (downscale-then-upscale) — good enough for redaction-style obscuring.</summary>
    private static System.Drawing.Bitmap BoxBlur(System.Drawing.Bitmap src, int radius)
    {
        int factor = System.Math.Clamp(radius / 2, 3, 24);
        int sw = System.Math.Max(1, src.Width / factor), sh = System.Math.Max(1, src.Height / factor);
        var small = new System.Drawing.Bitmap(sw, sh);
        using (var g = System.Drawing.Graphics.FromImage(small))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.DrawImage(src, 0, 0, sw, sh);
        }
        var outp = new System.Drawing.Bitmap(src.Width, src.Height);
        using (var g = System.Drawing.Graphics.FromImage(outp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(small, 0, 0, src.Width, src.Height);
        }
        small.Dispose();
        return outp;
    }

    private static SolidColorBrush HexBrush(string hex)
    {
        try { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; }
        catch { return Brushes.Black; }
    }
}
