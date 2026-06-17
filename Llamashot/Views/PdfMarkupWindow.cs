using System.IO;
using System.Windows;
using System.Windows.Controls;
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
/// Shared interactive PDF markup editor. Two modes:
///   "fillsign" — Fill &amp; Sign (text, check, signature, date + auto-detect fields)
///   "edit"     — PDF Editor (adds image + free text annotations)
/// Places <see cref="FillElement"/> annotations over rendered pages and exports via FillSignExporter.
/// </summary>
public class PdfMarkupWindow : Window
{
    private const int DisplayDpi = 120;

    private readonly string _mode;
    private string? _pdfPath;
    private int _pageCount;
    private int _curPage;             // 0-based
    private double _pageWpt, _pageHpt;

    private readonly List<FillElement> _elements = new();
    private readonly Dictionary<FillElement, Border> _chips = new();
    private FillElement? _selected;
    private string _tool = "select";

    private Image _pageImg = null!;
    private Canvas _overlay = null!;
    private TextBlock _txtPager = null!;
    private TextBlock _txtTitle = null!;
    private Button _btnSave = null!;
    private StackPanel _toolBar = null!;
    private readonly Dictionary<string, Border> _toolButtons = new();

    // drag state
    private bool _dragging;
    private Point _dragStart;
    private double _origLeft, _origTop;

    public PdfMarkupWindow(string mode = "fillsign")
    {
        _mode = mode == "edit" ? "edit" : "fillsign";
        Title = _mode == "edit" ? "Llamashot - PDF Editor" : "Llamashot - Fill & Sign";
        Width = 1300; Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowState = WindowState.Maximized;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        BuildUi();
        KeyDown += OnKeyDown;
    }

    private static SolidColorBrush B(string key) => (SolidColorBrush)Application.Current.Resources[key];
    private static void Dyn(FrameworkElement el, DependencyProperty p, string key) => el.SetResourceReference(p, key);

