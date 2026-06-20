using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Llamashot.Core;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Llamashot.Views;

/// <summary>
/// Document scanner: open a skewed photo of a card / receipt / page, drag the four corners onto
/// the document (or auto-detect them), and warp the quadrilateral into a flat, perfectly
/// rectangular scan. Apply a filter (None / Magic Colour / Grayscale / B&amp;W) and save as
/// PNG, JPG, or PDF. Powered by <see cref="DocumentScanner"/>.
/// </summary>
public partial class DocumentScanWindow : Window
{
    private byte[]? _src;            // source photo BGRA
    private int _w, _h;
    private WriteableBitmap? _wb;

    private byte[]? _result;         // warped rectangle BGRA
    private int _rw, _rh;
    private WriteableBitmap? _resultWb;

    private readonly (double x, double y)[] _corners = new (double, double)[4]; // TL,TR,BR,BL (image px)
    private DocumentScanner.Enhance _enhance = DocumentScanner.Enhance.Original;
    private bool _resultStage;

    // Display fit
    private double _scale = 1, _panX, _panY;
    private const int MaxDim = 4000;

    private string? _testImage; // harness-only / initial image to auto-load
    private bool _pickMode;     // "return a flattened scan to the caller" mode (e.g. Images → PDF)

    /// <summary>When opened in pick mode and the user clicks "Use in PDF", the path of the saved
    /// flattened scan (a PNG in the app's scans folder). Null if the dialog was cancelled.</summary>
    public string? PickResultPath { get; private set; }

    public DocumentScanWindow()
    {
        InitializeComponent();
        UpdateFilterButtons();
        UpdateStageUi();
        Loaded += (_, _) =>
        {
            if (_testImage != null) LoadFrom(_testImage);
            else SetLoadedUi(false); // start on the empty state with a centered Open button
            if (_pickMode) ConfigurePickMode();
        };
        KeyDown += OnKeyDown;
    }

    /// <summary>Harness-only: open with a fixed image (no dialog).</summary>
    public DocumentScanWindow(string testImage) : this() => _testImage = testImage;

    /// <summary>Open on a given image to return a flattened scan to the caller (Images → PDF).
    /// Show it with <c>ShowDialog()</c> and read <see cref="PickResultPath"/> when it returns true.</summary>
    public DocumentScanWindow(string imagePath, bool pickMode) : this()
    {
        _testImage = imagePath;
        _pickMode = pickMode;
    }

