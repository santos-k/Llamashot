using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Llamashot.Core;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Llamashot.Views;

/// <summary>
/// Interactive "Images → PDF" composer: pick a page size (A4/A3/…), margin and orientation,
/// drop one or many images onto each page, then move / resize / rotate (0–360°) them freely.
/// Coordinates are stored in PDF points (1 pt = 1/72") so the layout is resolution-independent;
/// the on-screen canvas just scales by <see cref="_k"/>.
/// </summary>
public partial class ImagePdfComposerWindow : Window
{
    private sealed class Placed
    {
        public required string Path { get; set; }
        public required BitmapImage Bmp { get; set; }
        public double NatRatio { get; set; } = 1;     // width / height
        public double X, Y, W, H;                     // top-left + size, page points (Y-down)
        public double Angle;                          // degrees, clockwise
        public bool FlipH, FlipV;                     // mirror horizontally / vertically

        // Visuals (rebuilt whenever the page is (re)loaded)
        public System.Windows.Controls.Canvas? Frame;
        public System.Windows.Controls.Image? Img;
        public RotateTransform? Rot;
        public ScaleTransform? Scale;
        public Rectangle? Sel;
        public Thumb[]? Corners;
        public Thumb? RotateGrip;
    }

    private sealed class PageM { public List<Placed> Images { get; } = new(); }

    private static readonly (string name, double w, double h)[] PageSizes =
    {
        ("A4",      595, 842),
        ("Letter",  612, 792),
        ("A3",      842, 1191),
        ("A5",      420, 595),
        ("Legal",   612, 1008),
        ("Tabloid", 792, 1224),
    };

    private readonly List<PageM> _pages = new();
    private int _cur;
    private double _pageWpt = 595, _pageHpt = 842, _marginPt = 24;
    private double _k = 1;                 // screen px per page point
    private Placed? _sel;
    private Rectangle? _marginGuide;
    private bool _suppress;               // guards property-panel → model feedback loops

    // Move drag state
    private bool _moving;
    private Point _moveStart;
    private double _moveOX, _moveOY;

    public ImagePdfComposerWindow()
    {
        InitializeComponent();

        foreach (var s in PageSizes) CmbPageSize.Items.Add(s.name);
        CmbPageSize.SelectedIndex = 0;

        _pages.Add(new PageM());
        _cur = 0;

        Loaded += (_, _) => { ApplyPageSize(); RebuildThumbs(); LoadPage(); };
        KeyDown += OnKeyDown;
    }

    // Guard against accidental loss of an in-progress layout.
    protected override void OnClosing(CancelEventArgs e)
    {
        bool hasWork = _pages.Any(p => p.Images.Count > 0);
        if (hasWork && !ConfirmDialog.Show(this, "Discard layout?",
                "You've placed images but haven't exported the PDF. Close and lose this layout?",
                "Close", "Keep editing"))
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    // =====================================================================
    //  Page size / margin
    // =====================================================================
    private void ApplyPageSize()
    {
        int i = Math.Max(0, CmbPageSize.SelectedIndex);
        var (_, w, h) = PageSizes[i];
        bool landscape = CmbOrient.SelectedIndex == 1;
        _pageWpt = landscape ? h : w;
        _pageHpt = landscape ? w : h;
    }

    private void PageSize_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyPageSize();
        // Keep every image's centre inside the new page bounds.
        foreach (var page in _pages)
            foreach (var p in page.Images) ClampCenter(p);
        RebuildThumbs();
        LoadPage();
    }