    // =====================================================================
    //  UI
    // =====================================================================
    private void BuildUi()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- top bar ----
        var top = new Border { Padding = new Thickness(22, 16, 22, 16), BorderThickness = new Thickness(0, 0, 0, 1) };
        Dyn(top, Border.BorderBrushProperty, "BorderSoftBrush");
        var topGrid = new Grid();
        var titleStack = new StackPanel();
        _txtTitle = new TextBlock { Text = _mode == "edit" ? "PDF Editor" : "Fill & Sign", FontSize = 22, FontWeight = FontWeights.Bold };
        Dyn(_txtTitle, TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var sub = new TextBlock { Text = _mode == "edit" ? "Add text, images and marks to your PDF" : "Type into fields, add checks and your signature", FontSize = 13 };
        Dyn(sub, TextBlock.ForegroundProperty, "TextMutedBrush");
        sub.Margin = new Thickness(0, 2, 0, 0);
        titleStack.Children.Add(_txtTitle); titleStack.Children.Add(sub);
        topGrid.Children.Add(titleStack);

        var openBtn = ChromeButton("📂  Open PDF", true);
        openBtn.HorizontalAlignment = HorizontalAlignment.Right;
        openBtn.Click += (_, _) => OpenPdf();
        topGrid.Children.Add(openBtn);
        top.Child = topGrid;
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        // ---- body: toolbar + canvas ----
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        body.ColumnDefinitions.Add(new ColumnDefinition());

        var rail = new Border { BorderThickness = new Thickness(0, 0, 1, 0) };
        Dyn(rail, Border.BackgroundProperty, "HeaderBrush");
        Dyn(rail, Border.BorderBrushProperty, "BorderSoftBrush");
        _toolBar = new StackPanel { Margin = new Thickness(14, 18, 14, 14) };
        _toolBar.Children.Add(RailLabel("TOOLS"));
        _toolBar.Children.Add(ToolButton("select", "↖  Select & move"));
        _toolBar.Children.Add(ToolButton("text", "🆃  Text"));
        _toolBar.Children.Add(ToolButton("check", "✓  Check mark"));
        _toolBar.Children.Add(ToolButton("date", "📅  Date"));
        _toolBar.Children.Add(ToolButton("signature", "✍  Signature"));
        if (_mode == "edit")
            _toolBar.Children.Add(ToolButton("image", "🖼  Image"));

        if (_mode == "fillsign")
        {
            _toolBar.Children.Add(RailLabel("ASSIST", 18));
            var detect = ChromeButton("✨  Auto-detect fields", false);
            detect.Margin = new Thickness(0, 2, 0, 0);
            detect.Click += async (_, _) => await AutoDetect();
            _toolBar.Children.Add(detect);
        }

        _toolBar.Children.Add(RailLabel("EDIT", 18));
        var del = ChromeButton("🗑  Delete selected", false);
        del.Margin = new Thickness(0, 2, 0, 0);
        del.Click += (_, _) => DeleteSelected();
        _toolBar.Children.Add(del);

        rail.Child = new ScrollViewer { Content = _toolBar, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(rail, 0);
        body.Children.Add(rail);

        // canvas area
        var canvasScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(24)
        };
        var pageStack = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        var pageShadow = new Border { Background = Brushes.White, SnapsToDevicePixels = true };
        pageShadow.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.25, Color = Colors.Black };
        _pageImg = new Image { Stretch = Stretch.None, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(_pageImg, BitmapScalingMode.HighQuality);
        _overlay = new Canvas { Background = Brushes.Transparent };
        _overlay.MouseLeftButtonDown += Overlay_MouseDown;
        _overlay.MouseMove += Overlay_MouseMove;
        _overlay.MouseLeftButtonUp += Overlay_MouseUp;
        var imgHost = new Grid();
        imgHost.Children.Add(_pageImg);
        imgHost.Children.Add(_overlay);
        pageShadow.Child = imgHost;
        pageStack.Children.Add(pageShadow);

        var emptyHint = new TextBlock
        {
            Text = "Open a PDF to start.\nThen pick a tool and click on the page.",
            TextAlignment = TextAlignment.Center, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 80, 0, 0)
        };
        Dyn(emptyHint, TextBlock.ForegroundProperty, "TextMutedBrush");
        _emptyHint = emptyHint;
        pageStack.Children.Add(emptyHint);

        canvasScroller.Content = pageStack;
        Grid.SetColumn(canvasScroller, 1);
        body.Children.Add(canvasScroller);

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        // ---- action bar ----
        var bar = new Border { Padding = new Thickness(22, 14, 22, 14), BorderThickness = new Thickness(0, 1, 0, 0) };
        Dyn(bar, Border.BackgroundProperty, "HeaderBrush");
        Dyn(bar, Border.BorderBrushProperty, "BorderSoftBrush");
        var barGrid = new Grid();