    private void ConfigurePickMode()
    {
        Title = "Scan Document — for PDF";
        BtnApply.Visibility = Visibility.Visible;
        BtnSavePng.Visibility = Visibility.Collapsed;
        BtnSaveJpg.Visibility = Visibility.Collapsed;
        BtnSavePdf.Visibility = Visibility.Collapsed;
        BtnClose.Visibility = Visibility.Collapsed; // closing the window cancels the pick
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        EnsureResult();
        if (_resultWb == null) return;
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Llamashot", "scans");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"scan_{Guid.NewGuid():N}.png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(_resultWb));
            using (var fs = new FileStream(path, FileMode.Create)) enc.Save(fs);
            PickResultPath = path;
            DialogResult = true; // closes the dialog
        }
        catch (Exception ex) { ConfirmDialog.Alert(this, "Scan Failed", ex.Message, ConfirmDialog.AlertKind.Error); }
    }
    /// <summary>Harness-only.</summary>
    public bool TestAutoDetect() => TryAutoDetect();
    /// <summary>Harness-only.</summary>
    public void TestCrop() => DoCrop();
    /// <summary>Harness-only.</summary>
    public void TestFilter(string tag) { if (Enum.TryParse<DocumentScanner.Enhance>(tag, out var e)) { _enhance = e; UpdateFilterButtons(); if (_src != null) DoCrop(); } }
    /// <summary>Harness-only.</summary>
    public (int w, int h)? TestResultSize() => _result == null ? null : (_rw, _rh);
    /// <summary>Harness-only.</summary>
    public void TestRotate(bool cw) => Rotate(cw);
    /// <summary>Harness-only.</summary>
    public void TestFlip(bool horizontal) => Flip(horizontal);
    /// <summary>Harness-only.</summary>
    public bool TestHasImage() => _src != null;
    /// <summary>Harness-only: configure pick mode UI (call after the window is shown).</summary>
    public void TestPickMode() { _pickMode = true; ConfigurePickMode(); }
    /// <summary>Harness-only: close without the unsaved-result prompt.</summary>
    public void TestClose() { _result = null; _pickMode = true; Close(); }
    /// <summary>Harness-only: run the same logic as Apply but return the written path (no DialogResult).</summary>
    public string? TestApplyToFile()
    {
        EnsureResult();
        if (_resultWb == null) return null;
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Llamashot", "scans");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"scan_test_{Guid.NewGuid():N}.png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(_resultWb));
        using var fs = new FileStream(path, FileMode.Create); enc.Save(fs);
        return path;
    }

    // =====================================================================
    //  Load
    // =====================================================================
    private void Open_Click(object sender, RoutedEventArgs e) => OpenImage();

    private bool OpenImage()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open document photo",
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

            BitmapSource s = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
            if (Math.Max(s.PixelWidth, s.PixelHeight) > MaxDim)
            {
                double f = (double)MaxDim / Math.Max(s.PixelWidth, s.PixelHeight);
                s = new TransformedBitmap(s, new ScaleTransform(f, f));
            }

            _w = s.PixelWidth; _h = s.PixelHeight;
            _src = new byte[_w * _h * 4];
            s.CopyPixels(_src, _w * 4, 0);
            _wb = new WriteableBitmap(_w, _h, 96, 96, PixelFormats.Bgra32, null);
            _wb.WritePixels(new Int32Rect(0, 0, _w, _h), _src, _w * 4, 0);
            Img.Source = _wb;

            _result = null; _resultWb = null;
            ResetCorners();
            EnterAdjust();
            TxtHint.Visibility = Visibility.Collapsed;
            Title = $"Document Scan — {Path.GetFileName(fileName)}";

            SetLoadedUi(true);
            // Try auto-detect on load; silently fall back to full-frame corners.
            TryAutoDetect();
            FitToView();
            return true;
        }
        catch (Exception ex)
        {
            ConfirmDialog.Alert(this, "Can't Open Image", ex.Message, ConfirmDialog.AlertKind.Error);
            return _src != null;
        }
    }

    private void ResetCorners()
    {
        double mx = _w * 0.04, my = _h * 0.04; // small inset so handles are grabbable
        _corners[0] = (mx, my);
        _corners[1] = (_w - mx, my);
        _corners[2] = (_w - mx, _h - my);
        _corners[3] = (mx, _h - my);
    }

    private void CloseFile_Click(object sender, RoutedEventArgs e)
    {
        if (_src == null) return;
        if (_result != null && !ConfirmDialog.Show(this, "Close this file?",
                "Close the current document? Any unsaved scan will be lost.", "Close", "Keep editing"))
            return;
        UnloadFile();
    }

    /// <summary>Drops the loaded image and returns to the empty state (centered Open button).</summary>
    private void UnloadFile()
    {
        _src = null; _result = null; _wb = null; _resultWb = null;
        Img.Source = null; ResultImg.Source = null;
        _resultStage = false;
        UpdateStageUi();
        Title = "Document Scan";
        if (TxtSize != null) TxtSize.Text = "";
        SetLoadedUi(false);
    }

    /// <summary>Toggles between the empty state and the editor, enabling the relevant toolbar actions.</summary>
    private void SetLoadedUi(bool has)
    {
        EmptyState.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        TxtHint.Visibility = Visibility.Collapsed;
        if (!has) Overlay.Visibility = Visibility.Collapsed; // hide stray corner handles on the empty state
        Button[] needsImage =
        {
            BtnClose, BtnAuto, BtnReset, BtnRotL, BtnRotR, BtnFlipH, BtnFlipV,
            BtnCrop, BtnEdit, BtnFNone, BtnFMagic, BtnFGray, BtnFBw,
            BtnSavePng, BtnSaveJpg, BtnSavePdf,
        };
        foreach (var b in needsImage) b.IsEnabled = has;
    }

    // =====================================================================
    //  Display / fit
    // =====================================================================
    private int ViewW => _resultStage ? _rw : _w;
    private int ViewH => _resultStage ? _rh : _h;

    private void CenterHost_SizeChanged(object sender, SizeChangedEventArgs e) => FitToView();

    private void FitToView()
    {
        if (_src == null || CenterHost.ActualWidth <= 0 || CenterHost.ActualHeight <= 0) return;
        double availW = CenterHost.ActualWidth - 80, availH = CenterHost.ActualHeight - 80;
        _scale = Math.Max(0.02, Math.Min(availW / ViewW, availH / ViewH));
        if (_scale > 1) _scale = 1;
        _panX = (CenterHost.ActualWidth - ViewW * _scale) / 2;
        _panY = (CenterHost.ActualHeight - ViewH * _scale) / 2;
        LayoutContent();
    }

    private void LayoutContent()
    {
        var active = _resultStage ? ResultImg : Img;
        active.Width = ViewW * _scale;
        active.Height = ViewH * _scale;
        Canvas.SetLeft(active, _panX);
        Canvas.SetTop(active, _panY);
        if (TxtSize != null) TxtSize.Text = $"{ViewW} × {ViewH}px · {_scale * 100:0}%";
        if (!_resultStage) PositionHandles();
    }

    private void PositionHandles()
    {
        var thumbs = new[] { ThTL, ThTR, ThBR, ThBL };
        var pts = new PointCollection();
        for (int i = 0; i < 4; i++)
        {
            double cx = _corners[i].x * _scale + _panX, cy = _corners[i].y * _scale + _panY;
            Canvas.SetLeft(thumbs[i], cx - thumbs[i].Width / 2);
            Canvas.SetTop(thumbs[i], cy - thumbs[i].Height / 2);
            pts.Add(new Point(cx, cy));
        }
        Quad.Points = pts;
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => FitToView();

    // =====================================================================
    //  Corner dragging + magnifier loupe
    // =====================================================================
    private void Corner_Start(object sender, DragStartedEventArgs e)
    {
        Loupe.Visibility = Visibility.Visible;
        UpdateLoupe((Thumb)sender);
    }

    private void Corner_Drag(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb t || t.Tag is not string s || !int.TryParse(s, out int i)) return;
        double nx = Math.Clamp(_corners[i].x + e.HorizontalChange / _scale, 0, _w);
        double ny = Math.Clamp(_corners[i].y + e.VerticalChange / _scale, 0, _h);
        _corners[i] = (nx, ny);
        PositionHandles();
        UpdateLoupe(t);
    }

    private void Corner_Done(object sender, DragCompletedEventArgs e) => Loupe.Visibility = Visibility.Collapsed;

    private void UpdateLoupe(Thumb t)
    {
        if (_wb == null || t.Tag is not string s || !int.TryParse(s, out int i)) return;
        const int R = 45; // source-px radius shown in the loupe
        int cx = (int)_corners[i].x, cy = (int)_corners[i].y;
        int x0 = Math.Clamp(cx - R, 0, _w - 1), y0 = Math.Clamp(cy - R, 0, _h - 1);
        int rw = Math.Min(2 * R, _w - x0), rh = Math.Min(2 * R, _h - y0);
        if (rw < 2 || rh < 2) return;
        LoupeImg.Source = new CroppedBitmap(_wb, new Int32Rect(x0, y0, rw, rh));

        // Park the loupe in the corner of the view farthest from the handle so it never hides it.
        double hx = _corners[i].x * _scale + _panX, hy = _corners[i].y * _scale + _panY;
        double lx = hx < CenterHost.ActualWidth / 2 ? CenterHost.ActualWidth - 170 : 20;
        double ly = hy < CenterHost.ActualHeight / 2 ? CenterHost.ActualHeight - 170 : 20;
        Canvas.SetLeft(Loupe, lx);
        Canvas.SetTop(Loupe, ly);
    }

    // =====================================================================
    //  Detect / reset / rotate
    // =====================================================================
    private void AutoDetect_Click(object sender, RoutedEventArgs e)
    {
        if (_src == null) return;
        if (_resultStage) EnterAdjust();
        if (!TryAutoDetect())
            ConfirmDialog.Alert(this, "No Document Found",
                "Couldn't auto-detect the document edges — drag the four corners onto it manually.",
                ConfirmDialog.AlertKind.Info);
        PositionHandles();
    }

    private bool TryAutoDetect()
    {
        if (_src == null) return false;
        var quad = DocumentScanner.AutoDetectCorners(_src, _w, _h);
        if (quad == null) return false;
        for (int i = 0; i < 4; i++) _corners[i] = quad[i];
        if (!_resultStage) PositionHandles();
        return true;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_src == null) return;
        if (_resultStage) EnterAdjust();
        ResetCorners();
        PositionHandles();
    }

    private void RotateLeft_Click(object sender, RoutedEventArgs e) => Rotate(false);
    private void RotateRight_Click(object sender, RoutedEventArgs e) => Rotate(true);
    private void FlipH_Click(object sender, RoutedEventArgs e) => Flip(horizontal: true);
    private void FlipV_Click(object sender, RoutedEventArgs e) => Flip(horizontal: false);

    /// <summary>Rotates 90°. In the result stage it rotates the finished scan; otherwise the source photo.</summary>
    private void Rotate(bool cw)
    {
        if (_src == null) return;
        if (_resultStage && _result != null)
        {
            _result = Rotate90(_result, _rw, _rh, cw, out int nrw, out int nrh);
            _rw = nrw; _rh = nrh;
            _resultWb = new WriteableBitmap(_rw, _rh, 96, 96, PixelFormats.Bgra32, null);
            _resultWb.WritePixels(new Int32Rect(0, 0, _rw, _rh), _result, _rw * 4, 0);
            ResultImg.Source = _resultWb;
            FitToView();
            return;
        }
        _src = Rotate90(_src, _w, _h, cw, out int nw, out int nh);
        _w = nw; _h = nh;
        RebuildSourceBitmap();
        if (!TryAutoDetect()) ResetCorners();
        FitToView();
    }

    /// <summary>Flips horizontally or vertically. In the result stage it flips the finished scan; otherwise the source.</summary>
    private void Flip(bool horizontal)
    {
        if (_src == null) return;
        if (_resultStage && _result != null)
        {
            FlipInPlace(_result, _rw, _rh, horizontal);
            _resultWb!.WritePixels(new Int32Rect(0, 0, _rw, _rh), _result, _rw * 4, 0);
            return;
        }
        FlipInPlace(_src, _w, _h, horizontal);
        RebuildSourceBitmap();
        if (!TryAutoDetect()) ResetCorners();
        PositionHandles();
    }

    private void RebuildSourceBitmap()
    {
        _wb = new WriteableBitmap(_w, _h, 96, 96, PixelFormats.Bgra32, null);
        _wb.WritePixels(new Int32Rect(0, 0, _w, _h), _src!, _w * 4, 0);
        Img.Source = _wb;
        _result = null;
        EnterAdjust();
    }

    private static byte[] Rotate90(byte[] s, int w, int h, bool cw, out int nw, out int nh)
    {
        nw = h; nh = w;
        var d = new byte[s.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int si = (y * w + x) * 4;
                int nx = cw ? h - 1 - y : y;
                int ny = cw ? x : w - 1 - x;
                int di = (ny * nw + nx) * 4;
                d[di] = s[si]; d[di + 1] = s[si + 1]; d[di + 2] = s[si + 2]; d[di + 3] = s[si + 3];
            }
        return d;
    }

    private static void FlipInPlace(byte[] s, int w, int h, bool horizontal)
    {
        if (horizontal)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w / 2; x++)
                    SwapPx(s, (y * w + x) * 4, (y * w + (w - 1 - x)) * 4);
        }
        else
        {
            for (int y = 0; y < h / 2; y++)
                for (int x = 0; x < w; x++)
                    SwapPx(s, (y * w + x) * 4, ((h - 1 - y) * w + x) * 4);
        }
    }

    private static void SwapPx(byte[] s, int a, int b)
    {
        for (int k = 0; k < 4; k++) { (s[a + k], s[b + k]) = (s[b + k], s[a + k]); }
    }

    // =====================================================================
    //  Crop (warp) + filters + stage
    // =====================================================================
    private (int ow, int oh) OutputSize()
    {
        double wTop = Dist(_corners[0], _corners[1]), wBot = Dist(_corners[3], _corners[2]);
        double hL = Dist(_corners[0], _corners[3]), hR = Dist(_corners[1], _corners[2]);
        int ow = Math.Clamp((int)Math.Round(Math.Max(wTop, wBot)), 16, 6000);
        int oh = Math.Clamp((int)Math.Round(Math.Max(hL, hR)), 16, 6000);
        return (ow, oh);
    }

    private static double Dist((double x, double y) a, (double x, double y) b)
        => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

    private void Crop_Click(object sender, RoutedEventArgs e) => DoCrop();

    private void DoCrop()
    {
        if (_src == null) return;
        var (ow, oh) = OutputSize();
        _result = DocumentScanner.Warp(_src, _w, _h, _corners, ow, oh, _enhance);
        _rw = ow; _rh = oh;
        _resultWb = new WriteableBitmap(_rw, _rh, 96, 96, PixelFormats.Bgra32, null);
        _resultWb.WritePixels(new Int32Rect(0, 0, _rw, _rh), _result, _rw * 4, 0);
        ResultImg.Source = _resultWb;
        EnterResult();
        FitToView();
    }

    private void EditCorners_Click(object sender, RoutedEventArgs e) => EnterAdjust();

    private void EnterAdjust() { _resultStage = false; UpdateStageUi(); FitToView(); }
    private void EnterResult() { _resultStage = true; UpdateStageUi(); }

    private void UpdateStageUi()
    {
        bool adj = !_resultStage;
        Img.Visibility = adj ? Visibility.Visible : Visibility.Collapsed;
        Overlay.Visibility = adj ? Visibility.Visible : Visibility.Collapsed;
        ResultImg.Visibility = adj ? Visibility.Collapsed : Visibility.Visible;
        BtnCrop.Visibility = adj ? Visibility.Visible : Visibility.Collapsed;
        BtnEdit.Visibility = adj ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tag && Enum.TryParse<DocumentScanner.Enhance>(tag, out var en))
        {
            _enhance = en;
            UpdateFilterButtons();
            if (_src != null) DoCrop(); // re-warp so the filter previews immediately
        }
    }

    private void UpdateFilterButtons()
    {
        void Set(Button b, DocumentScanner.Enhance k)
        {
            bool on = _enhance == k;
            b.BorderBrush = (Brush)FindResource(on ? "AccentBrush" : "BorderSoftBrush");
            b.BorderThickness = new Thickness(on ? 2 : 1);
        }
        Set(BtnFNone, DocumentScanner.Enhance.Original);
        Set(BtnFMagic, DocumentScanner.Enhance.Magic);
        Set(BtnFGray, DocumentScanner.Enhance.Grayscale);
        Set(BtnFBw, DocumentScanner.Enhance.BlackWhite);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.S) { SavePng_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key == Key.P) { SavePdf_Click(this, new RoutedEventArgs()); e.Handled = true; }
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        switch (e.Key)
        {
            case Key.O: OpenImage(); break;
            case Key.D: if (_src != null) AutoDetect_Click(this, new RoutedEventArgs()); break;
            case Key.R: if (_src != null) Reset_Click(this, new RoutedEventArgs()); break;
            case Key.Enter:
                if (_src == null) break;
                if (!_resultStage) DoCrop();
                else if (_pickMode) Apply_Click(this, new RoutedEventArgs());
                break;
            case Key.Escape: if (_pickMode) Close(); else CloseFile_Click(this, new RoutedEventArgs()); break;
            default: return;
        }
        e.Handled = true;
    }

    // =====================================================================
    //  Save
    // =====================================================================
    private void EnsureResult() { if (_result == null && _src != null) DoCrop(); }

    private void SavePng_Click(object sender, RoutedEventArgs e)
    {
        EnsureResult();
        if (_resultWb == null) return;
        string? path = AskSave("PNG image|*.png", ".png");
        if (path == null) return;
        try
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(_resultWb));
            using var fs = new FileStream(path, FileMode.Create);
            enc.Save(fs);
            Saved(path);
        }
        catch (Exception ex) { ConfirmDialog.Alert(this, "Save Failed", ex.Message, ConfirmDialog.AlertKind.Error); }
    }

    private void SaveJpg_Click(object sender, RoutedEventArgs e)
    {
        EnsureResult();
        if (_resultWb == null) return;
        string? path = AskSave("JPEG image|*.jpg", ".jpg");
        if (path == null) return;
        try
        {
            var enc = new JpegBitmapEncoder { QualityLevel = 92 };
            enc.Frames.Add(BitmapFrame.Create(_resultWb));
            using var fs = new FileStream(path, FileMode.Create);
            enc.Save(fs);
            Saved(path);
        }
        catch (Exception ex) { ConfirmDialog.Alert(this, "Save Failed", ex.Message, ConfirmDialog.AlertKind.Error); }
    }

    private async void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        EnsureResult();
        if (_resultWb == null) return;
        string? path = AskSave("PDF document|*.pdf", ".pdf");
        if (path == null) return;

        BusyOverlay.Visibility = Visibility.Visible;
        string tmp = Path.Combine(Path.GetTempPath(), $"llamascan_{Guid.NewGuid():N}.jpg");
        try
        {
            var enc = new JpegBitmapEncoder { QualityLevel = 92 };
            enc.Frames.Add(BitmapFrame.Create(_resultWb));
            using (var fs = new FileStream(tmp, FileMode.Create)) enc.Save(fs);

            double wPt = _rw * 72.0 / 96.0, hPt = _rh * 72.0 / 96.0; // 96dpi image → points
            var page = new FileToolsService.ComposePageSpec { WidthPt = wPt, HeightPt = hPt };
            page.Images.Add(new FileToolsService.ComposePlacement
            {
                SourcePath = tmp,
                Cm = new[] { wPt, 0, 0, hPt, 0.0, 0.0 }, // fill the page (image unit square → page)
            });
            await FileToolsService.ComposePdfAsync(new[] { page }, path);
            Saved(path);
        }
        catch (Exception ex) { ConfirmDialog.Alert(this, "Save Failed", ex.Message, ConfirmDialog.AlertKind.Error); }
        finally
        {
            BusyOverlay.Visibility = Visibility.Collapsed;
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private string? AskSave(string filter, string ext)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Llamashot", "Documents");
        try { Directory.CreateDirectory(dir); } catch { }
        var dlg = new SaveFileDialog { Filter = filter, FileName = "scan" + ext, InitialDirectory = dir };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    private void Saved(string path)
    {
        long size = new FileInfo(path).Length;
        ConfirmDialog.Alert(this, "Saved",
            $"Saved ({FileToolsService.FormatFileSize(size)}) to:\n{path}", ConfirmDialog.AlertKind.Success, "Done");
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
        UnloadFile(); // after a successful save, close the file and return to the Open state
    }

    // Guard against accidental close before saving.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_pickMode) { base.OnClosing(e); return; } // transient dialog: no discard prompt
        if (_result != null && !ConfirmDialog.Show(this, "Close Document Scan?",
                "Close this scan? Any unsaved result will be lost.", "Close", "Keep editing"))
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }
}
