using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Llamashot.Core;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Llamashot.Views;

/// <summary>
/// Interactive background-removal editor: auto-detect (border flood-fill), magic-wand click,
/// erase / restore / blur brushes, undo/redo, and export as transparent PNG or flattened JPG
/// (background colour of your choice). Works on a BGRA pixel buffer (<see cref="BackgroundRemover"/>).
/// </summary>
public partial class RemoveBackgroundWindow : Window
{
    private enum ToolKind { Wand, Erase, Restore, Blur, Pan }

    private byte[]? _pixels;     // working BGRA buffer
    private byte[]? _orig;       // pristine copy (for Restore)
    private int _w, _h;
    private double _scale = 1;
    private WriteableBitmap? _wb;

    private byte[]? _ghost;      // removed-area preview (shown while Restore is active)
    private WriteableBitmap? _ghostWb;

    private ToolKind _tool = ToolKind.Wand;
    private int _brush = 40;
    private int _tolerance = 30;
    private int _smooth = 2;     // edge-smoothing radius
    private string _bgHex = "#FFFFFF";

    private bool _painting;
    private Point _lastImg;

    // Zoom / pan
    private double _panX, _panY, _fitScale = 1, _minScale = 0.05;
    private bool _userZoomed;
    private bool _panning;
    private Point _panStart;
    private double _panOX, _panOY;
    private const double MaxZoom = 64; // pixel-level inspection

    private readonly Stack<byte[]> _undo = new();
    private readonly Stack<byte[]> _redo = new();
    private const int MaxUndo = 24;
    private const int MaxDim = 4000; // cap working resolution for responsiveness

    private string? _testImage; // harness-only: load this instead of prompting

    public RemoveBackgroundWindow()
    {
        InitializeComponent();
        UpdateToolButtons();
        UpdateUndoButtons();
        Loaded += (_, _) =>
        {
            if (_testImage != null) LoadFrom(_testImage);
            else SetLoadedUi(false); // start on the empty state with a centered Open button
        };
        KeyDown += OnKeyDown;
    }

    /// <summary>Harness-only: open with a fixed image (no file dialog).</summary>
    public RemoveBackgroundWindow(string testImage) : this() => _testImage = testImage;
    /// <summary>Harness-only: run Auto Remove.</summary>
    public void TestAuto() => Auto_Click(this, new RoutedEventArgs());
    /// <summary>Harness-only: zoom about the view centre.</summary>
    public void TestZoom(double scale) => ZoomTo(scale, ViewCenter());
    /// <summary>Harness-only: select a tool by name.</summary>
    public void TestTool(string tag) { if (Enum.TryParse<ToolKind>(tag, out var t)) { _tool = t; UpdateToolButtons(); RefreshGhost(); } }

    // =====================================================================
    //  Load
    // =====================================================================
    private void Open_Click(object sender, RoutedEventArgs e) => OpenImage();