    private void Margin_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _marginPt = e.NewValue;
        if (TxtMargin != null) TxtMargin.Text = $"MARGIN — {_marginPt:0} pt";
        UpdateMarginGuide();
    }

    // =====================================================================
    //  Scale + layout
    // =====================================================================
    private void CenterHost_SizeChanged(object sender, SizeChangedEventArgs e) => FitScale();

    private void FitScale()
    {
        if (CenterHost.ActualWidth <= 0 || CenterHost.ActualHeight <= 0) return;
        double availW = CenterHost.ActualWidth - 64;
        double availH = CenterHost.ActualHeight - 64;
        _k = Math.Max(0.05, Math.Min(availW / _pageWpt, availH / _pageHpt));

        PageCanvas.Width = _pageWpt * _k;
        PageCanvas.Height = _pageHpt * _k;

        UpdateMarginGuide();
        foreach (var p in _pages[_cur].Images) Layout(p);
    }

    private void UpdateMarginGuide()
    {
        if (_marginGuide == null || PageCanvas == null) return;
        double m = _marginPt * _k;
        _marginGuide.Width = Math.Max(0, PageCanvas.Width - 2 * m);
        _marginGuide.Height = Math.Max(0, PageCanvas.Height - 2 * m);
        System.Windows.Controls.Canvas.SetLeft(_marginGuide, m);
        System.Windows.Controls.Canvas.SetTop(_marginGuide, m);
    }

    private void LoadPage()
    {
        PageCanvas.Children.Clear();
        Deselect();

        _marginGuide = new Rectangle
        {
            Stroke = (Brush)FindResource("BorderSoftBrush"),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 4 },
            IsHitTestVisible = false,
            Fill = null,
        };
        PageCanvas.Children.Add(_marginGuide);

        var page = _pages[_cur];
        for (int i = 0; i < page.Images.Count; i++)
        {
            BuildFrame(page.Images[i]);
            System.Windows.Controls.Panel.SetZIndex(page.Images[i].Frame!, i + 1);
        }

        FitScale();
        RefreshThumbHighlight();
        UpdateProps();
    }

    private void BuildFrame(Placed p)
    {
        var frame = new System.Windows.Controls.Canvas
        {
            ClipToBounds = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var scale = new ScaleTransform(p.FlipH ? -1 : 1, p.FlipV ? -1 : 1);
        var rot = new RotateTransform(p.Angle);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(rot);
        frame.RenderTransform = group;

        var img = new System.Windows.Controls.Image
        {
            Source = p.Bmp,
            Stretch = Stretch.Fill,
            Cursor = Cursors.SizeAll,
        };
        frame.Children.Add(img);

        var sel = new Rectangle
        {
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Fill = null,
        };
        sel.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        frame.Children.Add(sel);

        var corners = new Thumb[4];
        var cursors = new[] { Cursors.SizeNWSE, Cursors.SizeNESW, Cursors.SizeNESW, Cursors.SizeNWSE };
        for (int i = 0; i < 4; i++)
        {
            var t = new Thumb { Style = (Style)FindResource("CornerHandle"), Cursor = cursors[i], Visibility = Visibility.Collapsed };
            t.DragDelta += (_, _) => ResizeUniform(p);
            t.DragStarted += (_, _) => Select(p);
            frame.Children.Add(t);
            corners[i] = t;
        }

        var grip = new Thumb { Style = (Style)FindResource("RotateHandle"), Visibility = Visibility.Collapsed };
        grip.DragDelta += (_, _) => RotateToMouse(p);
        grip.DragStarted += (_, _) => Select(p);
        frame.Children.Add(grip);

        img.MouseLeftButtonDown += (_, e) =>
        {
            Select(p);
            _moving = true;
            _moveStart = e.GetPosition(PageCanvas);
            _moveOX = p.X; _moveOY = p.Y;
            img.CaptureMouse();
            e.Handled = true;
        };
        img.MouseMove += (_, e) =>
        {
            if (!_moving) return;
            var pos = e.GetPosition(PageCanvas);
            p.X = _moveOX + (pos.X - _moveStart.X) / _k;
            p.Y = _moveOY + (pos.Y - _moveStart.Y) / _k;
            ClampCenter(p);
            Layout(p);
        };
        img.MouseLeftButtonUp += (_, _) =>
        {
            if (!_moving) return;
            _moving = false;
            img.ReleaseMouseCapture();
            RefreshThumbAt(_cur);
        };

        p.Frame = frame; p.Img = img; p.Rot = rot; p.Scale = scale; p.Sel = sel; p.Corners = corners; p.RotateGrip = grip;
        PageCanvas.Children.Add(frame);
        Layout(p);
    }

    private void Layout(Placed p)
    {
        if (p.Frame == null) return;
        double w = p.W * _k, h = p.H * _k;
        System.Windows.Controls.Canvas.SetLeft(p.Frame, p.X * _k);
        System.Windows.Controls.Canvas.SetTop(p.Frame, p.Y * _k);
        p.Frame.Width = w; p.Frame.Height = h;
        p.Img!.Width = w; p.Img.Height = h;
        p.Rot!.Angle = p.Angle;
        if (p.Scale != null) { p.Scale.ScaleX = p.FlipH ? -1 : 1; p.Scale.ScaleY = p.FlipV ? -1 : 1; }

        p.Sel!.Width = w; p.Sel.Height = h;
        System.Windows.Controls.Canvas.SetLeft(p.Sel, 0);
        System.Windows.Controls.Canvas.SetTop(p.Sel, 0);

        PlaceHandle(p.Corners![0], 0, 0);
        PlaceHandle(p.Corners[1], w, 0);
        PlaceHandle(p.Corners[2], 0, h);
        PlaceHandle(p.Corners[3], w, h);
        PlaceHandle(p.RotateGrip!, w / 2, -28);
    }

    private static void PlaceHandle(Thumb t, double cx, double cy)
    {
        System.Windows.Controls.Canvas.SetLeft(t, cx - t.Width / 2);
        System.Windows.Controls.Canvas.SetTop(t, cy - t.Height / 2);
    }

    private void ClampCenter(Placed p)
    {
        double cx = Math.Max(0, Math.Min(_pageWpt, p.X + p.W / 2));
        double cy = Math.Max(0, Math.Min(_pageHpt, p.Y + p.H / 2));
        p.X = cx - p.W / 2;
        p.Y = cy - p.H / 2;
    }

    // =====================================================================
    //  Selection
    // =====================================================================
    private void Center_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Click on empty canvas / backdrop → deselect.
        if (e.OriginalSource is System.Windows.Controls.Image) return;
        Deselect();
    }

    private void Select(Placed p)
    {
        _sel = p;
        foreach (var q in _pages[_cur].Images)
        {
            bool on = ReferenceEquals(q, p);
            if (q.Sel != null) q.Sel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (q.Corners != null) foreach (var c in q.Corners) c.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (q.RotateGrip != null) q.RotateGrip.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }
        TxtNoSel.Visibility = Visibility.Collapsed;
        PropsPanel.Visibility = Visibility.Visible;
        UpdateProps();
    }

    private void Deselect()
    {
        _sel = null;
        foreach (var q in _pages[_cur].Images)
        {
            if (q.Sel != null) q.Sel.Visibility = Visibility.Collapsed;
            if (q.Corners != null) foreach (var c in q.Corners) c.Visibility = Visibility.Collapsed;
            if (q.RotateGrip != null) q.RotateGrip.Visibility = Visibility.Collapsed;
        }
        if (TxtNoSel != null) TxtNoSel.Visibility = Visibility.Visible;
        if (PropsPanel != null) PropsPanel.Visibility = Visibility.Collapsed;
    }

    private void UpdateProps()
    {
        if (_sel == null) return;
        _suppress = true;
        SldRotate.Value = _sel.Angle;
        TxtRotate.Text = $"{_sel.Angle:0}°";
        TxtSize.Text = $"Size: {_sel.W:0} × {_sel.H:0} pt";
        _suppress = false;
    }

    // =====================================================================
    //  Resize / rotate maths
    // =====================================================================
    private static (double x, double y) RotateVec(double vx, double vy, double deg)
    {
        double r = deg * Math.PI / 180.0;
        double c = Math.Cos(r), s = Math.Sin(r);
        return (vx * c - vy * s, vx * s + vy * c);
    }

    private void ResizeUniform(Placed p)
    {
        var m = Mouse.GetPosition(PageCanvas);
        double mx = m.X / _k, my = m.Y / _k;
        double cx = p.X + p.W / 2, cy = p.Y + p.H / 2;
        var (lx, ly) = RotateVec(mx - cx, my - cy, -p.Angle); // into image-local space
        double newHalfDiag = Math.Sqrt(lx * lx + ly * ly);
        double oldHalfDiag = 0.5 * Math.Sqrt(p.W * p.W + p.H * p.H);
        if (oldHalfDiag < 1e-6) return;

        double scale = newHalfDiag / oldHalfDiag;
        double maxDim = Math.Max(_pageWpt, _pageHpt) * 3;
        double newW = Math.Max(24, Math.Min(p.W * scale, maxDim));
        double newH = newW / p.NatRatio;
        p.X = cx - newW / 2; p.Y = cy - newH / 2;
        p.W = newW; p.H = newH;
        Layout(p);
        UpdateProps();
    }

    private void RotateToMouse(Placed p)
    {
        var m = Mouse.GetPosition(PageCanvas);
        double mx = m.X / _k, my = m.Y / _k;
        double cx = p.X + p.W / 2, cy = p.Y + p.H / 2;
        double dx = mx - cx, dy = my - cy;
        if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6) return;

        double deg = Math.Atan2(dx, -dy) * 180.0 / Math.PI; // clockwise from straight up
        if (deg < 0) deg += 360;
        if (Keyboard.Modifiers == ModifierKeys.Shift) deg = Math.Round(deg / 15.0) * 15.0;
        p.Angle = deg % 360;
        Layout(p);
        UpdateProps();
    }

    private void Rotate_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress || _sel == null) return;
        _sel.Angle = e.NewValue;
        TxtRotate.Text = $"{_sel.Angle:0}°";
        Layout(_sel);
    }

    private void RotL_Click(object sender, RoutedEventArgs e) => NudgeRotate(-90);
    private void RotR_Click(object sender, RoutedEventArgs e) => NudgeRotate(+90);
    private void Rot0_Click(object sender, RoutedEventArgs e) { if (_sel != null) { _sel.Angle = 0; Layout(_sel); UpdateProps(); } }
    private void NudgeRotate(double d)
    {
        if (_sel == null) return;
        _sel.Angle = (_sel.Angle + d + 360) % 360;
        Layout(_sel);
        UpdateProps();
    }

    private void FlipH_Click(object sender, RoutedEventArgs e)
    {
        if (_sel == null) return;
        _sel.FlipH = !_sel.FlipH;
        Layout(_sel);
        RefreshThumbAt(_cur);
    }

    private void FlipV_Click(object sender, RoutedEventArgs e)
    {
        if (_sel == null) return;
        _sel.FlipV = !_sel.FlipV;
        Layout(_sel);
        RefreshThumbAt(_cur);
    }

    // =====================================================================
    //  Add images / pages
    // =====================================================================
    private void AddImages_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Add images",
            Multiselect = true,
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        Placed? last = null;
        foreach (var path in dlg.FileNames)
        {
            try { last = AddImage(path); }
            catch { /* skip unreadable */ }
        }
        LoadPage();
        if (last != null) Select(last);
        RebuildThumbs();
    }

    private Placed AddImage(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();

        double natW = bmp.PixelWidth, natH = bmp.PixelHeight;
        if (natW < 1 || natH < 1) natW = natH = 1;
        double ratio = natW / natH;

        double availW = Math.Max(40, _pageWpt - 2 * _marginPt);
        double availH = Math.Max(40, _pageHpt - 2 * _marginPt);
        double w, h;
        if (availW / availH > ratio) { h = availH; w = h * ratio; }
        else { w = availW; h = w / ratio; }

        var page = _pages[_cur];
        int existing = page.Images.Count;
        if (existing > 0) { w *= 0.55; h *= 0.55; }

        double off = Math.Min(existing * 18, Math.Max(_pageWpt, _pageHpt) * 0.25);
        var p = new Placed { Path = path, Bmp = bmp, NatRatio = ratio, W = w, H = h };
        p.X = (_pageWpt - w) / 2 + off;
        p.Y = (_pageHpt - h) / 2 + off;
        ClampCenter(p);
        page.Images.Add(p);
        return p;
    }

    private void AddPage_Click(object sender, RoutedEventArgs e)
    {
        _pages.Add(new PageM());
        _cur = _pages.Count - 1;
        RebuildThumbs();
        LoadPage();
    }

    /// <summary>Clears the whole layout back to a single empty page (used by "Start over" after export).</summary>
    private void StartOver()
    {
        _pages.Clear();
        _pages.Add(new PageM());
        _cur = 0;
        Deselect();
        RebuildThumbs();
        LoadPage();
    }

    // =====================================================================
    //  Document scan integration
    // =====================================================================
    /// <summary>Pick a document photo, flatten it in the scanner, and add the result to the page.</summary>
    private void ScanAdd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose a document photo to scan",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        string? scan = RunScanner(dlg.FileName);
        if (scan == null) return;
        try
        {
            var added = AddImage(scan);
            LoadPage();
            Select(added);
            RebuildThumbs();
        }
        catch (Exception ex) { ConfirmDialog.Alert(this, "Couldn't Add Scan", ex.Message, ConfirmDialog.AlertKind.Error); }
    }

    /// <summary>Re-scan the selected placed image, replacing its content with the flattened version.</summary>
    private void Rescan_Click(object sender, RoutedEventArgs e)
    {
        if (_sel == null) return;
        string? scan = RunScanner(_sel.Path);
        if (scan == null) return;
        try { ReplaceContent(_sel, scan); }
        catch (Exception ex) { ConfirmDialog.Alert(this, "Scan Failed", ex.Message, ConfirmDialog.AlertKind.Error); }
    }

    /// <summary>Swaps a placement's image for a new file in place (keeps position / angle / flip; follows
    /// the new aspect ratio). Updates only the existing visuals — no frame rebuild — to avoid disturbing
    /// the visual tree.</summary>
    private void ReplaceContent(Placed p, string newPath)
    {
        var bmp = LoadBitmap(newPath);
        double ratio = bmp.PixelWidth / (double)Math.Max(1, bmp.PixelHeight);
        double cx = p.X + p.W / 2, cy = p.Y + p.H / 2;
        double w = p.W, h = w / ratio;                    // keep width, follow the new aspect

        p.Path = newPath; p.Bmp = bmp; p.NatRatio = ratio;
        p.W = w; p.H = h; p.X = cx - w / 2; p.Y = cy - h / 2;
        ClampCenter(p);
        if (p.Img != null) p.Img.Source = bmp;            // update the live image in place
        Layout(p);
        UpdateProps();
        RebuildThumbs();                                  // safe full rebuild of the page rail
    }

    private string? RunScanner(string sourcePath)
    {
        var scanner = new DocumentScanWindow(sourcePath, pickMode: true) { Owner = this };
        return scanner.ShowDialog() == true ? scanner.PickResultPath : null;
    }

    /// <summary>AI-removes the background of the selected placed image and swaps in the transparent cut-out.</summary>
    private async void AiCutout_Click(object sender, RoutedEventArgs e)
    {
        if (_sel == null) return;
        var src = _sel;
        BusyOverlay.Visibility = Visibility.Visible;
        try
        {
            if (!AiMatting.ModelExists)
            {
                PrgBusy.IsIndeterminate = false;
                var prog = new Progress<double>(p => { PrgBusy.Value = p; TxtBusy.Text = $"Downloading AI model… {p:0}%"; });
                TxtBusy.Text = "Downloading AI model (~168 MB, one time)…";
                await AiMatting.EnsureModelAsync(prog);
            }
            PrgBusy.IsIndeterminate = true;
            TxtBusy.Text = "Removing background…";

            byte[] px = LoadBgra(src.Path, out int w, out int h);
            byte[] mask = await System.Threading.Tasks.Task.Run(() => AiMatting.ComputeMask(px, w, h));
            AiMatting.ApplyMask(px, mask);
            BackgroundRemover.SmoothAlpha(px, w, h, 2);

            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Llamashot", "scans");
            Directory.CreateDirectory(dir);
            string outPath = System.IO.Path.Combine(dir, $"cut_{Guid.NewGuid():N}.png");
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = new FileStream(outPath, FileMode.Create)) enc.Save(fs);

            if (_pages[_cur].Images.Contains(src)) ReplaceContent(src, outPath);
        }
        catch (Exception ex)
        {
            ConfirmDialog.Alert(this, "Background Removal Failed",
                $"{ex.Message}\n\nIf this is the first run, check your internet connection — the model downloads once.",
                ConfirmDialog.AlertKind.Error);
        }
        finally
        {
            BusyOverlay.Visibility = Visibility.Collapsed;
            PrgBusy.IsIndeterminate = true; PrgBusy.Value = 0;
        }
    }

    private static byte[] LoadBgra(string path, out int w, out int h)
    {
        var bi = new BitmapImage();
        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(path); bi.EndInit(); bi.Freeze();
        BitmapSource s = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
        w = s.PixelWidth; h = s.PixelHeight;
        var px = new byte[w * h * 4];
        s.CopyPixels(px, w * 4, 0);
        return px;
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    // ---- Harness-only hooks ----
    /// <summary>Harness-only: add an image and select it (mirrors the Add Images flow).</summary>
    public void TestAddImage(string path) { var p = AddImage(path); LoadPage(); Select(p); RebuildThumbs(); }
    /// <summary>Harness-only: close without the discard-layout prompt.</summary>
    public void TestClose() { foreach (var pg in _pages) pg.Images.Clear(); Close(); }
    /// <summary>Harness-only: run the (fixed) in-place replace on the selection; returns null on success.</summary>
    public string? TestReplaceContent(string newPath)
    {
        if (_sel == null) return "no selection";
        try { ReplaceContent(_sel, newPath); return null; }
        catch (Exception ex) { return ex.ToString(); }
    }
    /// <summary>Harness-only: reproduce the ORIGINAL object-replacement + LoadPage path to capture its error.</summary>
    public string? TestReplaceOldStyle(string newPath)
    {
        if (_sel == null) return "no selection";
        try
        {
            var src = _sel;
            int idx = _pages[_cur].Images.IndexOf(src);
            var bmp = LoadBitmap(newPath);
            double ratio = bmp.PixelWidth / (double)Math.Max(1, bmp.PixelHeight);
            var np = new Placed { Path = newPath, Bmp = bmp, NatRatio = ratio, W = src.W, H = src.W / ratio, X = src.X, Y = src.Y };
            _pages[_cur].Images[idx] = np;
            LoadPage();
            Select(np);
            RefreshThumbAt(_cur);
            return null;
        }
        catch (Exception ex) { return ex.ToString(); }
    }

    // =====================================================================
    //  Arrange / delete
    // =====================================================================
    private void Center_Click(object sender, RoutedEventArgs e)
    {
        if (_sel == null) return;
        _sel.X = (_pageWpt - _sel.W) / 2;
        _sel.Y = (_pageHpt - _sel.H) / 2;
        Layout(_sel); RefreshThumbAt(_cur);
    }

    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        if (_sel == null) return;
        double availW = Math.Max(40, _pageWpt - 2 * _marginPt);
        double availH = Math.Max(40, _pageHpt - 2 * _marginPt);
        double ratio = _sel.NatRatio;
        double w, h;
        if (availW / availH > ratio) { h = availH; w = h * ratio; }
        else { w = availW; h = w / ratio; }
        _sel.W = w; _sel.H = h;
        _sel.X = (_pageWpt - w) / 2; _sel.Y = (_pageHpt - h) / 2;
        _sel.Angle = 0;
        Layout(_sel); UpdateProps(); RefreshThumbAt(_cur);
    }

    private void Front_Click(object sender, RoutedEventArgs e) => Reorder(true);
    private void Back_Click(object sender, RoutedEventArgs e) => Reorder(false);
    private void Reorder(bool front)
    {
        if (_sel == null) return;
        var list = _pages[_cur].Images;
        list.Remove(_sel);
        if (front) list.Add(_sel); else list.Insert(0, _sel);
        var keep = _sel;
        LoadPage();
        Select(keep);
        RefreshThumbAt(_cur);
    }

    private void Dup_Click(object sender, RoutedEventArgs e)
    {
        if (_sel == null) return;
        var src = _sel;
        var copy = new Placed
        {
            Path = src.Path, Bmp = src.Bmp, NatRatio = src.NatRatio,
            W = src.W, H = src.H, Angle = src.Angle,
            FlipH = src.FlipH, FlipV = src.FlipV,
            X = src.X + 16, Y = src.Y + 16,
        };
        ClampCenter(copy);
        _pages[_cur].Images.Add(copy);
        LoadPage();
        Select(copy);
        RefreshThumbAt(_cur);
    }

    private void Del_Click(object sender, RoutedEventArgs e) => DeleteSelected();
    private void DeleteSelected()
    {
        if (_sel == null) return;
        _pages[_cur].Images.Remove(_sel);
        LoadPage();
        RefreshThumbAt(_cur);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_sel == null) return;
        double step = (Keyboard.Modifiers == ModifierKeys.Shift) ? 10 : 2;
        switch (e.Key)
        {
            case Key.Delete: DeleteSelected(); e.Handled = true; break;
            case Key.Escape: Deselect(); e.Handled = true; break;
            case Key.Left: _sel.X -= step; ClampCenter(_sel); Layout(_sel); e.Handled = true; break;
            case Key.Right: _sel.X += step; ClampCenter(_sel); Layout(_sel); e.Handled = true; break;
            case Key.Up: _sel.Y -= step; ClampCenter(_sel); Layout(_sel); e.Handled = true; break;
            case Key.Down: _sel.Y += step; ClampCenter(_sel); Layout(_sel); e.Handled = true; break;
        }
    }

    // =====================================================================
    //  Page thumbnails (left rail)
    // =====================================================================
    private void RebuildThumbs()
    {
        ThumbList.Children.Clear();
        for (int i = 0; i < _pages.Count; i++) ThumbList.Children.Add(BuildThumb(i));
    }

    private void RefreshThumbAt(int index)
    {
        if (index < 0 || index >= ThumbList.Children.Count) { RebuildThumbs(); return; }
        // Replace in place: the children collection's indexer setter rejects an occupied slot
        // ("Specified index is already in use"), so remove the old thumb before inserting the new one.
        ThumbList.Children.RemoveAt(index);
        ThumbList.Children.Insert(index, BuildThumb(index));
    }

    private void RefreshThumbHighlight()
    {
        for (int i = 0; i < ThumbList.Children.Count; i++)
            if (ThumbList.Children[i] is System.Windows.Controls.Border b)
                b.BorderBrush = (Brush)FindResource(i == _cur ? "AccentBrush" : "BorderSoftBrush");
    }

    private System.Windows.Controls.Border BuildThumb(int index)
    {
        var page = _pages[index];
        double ratio = _pageHpt / _pageWpt;
        const double tw = 132;
        double th = tw * ratio;
        double k = tw / _pageWpt;

        var canvas = new System.Windows.Controls.Canvas { Width = tw, Height = th, Background = Brushes.White, ClipToBounds = true };
        for (int i = 0; i < page.Images.Count; i++)
        {
            var p = page.Images[i];
            var tg = new TransformGroup();
            tg.Children.Add(new ScaleTransform(p.FlipH ? -1 : 1, p.FlipV ? -1 : 1));
            tg.Children.Add(new RotateTransform(p.Angle));
            var img = new System.Windows.Controls.Image
            {
                Source = p.Bmp, Stretch = Stretch.Fill,
                Width = p.W * k, Height = p.H * k,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = tg,
            };
            System.Windows.Controls.Canvas.SetLeft(img, p.X * k);
            System.Windows.Controls.Canvas.SetTop(img, p.Y * k);
            canvas.Children.Add(img);
        }

        var pageBox = new System.Windows.Controls.Border
        {
            Background = Brushes.White, Child = canvas, Width = tw, Height = th,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.4, Color = Colors.Black },
        };

        var num = new TextBlock
        {
            Text = $"Page {index + 1}", FontSize = 11,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        };

        var del = new Button
        {
            Content = "✕", Width = 22, Height = 22, FontSize = 11, Cursor = Cursors.Hand,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Background = (Brush)FindResource("SurfaceAltBrush"),
            BorderThickness = new Thickness(0), Padding = new Thickness(0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0), ToolTip = "Delete page",
            Visibility = _pages.Count > 1 ? Visibility.Visible : Visibility.Collapsed,
        };
        del.Click += (_, e) => { e.Handled = true; DeletePage(index); };

        var inner = new System.Windows.Controls.Grid();
        inner.Children.Add(new System.Windows.Controls.StackPanel { Children = { pageBox, num } });
        inner.Children.Add(del);

        var card = new System.Windows.Controls.Border
        {
            BorderBrush = (Brush)FindResource(index == _cur ? "AccentBrush" : "BorderSoftBrush"),
            BorderThickness = new Thickness(index == _cur ? 2 : 1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 6, 6, 4),
            Margin = new Thickness(0, 0, 0, 10),
            Background = (Brush)FindResource("SurfaceAltBrush"),
            Cursor = Cursors.Hand,
            Child = inner,
        };
        card.MouseLeftButtonDown += (_, _) => { _cur = index; LoadPage(); };
        return card;
    }

    private void DeletePage(int index)
    {
        if (_pages.Count <= 1) return;
        _pages.RemoveAt(index);
        if (_cur >= _pages.Count) _cur = _pages.Count - 1;
        RebuildThumbs();
        LoadPage();
    }

    // =====================================================================
    //  Export
    // =====================================================================
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        int total = _pages.Sum(p => p.Images.Count);
        if (total == 0)
        {
            ConfirmDialog.Alert(this, "Nothing to Export", "Add at least one image before exporting.");
            return;
        }

        string defDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", "PDF");
        try { Directory.CreateDirectory(defDir); } catch { }

        var dlg = new SaveFileDialog
        {
            Title = "Export PDF",
            Filter = "PDF document|*.pdf",
            FileName = "images.pdf",
            InitialDirectory = defDir,
        };
        if (dlg.ShowDialog(this) != true) return;
        string outPath = dlg.FileName;

        var specs = new List<FileToolsService.ComposePageSpec>();
        foreach (var page in _pages)
        {
            var spec = new FileToolsService.ComposePageSpec { WidthPt = _pageWpt, HeightPt = _pageHpt };
            foreach (var p in page.Images)
                spec.Images.Add(new FileToolsService.ComposePlacement { SourcePath = p.Path, Cm = ComputeCm(p) });
            specs.Add(spec);
        }

        BtnExport.IsEnabled = false;
        BtnExport.Content = "Exporting…";
        try
        {
            await FileToolsService.ComposePdfAsync(specs, outPath);
            long size = new FileInfo(outPath).Length;
            var choice = ConfirmDialog.PromptSaved(this, "PDF Created",
                $"Exported {_pages.Count} page(s) ({FileToolsService.FormatFileSize(size)}) to:\n{outPath}");
            if (choice == ConfirmDialog.SavedChoice.Open) DocumentScanWindow.OpenSaved(outPath);
            else if (choice == ConfirmDialog.SavedChoice.StartOver) StartOver();
        }
        catch (Exception ex)
        {
            ConfirmDialog.Alert(this, "Export Failed", ex.Message, ConfirmDialog.AlertKind.Error);
        }
        finally
        {
            BtnExport.IsEnabled = true;
            BtnExport.Content = "⬇  Export PDF";
        }
    }

    /// <summary>Builds the PDF cm matrix mapping this image's unit square to page points (Y-up),
    /// honouring rotation and horizontal/vertical flip.</summary>
    private double[] ComputeCm(Placed p)
    {
        // Image-content unit (u,v) → element-local point (top-left origin, Y-down), accounting for flip.
        // v is "up" in image space; element y is "down", hence the (1-v) when not flipped.
        (double x, double y) ToPdf(double u, double v)
        {
            double ex = p.FlipH ? (1 - u) * p.W : u * p.W;
            double ey = p.FlipV ? v * p.H : (1 - v) * p.H;
            var (rx, ry) = RotateVec(ex - p.W / 2, ey - p.H / 2, p.Angle);
            double lx = p.X + p.W / 2 + rx;       // layout point (Y-down)
            double ly = p.Y + p.H / 2 + ry;
            return (lx, _pageHpt - ly);           // PDF point (Y-up)
        }

        var p00 = ToPdf(0, 0); // unit bottom-left
        var p10 = ToPdf(1, 0); // unit bottom-right
        var p01 = ToPdf(0, 1); // unit top-left

        return new[]
        {
            p10.x - p00.x, p10.y - p00.y, // a, b
            p01.x - p00.x, p01.y - p00.y, // c, d
            p00.x,         p00.y,         // e, f
        };
    }
}