        var pager = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var prev = ChromeButton("‹", false); prev.Padding = new Thickness(14, 8, 14, 8); prev.Click += (_, _) => Page(-1);
        var next = ChromeButton("›", false); next.Padding = new Thickness(14, 8, 14, 8); next.Click += (_, _) => Page(+1);
        _txtPager = new TextBlock { Text = "– / –", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0), FontSize = 13 };
        Dyn(_txtPager, TextBlock.ForegroundProperty, "TextSecondaryBrush");
        pager.Children.Add(prev); pager.Children.Add(_txtPager); pager.Children.Add(next);
        barGrid.Children.Add(pager);

        _btnSave = ChromeButton(_mode == "edit" ? "Save Edited PDF  →" : "Save Signed PDF  →", true);
        _btnSave.HorizontalAlignment = HorizontalAlignment.Right;
        _btnSave.Padding = new Thickness(26, 12, 26, 12);
        _btnSave.FontWeight = FontWeights.Bold;
        _btnSave.Click += async (_, _) => await Save();
        barGrid.Children.Add(_btnSave);

        bar.Child = barGrid;
        Grid.SetRow(bar, 2);
        root.Children.Add(bar);

        Content = root;
        HighlightTool();
    }

    private TextBlock _emptyHint = null!;

    private TextBlock RailLabel(string text, double top = 2) => new()
    {
        Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(6, top, 0, 8), Foreground = B("TextMutedBrush")
    };

    private Border ToolButton(string id, string label)
    {
        var bd = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand, Tag = id };
        bd.Child = new TextBlock { Text = label, FontSize = 13.5 };
        bd.MouseLeftButtonUp += (_, _) => { _tool = id; HighlightTool(); };
        _toolButtons[id] = bd;
        return bd;
    }

    private void HighlightTool()
    {
        foreach (var (id, bd) in _toolButtons)
        {
            bool on = id == _tool;
            bd.Background = on ? B("AccentBrush") : Brushes.Transparent;
            ((TextBlock)bd.Child).Foreground = on ? B("AccentTextBrush") : B("TextSecondaryBrush");
            ((TextBlock)bd.Child).FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private Button ChromeButton(string text, bool accent)
    {
        var btn = new Button
        {
            Content = text, Cursor = Cursors.Hand, FontSize = 13, BorderThickness = new Thickness(accent ? 0 : 1),
            Padding = new Thickness(16, 9, 16, 9), Template = RoundedTemplate()
        };
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

        // unlock if protected
        string? pw = await PasswordDialog.UnlockAsync(this, dlg.FileName);
        if (pw == null) return;

        try
        {
            _pdfPath = dlg.FileName;
            _pageCount = await FileToolsService.GetPdfPageCountAsync(_pdfPath, string.IsNullOrEmpty(pw) ? null : pw);
            _elements.Clear();
            _selected = null;
            _curPage = 0;
            _emptyHint.Visibility = Visibility.Collapsed;
            await RenderPage();
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
    }

    // =====================================================================
    //  Chips (visual representation of elements on the current page)
    // =====================================================================
    private void LayoutChips()
    {
        _overlay.Children.Clear();
        _chips.Clear();
        foreach (var e in _elements)
            if (e.Page == _curPage) AddChip(e);
    }

    private void AddChip(FillElement e)
    {
        FrameworkElement content;
        if (e.Type is FillElementType.Signature or FillElementType.Stamp)
        {
            content = new Image
            {
                Source = e.ImagePath != null && File.Exists(e.ImagePath) ? new BitmapImage(new System.Uri(e.ImagePath)) : null,
                Width = FillSignGeometry.PointsToPixels(e.Width, DisplayDpi),
                Height = FillSignGeometry.PointsToPixels(e.Height, DisplayDpi),
                Stretch = Stretch.Fill
            };
        }
        else
        {
            var tb = new TextBox
            {
                Text = e.Text, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                Foreground = HexBrush(e.ColorHex), FontSize = FillSignGeometry.PointsToPixels(e.FontSize, DisplayDpi),
                FontWeight = e.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = e.Italic ? FontStyles.Italic : FontStyles.Normal,
                IsReadOnly = true, Padding = new Thickness(2, 0, 2, 0),
                MinWidth = 24, FontFamily = new System.Windows.Media.FontFamily(e.FontFamily)
            };
            tb.TextChanged += (_, _) => e.Text = tb.Text;
            content = tb;
        }

        var chip = new Border
        {
            Child = content, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent,
            CornerRadius = new CornerRadius(3), Cursor = Cursors.SizeAll, Tag = e, Background = Brushes.Transparent
        };
        Canvas.SetLeft(chip, FillSignGeometry.PointsToPixels(e.X, DisplayDpi));
        Canvas.SetTop(chip, FillSignGeometry.PointsToPixels(e.Y, DisplayDpi));

        chip.MouseLeftButtonDown += (s, ev) =>
        {
            Select(e);
            // double-click a text chip → edit it
            if (ev.ClickCount == 2 && content is TextBox dt)
            {
                dt.IsReadOnly = false; dt.Focus(); dt.SelectAll(); ev.Handled = true; return;
            }
            // start drag unless we're editing a focused textbox
            if (content is TextBox t && !t.IsReadOnly) return;
            _dragging = true; _dragChip = chip;
            _dragStart = ev.GetPosition(_overlay);
            _origLeft = Canvas.GetLeft(chip); _origTop = Canvas.GetTop(chip);
            chip.CaptureMouse();
            ev.Handled = true;
        };
        chip.MouseLeftButtonUp += (s, ev) => EndDrag(chip, e);
        chip.MouseMove += (s, ev) => DragMove(chip, e, ev);

        if (content is TextBox box)
            box.LostFocus += (_, _) => { box.IsReadOnly = true; };

        _overlay.Children.Add(chip);
        _chips[e] = chip;
    }

    private Border? _dragChip;

    private void DragMove(Border chip, FillElement e, System.Windows.Input.MouseEventArgs ev)
    {
        if (!_dragging || _dragChip != chip) return;
        var p = ev.GetPosition(_overlay);
        double nl = _origLeft + (p.X - _dragStart.X);
        double nt = _origTop + (p.Y - _dragStart.Y);
        nl = System.Math.Max(0, System.Math.Min(nl, _overlay.Width - 4));
        nt = System.Math.Max(0, System.Math.Min(nt, _overlay.Height - 4));
        Canvas.SetLeft(chip, nl); Canvas.SetTop(chip, nt);
    }

    private void EndDrag(Border chip, FillElement e)
    {
        if (!_dragging) return;
        _dragging = false; _dragChip = null; chip.ReleaseMouseCapture();
        e.X = FillSignGeometry.PixelsToPoints(Canvas.GetLeft(chip), DisplayDpi);
        e.Y = FillSignGeometry.PixelsToPoints(Canvas.GetTop(chip), DisplayDpi);
    }

    private void Select(FillElement? e)
    {
        _selected = e;
        foreach (var (el, chip) in _chips)
            chip.BorderBrush = el == e ? B("AccentBrush") : Brushes.Transparent;
    }

    private void DeleteSelected()
    {
        if (_selected == null) return;
        _elements.Remove(_selected);
        if (_chips.TryGetValue(_selected, out var chip)) _overlay.Children.Remove(chip);
        _chips.Remove(_selected);
        _selected = null;
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Delete && _selected != null)
        {
            // don't delete while editing text
            if (System.Windows.Input.Keyboard.FocusedElement is TextBox tb && !tb.IsReadOnly) return;
            DeleteSelected();
        }
    }

    // =====================================================================
    //  Placement (clicking empty canvas with a tool active)
    // =====================================================================
    private async void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_pdfPath == null || _tool == "select") { Select(null); return; }
        var p = e.GetPosition(_overlay);
        double xPt = FillSignGeometry.PixelsToPoints(p.X, DisplayDpi);
        double yPt = FillSignGeometry.PixelsToPoints(p.Y, DisplayDpi);

        switch (_tool)
        {
            case "text":
                PlaceText(xPt, yPt, "", 12); break;
            case "check":
                PlaceText(xPt, yPt, "X", 16); break;
            case "date":
                PlaceText(xPt, yPt, System.DateTime.Now.ToString("MMM d, yyyy"), 12); break;
            case "signature":
                await PlaceSignature(xPt, yPt); break;
            case "image":
                PlaceImage(xPt, yPt); break;
        }
    }

    private void Overlay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragging && _dragChip != null && _dragChip.Tag is FillElement el) DragMove(_dragChip, el, e);
    }

    private void Overlay_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging && _dragChip != null && _dragChip.Tag is FillElement el) EndDrag(_dragChip, el);
    }

    private void PlaceText(double xPt, double yPt, string text, double fontSize)
    {
        var el = new FillElement
        {
            Page = _curPage, Type = text == "X" ? FillElementType.Check : FillElementType.Text,
            X = xPt, Y = yPt, Width = 200, Height = fontSize * 1.4, Text = text, FontSize = fontSize
        };
        _elements.Add(el);
        AddChip(el);
        Select(el);
        _tool = "select"; HighlightTool();
        if (_chips[el].Child is TextBox tb && text == "")
        {
            tb.IsReadOnly = false; tb.Focus();
        }
    }

    private async System.Threading.Tasks.Task PlaceSignature(double xPt, double yPt)
    {
        var dlg = new SignatureDialog { Owner = this };
        _tool = "select"; HighlightTool();
        if (dlg.ShowDialog() != true || dlg.ResultPngPath == null) return;
        AddImageElement(dlg.ResultPngPath, xPt, yPt, FillElementType.Signature, 180);
    }

    private void PlaceImage(double xPt, double yPt)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
        _tool = "select"; HighlightTool();
        if (dlg.ShowDialog() != true) return;
        AddImageElement(dlg.FileName, xPt, yPt, FillElementType.Stamp, 220);
    }

    private void AddImageElement(string imagePath, double xPt, double yPt, FillElementType type, double widthPt)
    {
        double ratio = 0.4;
        try { var bi = new BitmapImage(new System.Uri(imagePath)); if (bi.PixelWidth > 0) ratio = (double)bi.PixelHeight / bi.PixelWidth; } catch { }
        var el = new FillElement
        {
            Page = _curPage, Type = type, X = xPt, Y = yPt,
            Width = widthPt, Height = widthPt * ratio, ImagePath = imagePath
        };
        _elements.Add(el);
        AddChip(el);
        Select(el);
    }

    // =====================================================================
    //  Auto-detect (Fill & Sign)
    // =====================================================================
    private async System.Threading.Tasks.Task AutoDetect()
    {
        if (_pdfPath == null) { ConfirmDialog.Alert(this, "No PDF", "Open a PDF first."); return; }
        try
        {
            using var bmp = await FillSignRender.RenderPageToBitmapAsync(_pdfPath, _curPage, DisplayDpi);
            var regions = FillSignDetector.DetectRegions(bmp);
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
                    Height = System.Math.Max(12, hPt), Text = "",
                    FontSize = System.Math.Max(9, System.Math.Min(16, hPt * 0.6))
                };
                _elements.Add(el);
                added++;
            }
            LayoutChips();
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

        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", _mode == "edit" ? "Edited" : "Signed");
        Directory.CreateDirectory(folder);
        string suffix = _mode == "edit" ? "_edited" : "_signed";
        string outPath = Path.Combine(folder, Path.GetFileNameWithoutExtension(_pdfPath) + suffix + ".pdf");

        _btnSave.IsEnabled = false;
        var prevText = _btnSave.Content;
        _btnSave.Content = "Saving…";
        try
        {
            var src = _pdfPath;
            var els = new List<FillElement>(_elements);
            await System.Threading.Tasks.Task.Run(() => FillSignExporter.Export(src, els, outPath));
            ConfirmDialog.Alert(this, "Saved",
                $"Saved your {(_mode == "edit" ? "edited" : "signed")} PDF to:\n{outPath}", ConfirmDialog.AlertKind.Success, "Done");
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outPath}\""); } catch { }
        }
        catch (System.Exception ex)
        {
            ConfirmDialog.Alert(this, "Save Failed", ex.Message, ConfirmDialog.AlertKind.Error);
        }
        finally
        {
            _btnSave.IsEnabled = true;
            _btnSave.Content = prevText;
        }
    }

    private static SolidColorBrush HexBrush(string hex)
    {
        try { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; }
        catch { return Brushes.Black; }
    }
}