    private bool OpenImage()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true) return false;
        return LoadFrom(dlg.FileName);
    }

    private bool LoadFrom(string fileName)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(fileName);
            bi.EndInit();
            bi.Freeze();

            BitmapSource src = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);

            // Downscale very large images so brushes/flood-fill stay snappy.
            int pw = src.PixelWidth, ph = src.PixelHeight;
            if (Math.Max(pw, ph) > MaxDim)
            {
                double f = (double)MaxDim / Math.Max(pw, ph);
                src = new TransformedBitmap(src, new ScaleTransform(f, f));
            }

            _w = src.PixelWidth; _h = src.PixelHeight;
            _pixels = new byte[_w * _h * 4];
            src.CopyPixels(_pixels, _w * 4, 0);
            _orig = (byte[])_pixels.Clone();

            _wb = new WriteableBitmap(_w, _h, 96, 96, PixelFormats.Bgra32, null);
            Img.Source = _wb;
            WriteFull();

            _undo.Clear(); _redo.Clear(); UpdateUndoButtons();
            TxtHint.Visibility = Visibility.Collapsed;
            Title = $"Remove Background — {Path.GetFileName(fileName)}";
            _userZoomed = false;
            SetLoadedUi(true);
            FitScale();
            return true;
        }
        catch (Exception ex)
        {
            ConfirmDialog.Alert(this, "Can't Open Image", ex.Message, ConfirmDialog.AlertKind.Error);
            return _pixels != null;
        }
    }

    private void CloseFile_Click(object sender, RoutedEventArgs e)
    {
        if (_pixels == null) return;
        if (_undo.Count > 0 && !ConfirmDialog.Show(this, "Close image?",
                "Close the current image? Unsaved edits will be lost.", "Close", "Keep editing"))
            return;
        UnloadFile();
    }

    /// <summary>Drops the loaded image and returns to the empty state (centered Open button).</summary>
    private void UnloadFile()
    {
        _pixels = null; _orig = null; _wb = null; _ghost = null; _ghostWb = null;
        Img.Source = null; GhostImg.Source = null; GhostImg.Visibility = Visibility.Collapsed;
        Img.Width = 0; Img.Height = 0;
        _undo.Clear(); _redo.Clear();
        Title = "Remove Background";
        SetLoadedUi(false);
    }

    /// <summary>Toggles between the empty state and the editor, enabling the relevant toolbar actions.</summary>
    private void SetLoadedUi(bool has)
    {
        EmptyState.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        TxtHint.Visibility = Visibility.Collapsed;
        Button[] need =
        {
            BtnClose, BtnAuto, BtnAi, BtnWand, BtnErase, BtnRestore, BtnBlur, BtnPan,
            BtnExportPng, BtnExportJpg, BtnBgColor,
        };
        foreach (var b in need) b.IsEnabled = has;
        SldTol.IsEnabled = has; SldBrush.IsEnabled = has; SldEdge.IsEnabled = has;
        if (has) UpdateUndoButtons();
        else { BtnUndo.IsEnabled = false; BtnRedo.IsEnabled = false; }
    }

    // =====================================================================
    //  Display: zoom (fit … pixel level) + pan
    // =====================================================================
    private void CenterHost_SizeChanged(object sender, SizeChangedEventArgs e) => FitScale();

    private void FitScale()
    {
        if (_wb == null || CenterHost.ActualWidth <= 0 || CenterHost.ActualHeight <= 0) return;
        double availW = CenterHost.ActualWidth - 48, availH = CenterHost.ActualHeight - 48;
        _fitScale = Math.Max(0.02, Math.Min(availW / _w, availH / _h));
        if (_fitScale > 1) _fitScale = 1; // fit never upscales
        _minScale = Math.Min(_fitScale, 0.05);
        if (!_userZoomed) { _scale = _fitScale; CenterImage(); }
        LayoutImg();
    }

    private void CenterImage()
    {
        _panX = (CenterHost.ActualWidth - _w * _scale) / 2;
        _panY = (CenterHost.ActualHeight - _h * _scale) / 2;
    }

    private void LayoutImg()
    {
        if (_wb == null) return;
        Img.Width = _w * _scale;
        Img.Height = _h * _scale;
        GhostImg.Width = _w * _scale;
        GhostImg.Height = _h * _scale;
        System.Windows.Controls.Canvas.SetLeft(ImgHost, _panX);
        System.Windows.Controls.Canvas.SetTop(ImgHost, _panY);
        // Crisp pixels when zoomed in; smooth when fit/out.
        var mode = _scale > 1.5 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality;
        RenderOptions.SetBitmapScalingMode(Img, mode);
        RenderOptions.SetBitmapScalingMode(GhostImg, mode);
        if (TxtZoom != null) TxtZoom.Text = $"{_scale * 100:0}%";
    }

    private void ZoomTo(double newScale, Point vp)
    {
        if (_wb == null) return;
        newScale = Math.Clamp(newScale, _minScale, MaxZoom);
        double imgX = (vp.X - _panX) / _scale, imgY = (vp.Y - _panY) / _scale;
        _scale = newScale;
        _userZoomed = _scale > _fitScale * 1.001 || _scale < _fitScale * 0.999;
        _panX = vp.X - imgX * _scale;
        _panY = vp.Y - imgY * _scale;
        LayoutImg();
    }

    private void Canvas_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (_wb == null) return;
        ZoomTo(_scale * (e.Delta > 0 ? 1.2 : 1 / 1.2), e.GetPosition(PanCanvas));
        e.Handled = true;
    }

    private Point ViewCenter() => new(CenterHost.ActualWidth / 2, CenterHost.ActualHeight / 2);
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomTo(_scale * 1.25, ViewCenter());
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomTo(_scale / 1.25, ViewCenter());
    private void OneToOne_Click(object sender, RoutedEventArgs e) => ZoomTo(1.0, ViewCenter());
    private void Fit_Click(object sender, RoutedEventArgs e) { _userZoomed = false; FitScale(); }

    // Pan with the Pan tool (left button) or middle / right button in any tool.
    private System.Windows.IInputElement? _panCapture;

    private void BeginPan(Point startInHost, System.Windows.IInputElement capture)
    {
        _panning = true;
        _panStart = startInHost;
        _panOX = _panX; _panOY = _panY;
        Mouse.OverrideCursor = Cursors.ScrollAll;
        _panCapture = capture;
        capture.CaptureMouse();
    }

    private void DoPan(Point posInHost)
    {
        _panX = _panOX + (posInHost.X - _panStart.X);
        _panY = _panOY + (posInHost.Y - _panStart.Y);
        LayoutImg();
    }

    private void EndPan()
    {
        if (!_panning) return;
        _panning = false;
        Mouse.OverrideCursor = null;
        _panCapture?.ReleaseMouseCapture();
        _panCapture = null;
    }

    private void Pan_Down(object sender, MouseButtonEventArgs e)
    {
        if (_wb == null || _panning) return;
        if (e.ChangedButton != MouseButton.Middle && e.ChangedButton != MouseButton.Right) return;
        BeginPan(e.GetPosition(CenterHost), PanCanvas);
        e.Handled = true;
    }

    private void Pan_Move(object sender, MouseEventArgs e)
    {
        if (_panning) DoPan(e.GetPosition(CenterHost));
    }

    private void Pan_Up(object sender, MouseButtonEventArgs e)
    {
        if (_panning) EndPan();
    }

    private void WriteFull()
    {
        if (_wb == null || _pixels == null) return;
        _wb.WritePixels(new Int32Rect(0, 0, _w, _h), _pixels, _w * 4, 0);
    }

    private void WriteRect(int x, int y, int rw, int rh)
    {
        if (_wb == null || _pixels == null) return;
        x = Math.Clamp(x, 0, _w - 1); y = Math.Clamp(y, 0, _h - 1);
        rw = Math.Clamp(rw, 1, _w - x); rh = Math.Clamp(rh, 1, _h - y);
        _wb.WritePixels(new Int32Rect(x, y, rw, rh), _pixels, _w * 4, (y * _w + x) * 4);
    }

    // =====================================================================
    //  Removed-area ghost (shown faintly while the Restore tool is active so you
    //  can see what was cut and decide where to paint back)
    // =====================================================================
    private void RefreshGhost()
    {
        if (_pixels == null || _orig == null || _tool != ToolKind.Restore)
        {
            if (GhostImg != null) GhostImg.Visibility = Visibility.Collapsed;
            return;
        }
        if (_ghost == null || _ghost.Length != _pixels.Length)
        {
            _ghost = new byte[_pixels.Length];
            _ghostWb = new WriteableBitmap(_w, _h, 96, 96, PixelFormats.Bgra32, null);
            GhostImg.Source = _ghostWb;
        }
        for (int p = 0; p < _w * _h; p++)
        {
            int i = p * 4;
            if (_pixels[i + 3] == 0) { _ghost[i] = _orig[i]; _ghost[i + 1] = _orig[i + 1]; _ghost[i + 2] = _orig[i + 2]; _ghost[i + 3] = 255; }
            else _ghost[i + 3] = 0;
        }
        _ghostWb!.WritePixels(new Int32Rect(0, 0, _w, _h), _ghost, _w * 4, 0);
        GhostImg.Visibility = Visibility.Visible;
        LayoutImg();
    }

    private void UpdateGhostRect(int x, int y, int rw, int rh)
    {
        if (_tool != ToolKind.Restore || _ghost == null || _ghostWb == null || _pixels == null || _orig == null) return;
        x = Math.Clamp(x, 0, _w - 1); y = Math.Clamp(y, 0, _h - 1);
        rw = Math.Clamp(rw, 1, _w - x); rh = Math.Clamp(rh, 1, _h - y);
        for (int yy = y; yy < y + rh; yy++)
            for (int xx = x; xx < x + rw; xx++)
            {
                int i = (yy * _w + xx) * 4;
                if (_pixels[i + 3] == 0) { _ghost[i] = _orig[i]; _ghost[i + 1] = _orig[i + 1]; _ghost[i + 2] = _orig[i + 2]; _ghost[i + 3] = 255; }
                else _ghost[i + 3] = 0;
            }
        _ghostWb.WritePixels(new Int32Rect(x, y, rw, rh), _ghost, _w * 4, (y * _w + x) * 4);
    }

    // =====================================================================
    //  Toolbar
    // =====================================================================
    private void Tol_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _tolerance = (int)e.NewValue;
        if (TxtTol != null) TxtTol.Text = $"TOLERANCE — {_tolerance}";
    }

    private void Brush_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _brush = (int)e.NewValue;
        if (TxtBrush != null) TxtBrush.Text = $"BRUSH — {_brush} px";
    }

    private void Edge_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _smooth = (int)e.NewValue;
        if (TxtEdge != null) TxtEdge.Text = $"EDGE — {_smooth} px";
    }

    /// <summary>Feathers the cut-out edge with the current Edge-slider radius.</summary>
    private void SmoothEdges()
    {
        if (_pixels != null && _smooth > 0) BackgroundRemover.SmoothAlpha(_pixels, _w, _h, _smooth);
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tag && Enum.TryParse<ToolKind>(tag, out var t))
            _tool = t;
        UpdateToolButtons();
        RefreshGhost();
    }

    private void UpdateToolButtons()
    {
        void Set(Button b, ToolKind k)
        {
            bool on = _tool == k;
            b.BorderBrush = (Brush)FindResource(on ? "AccentBrush" : "BorderSoftBrush");
            b.BorderThickness = new Thickness(on ? 2 : 1);
        }
        Set(BtnWand, ToolKind.Wand);
        Set(BtnErase, ToolKind.Erase);
        Set(BtnRestore, ToolKind.Restore);
        Set(BtnBlur, ToolKind.Blur);
        Set(BtnPan, ToolKind.Pan);

        // Cursor reflects the active tool.
        if (Img != null)
            Img.Cursor = _tool switch
            {
                ToolKind.Wand => Cursors.Cross,
                ToolKind.Pan => Cursors.SizeAll,
                _ => MakeDotCursor(),
            };
    }

    private static Cursor? _dotCursor;
    private static Cursor MakeDotCursor()
    {
        if (_dotCursor != null) return _dotCursor;
        // A tiny centred dot so the exact brush centre is visible (the ring shows the radius).
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(0, 0, 24, 24));
            dc.DrawEllipse(System.Windows.Media.Brushes.White,
                new System.Windows.Media.Pen(System.Windows.Media.Brushes.Black, 1), new Point(12, 12), 2.5, 2.5);
        }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(24, 24, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        png.Save(ms);
        _dotCursor = CursorFromPng(ms.ToArray(), 12, 12);
        return _dotCursor;
    }

    // Wrap a PNG as a .cur stream so WPF can load it as a Cursor with a hotspot.
    private static Cursor CursorFromPng(byte[] png, ushort hotX, ushort hotY)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            bw.Write((ushort)0); bw.Write((ushort)2); bw.Write((ushort)1);          // ICONDIR: reserved, type=2 (cursor), count=1
            bw.Write((byte)24); bw.Write((byte)24); bw.Write((byte)0); bw.Write((byte)0); // w,h,colorcount,reserved
            bw.Write(hotX); bw.Write(hotY);                                          // hotspot
            bw.Write(png.Length); bw.Write(22);                                      // bytes, offset
            bw.Write(png);
        }
        ms.Position = 0;
        return new Cursor(ms);
    }

    private void Auto_Click(object sender, RoutedEventArgs e)
    {
        if (_pixels == null) return;
        PushUndo();
        BackgroundRemover.AutoRemove(_pixels, _w, _h, _tolerance);
        SmoothEdges();
        WriteFull();
        RefreshGhost();
    }

    private async void AiRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_pixels == null) return;

        BusyOverlay.Visibility = Visibility.Visible;
        BtnAi.IsEnabled = false; BtnAuto.IsEnabled = false;
        try
        {
            if (!AiMatting.ModelExists)
            {
                PrgBusy.IsIndeterminate = false;
                TxtBusy.Text = "Downloading AI model (~168 MB, one time)…";
                var prog = new Progress<double>(p =>
                {
                    PrgBusy.Value = p;
                    TxtBusy.Text = $"Downloading AI model… {p:0}%";
                });
                await AiMatting.EnsureModelAsync(prog);
            }

            PrgBusy.IsIndeterminate = true;
            TxtBusy.Text = "Detecting subject…";
            byte[] snapshot = (byte[])_pixels.Clone();
            int w = _w, h = _h;
            byte[] mask = await System.Threading.Tasks.Task.Run(() => AiMatting.ComputeMask(snapshot, w, h));

            PushUndo();
            AiMatting.ApplyMask(_pixels, mask);
            SmoothEdges();
            WriteFull();
            RefreshGhost();
        }
        catch (Exception ex)
        {
            ConfirmDialog.Alert(this, "AI Removal Failed",
                $"{ex.Message}\n\nIf this is the first run, check your internet connection — the model needs to download once.",
                ConfirmDialog.AlertKind.Error);
        }
        finally
        {
            BusyOverlay.Visibility = Visibility.Collapsed;
            PrgBusy.IsIndeterminate = true; PrgBusy.Value = 0;
            BtnAi.IsEnabled = true; BtnAuto.IsEnabled = true;
        }
    }

    private void BgColor_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ColorPickerDialog(_bgHex) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.ResultHex != null)
        {
            _bgHex = dlg.ResultHex;
            BtnBgColor.Background = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(_bgHex));
        }
    }

    // =====================================================================
    //  Canvas interaction
    // =====================================================================
    private bool ToImage(Point p, out int ix, out int iy)
    {
        ix = (int)(p.X / _scale); iy = (int)(p.Y / _scale);
        bool inside = ix >= 0 && iy >= 0 && ix < _w && iy < _h;
        ix = Math.Clamp(ix, 0, _w - 1); iy = Math.Clamp(iy, 0, _h - 1);
        return inside;
    }

    private void Img_Down(object sender, MouseButtonEventArgs e)
    {
        if (_pixels == null) return;

        // Pan tool: left-drag pans the (zoomed) image.
        if (_tool == ToolKind.Pan)
        {
            BeginPan(e.GetPosition(CenterHost), Img);
            e.Handled = true;
            return;
        }

        if (!ToImage(e.GetPosition(Img), out int ix, out int iy)) return;
        PushUndo();

        if (_tool == ToolKind.Wand)
        {
            BackgroundRemover.MagicWand(_pixels, _w, _h, ix, iy, _tolerance);
            SmoothEdges();
            WriteFull();
            RefreshGhost();
            return;
        }

        _painting = true;
        _lastImg = new Point(ix, iy);
        ApplyBrush(ix, iy);
        Img.CaptureMouse();
    }

    private void Img_Move(object sender, MouseEventArgs e)
    {
        if (_pixels == null) return;
        if (_panning) { DoPan(e.GetPosition(CenterHost)); return; }

        var pc = e.GetPosition(CenterHost);
        UpdateBrushRing(pc);

        if (!_painting) return;
        ToImage(e.GetPosition(Img), out int ix, out int iy);
        // Interpolate along the drag so fast strokes leave no gaps.
        double dist = Math.Max(Math.Abs(ix - _lastImg.X), Math.Abs(iy - _lastImg.Y));
        int steps = (int)Math.Max(1, dist / Math.Max(1, _brush / 4.0));
        for (int s = 1; s <= steps; s++)
        {
            int px = (int)(_lastImg.X + (ix - _lastImg.X) * s / steps);
            int py = (int)(_lastImg.Y + (iy - _lastImg.Y) * s / steps);
            ApplyBrush(px, py);
        }
        _lastImg = new Point(ix, iy);
    }

    private void Img_Up(object sender, MouseButtonEventArgs e)
    {
        if (_panning) { EndPan(); return; }
        if (!_painting) return;
        _painting = false;
        Img.ReleaseMouseCapture();
    }

    private void Img_Leave(object sender, MouseEventArgs e) => BrushRing.Visibility = Visibility.Collapsed;

    private void ApplyBrush(int ix, int iy)
    {
        if (_pixels == null) return;
        int r = Math.Max(1, _brush / 2);
        (int x, int y, int w, int h) dirty = _tool switch
        {
            ToolKind.Erase => BackgroundRemover.Brush(_pixels, null, _w, _h, ix, iy, r, erase: true),
            ToolKind.Restore => BackgroundRemover.Brush(_pixels, _orig, _w, _h, ix, iy, r, erase: false),
            ToolKind.Blur => BackgroundRemover.BlurAt(_pixels, _w, _h, ix, iy, r, 3),
            _ => (ix, iy, 1, 1),
        };
        WriteRect(dirty.x, dirty.y, dirty.w, dirty.h);
        if (_tool == ToolKind.Restore || _tool == ToolKind.Erase) UpdateGhostRect(dirty.x, dirty.y, dirty.w, dirty.h);
    }

    private void UpdateBrushRing(Point posInHost)
    {
        if (_pixels == null || _tool == ToolKind.Wand || _tool == ToolKind.Pan) { BrushRing.Visibility = Visibility.Collapsed; return; }
        double d = _brush * _scale;
        BrushRing.Width = d; BrushRing.Height = d;
        System.Windows.Controls.Canvas.SetLeft(BrushRing, posInHost.X - d / 2);
        System.Windows.Controls.Canvas.SetTop(BrushRing, posInHost.Y - d / 2);
        BrushRing.Visibility = Visibility.Visible;
    }

    // =====================================================================
    //  Undo / redo
    // =====================================================================
    private void PushUndo()
    {
        if (_pixels == null) return;
        _undo.Push((byte[])_pixels.Clone());
        if (_undo.Count > MaxUndo)
        {
            var keep = _undo.ToArray(); // top..bottom
            _undo.Clear();
            for (int i = Math.Min(MaxUndo, keep.Length) - 1; i >= 0; i--) _undo.Push(keep[i]);
        }
        _redo.Clear();
        UpdateUndoButtons();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0 || _pixels == null) return;
        _redo.Push((byte[])_pixels.Clone());
        _pixels = _undo.Pop();
        WriteFull();
        RefreshGhost();
        UpdateUndoButtons();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redo.Count == 0 || _pixels == null) return;
        _undo.Push((byte[])_pixels.Clone());
        _pixels = _redo.Pop();
        WriteFull();
        RefreshGhost();
        UpdateUndoButtons();
    }

    private void UpdateUndoButtons()
    {
        if (BtnUndo != null) BtnUndo.IsEnabled = _undo.Count > 0;
        if (BtnRedo != null) BtnRedo.IsEnabled = _redo.Count > 0;
    }

    // Hard-coded keyboard shortcuts (not customizable).
    //   Tools:   W Wand · E Erase · R Restore · B Blur · H/Space Pan
    //   Actions: A Auto · I AI · O Open · Ctrl+S PNG · Ctrl+Shift+S JPG · Ctrl+Z/Y Undo/Redo
    //   Brush:   [ smaller · ] bigger
    //   Zoom:    + in · − out · F/0 fit · 1 actual size
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var none = new RoutedEventArgs();

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.Z) { Undo_Click(this, none); e.Handled = true; }
            else if (e.Key == Key.Y) { Redo_Click(this, none); e.Handled = true; }
            else if (e.Key == Key.S) { ExportPng_Click(this, none); e.Handled = true; }
            else if (e.Key == Key.OemPlus || e.Key == Key.Add) { ZoomIn_Click(this, none); e.Handled = true; }
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract) { ZoomOut_Click(this, none); e.Handled = true; }
            else if (e.Key == Key.D0 || e.Key == Key.NumPad0) { Fit_Click(this, none); e.Handled = true; }
            return;
        }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (e.Key == Key.S) { ExportJpg_Click(this, none); e.Handled = true; }
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None) return; // ignore Alt / lone Shift combos

        switch (e.Key)
        {
            // Tools
            case Key.W: SelectTool(ToolKind.Wand); break;
            case Key.E: SelectTool(ToolKind.Erase); break;
            case Key.R: SelectTool(ToolKind.Restore); break;
            case Key.B: SelectTool(ToolKind.Blur); break;
            case Key.H: case Key.Space: SelectTool(ToolKind.Pan); break;
            // Actions
            case Key.A: Auto_Click(this, none); break;
            case Key.I: AiRemove_Click(this, none); break;
            case Key.O: OpenImage(); break;
            // Brush size
            case Key.OemOpenBrackets: NudgeBrush(-10); break;
            case Key.OemCloseBrackets: NudgeBrush(+10); break;
            // Close file
            case Key.Escape: CloseFile_Click(this, new RoutedEventArgs()); break;
            // Zoom
            case Key.F: case Key.D0: case Key.NumPad0: Fit_Click(this, none); break;
            case Key.D1: case Key.NumPad1: OneToOne_Click(this, none); break;
            case Key.OemPlus: case Key.Add: ZoomIn_Click(this, none); break;
            case Key.OemMinus: case Key.Subtract: ZoomOut_Click(this, none); break;
            default: return;
        }
        e.Handled = true;
    }

    private void SelectTool(ToolKind k)
    {
        _tool = k;
        UpdateToolButtons();
        RefreshGhost();
    }

    private void NudgeBrush(int delta)
        => SldBrush.Value = Math.Clamp(SldBrush.Value + delta, SldBrush.Minimum, SldBrush.Maximum);

    // Guard against accidental loss of an in-progress cut-out.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_undo.Count > 0 && !ConfirmDialog.Show(this, "Discard cut-out?",
                "You've edited this image but haven't exported it. Close and lose your changes?",
                "Close", "Keep editing"))
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    // =====================================================================
    //  Export
    // =====================================================================
    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        if (_wb == null) return;
        string? path = AskSave("PNG image|*.png", ".png");
        if (path == null) return;
        try
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(_wb));
            using var fs = new FileStream(path, FileMode.Create);
            enc.Save(fs);
            Saved(path);
        }
        catch (Exception ex) { ConfirmDialog.Alert(this, "Export Failed", ex.Message, ConfirmDialog.AlertKind.Error); }
    }

    private void ExportJpg_Click(object sender, RoutedEventArgs e)
    {
        if (_pixels == null) return;
        string? path = AskSave("JPEG image|*.jpg", ".jpg");
        if (path == null) return;
        try
        {
            var c = (Color)System.Windows.Media.ColorConverter.ConvertFromString(_bgHex);
            byte[] flat = BackgroundRemover.FlattenOnto(_pixels, c.B, c.G, c.R);
            var bmp = BitmapSource.Create(_w, _h, 96, 96, PixelFormats.Bgra32, null, flat, _w * 4);
            var enc = new JpegBitmapEncoder { QualityLevel = 92 };
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = new FileStream(path, FileMode.Create);
            enc.Save(fs);
            Saved(path);
        }
        catch (Exception ex) { ConfirmDialog.Alert(this, "Export Failed", ex.Message, ConfirmDialog.AlertKind.Error); }
    }

    private string? AskSave(string filter, string ext)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", "Images");
        try { Directory.CreateDirectory(dir); } catch { }
        var dlg = new SaveFileDialog { Filter = filter, FileName = "cutout" + ext, InitialDirectory = dir };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    private void Saved(string path)
    {
        long size = new FileInfo(path).Length;
        var choice = ConfirmDialog.PromptSaved(this, "Image Saved",
            $"Saved ({FileToolsService.FormatFileSize(size)}) to:\n{path}");
        switch (choice)
        {
            case ConfirmDialog.SavedChoice.Open: DocumentScanWindow.OpenSaved(path); break;
            case ConfirmDialog.SavedChoice.StartOver: UnloadFile(); break;
            // Done: keep editing.
        }
    }
}
