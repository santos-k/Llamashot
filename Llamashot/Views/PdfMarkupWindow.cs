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
    private const double ChipPad = 2;   // hoverable / clickable ring around each placed element

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
    private ScrollViewer _scroller = null!;
    private bool _didFit;
    private TextBlock _txtPager = null!;
    private TextBlock _txtItems = null!;
    private TextBox _zoomBox = null!;
    private const double ZoomStep = 0.01;   // zoom in/out steps by 1%
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
    // draw-tool defaults
    private string _drawColor = "#C0392B";
    private int _drawWidth = 3;
    private string _highlightColor = "#FFE100";
    private int _highlightWidth = 16;

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

    // draw-tools (pen / highlight / line / arrow / rect / ellipse)
    private bool _drawing;
    private Point _drawStart;
    private readonly List<Point> _drawPts = new();      // pixel points during a freehand stroke
    private System.Windows.Shapes.Shape? _drawPreview;

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

    // palettes — 26 families incl. script/curly faces (all ship with Windows 10/11; resolver embeds them on export)
    private static readonly string[] Fonts =
    {
        "Segoe UI", "Arial", "Calibri", "Candara", "Corbel", "Tahoma", "Trebuchet MS", "Verdana",
        "Franklin Gothic Medium", "Bahnschrift", "Ebrima",
        "Times New Roman", "Georgia", "Constantia", "Palatino Linotype",
        "Consolas", "Courier New", "Lucida Console", "Impact",
        // script / curly
        "Segoe Script", "Segoe Print", "Ink Free", "Gabriola", "Lucida Handwriting", "Comic Sans MS"
    };
    // date / time format presets (≥6 each)
    private static readonly (string label, string fmt)[] DateFormats =
    {
        ("Jun 19, 2026", "MMM d, yyyy"),
        ("June 19, 2026", "MMMM d, yyyy"),
        ("19 Jun 2026", "d MMM yyyy"),
        ("06/19/2026", "MM/dd/yyyy"),
        ("19/06/2026", "dd/MM/yyyy"),
        ("2026-06-19", "yyyy-MM-dd"),
        ("Fri, Jun 19, 2026", "ddd, MMM d, yyyy"),
        ("Friday, June 19, 2026", "dddd, MMMM d, yyyy"),
    };
    private static readonly (string label, string fmt)[] TimeFormats =
    {
        ("2:30 PM", "h:mm tt"),
        ("2:30:45 PM", "h:mm:ss tt"),
        ("14:30", "HH:mm"),
        ("14:30:45", "HH:mm:ss"),
        ("Jun 19, 2026 2:30 PM", "MMM d, yyyy h:mm tt"),
        ("2026-06-19 14:30", "yyyy-MM-dd HH:mm"),
    };
    private string _dateFmt = "MMM d, yyyy";
    private string _timeFmt = "h:mm tt";
    private System.Drawing.Bitmap? _pageBmp;   // cached render of the current page (page image source / blur preview)
    // floating hover action bar (move / resize / delete) shown over the element under the pointer
    private Canvas _barLayer = null!;   // screen-space layer so the bar keeps constant size regardless of zoom
    private Border? _itemBar;
    private FillElement? _barTarget;
    private Border? _barChip;
    private System.Windows.Threading.DispatcherTimer? _barHideTimer;
    private bool _barMoving, _barResizing;
    private Point _barDragStart;
    private double _barOrigL, _barOrigT, _barW0, _barH0;
    private List<FillElement>? _barDragSnap;
    private FrameworkElement? _barResizeContent;
    private static readonly (string name, string hex)[] Swatches =
    {
        ("Black","#222222"), ("Ink Blue","#1C3FAA"), ("Green","#137A3F"), ("Red","#C0392B"),
        ("Amber","#B8860B"), ("Purple","#6D28D9"), ("Teal","#0E7490"), ("Gray","#555555")
    };
    private static readonly (string name, string hex)[] HighlightSwatches =
    {
        ("Yellow","#FFE100"), ("Green","#9BE15D"), ("Cyan","#73E8FF"), ("Pink","#FF9CC8"),
        ("Orange","#FFB347"), ("Lilac","#C9A7FF")
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
        Closing += OnClosing;
        Closed += (_, _) => { _pageBmp?.Dispose(); _pageBmp = null; };
    }

    private bool _dirty;        // unsaved placed/edited items
    private bool _forceClose;   // set once the user confirms closing

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose || !_dirty || _elements.Count == 0) return;
        e.Cancel = true;
        var choice = ConfirmDialog.PromptUnsaved(this, "Unsaved changes",
            "You have unsaved changes to this PDF. Do you want to save before closing?");
        if (choice == ConfirmDialog.CloseChoice.Cancel) return;             // stay open
        if (choice == ConfirmDialog.CloseChoice.Save && !await Save()) return; // save failed → stay open
        _forceClose = true;
        Close();
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
        panel.Children.Add(ToolButton("text", "T", "Text"));   // clear "text" glyph
        panel.Children.Add(ToolButton("check", "✓", "Check mark"));
        panel.Children.Add(ToolButton("cross", "✗", "Cross"));
        panel.Children.Add(ToolButton("radio", "◉", "Radio / dot"));

        panel.Children.Add(RailLabel("DRAW"));
        panel.Children.Add(ToolButton("pen", "✒", "Pen"));
        panel.Children.Add(ToolButton("highlight", "\U0001F58D", "Highlight"));
        panel.Children.Add(ToolButton("line", "╱", "Line"));
        panel.Children.Add(ToolButton("arrow", "↗", "Arrow"));
        panel.Children.Add(ToolButton("rect", "▭", "Rectangle"));
        panel.Children.Add(ToolButton("ellipse", "◯", "Ellipse"));

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
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // glyph
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // label
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // key hint
        var gl = new TextBlock { Text = glyph, FontSize = 15, Width = 22, TextAlignment = TextAlignment.Center };
        Grid.SetColumn(gl, 0); row.Children.Add(gl);
        var lt = new TextBlock { Text = label, FontSize = 13.5, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(lt, 1); row.Children.Add(lt);
        string key = KeyHintFor(id);
        if (!string.IsNullOrEmpty(key))
        {
            // a small "keycap" badge with the shortcut letter, right-aligned next to the name
            var cap = new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 0, 6, 1), MinWidth = 20, Tag = "keycap",
                Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = KeycapBrush(), BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = key, FontSize = 10.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, Foreground = B("TextMutedBrush") }
            };
            Grid.SetColumn(cap, 2); row.Children.Add(cap);
        }
        bd.Child = row;
        bd.ToolTip = string.IsNullOrEmpty(key) ? label : $"{label}  ({key})";
        bd.MouseLeftButtonUp += (_, _) => SetTool(id);
        _toolButtons[id] = bd;
        return bd;
    }

    /// <summary>Switches the active tool and refreshes the rail highlight / cursor / ghost.</summary>
    private void SetTool(string id) { _tool = id; Select(null); HighlightTool(); }

    /// <summary>The single-key shortcut shown in a tool's tooltip (kept in sync with <see cref="ToolForKey"/>).</summary>
    private static string KeyHintFor(string id)
    {
        var s = AppSettings.Instance;
        return id switch
        {
            "select" => s.ShortcutMove,
            "text" => s.ShortcutText,
            "pen" => s.ShortcutPen,
            "highlight" => s.ShortcutMarker,
            "line" => s.ShortcutLine,
            "arrow" => s.ShortcutArrow,
            "rect" => s.ShortcutRectangle,
            "ellipse" => s.ShortcutEllipse,
            "blur" => s.ShortcutBlur,
            "check" => s.ShortcutCheck,
            "cross" => s.ShortcutCross,
            "radio" => "O",
            "date" => "C",
            "time" => "W",
            "signature" => "S",
            "initials" => "I",
            "image" => "M",
            "redact" => "N",
            _ => ""
        };
    }

    private void HighlightTool()
    {
        foreach (var (id, bd) in _toolButtons)
        {
            bool on = id == _tool;
            bd.Background = on ? B("AccentBrush") : Brushes.Transparent;
            var panel = (System.Windows.Controls.Panel)bd.Child;
            // direct children are the glyph + label
            foreach (var tb in panel.Children.OfType<TextBlock>())
            {
                tb.Foreground = on ? B("AccentTextBrush") : B("TextSecondaryBrush");
                tb.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
            // recolor the keycap badge so the shortcut stays readable on the accent highlight
            foreach (var cap in panel.Children.OfType<Border>())
            {
                if (cap.Tag as string != "keycap") continue;
                cap.Background = on ? OnKeycapBrush() : KeycapBrush();
                cap.BorderBrush = on ? Brushes.Transparent : B("BorderSoftBrush");
                if (cap.Child is TextBlock ct) ct.Foreground = on ? B("AccentTextBrush") : B("TextMutedBrush");
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
        _scroller = scroller;
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
        // top, un-scaled layer for the hover action bar (positioned in screen space so it never shrinks with zoom)
        _barLayer = new Canvas { Background = null, IsHitTestVisible = true };
        area.Children.Add(_barLayer);
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
        ["image"] = "Image / stamp", ["redact"] = "Cover", ["blur"] = "Blur",
        ["pen"] = "Pen", ["highlight"] = "Highlight", ["line"] = "Line", ["arrow"] = "Arrow",
        ["rect"] = "Rectangle", ["ellipse"] = "Ellipse"
    };

    private static bool IsDrawTool(string t) => t is "pen" or "highlight" or "line" or "arrow" or "rect" or "ellipse";

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

        if (IsDrawTool(tool))
        {
            bool hi = tool == "highlight";
            _propHost.Children.Add(SectionLabel(hi ? "HIGHLIGHT WIDTH" : "STROKE WIDTH"));
            if (hi)
                _propHost.Children.Add(SizeStepper(_highlightWidth, v => _highlightWidth = System.Math.Clamp(v, 4, 60), 4, 60, 2));
            else
                _propHost.Children.Add(SizeStepper(_drawWidth, v => _drawWidth = System.Math.Clamp(v, 1, 40), 1, 40, 1));
            _propHost.Children.Add(SectionLabel("COLOR"));
            _propHost.Children.Add(SwatchGrid(hi ? HighlightSwatches : Swatches, hi ? _highlightColor : _drawColor,
                hex => { if (hi) _highlightColor = hex; else _drawColor = hex; }));
            _propHost.Children.Add(Hint(tool switch
            {
                "pen" => "Drag on the page to draw freehand.",
                "highlight" => "Drag across text to highlight it.",
                "line" => "Drag from one point to another (hold Shift to snap to 0/45/90°).",
                "arrow" => "Drag to draw an arrow (hold Shift to snap angle).",
                "rect" => "Drag to draw a rectangle (hold Shift for a square).",
                _ => "Drag to draw an ellipse (hold Shift for a circle)."
            }));
            return;
        }

        bool isMark = tool is "check" or "cross" or "radio";
        if (tool is "date" or "time")
        {
            _propHost.Children.Add(SectionLabel("FORMAT"));
            _propHost.Children.Add(FormatCombo(tool == "date"));
        }
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
            FillElementType.Draw => (e.Shape == "highlight" ? "Highlight" : "Drawing") + " selected",
            _ => "Text field selected"
        };

        if (e.Type == FillElementType.Draw)
        {
            bool hi = e.Shape == "highlight";
            _propHost.Children.Add(SectionLabel(hi ? "HIGHLIGHT WIDTH" : "STROKE WIDTH"));
            _propHost.Children.Add(SizeStepper((int)System.Math.Round(e.StrokeWidth),
                v => { e.StrokeWidth = System.Math.Clamp(v, hi ? 4 : 1, 60); EditCommit(e); }, hi ? 4 : 1, 60, hi ? 2 : 1));
            _propHost.Children.Add(SectionLabel("COLOR"));
            _propHost.Children.Add(SwatchGrid(hi ? HighlightSwatches : Swatches, e.ColorHex, hex => { e.ColorHex = hex; EditCommit(e); }));
            _propHost.Children.Add(SectionLabel("ROTATION (°)"));
            _propHost.Children.Add(RotationStepper(e));
            _propHost.Children.Add(SectionLabel("OPACITY (%)"));
            _propHost.Children.Add(OpacityStepper(e));
            _propHost.Children.Add(Hint("Drag to move it, or tweak the width, color and angle here."));
            return;
        }

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
            if (e.Type == FillElementType.DateTime)
            {
                _propHost.Children.Add(SectionLabel("FORMAT"));
                _propHost.Children.Add(ElementFormatCombo(e));
            }
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
            if (ChipTextBox(e) is { } t) t.Text = box.Text;
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

    /// <summary>Formats DateTime.Now with a pattern, falling back to a default if the pattern is invalid.</summary>
    private static string SafeFormat(string fmt)
    {
        try { return System.DateTime.Now.ToString(string.IsNullOrWhiteSpace(fmt) ? "MMM d, yyyy" : fmt); }
        catch { return System.DateTime.Now.ToString("MMM d, yyyy"); }
    }

    private TextBox ThemedTextBox(string text)
    {
        var box = new TextBox { Text = text, FontSize = 13, Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(1) };
        Dyn(box, System.Windows.Controls.Control.BackgroundProperty, "SurfaceBrush");
        Dyn(box, System.Windows.Controls.Control.ForegroundProperty, "TextPrimaryBrush");
        Dyn(box, System.Windows.Controls.Control.BorderBrushProperty, "BorderSoftBrush");
        return box;
    }

    // format picker for the Date/Time tool defaults — presets + a custom pattern with live preview
    private StackPanel FormatCombo(bool isDate)
    {
        var presets = isDate ? DateFormats : TimeFormats;
        var sp = new StackPanel();
        var cb = new ComboBox { Style = (Style)Application.Current.Resources["ThemedCombo"] };
        foreach (var (label, _) in presets) cb.Items.Add(new ComboBoxItem { Content = label });
        var customItem = new ComboBoxItem { Content = "Custom…", Tag = "custom" };
        cb.Items.Add(customItem);
        sp.Children.Add(cb);

        var customBox = ThemedTextBox(isDate ? _dateFmt : _timeFmt);
        customBox.Margin = new Thickness(0, 8, 0, 0);
        customBox.Visibility = Visibility.Collapsed;
        var preview = new TextBlock { FontSize = 11.5, Margin = new Thickness(2, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        Dyn(preview, TextBlock.ForegroundProperty, "TextMutedBrush");
        sp.Children.Add(customBox); sp.Children.Add(preview);

        void Set(string f) { if (isDate) _dateFmt = f; else _timeFmt = f; }
        void ShowCustom(bool on) { customBox.Visibility = preview.Visibility = on ? Visibility.Visible : Visibility.Collapsed; }
        void Recompute() { preview.Text = "Preview:  " + SafeFormat(customBox.Text) + "   (e.g. dd-MMM-yyyy · ddd HH:mm)"; }

        string cur = isDate ? _dateFmt : _timeFmt;
        int idx = System.Array.FindIndex(presets, p => p.fmt == cur);
        if (idx >= 0) cb.SelectedIndex = idx;
        else { cb.SelectedItem = customItem; ShowCustom(true); Recompute(); }

        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem == customItem) { ShowCustom(true); Set(customBox.Text); Recompute(); }
            else if (cb.SelectedIndex >= 0 && cb.SelectedIndex < presets.Length) { ShowCustom(false); Set(presets[cb.SelectedIndex].fmt); }
        };
        customBox.TextChanged += (_, _) => { Set(customBox.Text); Recompute(); };
        return sp;
    }

    // format picker for a selected Date/Time element — presets + Custom (free text or a pattern)
    private StackPanel ElementFormatCombo(FillElement e)
    {
        var presets = DateFormats.Concat(TimeFormats).ToArray();
        var now = System.DateTime.Now;
        var sp = new StackPanel();
        var cb = new ComboBox { Style = (Style)Application.Current.Resources["ThemedCombo"] };
        foreach (var (_, fmt) in presets) cb.Items.Add(new ComboBoxItem { Content = now.ToString(fmt) });
        var customItem = new ComboBoxItem { Content = "Custom…", Tag = "custom" };
        cb.Items.Add(customItem);
        sp.Children.Add(cb);

        var customBox = ThemedTextBox(e.Text);
        customBox.Margin = new Thickness(0, 8, 0, 0);
        customBox.Visibility = Visibility.Collapsed;
        var hint = new TextBlock { Text = "Type any custom date / time text", FontSize = 11, Margin = new Thickness(2, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        Dyn(hint, TextBlock.ForegroundProperty, "TextMutedBrush");
        sp.Children.Add(customBox); sp.Children.Add(hint);

        void ShowCustom(bool on) { customBox.Visibility = hint.Visibility = on ? Visibility.Visible : Visibility.Collapsed; }

        int idx = System.Array.FindIndex(presets, p => now.ToString(p.fmt) == e.Text);
        if (idx >= 0) cb.SelectedIndex = idx;
        else { cb.SelectedItem = customItem; ShowCustom(true); }

        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem == customItem) { ShowCustom(true); customBox.Focus(); }
            else if (cb.SelectedIndex >= 0 && cb.SelectedIndex < presets.Length)
            {
                ShowCustom(false);
                e.Text = now.ToString(presets[cb.SelectedIndex].fmt);
                EditCommit(e);
            }
        };
        // commit the typed literal value on Enter or focus-out (live-update the chip text as they type)
        customBox.TextChanged += (_, _) => { e.Text = customBox.Text; if (ChipTextBox(e) is { } cb2) cb2.Text = customBox.Text; };
        customBox.LostFocus += (_, _) => EditCommit(e);
        customBox.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) EditCommit(e); };
        return sp;
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
        string cur = current;
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
                cur = hex;
                foreach (var (h, s) in sws) s.BorderBrush = h == hex ? B("AccentBrush") : B("BorderSoftBrush");
                set(hex);
            };
            wp.Children.Add(sw);
        }

        // custom color picker — pick any color; covers every tool's COLOR section
        bool currentIsPreset = colors.Any(c => string.Equals(c.hex, current, System.StringComparison.OrdinalIgnoreCase));
        var custom = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 8, 8),
            Cursor = Cursors.Hand, BorderThickness = new Thickness(2), ToolTip = "Custom color…",
            Background = currentIsPreset ? RainbowBrush() : HexBrush(current),
            BorderBrush = currentIsPreset ? B("BorderSoftBrush") : B("AccentBrush")
        };
        custom.Child = new TextBlock
        {
            Text = "+", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.7, Color = System.Windows.Media.Colors.Black }
        };
        custom.MouseLeftButtonUp += (_, _) =>
        {
            var picked = PickColor(cur);
            if (picked == null) return;
            cur = picked;
            foreach (var (_, s) in sws) s.BorderBrush = B("BorderSoftBrush");
            custom.Background = HexBrush(picked); custom.BorderBrush = B("AccentBrush");
            set(picked);
        };
        wp.Children.Add(custom);
        return wp;
    }

    /// <summary>Opens the modern color picker seeded with the current color; returns a #RRGGBB hex or null if cancelled.</summary>
    private string? PickColor(string currentHex)
    {
        var dlg = new ColorPickerDialog(currentHex) { Owner = this };
        return dlg.ShowDialog() == true ? dlg.ResultHex : null;
    }

    private static LinearGradientBrush? _rainbow;
    private static LinearGradientBrush RainbowBrush()
    {
        if (_rainbow == null)
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            g.GradientStops.Add(new GradientStop(System.Windows.Media.Colors.Red, 0.0));
            g.GradientStops.Add(new GradientStop(System.Windows.Media.Colors.Orange, 0.2));
            g.GradientStops.Add(new GradientStop(System.Windows.Media.Colors.LimeGreen, 0.4));
            g.GradientStops.Add(new GradientStop(System.Windows.Media.Colors.DeepSkyBlue, 0.6));
            g.GradientStops.Add(new GradientStop(System.Windows.Media.Colors.Blue, 0.8));
            g.GradientStops.Add(new GradientStop(System.Windows.Media.Colors.Magenta, 1.0));
            g.Freeze();
            _rainbow = g;
        }
        return _rainbow;
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
        var zout = IconButton("−", "Zoom out (1%)"); zout.Click += (_, _) => Zoom(-ZoomStep);
        _zoomBox = MakeZoomBox();
        var zin = IconButton("+", "Zoom in (1%)"); zin.Click += (_, _) => Zoom(+ZoomStep);
        var fit = IconButton("⤢", "Fit width"); fit.Margin = new Thickness(8, 0, 0, 0); fit.Click += (_, _) => FitToWindow();
        var save = ChromeButton("Save PDF  →", true);
        save.Margin = new Thickness(16, 0, 0, 0); save.Padding = new Thickness(26, 10, 26, 10); save.FontWeight = FontWeights.Bold;
        save.Click += async (_, _) => await Save();
        _btnSave = save;
        right.Children.Add(zout); right.Children.Add(_zoomBox); right.Children.Add(zin); right.Children.Add(fit); right.Children.Add(save);
        g.Children.Add(right);

        bar.Child = g;
        Grid.SetRow(bar, 2);
        return bar;
    }

    private void Zoom(double delta) => SetZoom(_zoom + delta);

    private void SetZoom(double z)
    {
        _zoom = System.Math.Clamp(z, 0.08, 5.0);
        _pageHost.LayoutTransform = _zoom == 1.0 ? null : new ScaleTransform(_zoom, _zoom);
        // don't stomp the text the user is currently typing into the box
        if (_zoomBox != null && !_zoomBox.IsKeyboardFocused)
            _zoomBox.Text = $"{(int)System.Math.Round(_zoom * 100)}%";
    }

    /// <summary>Editable zoom % field — type a value and press Enter (or click away) to apply.</summary>
    private TextBox MakeZoomBox()
    {
        var tb = new TextBox
        {
            Text = "100%", Width = 54, FontSize = 12.5, TextAlignment = TextAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0), Padding = new Thickness(2, 3, 2, 3), BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, ToolTip = "Type a zoom % and press Enter"
        };
        Dyn(tb, TextBox.ForegroundProperty, "TextSecondaryBrush");
        Dyn(tb, TextBox.BorderBrushProperty, "BorderSoftBrush");
        Dyn(tb, TextBox.CaretBrushProperty, "TextSecondaryBrush");
        tb.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) { ApplyZoomFromBox(); Keyboard.ClearFocus(); ev.Handled = true; } };
        tb.LostFocus += (_, _) => ApplyZoomFromBox();
        tb.GotKeyboardFocus += (_, _) => tb.SelectAll();
        return tb;
    }

    private void ApplyZoomFromBox()
    {
        var digits = new string(_zoomBox.Text.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out int pct) && pct > 0) SetZoom(pct / 100.0);
        else SetZoom(_zoom);   // unparseable → restore the current value
    }

    /// <summary>Scales the page to fill the canvas width (scrolling vertically) — a document-reader default.</summary>
    private void FitToWindow()
    {
        if (_pageImg?.Source == null) return;
        // reserve ~18px for the vertical scrollbar that appears once the page is taller than the viewport
        double vw = _scroller.ViewportWidth - _scroller.Padding.Left - _scroller.Padding.Right - 18;
        if (vw < 20) vw = _scroller.ActualWidth - 64;
        if (vw < 20 || _pageImg.Width < 1) return;
        SetZoom(vw / _pageImg.Width);   // fit-to-width
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
            _dirty = false;
            _curPage = 0;
            _zoom = 1.0; _pageHost.LayoutTransform = null; _zoomBox.Text = "100%";
            _didFit = false;
            _dropZone.Visibility = Visibility.Collapsed;
            _pageHost.Visibility = Visibility.Visible;
            _txtItems.Visibility = Visibility.Visible;
            _pagerPanel.Visibility = Visibility.Visible;
            _actionControls.Visibility = Visibility.Visible;
            await RenderPage();
            ApplyPageScaleDefaults();   // size defaults to the page so elements aren't tiny on huge scanned pages
            UpdateDocInfo();
            RefreshProps();
            UpdateItemCount();
            // default to a fit-to-window view once layout has settled (huge scanned pages otherwise open way over 100%)
            Dispatcher.BeginInvoke(new System.Action(() => { if (!_didFit) { _didFit = true; FitToWindow(); } }),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch (System.Exception ex)
        {
            ConfirmDialog.Alert(this, "Can't Open PDF", ex.Message, ConfirmDialog.AlertKind.Error);
        }
    }

    private double _pageScale = 1.0;

    /// <summary>Scales the default element sizes to the page. Scanned PDFs whose MediaBox equals the scan's
    /// pixel size (e.g. 4588×6493 pt) make a 12pt font microscopic; scaling keeps placed items readable.</summary>
    private void ApplyPageScaleDefaults()
    {
        _pageScale = System.Math.Clamp(_pageHpt / 792.0, 1.0, 40.0);   // 792pt ≈ a normal Letter page
        _def.FontSize = System.Math.Clamp(System.Math.Round(12 * _pageScale), 6, 480);
        _drawWidth = (int)System.Math.Clamp(System.Math.Round(3 * _pageScale), 1, 120);
        _highlightWidth = (int)System.Math.Clamp(System.Math.Round(16 * _pageScale), 2, 400);
        _blurStrength = (int)System.Math.Clamp(System.Math.Round(14 * _pageScale), 4, 200);
    }

    private async System.Threading.Tasks.Task RenderPage()
    {
        if (_pdfPath == null) return;
        var (wpt, hpt, _) = await FillSignRender.GetPageSizeAsync(_pdfPath, _curPage);
        _pageWpt = wpt; _pageHpt = hpt;
        _pageBmp?.Dispose();
        _pageBmp = await FillSignRender.RenderPageToBitmapAsync(_pdfPath, _curPage, DisplayDpi);
        _pageImg.Source = BitmapToSource(_pageBmp);   // kept alive for font detection

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
        Rotation = e.Rotation, Opacity = e.Opacity,
        Shape = e.Shape, StrokeWidth = e.StrokeWidth, Points = new List<(double, double)>(e.Points)
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
        _dirty = true;
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
        _undo.Push(snap); _redo.Clear(); _dirty = true; UpdateUndoButtons();
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
        else if (e.Type == FillElementType.Draw)
        {
            content = BuildDrawVisual(e, wpx, hpx);
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
                // read-only fields don't grab clicks (so a click reaches the chip → enters edit);
                // EnterEdit re-enables hit testing for caret placement while typing
                IsHitTestVisible = false,
                // small min so an empty field is just a caret box that grows with what you type; marks hug their glyph
                MinWidth = e.Type == FillElementType.Check ? 24 : 48,
                FontFamily = new System.Windows.Media.FontFamily(e.FontFamily),
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
            CornerRadius = new CornerRadius(3), Cursor = Cursors.SizeAll, Tag = e, Background = Brushes.Transparent,
            // a 2px pad makes the whole element (plus a 2px ring) hoverable / clickable, not just the glyphs
            Padding = new Thickness(ChipPad)
        };
        chip.Opacity = System.Math.Clamp(e.Opacity / 100.0, 0.05, 1.0);
        chip.RenderTransformOrigin = new Point(0.5, 0.5);
        chip.RenderTransform = new RotateTransform(e.Rotation);
        Canvas.SetLeft(chip, FillSignGeometry.PointsToPixels(e.X, DisplayDpi) - ChipPad);
        Canvas.SetTop(chip, FillSignGeometry.PointsToPixels(e.Y, DisplayDpi) - ChipPad);

        // hover outline + floating action bar (move / resize / delete) anywhere on the element
        chip.MouseEnter += (_, _) => { if (_selected != e) chip.BorderBrush = HoverBrush(); ShowItemBar(e, chip); };
        chip.MouseLeave += (_, _) => { if (_selected != e) chip.BorderBrush = Brushes.Transparent; ScheduleHideItemBar(); };

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
                if (textBox != null) { textBox.IsReadOnly = true; textBox.IsHitTestVisible = false; } // a drag is a move, never an edit
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
                e.X = FillSignGeometry.PixelsToPoints(Canvas.GetLeft(chip) + ChipPad, DisplayDpi);
                e.Y = FillSignGeometry.PixelsToPoints(Canvas.GetTop(chip) + ChipPad, DisplayDpi);
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
                textBox.IsReadOnly = true; textBox.IsHitTestVisible = false; textBox.Background = Brushes.Transparent;
                // never leave an empty text field behind — clicking away from a blank field removes it
                if (e.Type == FillElementType.Text && string.IsNullOrWhiteSpace(textBox.Text))
                {
                    DiscardElement(e);
                    _editSnap = null;
                    return;
                }
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

    /// <summary>The editable TextBox inside an element's chip (chips wrap their content in a host Grid).</summary>
    private TextBox? ChipTextBox(FillElement e) =>
        _chips.TryGetValue(e, out var chip) && chip.Child is Grid g
            ? g.Children.OfType<TextBox>().FirstOrDefault()
            : null;

    /// <summary>Topmost editable text/date field whose chip contains the overlay-space point, or null.</summary>
    private FillElement? TextChipAtPoint(Point p)
    {
        FillElement? found = null;
        foreach (var (el, chip) in _chips)
        {
            if (el.Type is not (FillElementType.Text or FillElementType.DateTime)) continue;
            double l = Canvas.GetLeft(chip), tp = Canvas.GetTop(chip);
            if (double.IsNaN(l)) l = 0;
            if (double.IsNaN(tp)) tp = 0;
            double w = chip.ActualWidth > 0 ? chip.ActualWidth : chip.DesiredSize.Width;
            double h = chip.ActualHeight > 0 ? chip.ActualHeight : chip.DesiredSize.Height;
            if (p.X >= l && p.X <= l + w && p.Y >= tp && p.Y <= tp + h)
                found = el;   // chips later in the dictionary draw on top → keep the last match
        }
        return found;
    }

    /// <summary>Puts a text chip into editable mode with a reliable, deferred focus + caret.</summary>
    private void EnterEdit(TextBox tb)
    {
        _editSnap = Snapshot(); _editStartText = tb.Text;  // capture pre-edit state for undo
        tb.IsReadOnly = false;
        tb.IsHitTestVisible = true;   // allow caret placement / selection while editing
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

    /// <summary>Silently remove an abandoned element (e.g. an empty text field clicked away from).
    /// Pops the placement snapshot so the place-then-abandon pair leaves no undo step.</summary>
    private void DiscardElement(FillElement e)
    {
        _elements.Remove(e);
        if (_chips.TryGetValue(e, out var chip)) _overlay.Children.Remove(chip);
        _chips.Remove(e);
        _handles.Remove(e);
        if (_selected == e) _selected = null;
        if (_undo.Count > 0) _undo.Pop();   // drop the snapshot PlaceText pushed before adding this field
        UpdateUndoButtons();
        UpdateItemCount();
        RefreshProps();
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
            if (e.Key == Key.OemPlus || e.Key == Key.Add) { Zoom(+ZoomStep); e.Handled = true; return; }
            if (e.Key == Key.OemMinus || e.Key == Key.Subtract) { Zoom(-ZoomStep); e.Handled = true; return; }
        }
        if ((e.Key == Key.Delete || e.Key == Key.Back) && _selected != null && !editing)
        {
            DeleteSelected();
            e.Handled = true;
            return;
        }
        // single-key tool shortcuts (mirroring the screenshot annotation tools).
        // never fire while a text field is being edited, so typing letters isn't hijacked.
        if (!editing && Keyboard.Modifiers == ModifierKeys.None)
        {
            var t = ToolForKey(e);
            if (t != null) { SetTool(t); e.Handled = true; return; }
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
            Canvas.SetLeft(chip, FillSignGeometry.PointsToPixels(_selected.X, DisplayDpi) - ChipPad);
            Canvas.SetTop(chip, FillSignGeometry.PointsToPixels(_selected.Y, DisplayDpi) - ChipPad);
        }
    }

    /// <summary>Maps a plain key press to a tool id. Screenshot-tool actions honor the user's
    /// configured shortcuts; PDF-only tools use fixed mnemonic keys. Returns null if no match.</summary>
    private static string? ToolForKey(KeyEventArgs e)
    {
        var s = AppSettings.Instance;
        if (ShortcutHelper.Matches(e, s.ShortcutMove)) return "select";
        if (ShortcutHelper.Matches(e, s.ShortcutText)) return "text";
        if (ShortcutHelper.Matches(e, s.ShortcutPen)) return "pen";
        if (ShortcutHelper.Matches(e, s.ShortcutMarker)) return "highlight";
        if (ShortcutHelper.Matches(e, s.ShortcutLine)) return "line";
        if (ShortcutHelper.Matches(e, s.ShortcutArrow)) return "arrow";
        if (ShortcutHelper.Matches(e, s.ShortcutRectangle)) return "rect";
        if (ShortcutHelper.Matches(e, s.ShortcutEllipse)) return "ellipse";
        if (ShortcutHelper.Matches(e, s.ShortcutBlur)) return "blur";
        if (ShortcutHelper.Matches(e, s.ShortcutCheck)) return "check";
        if (ShortcutHelper.Matches(e, s.ShortcutCross)) return "cross";
        return e.Key switch
        {
            Key.O => "radio",
            Key.C => "date",       // Calendar
            Key.W => "time",       // Watch / clock
            Key.S => "signature",  // Sign (plain S; Ctrl+S still saves)
            Key.I => "initials",
            Key.M => "image",      // iMage / staMp
            Key.N => "redact",     // cover box
            _ => null
        };
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

        // draw tools drag a stroke / shape
        if (IsDrawTool(_tool)) { BeginDraw(e.GetPosition(_overlay)); return; }

        var p = e.GetPosition(_overlay);

        // clicking an existing text/date field with a text-like tool edits it in place
        // instead of stacking a new field on top of it
        if (_tool is "text" or "initials" or "date" or "time")
        {
            var hit = TextChipAtPoint(p);
            if (hit != null)
            {
                Select(hit);
                if (ChipTextBox(hit) is { } htb) EnterEdit(htb);
                return;
            }
        }

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
            case "date": PlaceText(cx, cy, SafeFormat(_dateFmt), FillElementType.DateTime); break;
            case "time": PlaceText(cx, cy, SafeFormat(_timeFmt), FillElementType.DateTime); break;
            case "signature": await PlaceSignature(cx, cy); break;
            case "image": PlaceImage(cx, cy); break;
        }
    }

    private void Overlay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var p = e.GetPosition(_overlay);
        if (_drawingRegion) { UpdateRegion(p); return; }
        if (_drawing) { UpdateDraw(p); return; }
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
            case "date":  glyph = SafeFormat(_dateFmt); fam = _def.FontFamily; break;
            case "time":  glyph = SafeFormat(_timeFmt); fam = _def.FontFamily; break;
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
        else if (_drawing) EndDraw(e.GetPosition(_overlay));
    }

    // ---- draw tools (pen / highlight / line / arrow / rect / ellipse) ----
    private bool IsFreehand => _tool is "pen" or "highlight";
    private string DrawColor => _tool == "highlight" ? _highlightColor : _drawColor;
    private int DrawWidthPt => _tool == "highlight" ? _highlightWidth : _drawWidth;

    private void BeginDraw(Point start)
    {
        _drawing = true;
        _drawStart = start;
        _drawPts.Clear();
        _drawPts.Add(start);

        double wpx = FillSignGeometry.PointsToPixels(DrawWidthPt, DisplayDpi);
        var stroke = HexBrush(DrawColor);
        bool hi = _tool == "highlight";

        System.Windows.Shapes.Shape shape = _tool switch
        {
            "rect" => new System.Windows.Shapes.Rectangle(),
            "ellipse" => new System.Windows.Shapes.Ellipse(),
            "line" or "arrow" => new System.Windows.Shapes.Line { X1 = start.X, Y1 = start.Y, X2 = start.X, Y2 = start.Y },
            _ => new System.Windows.Shapes.Polyline { Points = new PointCollection { start } }
        };
        shape.Stroke = stroke;
        shape.StrokeThickness = wpx;
        shape.StrokeStartLineCap = PenLineCap.Round;
        shape.StrokeEndLineCap = PenLineCap.Round;
        shape.StrokeLineJoin = PenLineJoin.Round;
        shape.Opacity = hi ? 0.4 : 1.0;
        shape.IsHitTestVisible = false;
        if (shape is System.Windows.Shapes.Rectangle or System.Windows.Shapes.Ellipse)
        { Canvas.SetLeft(shape, start.X); Canvas.SetTop(shape, start.Y); }
        _drawPreview = shape;
        _overlay.Children.Add(shape);
        _overlay.CaptureMouse();
        UpdateGhost(null);
    }

    private void UpdateDraw(Point cur)
    {
        if (_drawPreview == null) return;
        bool snap = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        switch (_drawPreview)
        {
            case System.Windows.Shapes.Polyline pl:
                _drawPts.Add(cur); pl.Points.Add(cur); break;
            case System.Windows.Shapes.Line ln:
                var end = snap ? SnapLine(_drawStart, cur) : cur;
                ln.X2 = end.X; ln.Y2 = end.Y; break;
            case var sh: // rectangle / ellipse
                var (l, t, w, h) = RectFrom(_drawStart, cur, snap);
                Canvas.SetLeft(sh, l); Canvas.SetTop(sh, t); sh.Width = w; sh.Height = h; break;
        }
    }

    private static (double l, double t, double w, double h) RectFrom(Point start, Point cur, bool square)
    {
        double w = System.Math.Abs(cur.X - start.X), h = System.Math.Abs(cur.Y - start.Y);
        if (square) { double s = System.Math.Min(w, h); w = h = s; }
        double l = cur.X < start.X ? start.X - w : start.X;
        double t = cur.Y < start.Y ? start.Y - h : start.Y;
        return (l, t, w, h);
    }

    private FrameworkElement BuildDrawVisual(FillElement e, double wpx, double hpx)
    {
        var canvas = new Canvas { Width = System.Math.Max(1, wpx), Height = System.Math.Max(1, hpx), Background = Brushes.Transparent };
        double sw = System.Math.Max(0.5, FillSignGeometry.PointsToPixels(e.StrokeWidth, DisplayDpi));
        var stroke = HexBrush(e.ColorHex);
        double Px(double pt) => FillSignGeometry.PointsToPixels(pt, DisplayDpi);

        void Style(System.Windows.Shapes.Shape s)
        {
            s.Stroke = stroke; s.StrokeThickness = sw;
            s.StrokeStartLineCap = PenLineCap.Round; s.StrokeEndLineCap = PenLineCap.Round; s.StrokeLineJoin = PenLineJoin.Round;
            s.IsHitTestVisible = false;
        }

        switch (e.Shape)
        {
            case "rect":
                var r = new System.Windows.Shapes.Rectangle { Width = wpx, Height = hpx }; Style(r); canvas.Children.Add(r); break;
            case "ellipse":
                var el = new System.Windows.Shapes.Ellipse { Width = wpx, Height = hpx }; Style(el); canvas.Children.Add(el); break;
            case "line":
                if (e.Points.Count >= 2)
                { var ln = new System.Windows.Shapes.Line { X1 = Px(e.Points[0].X), Y1 = Px(e.Points[0].Y), X2 = Px(e.Points[1].X), Y2 = Px(e.Points[1].Y) }; Style(ln); canvas.Children.Add(ln); }
                break;
            case "arrow":
                if (e.Points.Count >= 2)
                {
                    double sx = Px(e.Points[0].X), sy = Px(e.Points[0].Y), tx = Px(e.Points[1].X), ty = Px(e.Points[1].Y);
                    var shaft = new System.Windows.Shapes.Line { X1 = sx, Y1 = sy, X2 = tx, Y2 = ty }; Style(shaft); canvas.Children.Add(shaft);
                    double dx = tx - sx, dy = ty - sy, len = System.Math.Sqrt(dx * dx + dy * dy);
                    if (len > 0.001)
                    {
                        dx /= len; dy /= len;
                        double head = System.Math.Max(Px(8), sw * 3.5);
                        const double a = 25 * System.Math.PI / 180.0; double cs = System.Math.Cos(a), sn = System.Math.Sin(a);
                        var h1 = new System.Windows.Shapes.Line { X1 = tx, Y1 = ty, X2 = tx - head * (dx * cs - dy * sn), Y2 = ty - head * (dx * sn + dy * cs) }; Style(h1); canvas.Children.Add(h1);
                        var h2 = new System.Windows.Shapes.Line { X1 = tx, Y1 = ty, X2 = tx - head * (dx * cs + dy * sn), Y2 = ty - head * (-dx * sn + dy * cs) }; Style(h2); canvas.Children.Add(h2);
                    }
                }
                break;
            default: // pen / highlight
                if (e.Points.Count >= 2)
                {
                    var pl = new System.Windows.Shapes.Polyline { Points = new PointCollection() }; Style(pl);
                    foreach (var p in e.Points) pl.Points.Add(new Point(Px(p.X), Px(p.Y)));
                    canvas.Children.Add(pl);
                }
                break;
        }
        return canvas;
    }

    private static Point SnapLine(Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double ang = System.Math.Atan2(dy, dx);
        double step = System.Math.PI / 4; // 45°
        double snapped = System.Math.Round(ang / step) * step;
        double len = System.Math.Sqrt(dx * dx + dy * dy);
        return new Point(a.X + len * System.Math.Cos(snapped), a.Y + len * System.Math.Sin(snapped));
    }

    private void EndDraw(Point end)
    {
        _drawing = false;
        _overlay.ReleaseMouseCapture();
        if (_drawPreview != null) { _overlay.Children.Remove(_drawPreview); _drawPreview = null; }

        bool hi = _tool == "highlight";
        bool freehand = IsFreehand;
        bool snap = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        // collect the path points (pixels), then compute the bounding box
        List<Point> px;
        if (freehand) px = new List<Point>(_drawPts);
        else if (_tool is "line" or "arrow") px = new List<Point> { _drawStart, snap ? SnapLine(_drawStart, end) : end };
        else // rect / ellipse — corners
        {
            var (l, t, w, h) = RectFrom(_drawStart, end, snap);
            px = new List<Point> { new(l, t), new(l + w, t + h) };
        }

        double minX = px.Min(p => p.X), minY = px.Min(p => p.Y);
        double maxX = px.Max(p => p.X), maxY = px.Max(p => p.Y);
        if (maxX - minX < 3 && maxY - minY < 3) return; // ignore taps

        var el = new FillElement
        {
            Page = _curPage, Type = FillElementType.Draw, Shape = _tool,
            X = FillSignGeometry.PixelsToPoints(minX, DisplayDpi),
            Y = FillSignGeometry.PixelsToPoints(minY, DisplayDpi),
            Width = FillSignGeometry.PixelsToPoints(maxX - minX, DisplayDpi),
            Height = FillSignGeometry.PixelsToPoints(maxY - minY, DisplayDpi),
            ColorHex = DrawColor,
            StrokeWidth = DrawWidthPt,
            Opacity = hi ? 40 : 100,
        };
        // store vertices relative to the bounding box, in points
        if (freehand || _tool is "line" or "arrow")
            foreach (var p in px)
                el.Points.Add((FillSignGeometry.PixelsToPoints(p.X - minX, DisplayDpi), FillSignGeometry.PixelsToPoints(p.Y - minY, DisplayDpi)));

        PushSnap(Snapshot());
        _elements.Add(el);
        AddChip(el);
        Select(el);
        UpdateItemCount();
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

        // effective style comes straight from the panel defaults (12pt unless the user changed it)
        double fontSize = _def.FontSize; string colorHex = _def.ColorHex;
        bool bold = _def.Bold, italic = _def.Italic;

        var probe = new FillElement { FontFamily = _def.FontFamily, FontSize = fontSize, Bold = bold, Italic = italic };
        double h = fontSize * 1.5;
        double w; double x;
        if (string.IsNullOrEmpty(text))
        {
            w = 90; x = cx;                  // empty field: small caret box at the cursor, grows as you type
        }
        else
        {
            w = MeasureWidthPt(text, probe) + 10; x = cx - w / 2;   // known content: centered
        }
        var el = new FillElement
        {
            Page = _curPage, Type = type, X = x, Y = cy - h / 2, Width = w, Height = h,
            Text = text, FontFamily = _def.FontFamily, FontSize = fontSize,
            Bold = bold, Italic = italic, Underline = _def.Underline, Align = _def.Align, ColorHex = colorHex
        };
        _elements.Add(el);
        AddChip(el);
        Select(el);
        UpdateItemCount();
        // start typing immediately for an empty text field (the chip wraps the TextBox in a host Grid)
        if (string.IsNullOrEmpty(text) && ChipTextBox(el) is { } tb) EnterEdit(tb);
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
    //  Save
    // =====================================================================
    private async System.Threading.Tasks.Task<bool> Save()
    {
        if (_pdfPath == null) { ConfirmDialog.Alert(this, "No PDF", "Open a PDF first."); return false; }
        if (_elements.Count == 0) { ConfirmDialog.Alert(this, "Nothing to Save", "Add at least one item before saving."); return false; }

        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", "Edited");
        Directory.CreateDirectory(folder);
        string outPath = Path.Combine(folder, Path.GetFileNameWithoutExtension(_pdfPath) + "_edited.pdf");

        _btnSave.IsEnabled = false;
        var prevText = _btnSave.Content;
        _btnSave.Content = "Saving…";
        string? blurDir = null;
        bool ok = false;
        try
        {
            var src = _pdfPath;
            var els = new List<FillElement>(_elements);
            blurDir = await RenderBlursAsync(els);
            await System.Threading.Tasks.Task.Run(() => FillSignExporter.Export(src, els, outPath));
            _dirty = false; ok = true;
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
        return ok;
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

    private SolidColorBrush? _keycapBrush;
    private SolidColorBrush KeycapBrush()
    {
        if (_keycapBrush == null)
        {
            var c = B("TextMutedBrush").Color;
            _keycapBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(28, c.R, c.G, c.B)); // faint chip
            _keycapBrush.Freeze();
        }
        return _keycapBrush;
    }

    private SolidColorBrush? _onKeycapBrush;
    private SolidColorBrush OnKeycapBrush()   // contrasting chip for the selected tool's accent row
    {
        if (_onKeycapBrush == null)
        {
            _onKeycapBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(64, 255, 255, 255));
            _onKeycapBrush.Freeze();
        }
        return _onKeycapBrush;
    }

    private SolidColorBrush? _hoverBrush;
    private SolidColorBrush HoverBrush()
    {
        if (_hoverBrush == null)
        {
            var c = B("AccentBrush").Color;
            _hoverBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(150, c.R, c.G, c.B));
            _hoverBrush.Freeze();
        }
        return _hoverBrush;
    }

    // =====================================================================
    //  Hover action bar  (move / resize / delete shown over the hovered item)
    // =====================================================================
    private void ShowItemBar(FillElement e, Border chip)
    {
        if (_barMoving || _barResizing) return;             // don't retarget mid-drag
        _barHideTimer?.Stop();
        _barTarget = e; _barChip = chip;
        EnsureItemBar();
        if (_itemBar!.Parent != _barLayer) _barLayer.Children.Add(_itemBar);
        _itemBar.Visibility = Visibility.Visible;
        PositionItemBar();
    }

    private void ScheduleHideItemBar()
    {
        if (_barMoving || _barResizing) return;
        _barHideTimer ??= MakeBarHideTimer();
        _barHideTimer.Stop(); _barHideTimer.Start();        // small grace period to move onto the bar
    }

    private System.Windows.Threading.DispatcherTimer MakeBarHideTimer()
    {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        t.Tick += (_, _) => { t.Stop(); HideItemBar(); };
        return t;
    }

    private void HideItemBar()
    {
        if (_barMoving || _barResizing) return;
        if (_itemBar != null) _itemBar.Visibility = Visibility.Collapsed;
        _barTarget = null; _barChip = null;
    }

    private void PositionItemBar()
    {
        if (_itemBar == null || _barChip == null || !_barChip.IsVisible) return;
        _itemBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double bw = _itemBar.DesiredSize.Width, bh = _itemBar.DesiredSize.Height;
        // map the chip's on-screen rect into the (un-scaled) bar layer so the bar tracks it at any zoom
        double cw = _barChip.ActualWidth, chH = _barChip.ActualHeight;
        System.Windows.Media.GeneralTransform tf;
        try { tf = _barChip.TransformToVisual(_barLayer); } catch { return; }
        Point tl = tf.Transform(new Point(0, 0));
        Point br = tf.Transform(new Point(cw, chH));
        double minX = System.Math.Min(tl.X, br.X), minY = System.Math.Min(tl.Y, br.Y), maxY = System.Math.Max(tl.Y, br.Y);
        double top = minY - bh - 6;
        if (top < 0) top = maxY + 6;                                   // not enough room above → drop below
        double left = System.Math.Max(0, System.Math.Min(minX, System.Math.Max(0, _barLayer.ActualWidth - bw)));
        Canvas.SetLeft(_itemBar, left);
        Canvas.SetTop(_itemBar, top);
    }

    private void EnsureItemBar()
    {
        if (_itemBar != null) return;
        var bar = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(3),
            Background = B("SurfaceBrush"), BorderBrush = B("BorderSoftBrush"), BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.30, Color = System.Windows.Media.Colors.Black }
        };
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        bar.Child = row;

        var move = ItemBarButton("✥", "Drag to move");      // ✥-style move glyph
        var resize = ItemBarButton("⤡", "Drag to resize");  // ⤡ diagonal resize
        var edit = ItemBarButton("✎", "Edit");              // ✎ update content
        var del = ItemBarButton("\U0001F5D1", "Delete");         // 🗑
        row.Children.Add(move); row.Children.Add(resize); row.Children.Add(edit); row.Children.Add(del);

        WireBarMove(move);
        WireBarResize(resize);
        edit.MouseLeftButtonDown += (_, ev) =>
        {
            ev.Handled = true;
            var t = _barTarget; var chip = _barChip; if (t == null) return;
            _barHideTimer?.Stop();
            Select(t);
            if (t.Type is FillElementType.Text or FillElementType.DateTime)
            {
                if (chip != null && ChipTextBox(t) is { } tb) EnterEdit(tb);
            }
            else if (t.Type is FillElementType.Signature or FillElementType.Stamp)
            {
                ReplaceImage(t);
            }
        };
        del.MouseLeftButtonDown += (_, ev) =>
        {
            ev.Handled = true;
            var t = _barTarget; if (t == null) return;
            HideItemBar();
            Select(t); DeleteSelected();
        };

        bar.MouseEnter += (_, _) => _barHideTimer?.Stop();
        bar.MouseLeave += (_, _) => ScheduleHideItemBar();
        _itemBar = bar;
    }

    private Border ItemBarButton(string glyph, string tip)
    {
        var b = new Border
        {
            Width = 30, Height = 28, CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(1, 0, 1, 0), ToolTip = tip
        };
        var tb = new TextBlock { Text = glyph, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Dyn(tb, TextBlock.ForegroundProperty, "TextSecondaryBrush");
        b.Child = tb;
        b.MouseEnter += (_, _) => b.Background = HoverBrush();
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        return b;
    }

    private void WireBarMove(Border handle)
    {
        handle.MouseLeftButtonDown += (s, ev) =>
        {
            if (_barChip == null || _barTarget == null) return;
            Select(_barTarget);
            _barMoving = true; _barHideTimer?.Stop();
            _barDragStart = ev.GetPosition(_overlay);
            _barOrigL = Canvas.GetLeft(_barChip); if (double.IsNaN(_barOrigL)) _barOrigL = 0;
            _barOrigT = Canvas.GetTop(_barChip); if (double.IsNaN(_barOrigT)) _barOrigT = 0;
            _barDragSnap = Snapshot();
            handle.CaptureMouse(); ev.Handled = true;
        };
        handle.MouseMove += (s, ev) =>
        {
            if (!_barMoving || _barChip == null) return;
            var p = ev.GetPosition(_overlay);
            double nl = System.Math.Max(0, System.Math.Min(_barOrigL + (p.X - _barDragStart.X), _overlay.Width - 4));
            double nt = System.Math.Max(0, System.Math.Min(_barOrigT + (p.Y - _barDragStart.Y), _overlay.Height - 4));
            Canvas.SetLeft(_barChip, nl); Canvas.SetTop(_barChip, nt);
            PositionItemBar();
            ev.Handled = true;
        };
        handle.MouseLeftButtonUp += (s, ev) =>
        {
            if (!_barMoving) return;
            _barMoving = false; handle.ReleaseMouseCapture();
            if (_barChip != null && _barTarget != null)
            {
                PushSnap(_barDragSnap);
                _barTarget.X = FillSignGeometry.PixelsToPoints(Canvas.GetLeft(_barChip) + ChipPad, DisplayDpi);
                _barTarget.Y = FillSignGeometry.PixelsToPoints(Canvas.GetTop(_barChip) + ChipPad, DisplayDpi);
            }
            _barDragSnap = null;
            ev.Handled = true;
        };
    }

    private void WireBarResize(Border handle)
    {
        handle.MouseLeftButtonDown += (s, ev) =>
        {
            if (_barChip == null || _barTarget == null) return;
            Select(_barTarget);
            _barResizing = true; _barHideTimer?.Stop();
            _barDragStart = ev.GetPosition(_overlay);
            _barW0 = _barTarget.Width; _barH0 = _barTarget.Height;
            _barResizeContent = ChipContent(_barChip);
            _barDragSnap = Snapshot();
            handle.CaptureMouse(); ev.Handled = true;
        };
        handle.MouseMove += (s, ev) =>
        {
            if (!_barResizing || _barTarget == null) return;
            var p = ev.GetPosition(_overlay);
            double dxPx = p.X - _barDragStart.X, dyPx = p.Y - _barDragStart.Y;
            double th = _barTarget.Rotation * System.Math.PI / 180.0;       // project onto the element's local axes
            double ldx = dxPx * System.Math.Cos(th) + dyPx * System.Math.Sin(th);
            double ldy = -dxPx * System.Math.Sin(th) + dyPx * System.Math.Cos(th);
            double newW = System.Math.Max(14, _barW0 + FillSignGeometry.PixelsToPoints(ldx, DisplayDpi));
            double newH;
            if (_barTarget.Type is FillElementType.Signature or FillElementType.Stamp)
            { double ratio = _barH0 / System.Math.Max(1, _barW0); newH = newW * ratio; }   // images keep aspect
            else newH = System.Math.Max(10, _barH0 + FillSignGeometry.PixelsToPoints(ldy, DisplayDpi));
            _barTarget.Width = newW; _barTarget.Height = newH;
            if (_barResizeContent != null)
            {
                _barResizeContent.Width = FillSignGeometry.PointsToPixels(newW, DisplayDpi);
                _barResizeContent.Height = FillSignGeometry.PointsToPixels(newH, DisplayDpi);
            }
            PositionItemBar();
            ev.Handled = true;
        };
        handle.MouseLeftButtonUp += (s, ev) =>
        {
            if (!_barResizing) return;
            _barResizing = false; handle.ReleaseMouseCapture();
            PushSnap(_barDragSnap); _barDragSnap = null;
            if (_barTarget != null && _barTarget.Type == FillElementType.Blur && _barResizeContent is Image bi)
                bi.Source = CropPage(_barTarget);   // re-crop the blur preview at the new size
            _barResizeContent = null;
            RefreshProps();
            ev.Handled = true;
        };
    }

    private FrameworkElement? ChipContent(Border chip) =>
        chip.Child is Grid g ? g.Children.OfType<FrameworkElement>().FirstOrDefault() : null;

}
