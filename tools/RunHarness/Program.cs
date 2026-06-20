using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Llamashot.Core;
using Llamashot.Models;
using Llamashot.Views;
using SD = System.Drawing;

namespace RunHarness;

internal static class Program
{
    static readonly string Dir = Path.Combine(Path.GetTempPath(), "run_harness_out");

    [STAThread]
    static void Main()
    {
        Directory.CreateDirectory(Dir);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        // Load the shared control styles (theme color dictionaries are managed by ThemeManager).
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Llamashot;component/Themes/Shared.xaml", UriKind.Relative)
        });

        app.DispatcherUnhandledException += (_, a) =>
            { File.AppendAllText(Path.Combine(Dir, "err.txt"), a.Exception + "\n\n"); a.Handled = true; };
        app.Startup += async (_, _) =>
        {
            try { await Run(); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(Dir, "err.txt"), ex.ToString()); }
            finally { app.Shutdown(); }
        };
        app.Run();
    }

    static async Task Run()
    {
        ThemeManager.Initialize(ThemeManager.Light);

        // Capture the redesigned windows in both themes (no settings writes — Apply persist:false).
        if (Environment.GetEnvironmentVariable("LLAMASHOT_THEMESHOT") == "1")
        {
            foreach (var theme in new[] { ThemeManager.Dark, ThemeManager.Light })
            {
                ThemeManager.Apply(theme, persist: false);
                ThemeManager.ApplyAccent();

                var ft = new FileToolsWindow
                {
                    WindowState = WindowState.Normal, Width = 1280, Height = 820,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                ft.Show(); await Task.Delay(500); ShotRtb(ft, $"theme_filetools_{theme}.png"); ft.Close();

                var st = new SettingsWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                st.Show(); await Task.Delay(300); ShotRtb(st, $"theme_settings_{theme}.png"); st.Close();

                var ab = new AboutWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                ab.Show(); await Task.Delay(300); ShotRtb(ab, $"theme_about_{theme}.png"); ab.Close();
            }
            return;
        }

        // Image→PDF composer: verify the export cm-matrix math (position, Y-flip, rotation)
        // by rendering the produced PDF back to PNG and sampling pixels.
        if (Environment.GetEnvironmentVariable("LLAMASHOT_COMPOSE") == "1")
        {
            var log = new List<string>();
            void A(string n, bool ok) => log.Add($"{(ok ? "PASS" : "FAIL")}  {n}");

            string tmp = Path.Combine(Dir, "compose");
            Directory.CreateDirectory(tmp);

            // 200×200 test image: red left half, blue right half.
            string imgPath = Path.Combine(tmp, "halves.png");
            using (var bmp = new SD.Bitmap(200, 200))
            {
                using var g = SD.Graphics.FromImage(bmp);
                g.FillRectangle(SD.Brushes.Red, 0, 0, 100, 200);
                g.FillRectangle(SD.Brushes.Blue, 100, 0, 100, 200);
                bmp.Save(imgPath, SD.Imaging.ImageFormat.Png);
            }

            // Mirror of ImagePdfComposerWindow.ComputeCm — same maths the UI exports with.
            (double x, double y) RotateVec(double vx, double vy, double deg)
            { double r = deg * Math.PI / 180; double c = Math.Cos(r), s = Math.Sin(r); return (vx * c - vy * s, vx * s + vy * c); }
            double[] Cm(double X, double Y, double W, double H, double Angle, double pageH, bool flipH = false, bool flipV = false)
            {
                (double x, double y) ToPdf(double u, double v)
                {
                    double ex = flipH ? (1 - u) * W : u * W;
                    double ey = flipV ? v * H : (1 - v) * H;
                    var (rx, ry) = RotateVec(ex - W / 2, ey - H / 2, Angle);
                    return (X + W / 2 + rx, pageH - (Y + H / 2 + ry));
                }
                var p00 = ToPdf(0, 0); var p10 = ToPdf(1, 0); var p01 = ToPdf(0, 1);
                return new[] { p10.x - p00.x, p10.y - p00.y, p01.x - p00.x, p01.y - p00.y, p00.x, p00.y };
            }

            double pageW = 595, pageH = 842;
            bool IsRed(SD.Color c) => c.R > 150 && c.G < 100 && c.B < 100;
            bool IsBlue(SD.Color c) => c.B > 150 && c.R < 100;
            bool IsWhite(SD.Color c) => c.R > 200 && c.G > 200 && c.B > 200;
            // Derive the scale from the actual render width (the renderer's DPI is its own business).
            SD.Color At(SD.Bitmap b, double lx, double ly)
            {
                double s = b.Width / pageW;
                return b.GetPixel(Math.Clamp((int)(lx * s), 0, b.Width - 1), Math.Clamp((int)(ly * s), 0, b.Height - 1));
            }

            // Test 1 — axis-aligned: 200×100 at top-left (100,50). Expect red left, blue right, upright.
            var s1 = new FileToolsService.ComposePageSpec { WidthPt = pageW, HeightPt = pageH };
            s1.Images.Add(new FileToolsService.ComposePlacement { SourcePath = imgPath, Cm = Cm(100, 50, 200, 100, 0, pageH) });
            string pdf1 = Path.Combine(tmp, "axis.pdf");
            await FileToolsService.ComposePdfAsync(new[] { s1 }, pdf1);
            A("axis PDF created", File.Exists(pdf1) && new FileInfo(pdf1).Length > 0);
            try
            {
                var png1 = (await FileToolsService.PdfToImagesAsync(pdf1, Path.Combine(tmp, "axis"), "png", 150))[0];
                using var b1 = new SD.Bitmap(png1);
                A($"axis: red at left quarter ({At(b1, 150, 100)})", IsRed(At(b1, 150, 100)));
                A($"axis: blue at right quarter ({At(b1, 250, 100)})", IsBlue(At(b1, 250, 100)));
                A($"axis: white outside image ({At(b1, 50, 50)})", IsWhite(At(b1, 50, 50)));
                A($"axis: white below image ({At(b1, 200, 300)})", IsWhite(At(b1, 200, 300)));
            }
            catch (Exception ex) { A($"axis render: {ex.Message}", false); }

            // Test 2 — 90° clockwise about centre. Red (left edge) should rotate to the TOP.
            double X = (pageW - 200) / 2, Y = (pageH - 200) / 2, cx = X + 100, cy = Y + 100;
            var s2 = new FileToolsService.ComposePageSpec { WidthPt = pageW, HeightPt = pageH };
            s2.Images.Add(new FileToolsService.ComposePlacement { SourcePath = imgPath, Cm = Cm(X, Y, 200, 200, 90, pageH) });
            string pdf2 = Path.Combine(tmp, "rot.pdf");
            await FileToolsService.ComposePdfAsync(new[] { s2 }, pdf2);
            try
            {
                var png2 = (await FileToolsService.PdfToImagesAsync(pdf2, Path.Combine(tmp, "rot"), "png", 150))[0];
                using var b2 = new SD.Bitmap(png2);
                A($"rot90: red moved to top ({At(b2, cx, cy - 60)})", IsRed(At(b2, cx, cy - 60)));
                A($"rot90: blue moved to bottom ({At(b2, cx, cy + 60)})", IsBlue(At(b2, cx, cy + 60)));
            }
            catch (Exception ex) { A($"rot render: {ex.Message}", false); }

            // Test 3 — horizontal flip (no rotation): red (left) should mirror to the RIGHT.
            var s3 = new FileToolsService.ComposePageSpec { WidthPt = pageW, HeightPt = pageH };
            s3.Images.Add(new FileToolsService.ComposePlacement { SourcePath = imgPath, Cm = Cm(100, 50, 200, 100, 0, pageH, flipH: true) });
            string pdf3 = Path.Combine(tmp, "fliph.pdf");
            await FileToolsService.ComposePdfAsync(new[] { s3 }, pdf3);
            try
            {
                var png3 = (await FileToolsService.PdfToImagesAsync(pdf3, Path.Combine(tmp, "fliph"), "png", 150))[0];
                using var b3 = new SD.Bitmap(png3);
                A($"flipH: blue now at left ({At(b3, 150, 100)})", IsBlue(At(b3, 150, 100)));
                A($"flipH: red now at right ({At(b3, 250, 100)})", IsRed(At(b3, 250, 100)));
            }
            catch (Exception ex) { A($"flipH render: {ex.Message}", false); }

            // Test 4 — vertical flip on the 200×200 square. Halves stay L/R; assert it still renders upright-ish.
            var s4 = new FileToolsService.ComposePageSpec { WidthPt = pageW, HeightPt = pageH };
            s4.Images.Add(new FileToolsService.ComposePlacement { SourcePath = imgPath, Cm = Cm(100, 50, 200, 200, 0, pageH, flipV: true) });
            string pdf4 = Path.Combine(tmp, "flipv.pdf");
            await FileToolsService.ComposePdfAsync(new[] { s4 }, pdf4);
            try
            {
                var png4 = (await FileToolsService.PdfToImagesAsync(pdf4, Path.Combine(tmp, "flipv"), "png", 150))[0];
                using var b4 = new SD.Bitmap(png4);
                A($"flipV: red still at left ({At(b4, 150, 150)})", IsRed(At(b4, 150, 150)));
                A($"flipV: blue still at right ({At(b4, 250, 150)})", IsBlue(At(b4, 250, 150)));
            }
            catch (Exception ex) { A($"flipV render: {ex.Message}", false); }

            // Composer window chrome render.
            try
            {
                var cw = new ImagePdfComposerWindow { Width = 1100, Height = 760, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                cw.Show(); await Task.Delay(500); ShotRtb(cw, "compose_window.png"); cw.Close();
                A("composer window rendered", true);
            }
            catch (Exception ex) { A($"composer window: {ex.Message}", false); }

            File.WriteAllText(Path.Combine(Dir, "composeverify.txt"), string.Join("\n", log));
            return;
        }

        // Remove-Background algorithms: auto flood-fill, magic wand, JPG flatten.
        if (Environment.GetEnvironmentVariable("LLAMASHOT_BGREMOVE") == "1")
        {
            var log = new List<string>();
            void A(string n, bool ok) => log.Add($"{(ok ? "PASS" : "FAIL")}  {n}");

            int w = 200, h = 200;
            byte[] Build()
            {
                var px = new byte[w * h * 4];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = (y * w + x) * 4;
                        bool center = x >= 60 && x < 140 && y >= 60 && y < 140;
                        if (center) { px[i] = 0; px[i + 1] = 0; px[i + 2] = 255; px[i + 3] = 255; }   // red
                        else { px[i] = 255; px[i + 1] = 255; px[i + 2] = 255; px[i + 3] = 255; }      // white
                    }
                return px;
            }
            int Ctr = (100 * w + 100) * 4;

            var a = Build();
            BackgroundRemover.AutoRemove(a, w, h, 30);
            A($"auto: white corner removed (a={a[3]})", a[3] == 0);
            A($"auto: red centre kept (a={a[Ctr + 3]}, R={a[Ctr + 2]})", a[Ctr + 3] == 255 && a[Ctr + 2] == 255);

            var b = Build();
            BackgroundRemover.MagicWand(b, w, h, 0, 0, 30);
            A($"wand: clicked corner removed (a={b[3]})", b[3] == 0);
            A($"wand: red centre kept (a={b[Ctr + 3]})", b[Ctr + 3] == 255);

            var flat = BackgroundRemover.FlattenOnto(a, 0, 255, 0); // onto green
            A($"flatten: removed area is green (B={flat[0]},G={flat[1]},R={flat[2]})", flat[1] == 255 && flat[0] == 0 && flat[2] == 0);
            A($"flatten: subject stays red (R={flat[Ctr + 2]})", flat[Ctr + 2] == 255);

            File.WriteAllText(Path.Combine(Dir, "bgremoveverify.txt"), string.Join("\n", log));
            return;
        }

        // Headless: run AutoRemove on a real image file and dump transparent + magenta-composite results.
        // Driven by a config file (env vars proved unreliable here): line1=src path, line2=tolerance.
        string bgCmd = Path.Combine(Dir, "bg_cmd.txt");
        if (File.Exists(bgCmd))
        {
            var lines = File.ReadAllLines(bgCmd);
            string src = lines.Length > 0 ? lines[0].Trim() : "";
            int tol = lines.Length > 1 && int.TryParse(lines[1].Trim(), out var tt) ? tt : 30;
            bool ai = lines.Length > 2 && lines[2].Trim().Equals("ai", StringComparison.OrdinalIgnoreCase);
            int smooth = lines.Length > 3 && int.TryParse(lines[3].Trim(), out var ss) ? ss : 0;
            try { File.Delete(bgCmd); } catch { }
            string outDir = Path.Combine(Dir, "bgfile");
            Directory.CreateDirectory(outDir);
            string stem = Path.GetFileNameWithoutExtension(src);

            var bi = new BitmapImage();
            bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(src); bi.EndInit(); bi.Freeze();
            BitmapSource bsrc = new FormatConvertedBitmap(bi, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int w = bsrc.PixelWidth, h = bsrc.PixelHeight;
            var px = new byte[w * h * 4];
            bsrc.CopyPixels(px, w * 4, 0);

            if (ai)
            {
                await AiMatting.EnsureModelAsync(new Progress<double>(p => { }));
                var mask = AiMatting.ComputeMask(px, w, h);
                AiMatting.ApplyMask(px, mask);
            }
            else
            {
                BackgroundRemover.AutoRemove(px, w, h, tol);
            }
            if (smooth > 0) BackgroundRemover.SmoothAlpha(px, w, h, smooth);

            int removed = 0; for (int i = 3; i < px.Length; i += 4) if (px[i] == 0) removed++;
            double pct = removed * 100.0 / (w * h);

            void SavePng(byte[] buf, string name)
            {
                var bmp = BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, buf, w * 4);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = File.Create(Path.Combine(outDir, name)); enc.Save(fs);
            }
            SavePng(px, $"{stem}_cut.png");
            SavePng(BackgroundRemover.FlattenOnto(px, 255, 0, 255), $"{stem}_magenta.png");
            File.WriteAllText(Path.Combine(outDir, $"{stem}_report.txt"), $"tol={tol}  removed={pct:0.0}% of {w}x{h}");
            return;
        }

        // Document scanner: synthetic correctness of perspective warp + filters + auto-detect.
        // Driven by a marker file docscan_cmd.txt (optional line1 = real image path to also process).
        string dsCmd = Path.Combine(Dir, "docscan_cmd.txt");
        if (File.Exists(dsCmd))
        {
            var dsLines = File.ReadAllLines(dsCmd);
            try { File.Delete(dsCmd); } catch { }
            var log = new List<string>();
            void A(string n, bool ok) => log.Add($"{(ok ? "PASS" : "FAIL")}  {n}");
            static void px4(byte[] a, int i, byte b, byte g, byte r) { a[i] = b; a[i + 1] = g; a[i + 2] = r; a[i + 3] = 255; }
            static double Hyp((double x, double y) p, (double x, double y) q)
                => System.Math.Sqrt((p.x - q.x) * (p.x - q.x) + (p.y - q.y) * (p.y - q.y));

            // Build a 200x200 source: white with a red 80x80 square at (60,60)-(140,140).
            int w = 200, h = 200;
            var src = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    bool red = x >= 60 && x < 140 && y >= 60 && y < 140;
                    px4(src, i, red ? (byte)0 : (byte)255, red ? (byte)0 : (byte)255, (byte)255);
                }

            // 1) Identity warp (full frame) reproduces the source.
            var full = new (double, double)[] { (0, 0), (w - 1, 0), (w - 1, h - 1), (0, h - 1) };
            var idw = DocumentScanner.Warp(src, w, h, full, w, h, DocumentScanner.Enhance.Original);
            int ctr = (100 * w + 100) * 4;
            A($"identity: centre stays red (R={idw[ctr + 2]},G={idw[ctr + 1]})", idw[ctr + 2] == 255 && idw[ctr + 1] == 0);
            A($"identity: corner stays white (R={idw[2]},G={idw[1]},B={idw[0]})", idw[0] == 255 && idw[1] == 255 && idw[2] == 255);

            // 2) Crop the red square exactly (quad = its bounds) → output is all red.
            var sq = new (double, double)[] { (60, 60), (139, 60), (139, 139), (60, 139) };
            var cw = DocumentScanner.Warp(src, w, h, sq, 80, 80, DocumentScanner.Enhance.Original);
            bool allRed = true;
            for (int p = 0; p < 80 * 80 && allRed; p++) if (!(cw[p * 4 + 2] == 255 && cw[p * 4 + 1] == 0)) allRed = false;
            A("crop-square: warped output is fully red", allRed);

            // 3) Perspective: skewed quad still warps to a rectangle filled with red.
            var skew = new (double, double)[] { (62, 60), (138, 64), (140, 138), (60, 140) };
            var pw = DocumentScanner.Warp(src, w, h, skew, 80, 80, DocumentScanner.Enhance.Original);
            int pc = (40 * 80 + 40) * 4;
            A($"perspective: centre red (R={pw[pc + 2]})", pw[pc + 2] == 255 && pw[pc + 1] == 0);

            // 4) Filters.
            var gray = DocumentScanner.Warp(src, w, h, sq, 80, 80, DocumentScanner.Enhance.Grayscale);
            A($"grayscale: r==g==b ({gray[2]},{gray[1]},{gray[0]})", gray[0] == gray[1] && gray[1] == gray[2]);
            var bw = DocumentScanner.Warp(src, w, h, full, w, h, DocumentScanner.Enhance.BlackWhite);
            bool binary = true; for (int p = 0; p < w * h && binary; p++) { byte v = bw[p * 4]; if (v != 0 && v != 255) binary = false; }
            A("blackwhite: pixels are pure 0 or 255", binary);

            // 5) Auto-detect the red square on white background.
            var det = DocumentScanner.AutoDetectCorners(src, w, h);
            bool detOk = det != null && det[0].x < 90 && det[0].y < 90 && det[2].x > 110 && det[2].y > 110;
            A($"autodetect: found square corners ({(det == null ? "null" : $"TL=({det[0].x:0},{det[0].y:0}) BR=({det[2].x:0},{det[2].y:0})")})", detOk);

            File.WriteAllText(Path.Combine(Dir, "docscanverify.txt"), string.Join("\n", log));

            // Optional: process a real photo end-to-end and dump per-filter results.
            string real = dsLines.Length > 0 ? dsLines[0].Trim() : "";
            if (real.Length > 0 && File.Exists(real))
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(real); bi.EndInit(); bi.Freeze();
                BitmapSource bsrc = new FormatConvertedBitmap(bi, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                int rw = bsrc.PixelWidth, rh = bsrc.PixelHeight;
                var rpx = new byte[rw * rh * 4]; bsrc.CopyPixels(rpx, rw * 4, 0);
                var quad = DocumentScanner.AutoDetectCorners(rpx, rw, rh)
                           ?? new (double, double)[] { (0, 0), (rw - 1, 0), (rw - 1, rh - 1), (0, rh - 1) };
                double wt = System.Math.Max(Hyp(quad[0], quad[1]), Hyp(quad[3], quad[2]));
                double ht = System.Math.Max(Hyp(quad[0], quad[3]), Hyp(quad[1], quad[2]));
                int ow = (int)wt, oh = (int)ht;
                string outDir = Path.Combine(Dir, "docscan"); Directory.CreateDirectory(outDir);
                string stem = Path.GetFileNameWithoutExtension(real);
                foreach (var en in new[] { DocumentScanner.Enhance.Original, DocumentScanner.Enhance.Magic, DocumentScanner.Enhance.Grayscale, DocumentScanner.Enhance.BlackWhite })
                {
                    var outp = DocumentScanner.Warp(rpx, rw, rh, quad, ow, oh, en);
                    var bmp = BitmapSource.Create(ow, oh, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, outp, ow * 4);
                    var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
                    using var fs = File.Create(Path.Combine(outDir, $"{stem}_{en}.png")); enc.Save(fs);
                }
                File.AppendAllText(Path.Combine(Dir, "docscanverify.txt"),
                    $"\nreal: {stem} {rw}x{rh} -> {ow}x{oh}  quad=TL({quad[0].Item1:0},{quad[0].Item2:0}) BR({quad[2].Item1:0},{quad[2].Item2:0})");
            }
            return;
        }

        // Document-Scan window: empty state, loaded + auto-detected corners, crop, filters, rotate/flip.
        if (Environment.GetEnvironmentVariable("LLAMASHOT_DOCUI") == "1")
        {
            string tmp = Path.Combine(Dir, "docui");
            Directory.CreateDirectory(tmp);
            static SD.PointF Lerp(SD.PointF a, SD.PointF b, float t) => new SD.PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

            // Synthetic angled "document": a white perspective quad with black text lines on a dark desk.
            string imgPath = Path.Combine(tmp, "doc.png");
            using (var bmp = new SD.Bitmap(520, 680))
            {
                using var g = SD.Graphics.FromImage(bmp);
                g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(SD.Color.FromArgb(45, 42, 48)); // dark desk
                var quad = new[]
                {
                    new SD.PointF(150, 90), new SD.PointF(420, 150),
                    new SD.PointF(380, 600), new SD.PointF(110, 520),
                };
                using (var white = new SD.SolidBrush(SD.Color.White)) g.FillPolygon(white, quad);
                // A few "text" lines following the top edge so de-skew is visible.
                using var pen = new SD.Pen(SD.Color.FromArgb(30, 30, 30), 6);
                for (int r = 0; r < 6; r++)
                {
                    float t = 0.18f + r * 0.11f;
                    var a = Lerp(quad[0], quad[3], t); var b = Lerp(quad[1], quad[2], t);
                    var a2 = Lerp(a, b, 0.1f); var b2 = Lerp(a, b, 0.85f);
                    g.DrawLine(pen, a2, b2);
                }
                bmp.Save(imgPath, SD.Imaging.ImageFormat.Png);
            }

            // Empty state (no image): centered Open button + toolbar.
            var empty = new DocumentScanWindow
            {
                WindowState = WindowState.Normal, Width = 1100, Height = 720,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            empty.Show();
            await Task.Delay(400);
            ShotRtb(empty, "docui_empty.png");
            empty.Close();

            var w0 = new DocumentScanWindow(imgPath)
            {
                WindowState = WindowState.Normal, Width = 1100, Height = 720,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            w0.Show();
            await Task.Delay(500);
            ShotRtb(w0, "docui_adjust.png");      // loaded + auto-detected corner overlay
            w0.TestCrop();
            await Task.Delay(300);
            ShotRtb(w0, "docui_result.png");       // warped flat rectangle
            w0.TestFilter("BlackWhite");
            await Task.Delay(300);
            ShotRtb(w0, "docui_bw.png");
            w0.TestFilter("Magic");
            await Task.Delay(200);
            w0.TestRotate(true);
            await Task.Delay(300);
            ShotRtb(w0, "docui_rotated.png");
            w0.TestFlip(true);
            await Task.Delay(300);
            ShotRtb(w0, "docui_flipped.png");
            w0.TestClose();

            // Pick mode (Images → PDF integration): toolbar shows "Use in PDF", no save/close buttons.
            var wp = new DocumentScanWindow(imgPath)
            {
                WindowState = WindowState.Normal, Width = 1100, Height = 720,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            wp.Show();
            await Task.Delay(500);
            wp.TestPickMode();
            wp.TestCrop();
            await Task.Delay(300);
            ShotRtb(wp, "docui_pick.png");
            string? applied = wp.TestApplyToFile();
            File.WriteAllText(Path.Combine(Dir, "docpick.txt"),
                applied != null && File.Exists(applied)
                    ? $"PASS  pick applied -> {Path.GetFileName(applied)} ({new FileInfo(applied).Length} bytes)"
                    : "FAIL  pick produced no file");
            wp.TestClose();
            return;
        }

        // Images→PDF composer: reproduce the Rescan "Specified index already in use" bug + verify the fix.
        if (Environment.GetEnvironmentVariable("LLAMASHOT_COMPOSERSCAN") == "1")
        {
            string tmp = Path.Combine(Dir, "composerscan");
            Directory.CreateDirectory(tmp);
            string imgA = Path.Combine(tmp, "a.png"), imgB = Path.Combine(tmp, "b.png");
            using (var bmp = new SD.Bitmap(300, 450)) { using var g = SD.Graphics.FromImage(bmp); g.Clear(SD.Color.SteelBlue); bmp.Save(imgA); }
            using (var bmp = new SD.Bitmap(420, 280)) { using var g = SD.Graphics.FromImage(bmp); g.Clear(SD.Color.IndianRed); bmp.Save(imgB); }

            var log = new List<string>();
            var w = new ImagePdfComposerWindow { WindowState = WindowState.Normal, Width = 1100, Height = 720 };
            w.Show();
            await Task.Delay(500);
            w.TestAddImage(imgA);
            await Task.Delay(200);
            string? oldErr = w.TestReplaceOldStyle(imgB);
            log.Add(oldErr == null ? "old-style: (no error)" : "old-style ERROR:\n" + oldErr);

            // Re-add a clean image and verify the FIX works.
            var w2 = new ImagePdfComposerWindow { WindowState = WindowState.Normal, Width = 1100, Height = 720 };
            w2.Show();
            await Task.Delay(400);
            w2.TestAddImage(imgA);
            await Task.Delay(200);
            string? newErr = w2.TestReplaceContent(imgB);
            log.Add(newErr == null ? "PASS  in-place replace: no error" : "FAIL  in-place replace:\n" + newErr);
            await Task.Delay(200);
            ShotRtb(w2, "composerscan_after.png");

            File.WriteAllText(Path.Combine(Dir, "composerscan.txt"), string.Join("\n\n", log));
            w.TestClose(); w2.TestClose();
            return;
        }

        // Remove-Background window: maximize, auto-remove on a realistic photo, pixel-zoom render.
        if (Environment.GetEnvironmentVariable("LLAMASHOT_BGUI") == "1")
        {
            string tmp = Path.Combine(Dir, "bgui");
            Directory.CreateDirectory(tmp);

            // Synthetic "photo": sky-blue background with a solid red subject block.
            string imgPath = Path.Combine(tmp, "photo.png");
            using (var bmp = new SD.Bitmap(360, 480))
            {
                using var g = SD.Graphics.FromImage(bmp);
                g.Clear(SD.Color.FromArgb(150, 200, 245)); // sky blue
                using var sub = new SD.SolidBrush(SD.Color.FromArgb(210, 40, 40));
                g.FillRectangle(sub, 120, 150, 130, 230); // subject
                bmp.Save(imgPath, SD.Imaging.ImageFormat.Png);
            }

            var w0 = new RemoveBackgroundWindow(imgPath)
            {
                WindowState = WindowState.Normal, Width = 1100, Height = 720,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            w0.Show();
            await Task.Delay(500);
            ShotRtb(w0, "bgui_loaded.png");
            w0.TestAuto();
            await Task.Delay(300);
            ShotRtb(w0, "bgui_autoremoved.png");
            w0.TestZoom(6);
            await Task.Delay(300);
            ShotRtb(w0, "bgui_zoomed.png");
            w0.TestZoom(1);
            w0.TestTool("Restore");
            await Task.Delay(300);
            ShotRtb(w0, "bgui_restoreghost.png");
            w0.Close();
            return;
        }

        // Programmatic verification of the accent/theme engine + History render.
        if (Environment.GetEnvironmentVariable("LLAMASHOT_THEMEVERIFY") == "1")
        {
            var log = new List<string>();
            void A(string name, bool ok) => log.Add($"{(ok ? "PASS" : "FAIL")}  {name}");
            Color Hex(string h) => (Color)ColorConverter.ConvertFromString(h);
            Color AccentNow() => ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;

            ThemeManager.ApplyAccent(); // defaults
            A($"default accent is teal ({AccentNow()})", AccentNow() == Hex("#2DD4BF"));
            A("AccentGradientBrush is a gradient", Application.Current.Resources["AccentGradientBrush"] is LinearGradientBrush);
            A("WindowGradient tinted when tint on", Application.Current.Resources.Contains("WindowGradientBrush"));

            ThemeManager.ApplyAccent("#FF0000", "#0000FF", 90, true);
            A($"custom accent applies ({AccentNow()})", AccentNow() == Hex("#FF0000"));
            var at = ((SolidColorBrush)Application.Current.Resources["AccentTextBrush"]).Color;
            A($"accent-text auto-contrasts to white on red ({at})", at == Colors.White);

            ThemeManager.ApplyAccent("#2DD4BF", "#34D399", 135, true);
            var tintedStop = ((LinearGradientBrush)Application.Current.Resources["WindowGradientBrush"]).GradientStops[0].Color;
            ThemeManager.ApplyAccent("#2DD4BF", "#34D399", 135, false);
            var neutralStop = ((LinearGradientBrush)Application.Current.Resources["WindowGradientBrush"]).GradientStops[0].Color;
            A($"tint ON vs OFF changes window gradient ({tintedStop} vs {neutralStop})", tintedStop != neutralStop);

            ThemeManager.ResetAccent();
            A($"reset restores teal ({AccentNow()})", AccentNow() == Hex("#2DD4BF"));

            ThemeManager.Apply(ThemeManager.Light, persist: false); ThemeManager.ApplyAccent();
            A($"accent survives theme swap ({AccentNow()})", AccentNow() == Hex("#2DD4BF"));
            var lightWinBrush = Application.Current.Resources["WindowGradientBrush"];
            A("WindowGradient present after swap", lightWinBrush is LinearGradientBrush);

            // Render History in both themes
            foreach (var theme in new[] { ThemeManager.Dark, ThemeManager.Light })
            {
                ThemeManager.Apply(theme, persist: false); ThemeManager.ApplyAccent();
                try
                {
                    var hw = new HistoryWindow { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                    hw.Show(); await Task.Delay(350); ShotRtb(hw, $"theme_history_{theme}.png"); hw.Close();
                    A($"HistoryWindow rendered ({theme})", true);
                }
                catch (Exception ex) { A($"HistoryWindow ({theme}): {ex.Message}", false); }
            }

            File.WriteAllText(Path.Combine(Dir, "themeverify.txt"), string.Join("\n", log));
            return;
        }

        if (Environment.GetEnvironmentVariable("LLAMASHOT_VERIFY") == "1")
        {
            await VerifyExports();
            return;
        }

        if (Environment.GetEnvironmentVariable("LLAMASHOT_EDITORTEST") == "1")
        {
            await VerifyEditorOnFile(Environment.GetEnvironmentVariable("LLAMASHOT_PDF") ?? @"C:\Users\DELL\Downloads\137.pdf");
            return;
        }

        if (Environment.GetEnvironmentVariable("LLAMASHOT_EDITORUI") == "1")
        {
            await VerifyEditorLogic(Environment.GetEnvironmentVariable("LLAMASHOT_PDF") ?? @"C:\Users\DELL\Downloads\137.pdf");
            return;
        }

        if (Environment.GetEnvironmentVariable("LLAMASHOT_DRAWTEST") == "1")
        {
            await VerifyDrawAndFonts(Environment.GetEnvironmentVariable("LLAMASHOT_PDF") ?? @"C:\Users\DELL\Downloads\137.pdf");
            return;
        }

        if (Environment.GetEnvironmentVariable("LLAMASHOT_DRAWUI") == "1")
        {
            await CaptureDrawUi(Environment.GetEnvironmentVariable("LLAMASHOT_PDF") ?? @"C:\Users\DELL\Downloads\137.pdf");
            return;
        }

        if (Environment.GetEnvironmentVariable("LLAMASHOT_TEXTREPRO") == "1")
        {
            await ReproTextBoxes(Environment.GetEnvironmentVariable("LLAMASHOT_PDF") ?? @"C:\Users\DELL\Downloads\137.pdf");
            return;
        }

        if (Environment.GetEnvironmentVariable("LLAMASHOT_COLORPICKER") == "1")
        {
            var dlg = new ColorPickerDialog("#1C3FAA");
            dlg.Show(); dlg.UpdateLayout();
            await Task.Delay(300); dlg.UpdateLayout(); await Task.Delay(150);
            ShotRtb(dlg, "colorpicker.png");
            dlg.Close();
            return;
        }


        if (Environment.GetEnvironmentVariable("LLAMASHOT_MERGEPREVIEW") == "1")
        {
            await CaptureMergePreview();
            return;
        }

        if (Environment.GetEnvironmentVariable("LLAMASHOT_PROTECT") == "1")
        {
            await VerifyProtect(Environment.GetEnvironmentVariable("LLAMASHOT_PDF") ?? @"C:\Users\DELL\Downloads\137.pdf");
            return;
        }

        if (Environment.GetEnvironmentVariable("LLAMASHOT_CURSORTEST") == "1")
        {
            VerifyCursorOverlay();
            return;
        }

        if (Environment.GetEnvironmentVariable("LLAMASHOT_OVERLAYTEST") == "1")
        {
            await VerifyOverlaySelection();
            return;
        }

        if (Environment.GetEnvironmentVariable("LLAMASHOT_COMPRESSTEST") == "1")
        {
            await VerifyCompression();
            return;
        }

        if (Environment.GetEnvironmentVariable("LLAMASHOT_COMPRESSUI") == "1")
        {
            await CaptureCompressUi();
            return;
        }

        if (Environment.GetEnvironmentVariable("LLAMASHOT_HISTORYVIEWS") == "1")
        {
            await VerifyHistoryViews();
            return;
        }

        if (Environment.GetEnvironmentVariable("LLAMASHOT_HISTORYFILTERS") == "1")
        {
            VerifyHistoryFilters();
            return;
        }

        // Workspace-only mode: skip the flaky FileToolsWindow/alert/password captures.
        if (Environment.GetEnvironmentVariable("LLAMASHOT_WS_ONLY") == "1")
        {
            // warm-up: pump the message loop with a throwaway window so the first real capture paints
            await CaptureWorkspace("_warmup.png", "pdf_to_images", false);
            await Task.Delay(300);

            ThemeManager.Apply(ThemeManager.Dark, persist: false);
            await Task.Delay(150);
            await CaptureMarkup("fillsign_dark.png", "fillsign");
            ThemeManager.Apply(ThemeManager.Light, persist: false);
            await Task.Delay(150);
            await CaptureMarkup("editor_light.png", "edit");

            // mock image rows from real PNGs in the output dir
            var pngs = System.IO.Directory.GetFiles(Dir, "*.png");
            var imgRows = new[]
            {
                (pngs.Length > 0 ? pngs[0] : typeof(Program).Assembly.Location, 1),
                (pngs.Length > 1 ? pngs[1] : typeof(Program).Assembly.Location, 1),
                (pngs.Length > 2 ? pngs[2] : typeof(Program).Assembly.Location, 1),
            };

            ThemeManager.Apply(ThemeManager.Dark, persist: false);
            await Task.Delay(200);
            await CaptureMergeList("i2p_dark.png", "images_to_pdf", imgRows);
            foreach (var id in new[] { "rotate_pdf", "extract_pages", "insert_pages", "page_numbers", "watermark", "protect_pdf" })
                await CaptureWorkspace($"{id}_dark.png", id, false);
            ThemeManager.Apply(ThemeManager.Light, persist: false);
            await Task.Delay(200);
            await CaptureMergeList("i2p_light.png", "images_to_pdf", imgRows);
            foreach (var id in new[] { "rotate_pdf", "extract_pages", "insert_pages", "page_numbers", "watermark", "protect_pdf" })
                await CaptureWorkspace($"{id}_light.png", id, false);
            return;
        }

        var win = new FileToolsWindow { Width = 1300, Height = 860,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
        win.Show(); win.Activate();
        await Task.Delay(900);
        win.UpdateLayout();
        await Task.Delay(300);

        // Home — light
        Shot(win, "home_light.png");

        // Home — dark
        ThemeManager.Apply(ThemeManager.Dark, persist: false);
        await Task.Delay(300);
        win.UpdateLayout();
        await Task.Delay(200);
        Shot(win, "home_dark.png");

        // Open a tool panel (Merge PDF) and capture both themes
        ThemeManager.Apply(ThemeManager.Light, persist: false);
        await Task.Delay(200);
        var card = FindByTag(win, "merge_pdf");
        if (card != null)
        {
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent, Source = card });
            await Task.Delay(500); // wait out the 150ms fade + settle
            win.UpdateLayout();
            await Task.Delay(200);
            Shot(win, "panel_light.png");

            ThemeManager.Apply(ThemeManager.Dark, persist: false);
            await Task.Delay(300);
            win.UpdateLayout();
            await Task.Delay(200);
            Shot(win, "panel_dark.png");
        }
        else
        {
            File.AppendAllText(Path.Combine(Dir, "err.txt"), "merge_pdf card not found\n");
        }

        // Themed alert dialog — capture in both themes (screenshot fires during the modal loop)
        ThemeManager.Apply(ThemeManager.Light, persist: false);
        await Task.Delay(150);
        CaptureAlert(win, "alert_light.png");
        await Task.Delay(150);
        ThemeManager.Apply(ThemeManager.Dark, persist: false);
        await Task.Delay(150);
        CaptureAlert(win, "alert_dark.png");

        // Password dialog — both themes (best-effort; don't abort the run if flaky)
        try
        {
            ThemeManager.Apply(ThemeManager.Light, persist: false);
            await Task.Delay(150);
            CapturePasswordDialog(win, "password_light.png");
            await Task.Delay(150);
            ThemeManager.Apply(ThemeManager.Dark, persist: false);
            await Task.Delay(150);
            CapturePasswordDialog(win, "password_dark.png");
        }
        catch (Exception ex) { File.AppendAllText(Path.Combine(Dir, "err.txt"), "password capture skipped: " + ex.Message + "\n"); }

        win.Hide();

        // Unified workspace window — Split + Compress, both themes
        ThemeManager.Apply(ThemeManager.Dark, persist: false);
        await Task.Delay(150);
        await CaptureWorkspace("workspace_dark.png", "split_pdf", false);
        await CaptureWorkspace("compress_dark.png", "compress_pdf", false);
        await CaptureWorkspace("p2i_dark.png", "pdf_to_images", false);
        await CaptureWorkspace("busy_dark.png", "compress_pdf", true);
        ThemeManager.Apply(ThemeManager.Light, persist: false);
        await Task.Delay(150);
        await CaptureWorkspace("workspace_light.png", "split_pdf", false);
        await CaptureWorkspace("compress_light.png", "compress_pdf", false);
        await CaptureWorkspace("p2i_light.png", "pdf_to_images", false);
        await CaptureWorkspace("busy_light.png", "compress_pdf", true);

        // Merge file-list layout (inject mock rows so the list renders without real PDFs)
        ThemeManager.Apply(ThemeManager.Dark, persist: false);
        await Task.Delay(150);
        await CaptureMergeList("merge_dark.png");
        ThemeManager.Apply(ThemeManager.Light, persist: false);
        await Task.Delay(150);
        await CaptureMergeList("merge_light.png");
    }

    /// <summary>End-to-end check of the PDF export pipeline (Images→PDF, Fill&amp;Sign export, Protect, Extract).</summary>
    static async Task VerifyExports()
    {
        var log = new System.Text.StringBuilder();
        void L(string s) { log.AppendLine(s); }

        try
        {
            // 1) build a 3-page sample PDF from existing PNGs
            var pngs = Directory.GetFiles(Dir, "*.png").Where(p => !Path.GetFileName(p).StartsWith("verify")).Take(3).ToArray();
            if (pngs.Length == 0)
            {
                // seed Dir with plain white pages so VERIFY works on a clean temp dir
                for (int i = 0; i < 3; i++)
                {
                    using var b = new SD.Bitmap(850, 1100);
                    using (var g = SD.Graphics.FromImage(b)) g.Clear(SD.Color.White);
                    b.Save(Path.Combine(Dir, $"seed_{i}.png"), SD.Imaging.ImageFormat.Png);
                }
                pngs = Directory.GetFiles(Dir, "seed_*.png");
            }
            if (pngs.Length == 0) { L("FAIL: no PNGs available to build a sample PDF"); File.WriteAllText(Path.Combine(Dir, "verify.txt"), log.ToString()); return; }
            string sample = Path.Combine(Dir, "sample.pdf");
            await FileToolsService.ImagesToPdfAsync(pngs, sample);
            int pc = await FileToolsService.GetPdfPageCountAsync(sample);
            L($"sample.pdf: {pngs.Length} images -> {pc} pages  [{(pc == pngs.Length ? "PASS" : "FAIL")}]");

            // 2) Fill & Sign export (text + check + signature image) via the real exporter
            var els = new List<FillElement>
            {
                new() { Page = 0, Type = FillElementType.Text,  X = 60, Y = 60,  Width = 360, Height = 26, Text = "Verified by Llamashot", FontSize = 20, ColorHex = "#C81E1E", Bold = true },
                new() { Page = 0, Type = FillElementType.Check, X = 60, Y = 110, Width = 24,  Height = 24, Text = "X", FontSize = 22, ColorHex = "#1E6FC8" },
                new() { Page = 0, Type = FillElementType.DateTime, X = 60, Y = 150, Width = 240, Height = 22, Text = "June 17, 2026", FontSize = 14, ColorHex = "#222222" },
                new() { Page = 0, Type = FillElementType.Signature, X = 60, Y = 190, Width = 180, Height = 70, ImagePath = pngs[0] },
                // new editor features: mark glyphs, underline, alignment
                new() { Page = 0, Type = FillElementType.Check, X = 60, Y = 280, Width = 28, Height = 24, Text = "✓", FontFamily = "Segoe UI Symbol", FontSize = 18, ColorHex = "#137A3F" },
                new() { Page = 0, Type = FillElementType.Check, X = 100, Y = 280, Width = 28, Height = 24, Text = "✗", FontFamily = "Segoe UI Symbol", FontSize = 18, ColorHex = "#C0392B" },
                new() { Page = 0, Type = FillElementType.Check, X = 140, Y = 280, Width = 28, Height = 24, Text = "●", FontFamily = "Segoe UI Symbol", FontSize = 18, ColorHex = "#1C3FAA" },
                new() { Page = 0, Type = FillElementType.Text, X = 60, Y = 320, Width = 300, Height = 22, Text = "Underlined text", FontSize = 16, Underline = true, ColorHex = "#222222" },
                new() { Page = 0, Type = FillElementType.Text, X = 60, Y = 360, Width = 360, Height = 22, Text = "Center aligned", FontSize = 16, Align = "center", ColorHex = "#0E7490" },
                new() { Page = 0, Type = FillElementType.Text, X = 60, Y = 390, Width = 360, Height = 22, Text = "Right aligned", FontSize = 16, Align = "right", ColorHex = "#6D28D9" },
            };
            string signed = Path.Combine(Dir, "verify_signed.pdf");
            FillSignExporter.Export(sample, els, signed);
            bool signedOk = File.Exists(signed) && new FileInfo(signed).Length > 0;
            int signedPc = signedOk ? await FileToolsService.GetPdfPageCountAsync(signed) : 0;
            L($"verify_signed.pdf: exists={signedOk}, pages={signedPc}  [{(signedOk && signedPc == pc ? "PASS" : "FAIL")}]");
            if (signedOk)
            {
                using var bmp = await FillSignRender.RenderPageToBitmapAsync(signed, 0, 120);
                bmp.Save(Path.Combine(Dir, "verify_signed.png"), SD.Imaging.ImageFormat.Png);
                L("rendered verify_signed.png (page 1)");
            }

            // 3) Protect with a password, then confirm it reads back as encrypted
            string prot = Path.Combine(Dir, "verify_protected.pdf");
            await FileToolsService.ProtectPdfAsync(sample, prot, "secret123");
            bool enc = await FileToolsService.IsPdfEncryptedAsync(prot);
            L($"verify_protected.pdf: encrypted={enc}  [{(enc ? "PASS" : "FAIL")}]");

            // 4) Extract pages 1 and 3
            string extracted = Path.Combine(Dir, "verify_extracted.pdf");
            await FileToolsService.ExtractPdfPagesAsync(sample, new[] { 1, 3 }, extracted);
            int exPc = await FileToolsService.GetPdfPageCountAsync(extracted);
            L($"verify_extracted.pdf: pages={exPc}  [{(exPc == 2 ? "PASS" : "FAIL")}]");
        }
        catch (Exception ex) { L("EXCEPTION: " + ex); }

        File.WriteAllText(Path.Combine(Dir, "verify.txt"), log.ToString());
    }

    /// <summary>Drives the PDF Editor's place/move/edit/delete/undo/redo code paths via reflection (no UI clicks).</summary>
    static async Task VerifyProtect(string pdf)
    {
        var log = new System.Text.StringBuilder();
        void L(string s) => log.AppendLine(s);
        string outDir = Dir;
        try
        {
            if (!File.Exists(pdf)) { L($"FAIL: file not found: {pdf}"); File.WriteAllText(Path.Combine(outDir, "protect.txt"), log.ToString()); return; }

            // 1) Add password
            string locked = Path.Combine(outDir, "protect_locked.pdf");
            await FileToolsService.ProtectPdfAsync(pdf, locked, "1234");
            bool enc = await FileToolsService.IsPdfEncryptedAsync(locked);
            L($"add password: encrypted={enc}  [{(enc ? "PASS" : "FAIL")}]");

            // 2) Remove password (known)
            string unlocked = Path.Combine(outDir, "protect_unlocked.pdf");
            await FileToolsService.RemovePdfPasswordAsync(locked, unlocked, "1234");
            bool stillEnc = await FileToolsService.IsPdfEncryptedAsync(unlocked);
            int upc = await FileToolsService.GetPdfPageCountAsync(unlocked);
            L($"remove password: encrypted={stillEnc}, pages={upc}  [{(!stillEnc && upc > 0 ? "PASS" : "FAIL")}]");

            // 3) Break password (unknown) — digits up to length 4 should recover "1234"
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var cts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(120));
            string? found = await FileToolsService.RecoverPdfPasswordAsync(locked, "0123456789", 4, null, cts.Token);
            sw.Stop();
            L($"break (dictionary '1234'): found='{found}' in {sw.ElapsedMilliseconds} ms  [{(found == "1234" ? "PASS" : "FAIL")}]");

            // brute-force-only: a non-dictionary numeric password
            string locked2 = Path.Combine(outDir, "protect_locked2.pdf");
            await FileToolsService.ProtectPdfAsync(pdf, locked2, "8254");
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            string? found2 = await FileToolsService.RecoverPdfPasswordAsync(locked2, "0123456789", 4, null, cts.Token);
            sw2.Stop();
            L($"break (brute '8254'): found='{found2}' in {sw2.ElapsedMilliseconds} ms  [{(found2 == "8254" ? "PASS" : "FAIL")}]");
        }
        catch (Exception ex) { L("EXCEPTION: " + ex); }
        File.WriteAllText(Path.Combine(outDir, "protect.txt"), log.ToString());
    }

    static async Task VerifyEditorLogic(string pdf)
    {
        var log = new System.Text.StringBuilder();
        void L(string s) => log.AppendLine(s);
        const System.Reflection.BindingFlags BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var t = typeof(PdfMarkupWindow);
        object? Inv(object w, string m, params object?[] a) => t.GetMethod(m, BF)!.Invoke(w, a);
        try
        {
            if (!File.Exists(pdf)) { L($"FAIL: file not found {pdf}"); File.WriteAllText(Path.Combine(Dir, "editorui.txt"), log.ToString()); return; }
            var w = new PdfMarkupWindow();
            await (Task)Inv(w, "LoadPdfFile", pdf)!;
            var elsF = t.GetField("_elements", BF)!;
            var els = (System.Collections.Generic.List<FillElement>)elsF.GetValue(w)!;
            void Count(string label, int expect) => L($"{label}: {els.Count} (expect {expect})  [{(els.Count == expect ? "PASS" : "FAIL")}]");
            Count("after load", 0);

            Inv(w, "PlaceText", 100.0, 100.0, "Hello", FillElementType.Text);
            Inv(w, "PlaceText", 120.0, 200.0, "World", FillElementType.Text);
            Inv(w, "PlaceMark", 140.0, 300.0, "✓");
            Count("placed 2 text + 1 mark", 3);

            // empty text field must auto-enter edit mode (TextBox becomes editable) so the user can type immediately
            Inv(w, "PlaceText", 200.0, 250.0, "", FillElementType.Text);
            var emptyEl = els[els.Count - 1];
            var tbox = (System.Windows.Controls.TextBox?)Inv(w, "ChipTextBox", emptyEl);
            bool editable = tbox != null && !tbox.IsReadOnly;
            L($"empty text auto-edit: textbox found={tbox != null}, editable={editable}  [{(editable ? "PASS" : "FAIL")}]");
            // typing simulation — set text and confirm it flows back to the element
            if (tbox != null) { tbox.Text = "Typed!"; L($"type into field: el.Text='{emptyEl.Text}'  [{(emptyEl.Text == "Typed!" ? "PASS" : "FAIL")}]"); }
            Inv(w, "Select", els[0]); // leave a clean selection state
            els.Remove(emptyEl);

            // abandoned empty field must be discarded with NO leftover undo step
            int undoBefore = ((System.Collections.ICollection)t.GetField("_undo", BF)!.GetValue(w)!).Count;
            Inv(w, "PlaceText", 300.0, 350.0, "", FillElementType.Text);
            int afterPlace = els.Count;
            var blankEl = els[els.Count - 1];
            Inv(w, "DiscardElement", blankEl);
            int undoAfter = ((System.Collections.ICollection)t.GetField("_undo", BF)!.GetValue(w)!).Count;
            bool discardOk = els.Count == afterPlace - 1 && undoAfter == undoBefore && !els.Contains(blankEl);
            L($"discard empty field: count {afterPlace}->{els.Count}, undo {undoBefore}->{undoAfter}  [{(discardOk ? "PASS" : "FAIL")}]");

            // Properties CONTENT edit must flow to the on-page chip (the host-Grid sync bug)
            var contentBox = (System.Windows.Controls.TextBox)Inv(w, "ContentBox", els[0])!;
            contentBox.Text = "Edited via props";
            var chipTb = (System.Windows.Controls.TextBox?)Inv(w, "ChipTextBox", els[0]);
            bool sync = els[0].Text == "Edited via props" && chipTb?.Text == "Edited via props";
            L($"props->page sync: el='{els[0].Text}' chip='{chipTb?.Text}'  [{(sync ? "PASS" : "FAIL")}]");
            els[0].Text = "Hello"; if (chipTb != null) chipTb.Text = "Hello"; // restore for later asserts

            // move element 0 (+50 X) and commit through the edit/undo pipeline
            var e0 = els[0];
            Inv(w, "Select", e0);
            double oldX = e0.X; e0.X = oldX + 50;
            Inv(w, "EditCommit", e0);
            L($"move: X {oldX:0}->{els[0].X:0}  [{(System.Math.Abs(els[0].X - oldX - 50) < 0.5 ? "PASS" : "FAIL")}]");

            Inv(w, "Undo");
            L($"undo move: X={els[0].X:0} (expect {oldX:0})  [{(System.Math.Abs(els[0].X - oldX) < 0.5 ? "PASS" : "FAIL")}]");

            // delete element 0
            Inv(w, "Select", els[0]);
            Inv(w, "DeleteSelected");
            Count("after delete", 2);

            Inv(w, "Undo");
            Count("undo delete", 3);

            Inv(w, "Redo");
            Count("redo delete", 2);

            // edit text content of element 0, commit, undo
            var te = els[0];
            string oldText = te.Text;
            Inv(w, "Select", te);
            te.Text = "Changed";
            Inv(w, "EditCommit", te);
            L($"edit text: '{oldText}'->'{els[0].Text}'  [{(els[0].Text == "Changed" ? "PASS" : "FAIL")}]");
            Inv(w, "Undo");
            L($"undo edit: '{els[0].Text}' (expect '{oldText}')  [{(els[0].Text == oldText ? "PASS" : "FAIL")}]");
        }
        catch (Exception ex) { L("EXCEPTION: " + ex); }
        File.WriteAllText(Path.Combine(Dir, "editorui.txt"), log.ToString());
    }

    /// <summary>Reproduces the "first text vs second text" behaviour: place a long text and a short one, log geometry + export.</summary>
    static async Task ReproTextBoxes(string pdf)
    {
        var log = new System.Text.StringBuilder();
        void L(string s) => log.AppendLine(s);
        const System.Reflection.BindingFlags BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var t = typeof(PdfMarkupWindow);
        object? Inv(object w, string m, params object?[] a) => t.GetMethod(m, BF)!.Invoke(w, a);
        try
        {
            ThemeManager.Apply(ThemeManager.Dark, persist: false);
            var w = new PdfMarkupWindow { WindowState = WindowState.Normal, Width = 1380, Height = 880, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
            w.Show(); w.Activate(); await Task.Delay(400);
            await (Task)Inv(w, "LoadPdfFile", pdf)!; await Task.Delay(500);
            var els = (System.Collections.Generic.List<FillElement>)t.GetField("_elements", BF)!.GetValue(w)!;

            // mimic the interactive path: click (empty) then type
            Inv(w, "PlaceText", 120.0, 150.0, "", FillElementType.Text);
            var tbA = (System.Windows.Controls.TextBox?)Inv(w, "ChipTextBox", els[^1]);
            if (tbA != null) tbA.Text = "This is a long text aaaaaaaaaaa bbbbbbbbb";
            var elA = els[^1];

            Inv(w, "PlaceText", 120.0, 230.0, "", FillElementType.Text);
            var tbB = (System.Windows.Controls.TextBox?)Inv(w, "ChipTextBox", els[^1]);
            if (tbB != null) tbB.Text = "ass";
            var elB = els[^1];

            await Task.Delay(200); w.UpdateLayout(); await Task.Delay(150);
            L($"A long : X={elA.X:0} W={elA.Width:0} size={elA.FontSize} bold={elA.Bold} italic={elA.Italic} color={elA.ColorHex} align={elA.Align} text='{elA.Text}'");
            L($"B short: X={elB.X:0} W={elB.Width:0} size={elB.FontSize} bold={elB.Bold} italic={elB.Italic} color={elB.ColorHex} align={elB.Align} text='{elB.Text}'");
            if (tbA != null) L($"A textbox: ActualWidth={tbA.ActualWidth:0} TextAlignment={tbA.TextAlignment}");
            if (tbB != null) L($"B textbox: ActualWidth={tbB.ActualWidth:0} TextAlignment={tbB.TextAlignment}");
            ShotRtb(w, "textrepro_editor.png");

            // export + render to see how each lands on the page
            string outPdf = Path.Combine(Dir, "textrepro.pdf");
            FillSignExporter.Export(pdf, new List<FillElement>(els), outPdf);
            using (var rb = await FillSignRender.RenderPageToBitmapAsync(outPdf, 0, 150))
                rb.Save(Path.Combine(Dir, "textrepro_export.png"), SD.Imaging.ImageFormat.Png);
            L("rendered textrepro_export.png");
            w.Close();
        }
        catch (Exception ex) { L("EXCEPTION: " + ex); }
        File.WriteAllText(Path.Combine(Dir, "textrepro.txt"), log.ToString());
    }

    /// <summary>Captures the editor with the Pen tool active (DRAW rail + stroke props) and the colourful About update card.</summary>
    static async Task CaptureDrawUi(string pdf)
    {
        const System.Reflection.BindingFlags BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var t = typeof(PdfMarkupWindow);

        ThemeManager.Apply(ThemeManager.Dark, persist: false);
        var w = new PdfMarkupWindow { WindowState = WindowState.Normal, Width = 1380, Height = 880, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
        w.Show(); w.Activate();
        await Task.Delay(400);
        if (File.Exists(pdf)) { await (Task)t.GetMethod("LoadPdfFile", BF)!.Invoke(w, new object?[] { pdf })!; await Task.Delay(600); }
        // activate the Pen tool so the DRAW rail highlight + stroke-width/colour props show
        t.GetField("_tool", BF)!.SetValue(w, "pen");
        t.GetMethod("HighlightTool", BF)!.Invoke(w, null);
        t.GetMethod("RefreshProps", BF)!.Invoke(w, null);
        await Task.Delay(300);
        w.UpdateLayout(); await Task.Delay(200);
        ShotRtb(w, "draw_editor.png");

        // date tool to show the FORMAT picker
        t.GetField("_tool", BF)!.SetValue(w, "date");
        t.GetMethod("HighlightTool", BF)!.Invoke(w, null);
        t.GetMethod("RefreshProps", BF)!.Invoke(w, null);
        await Task.Delay(250); w.UpdateLayout(); await Task.Delay(150);
        ShotRtb(w, "draw_dateformat.png");

        // text tool to show MATCH DOCUMENT toggle + font list
        t.GetField("_tool", BF)!.SetValue(w, "text");
        t.GetMethod("HighlightTool", BF)!.Invoke(w, null);
        t.GetMethod("RefreshProps", BF)!.Invoke(w, null);
        await Task.Delay(250); w.UpdateLayout(); await Task.Delay(150);
        ShotRtb(w, "draw_textmatch.png");
        w.Close();

        // signature dialog (Type mode) — show after a tiny delay then capture
        var sigTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        sigTimer.Tick += (_, _) =>
        {
            sigTimer.Stop();
            var dlg = System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<SignatureDialog>(Application.Current.Windows));
            if (dlg != null)
            {
                var dt = typeof(SignatureDialog);
                dt.GetField("_mode", BF)!.SetValue(dlg, "type");
                dt.GetField("_typeBox", BF)!.GetValue(dlg);
                var box = (System.Windows.Controls.TextBox)dt.GetField("_typeBox", BF)!.GetValue(dlg)!;
                box.Text = "Santosh Kumar";
                dt.GetMethod("ApplyMode", BF)!.Invoke(dlg, null);
                dlg.UpdateLayout();
                ShotRtb(dlg, "draw_signature.png");
                dlg.Close();
            }
        };
        sigTimer.Start();
        var sig = new SignatureDialog { Topmost = true };
        sig.ShowDialog();

        // About window update card
        var about = new AboutWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
        about.Show(); about.Activate();
        await Task.Delay(500); about.UpdateLayout(); await Task.Delay(200);
        ShotRtb(about, "draw_about.png");
        about.Close();
    }

    /// <summary>Verifies the DRAW tools export, the expanded font resolver, date formats, and font detection.</summary>
    static async Task VerifyDrawAndFonts(string pdf)
    {
        var log = new System.Text.StringBuilder();
        void L(string s) => log.AppendLine(s);
        string outDir = Dir;
        try
        {
            if (!File.Exists(pdf)) { L($"FAIL: file not found: {pdf}"); File.WriteAllText(Path.Combine(outDir, "drawtest.txt"), log.ToString()); return; }

            // 1) Font resolver — every offered family must resolve to an existing font file.
            var families = new[]
            {
                "Segoe UI","Arial","Calibri","Candara","Corbel","Tahoma","Trebuchet MS","Verdana",
                "Franklin Gothic Medium","Bahnschrift","Ebrima","Times New Roman","Georgia","Constantia",
                "Palatino Linotype","Consolas","Courier New","Lucida Console","Impact",
                "Segoe Script","Segoe Print","Ink Free","Gabriola","Lucida Handwriting","Comic Sans MS"
            };
            var resolver = new FillSignFontResolver();
            int fontOk = 0, fontFallback = 0;
            foreach (var f in families)
            {
                var info = resolver.ResolveTypeface(f, false, false);
                bool exists = info != null && File.Exists(info.FaceName);
                bool bytes = exists && resolver.GetFont(info!.FaceName) is { Length: > 0 };
                bool mapped = info != null && Path.GetFileName(info.FaceName).IndexOf("arial", StringComparison.OrdinalIgnoreCase) < 0;
                if (bytes) { fontOk++; if (!mapped) fontFallback++; }
                L($"font {f,-22} -> {(info != null ? Path.GetFileName(info.FaceName) : "null"),-18} {(bytes ? "OK" : "MISSING")}{(bytes && !mapped ? " (fallback→Arial)" : "")}");
            }
            L($"fonts: {fontOk}/{families.Length} embeddable, {fontFallback} fell back to Arial  [{(fontOk == families.Length ? "PASS" : "WARN")}]");

            // 1b) Custom date/time format — pattern formats, invalid pattern falls back without throwing.
            var sf = typeof(PdfMarkupWindow).GetMethod("SafeFormat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            string custom = (string)sf.Invoke(null, new object?[] { "dd-MMM-yyyy" })!;
            string custom2 = (string)sf.Invoke(null, new object?[] { "ddd HH:mm" })!;
            string bad = (string)sf.Invoke(null, new object?[] { "q%%z!!" })!;
            bool okFmt = custom.Length > 0 && custom2.Length > 0 && bad.Length > 0;
            L($"custom format: 'dd-MMM-yyyy'->{custom} · 'ddd HH:mm'->{custom2} · invalid->{bad}  [{(okFmt ? "PASS" : "FAIL")}]");

            // 2) Font detection — render the page and sample the centre band for text.
            using (var pageBmp = await FillSignRender.RenderPageToBitmapAsync(pdf, 0, 120))
            {
                int hits = 0;
                for (int yy = pageBmp.Height / 5; yy < pageBmp.Height * 4 / 5; yy += Math.Max(8, pageBmp.Height / 40))
                {
                    var s = FillSignDetector.DetectFontAt(pageBmp, pageBmp.Width / 2, yy, 120);
                    if (s != null) { hits++; if (hits <= 3) L($"detect @y={yy}: size≈{s.FontSizePt}pt color={s.ColorHex} bold={s.Bold} italic={s.Italic}"); }
                }
                L($"font detection: {hits} text rows sampled  [{(hits > 0 ? "PASS" : "WARN (no text found)")}]");
            }

            // 3) Draw + cursive + date export.
            int pc = await FileToolsService.GetPdfPageCountAsync(pdf);
            var els = new List<FillElement>
            {
                new() { Page = 0, Type = FillElementType.Draw, Shape = "rect", X = 80, Y = 90, Width = 180, Height = 70, ColorHex = "#C0392B", StrokeWidth = 3 },
                new() { Page = 0, Type = FillElementType.Draw, Shape = "ellipse", X = 300, Y = 90, Width = 150, Height = 80, ColorHex = "#1C3FAA", StrokeWidth = 2 },
                new() { Page = 0, Type = FillElementType.Draw, Shape = "line", X = 80, Y = 200, Width = 200, Height = 60, ColorHex = "#137A3F", StrokeWidth = 3,
                        Points = new() { (0,0), (200,60) } },
                new() { Page = 0, Type = FillElementType.Draw, Shape = "arrow", X = 320, Y = 200, Width = 160, Height = 50, ColorHex = "#6D28D9", StrokeWidth = 3,
                        Points = new() { (0,50), (160,0) } },
                new() { Page = 0, Type = FillElementType.Draw, Shape = "pen", X = 80, Y = 300, Width = 160, Height = 60, ColorHex = "#222222", StrokeWidth = 2,
                        Points = new() { (0,30), (30,0), (60,40), (90,5), (120,35), (160,10) } },
                new() { Page = 0, Type = FillElementType.Draw, Shape = "highlight", X = 80, Y = 400, Width = 300, Height = 18, ColorHex = "#FFE100", StrokeWidth = 16, Opacity = 40,
                        Points = new() { (0,9), (300,9) } },
                new() { Page = 0, Type = FillElementType.Text, X = 80, Y = 470, Width = 300, Height = 40, Text = "Santosh Kumar", FontFamily = "Segoe Script", FontSize = 28, ColorHex = "#1C3FAA" },
                new() { Page = 0, Type = FillElementType.DateTime, X = 80, Y = 530, Width = 220, Height = 18, Text = DateTime.Now.ToString("dddd, MMMM d, yyyy"), FontFamily = "Georgia", FontSize = 13, ColorHex = "#222222" },
            };

            string outPdf = Path.Combine(outDir, "drawtest.pdf");
            FillSignExporter.Export(pdf, els, outPdf);
            bool ok = File.Exists(outPdf) && new FileInfo(outPdf).Length > 0;
            int opc = ok ? await FileToolsService.GetPdfPageCountAsync(outPdf) : 0;
            L($"drawtest.pdf: exists={ok}, pages={opc}/{pc}  [{(ok && opc == pc ? "PASS" : "FAIL")}]");
            if (ok)
            {
                using var rb = await FillSignRender.RenderPageToBitmapAsync(outPdf, 0, 150);
                rb.Save(Path.Combine(outDir, "drawtest.png"), SD.Imaging.ImageFormat.Png);
                L("rendered drawtest.png (page 1)");
            }
        }
        catch (Exception ex) { L("EXCEPTION: " + ex); }
        File.WriteAllText(Path.Combine(outDir, "drawtest.txt"), log.ToString());
    }

    /// <summary>Exercises the new PDF Editor element types (text, marks, date/time, cover, blur) against a real file.</summary>
    static async Task VerifyEditorOnFile(string pdf)
    {
        var log = new System.Text.StringBuilder();
        void L(string s) => log.AppendLine(s);
        string outDir = Dir;
        try
        {
            if (!File.Exists(pdf)) { L($"FAIL: file not found: {pdf}"); File.WriteAllText(Path.Combine(outDir, "editor.txt"), log.ToString()); return; }
            int pc = await FileToolsService.GetPdfPageCountAsync(pdf);
            var (wPt, hPt, _) = await FillSignRender.GetPageSizeAsync(pdf, 0);
            L($"{Path.GetFileName(pdf)}: {pc} pages, page1 = {wPt:0}×{hPt:0} pt  [PASS]");

            // coordinates are in PDF points for an A4 page (595×842)
            var els = new List<FillElement>
            {
                new() { Page = 0, Type = FillElementType.Text, X = 150, Y = 300, Width = 240, Height = 18, Text = "Santosh Kumar", FontFamily = "Segoe UI", FontSize = 13, ColorHex = "#1C3FAA" },
                new() { Page = 0, Type = FillElementType.Check, X = 300, Y = 350, Width = 22, Height = 18, Text = "✓", FontFamily = "Segoe UI Symbol", FontSize = 16, ColorHex = "#137A3F" },
                new() { Page = 0, Type = FillElementType.Check, X = 330, Y = 350, Width = 22, Height = 18, Text = "✗", FontFamily = "Segoe UI Symbol", FontSize = 16, ColorHex = "#C0392B" },
                new() { Page = 0, Type = FillElementType.Check, X = 360, Y = 350, Width = 22, Height = 18, Text = "●", FontFamily = "Segoe UI Symbol", FontSize = 16, ColorHex = "#1C3FAA" },
                new() { Page = 0, Type = FillElementType.DateTime, X = 150, Y = 330, Width = 130, Height = 16, Text = "Jun 18, 2026", FontSize = 13, ColorHex = "#222222" },
                new() { Page = 0, Type = FillElementType.DateTime, X = 300, Y = 330, Width = 90, Height = 16, Text = "3:37 PM", FontSize = 13, ColorHex = "#222222" },
                // Cover (redact): white strip then black strip
                new() { Page = 0, Type = FillElementType.Redact, X = 150, Y = 400, Width = 200, Height = 16, ColorHex = "#FFFFFF" },
                new() { Page = 0, Type = FillElementType.Redact, X = 150, Y = 420, Width = 200, Height = 16, ColorHex = "#222222" },
                // Blur over the top title block (with 60% opacity)
                new() { Page = 0, Type = FillElementType.Blur, X = 150, Y = 38, Width = 320, Height = 24, FontSize = 16, Opacity = 60 },
                // Rotated text + rotated cover to exercise the new rotation export path
                new() { Page = 0, Type = FillElementType.Text, X = 400, Y = 500, Width = 160, Height = 18, Text = "Rotated 30°", FontSize = 13, ColorHex = "#6D28D9", Rotation = 30 },
                new() { Page = 0, Type = FillElementType.Redact, X = 400, Y = 560, Width = 120, Height = 20, ColorHex = "#C0392B", Rotation = 45, Opacity = 50 },
            };

            // pre-render blur regions to images (mirrors PdfMarkupWindow.RenderBlursAsync)
            const int dpi = 200;
            string blurDir = Path.Combine(outDir, "blurs");
            Directory.CreateDirectory(blurDir);
            foreach (var grp in els.FindAll(e => e.Type == FillElementType.Blur).GroupBy(e => e.Page))
            {
                using var bmp = await FillSignRender.RenderPageToBitmapAsync(pdf, grp.Key, dpi);
                int i = 0;
                foreach (var e in grp)
                {
                    int x = (int)(e.X * dpi / 72.0), y = (int)(e.Y * dpi / 72.0), w = (int)(e.Width * dpi / 72.0), h = (int)(e.Height * dpi / 72.0);
                    x = Math.Clamp(x, 0, bmp.Width - 1); y = Math.Clamp(y, 0, bmp.Height - 1);
                    w = Math.Clamp(w, 1, bmp.Width - x); h = Math.Clamp(h, 1, bmp.Height - y);
                    using var crop = bmp.Clone(new SD.Rectangle(x, y, w, h), bmp.PixelFormat);
                    int factor = Math.Clamp((int)(e.FontSize * dpi / 120.0) / 2, 3, 24);
                    int sw = Math.Max(1, crop.Width / factor), sh = Math.Max(1, crop.Height / factor);
                    using var small = new SD.Bitmap(sw, sh);
                    using (var g = SD.Graphics.FromImage(small)) { g.InterpolationMode = SD.Drawing2D.InterpolationMode.HighQualityBilinear; g.DrawImage(crop, 0, 0, sw, sh); }
                    var big = new SD.Bitmap(crop.Width, crop.Height);
                    using (var g = SD.Graphics.FromImage(big)) { g.InterpolationMode = SD.Drawing2D.InterpolationMode.HighQualityBilinear; g.DrawImage(small, 0, 0, crop.Width, crop.Height); }
                    string f = Path.Combine(blurDir, $"b{grp.Key}_{i++}.png");
                    big.Save(f, SD.Imaging.ImageFormat.Png); big.Dispose();
                    e.ImagePath = f;
                }
            }

            string outPdf = Path.Combine(outDir, "editor_137.pdf");
            FillSignExporter.Export(pdf, els, outPdf);
            bool ok = File.Exists(outPdf) && new FileInfo(outPdf).Length > 0;
            int opc = ok ? await FileToolsService.GetPdfPageCountAsync(outPdf) : 0;
            L($"editor_137.pdf: exists={ok}, pages={opc}  [{(ok && opc == pc ? "PASS" : "FAIL")}]");
            if (ok)
            {
                using var rb = await FillSignRender.RenderPageToBitmapAsync(outPdf, 0, 150);
                rb.Save(Path.Combine(outDir, "editor_137.png"), SD.Imaging.ImageFormat.Png);
                L("rendered editor_137.png (page 1)");
            }
        }
        catch (Exception ex) { L("EXCEPTION: " + ex); }
        File.WriteAllText(Path.Combine(outDir, "editor.txt"), log.ToString());
    }

    // Exercises ScreenRecorder's private cursor compositor: grabs the screen, draws the
    // live cursor + yellow highlight onto it, and writes a PNG for visual inspection.
    static void VerifyCursorOverlay()
    {
        var log = new System.Text.StringBuilder();
        void L(string m) { log.AppendLine(m); Console.WriteLine(m); }
        try
        {
            var nm = typeof(ScreenCapture).Assembly.GetType("Llamashot.Core.NativeMethods")!;

            // Move the cursor to a known spot (screen center) so we know where to look.
            var vb = ScreenCapture.GetVirtualScreenBounds();
            int cx = (int)(vb.X + vb.Width / 2), cy = (int)(vb.Y + vb.Height / 2);
            nm.GetMethod("SetCursorPos")!.Invoke(null, new object[] { cx, cy });
            System.Threading.Thread.Sleep(120);

            // Capture the full virtual screen into a System.Drawing bitmap.
            var src = ScreenCapture.CaptureFullScreen();
            using var bmp = ScreenCapture.BitmapSourceToDrawingBitmap(src);

            // Configure a recorder over the whole captured region and invoke the private compositor.
            var rec = new ScreenRecorder(10) { CaptureCursor = true, HighlightCursor = true };
            var t = typeof(ScreenRecorder);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            t.GetField("_regionX", flags)!.SetValue(rec, (int)vb.X);
            t.GetField("_regionY", flags)!.SetValue(rec, (int)vb.Y);
            t.GetField("_regionW", flags)!.SetValue(rec, bmp.Width);
            t.GetField("_regionH", flags)!.SetValue(rec, bmp.Height);
            t.GetMethod("DrawCursorOnFrame", flags)!.Invoke(rec, new object[] { bmp });

            string outPath = Path.Combine(Dir, "cursor_overlay.png");
            bmp.Save(outPath, SD.Imaging.ImageFormat.Png);
            L($"cursor at screen ({cx},{cy}); region {bmp.Width}x{bmp.Height}");

            // Sample pixels in a box around the cursor for yellow-ish highlight pixels.
            int lx = (int)(bmp.Width / 2), ly = (int)(bmp.Height / 2);
            int yellow = 0, sampled = 0;
            for (int dy = -30; dy <= 30; dy++)
                for (int dx = -30; dx <= 30; dx++)
                {
                    int px = lx + dx, py = ly + dy;
                    if (px < 0 || py < 0 || px >= bmp.Width || py >= bmp.Height) continue;
                    var c = bmp.GetPixel(px, py);
                    sampled++;
                    if (c.R > 180 && c.G > 150 && c.B < 140) yellow++;
                }
            L($"yellow-ish pixels near cursor: {yellow}/{sampled}");
            L(yellow > 0 ? "PASS: highlight rendered" : "FAIL: no highlight detected");

            // Crop a 240x240 region around the cursor for easy viewing.
            int half = 120;
            int rx = Math.Max(0, lx - half), ry = Math.Max(0, ly - half);
            int rw = Math.Min(half * 2, bmp.Width - rx), rh = Math.Min(half * 2, bmp.Height - ry);
            using var crop = bmp.Clone(new SD.Rectangle(rx, ry, rw, rh), bmp.PixelFormat);
            crop.Save(Path.Combine(Dir, "cursor_overlay_crop.png"), SD.Imaging.ImageFormat.Png);
            L("wrote cursor_overlay.png + cursor_overlay_crop.png");
        }
        catch (Exception ex) { L("EXCEPTION: " + ex); }
        File.WriteAllText(Path.Combine(Dir, "cursor.txt"), log.ToString());
    }

    // ---- Harness safety: never mutate the user's real settings.json or history ----
    // HistoryWindow's ctor calls AppSettings.Save() and HistoryManager touches the index,
    // both at fixed real paths. Snapshot settings.json + redirect history to a temp dir.
    static (string path, byte[]? backup) ProtectUserData()
    {
        string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Llamashot", "settings.json");
        byte[]? b = File.Exists(p) ? File.ReadAllBytes(p) : null;
        var tempHist = Path.Combine(Path.GetTempPath(), "llamashot_harness_hist");
        Directory.CreateDirectory(tempHist);
        Llamashot.Core.AppSettings.Instance.HistoryDirectory = tempHist; // any history write lands here
        return (p, b);
    }

    static void RestoreUserData((string path, byte[]? backup) s)
    {
        try { if (s.backup != null) File.WriteAllBytes(s.path, s.backup); else if (File.Exists(s.path)) File.Delete(s.path); }
        catch { }
    }

    // Verifies the History filter pills classify by media type (extension) and clipboard
    // origin — not by the save/copy action — so copied screenshots show under Images.
    static void VerifyHistoryFilters()
    {
        var log = new System.Text.StringBuilder();
        void L(string m) { log.AppendLine(m); Console.WriteLine(m); }

        // (ext, isClipboard) → expected buckets.
        var mocks = new System.Collections.Generic.List<HistoryItemViewModel>
        {
            new() { Ext = ".png",  IsClipboard = true  }, // Image + Clipboard
            new() { Ext = ".jpg",  IsClipboard = false }, // Image
            new() { Ext = ".bmp",  IsClipboard = false }, // Image
            new() { Ext = ".mp4",  IsClipboard = false }, // Video
            new() { Ext = ".webm", IsClipboard = false }, // Video
            new() { Ext = ".gif",  IsClipboard = false }, // GIF
            new() { Ext = ".gif",  IsClipboard = true  }, // GIF + Clipboard
            new() { Ext = ".png",  IsClipboard = false }, // Image
        };
        var expected = new (string filter, int count)[]
        {
            ("All", 8), ("Images", 4), ("Videos", 2), ("GIFs", 2), ("Clipboard", 2)
        };

        var prot = ProtectUserData();
        try
        {
            var win = new HistoryWindow { ShowActivated = false };
            var t = typeof(HistoryWindow);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            t.GetField("_allItems", flags)!.SetValue(win, mocks);
            var filterField = t.GetField("_activeFilter", flags)!;
            var applyFilter = t.GetMethod("ApplyFilter", flags)!;
            var itemsField = t.GetField("_items", flags)!;

            bool allPass = true;
            foreach (var (filter, count) in expected)
            {
                filterField.SetValue(win, filter);
                applyFilter.Invoke(win, null);
                var items = (System.Collections.IList)itemsField.GetValue(win)!;
                bool pass = items.Count == count;
                allPass &= pass;
                L($"[{filter}] got {items.Count}, expected {count}  {(pass ? "PASS" : "FAIL")}");
            }
            L(allPass ? "ALL HISTORY FILTER CHECKS PASSED" : "HISTORY FILTER CHECKS FAILED");
            win.Close();
        }
        finally { RestoreUserData(prot); }
        File.WriteAllText(Path.Combine(Dir, "historyfilters.txt"), log.ToString());
    }

    // Opens the History window, injects mock items, and drives every Explorer-style
    // view mode — asserting template/panel/header state and capturing each layout.
    static async Task VerifyHistoryViews()
    {
        var log = new System.Text.StringBuilder();
        void L(string m) { log.AppendLine(m); Console.WriteLine(m); }

        var prot = ProtectUserData();
        try
        {

        // Gather some real PNGs to use as thumbnails.
        var pngs = Directory.GetFiles(Dir, "*.png").Where(p => new FileInfo(p).Length > 1000).Take(6).ToArray();
        if (pngs.Length == 0) pngs = new[] { typeof(Program).Assembly.Location };

        var mocks = new System.Collections.Generic.List<HistoryItemViewModel>();
        string[] types = { "Saved", "Video", "GIF", "Copied" };
        string[] colors = { "#66BB6A", "#F44336", "#4CAF50", "#42A5F5" };
        for (int i = 0; i < 11; i++)
        {
            mocks.Add(new HistoryItemViewModel
            {
                ThumbnailPath = pngs[i % pngs.Length],
                FilePath = $@"C:\Users\DELL\Pictures\Llamashot\shot_{i:00}.png",
                NameText = $"shot_{i:00}.png",
                DateText = $"Jun {18 - i % 6}, 1{i % 6}:0{i % 6}",
                SizeText = $"{1280 + i} x {720 + i}",
                TypeText = types[i % 4],
                TypeDescText = types[i % 4] == "Saved" ? "Image" : types[i % 4] == "Copied" ? "Clipboard" : types[i % 4],
                TypeColor = colors[i % 4],
                ToolTipText = $"shot_{i:00}.png"
            });
        }

        var win = new HistoryWindow
        {
            WindowState = WindowState.Normal, Width = 1280, Height = 820,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true
        };
        win.Show(); win.Activate();
        await Task.Delay(600);

        var t = typeof(HistoryWindow);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var listField = t.GetField("HistoryList", flags)!;
        var applyMethod = t.GetMethod("ApplyViewMode", flags)!;

        // Inject mocks directly into the ItemsControl.
        var list = (System.Windows.Controls.ItemsControl)listField.GetValue(win)!;
        list.ItemsSource = mocks;
        await Task.Delay(200);

        bool allPass = true;
        foreach (HistoryViewMode mode in Enum.GetValues(typeof(HistoryViewMode)))
        {
            applyMethod.Invoke(win, new object[] { mode });
            win.UpdateLayout();
            await Task.Delay(250);

            bool tpl = list.ItemTemplate != null;
            bool panel = list.ItemsPanel != null;
            bool pass = tpl && panel;
            allPass &= pass;
            L($"[{mode}] template={tpl} panel={panel}  {(pass ? "PASS" : "FAIL")}");
            ShotRtb(win, $"history_{mode}.png");
        }

        L(allPass ? "ALL HISTORY VIEW CHECKS PASSED" : "HISTORY VIEW CHECKS FAILED");
        win.Close();

        }
        finally { RestoreUserData(prot); }
        File.WriteAllText(Path.Combine(Dir, "historyviews.txt"), log.ToString());
    }

    // Opens the Compress PDF workspace and captures both compression modes so the
    // new "By level / By target size" toggle and target input can be eyeballed.
    static async Task CaptureCompressUi()
    {
        var ws = new ToolWorkspaceWindow("compress_pdf")
        {
            WindowState = WindowState.Normal, Width = 1380, Height = 880,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true
        };
        ws.Show(); ws.Activate();
        await Task.Delay(700);
        ws.UpdateLayout();
        await Task.Delay(200);
        ShotRtb(ws, "compress_level.png");

        var setMode = typeof(ToolWorkspaceWindow).GetMethod("SetCompressMode",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        setMode!.Invoke(ws, new object[] { true });
        ws.UpdateLayout();
        await Task.Delay(250);
        ShotRtb(ws, "compress_target.png");
        Console.WriteLine("wrote compress_level.png + compress_target.png");
        ws.Close();
    }

    // Confirms compression never produces a file larger than the source:
    //  - a beneficial case still shrinks (regression guard for the feature working)
    //  - an already-optimized / low-entropy case keeps the original bytes
    static async Task VerifyCompression()
    {
        var log = new System.Text.StringBuilder();
        void L(string m) { log.AppendLine(m); Console.WriteLine(m); }
        string dir = Path.Combine(Dir, "compresstest");
        Directory.CreateDirectory(dir);
        bool allPass = true;
        try
        {
            // ---- Image case A: a high-quality noisy photo SHOULD shrink at q40. ----
            using (var photo = new SD.Bitmap(1200, 900))
            {
                var rnd = new Random(7);
                for (int y = 0; y < photo.Height; y++)
                    for (int x = 0; x < photo.Width; x++)
                        photo.SetPixel(x, y, SD.Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256)));
                photo.Save(Path.Combine(dir, "photo.jpg"), SD.Imaging.ImageFormat.Jpeg);
            }
            string photoIn = Path.Combine(dir, "photo.jpg");
            string photoOut = Path.Combine(dir, "photo_compressed.jpg");
            long photoOrig = new FileInfo(photoIn).Length;
            long photoNew = await FileToolsService.CompressImageAsync(photoIn, photoOut, quality: 40);
            bool aPass = photoNew <= photoOrig;
            allPass &= aPass;
            L($"[image-beneficial] {photoOrig} -> {photoNew}  {(aPass ? "PASS (not larger)" : "FAIL (grew!)")}");

            // ---- Image case B: a tiny low-quality JPEG must NOT grow when "compressed" at q85. ----
            using (var small = new SD.Bitmap(400, 300))
            {
                using var g = SD.Graphics.FromImage(small);
                g.Clear(SD.Color.SteelBlue);
                var enc = SD.Imaging.ImageCodecInfo.GetImageEncoders()
                    .First(e => e.FormatID == SD.Imaging.ImageFormat.Jpeg.Guid);
                var ep = new SD.Imaging.EncoderParameters(1);
                ep.Param[0] = new SD.Imaging.EncoderParameter(SD.Imaging.Encoder.Quality, 25L);
                small.Save(Path.Combine(dir, "small.jpg"), enc, ep);
            }
            string smallIn = Path.Combine(dir, "small.jpg");
            string smallOut = Path.Combine(dir, "small_compressed.jpg");
            long smallOrig = new FileInfo(smallIn).Length;
            long smallNew = await FileToolsService.CompressImageAsync(smallIn, smallOut, quality: 85);
            bool bPass = smallNew <= smallOrig;
            allPass &= bPass;
            L($"[image-optimized] {smallOrig} -> {smallNew}  {(bPass ? "PASS (kept original)" : "FAIL (grew!)")}");

            // ---- PDF case: build a PDF, then compress; output must never exceed the source. ----
            string pdfPath = Path.Combine(dir, "doc.pdf");
            await FileToolsService.ImagesToPdfAsync(new[] { smallIn, photoIn }, pdfPath, null);
            string pdfOut = Path.Combine(dir, "doc_compressed.pdf");
            long pdfOrig = new FileInfo(pdfPath).Length;
            long pdfNew = await FileToolsService.CompressPdfAsync(pdfPath, pdfOut, quality: 65);
            bool cPass = pdfNew <= pdfOrig;
            allPass &= cPass;
            L($"[pdf] {pdfOrig} -> {pdfNew}  {(cPass ? "PASS (not larger)" : "FAIL (grew!)")}");

            // ---- Target-size image: bring the 650 KB photo under 100 KB. ----
            string tImgOut = Path.Combine(dir, "photo_target.jpg");
            long tImgTarget = 100 * 1024;
            var tImg = await FileToolsService.CompressImageToTargetAsync(photoIn, tImgOut, tImgTarget);
            bool dPass = tImg.Bytes <= tImgTarget && tImg.TargetMet && tImg.Bytes > 0;
            allPass &= dPass;
            L($"[image-target<=100KB] {tImg.Bytes} bytes, met={tImg.TargetMet}  {(dPass ? "PASS" : "FAIL")}");

            // ---- Target-size image already under target: must be kept, flagged met. ----
            string tSmallOut = Path.Combine(dir, "small_target.jpg");
            var tSmall = await FileToolsService.CompressImageToTargetAsync(smallIn, tSmallOut, 1024 * 1024);
            bool ePass = tSmall.TargetMet && tSmall.Bytes <= smallOrig;
            allPass &= ePass;
            L($"[image-target-already] {tSmall.Bytes} bytes, met={tSmall.TargetMet}  {(ePass ? "PASS" : "FAIL")}");

            // ---- Target-size PDF: bring the doc under 150 KB. ----
            string tPdfOut = Path.Combine(dir, "doc_target.pdf");
            long tPdfTarget = 150 * 1024;
            var tPdf = await FileToolsService.CompressPdfToTargetAsync(pdfPath, tPdfOut, tPdfTarget);
            // Either it met the target, or it's a best-effort result no larger than the source.
            bool fPass = tPdf.Bytes > 0 && (tPdf.TargetMet ? tPdf.Bytes <= tPdfTarget : tPdf.Bytes <= pdfOrig);
            allPass &= fPass;
            L($"[pdf-target<=150KB] {tPdf.Bytes} bytes, met={tPdf.TargetMet}  {(fPass ? "PASS" : "FAIL")}");

            L(allPass ? "ALL COMPRESSION CHECKS PASSED" : "COMPRESSION CHECKS FAILED");
        }
        catch (Exception ex) { L("EXCEPTION: " + ex); }
        File.WriteAllText(Path.Combine(Dir, "compress.txt"), log.ToString());
    }

    // Drives OverlayWindow.StartCapture and inspects internal state to confirm the
    // default work-area selection, persistent mode picker, and mode switching.
    static async Task VerifyOverlaySelection()
    {
        var log = new System.Text.StringBuilder();
        void L(string m) { log.AppendLine(m); Console.WriteLine(m); }
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        try
        {
            var ot = typeof(OverlayWindow);
            var overlay = (OverlayWindow)Activator.CreateInstance(ot)!;
            var modeEnum = ot.GetNestedType("CaptureMode", System.Reflection.BindingFlags.NonPublic);
            object screenshotMode = Enum.Parse(modeEnum!, "Screenshot");
            ot.GetMethod("StartCapture", flags)!.Invoke(overlay, new object[] { screenshotMode });

            await Task.Delay(700); // let the Loaded dispatcher apply the default selection

            T GetF<T>(string n) => (T)ot.GetField(n, flags)!.GetValue(overlay)!;
            Visibility Vis(string n) => ((UIElement)ot.GetField(n, flags)!.GetValue(overlay)!).Visibility;

            bool hasSel = GetF<bool>("_hasSelection");
            bool full = GetF<bool>("_isFullRegion");
            var sel = GetF<Rect>("_selection");
            var wa = SystemParameters.WorkArea;
            L($"_hasSelection={hasSel} _isFullRegion={full} _selection={(int)sel.Width}x{(int)sel.Height}  workArea={(int)wa.Width}x{(int)wa.Height}");
            L($"picker(SnippingToolbarCanvas)={Vis("SnippingToolbarCanvas")}  drawingToolbar(ToolbarCanvas)={Vis("ToolbarCanvas")}  handles={Vis("HandleCanvas")}");
            bool p1 = hasSel && full
                && Math.Abs(sel.Width - wa.Width) < 2 && Math.Abs(sel.Height - wa.Height) < 2
                && Vis("SnippingToolbarCanvas") == Visibility.Visible
                && Vis("ToolbarCanvas") == Visibility.Visible;
            L(p1 ? "PASS: screenshot pre-selected to work area, picker + toolbar visible"
                 : "FAIL: screenshot default selection wrong");

            // Switch to Video — selection should persist, video toolbar appears, picker stays.
            ot.GetMethod("ModeVideo_Click", flags)!.Invoke(overlay, new object[] { overlay, new RoutedEventArgs() });
            await Task.Delay(150);
            bool p2 = GetF<bool>("_hasSelection")
                && Vis("VideoToolbarCanvas") == Visibility.Visible
                && Vis("SnippingToolbarCanvas") == Visibility.Collapsed;
            L($"after Video: hasSel={GetF<bool>("_hasSelection")} videoToolbar={Vis("VideoToolbarCanvas")} picker={Vis("SnippingToolbarCanvas")}");
            L(p2 ? "PASS: video keeps selection, shows ONLY start bar (picker hidden)"
                 : "FAIL: video mode switch wrong");

            // Switch to OCR — region selection cleared back to idle.
            ot.GetMethod("ModeOcr_Click", flags)!.Invoke(overlay, new object[] { overlay, new RoutedEventArgs() });
            await Task.Delay(150);
            var interaction = ot.GetField("_interaction", flags)!.GetValue(overlay)!.ToString();
            bool p3 = !GetF<bool>("_hasSelection") && interaction == "ToolbarIdle"
                && Vis("SnippingToolbarCanvas") == Visibility.Visible;
            L($"after OCR: hasSel={GetF<bool>("_hasSelection")} interaction={interaction} picker={Vis("SnippingToolbarCanvas")}");
            L(p3 ? "PASS: OCR clears selection to idle, picker stays"
                 : "FAIL: OCR mode switch wrong");

            // Back to Screenshot — default selection re-applied.
            ot.GetMethod("ModeScreenshot_Click", flags)!.Invoke(overlay, new object[] { overlay, new RoutedEventArgs() });
            await Task.Delay(150);
            bool p4 = GetF<bool>("_hasSelection") && GetF<bool>("_isFullRegion");
            L($"back to Screenshot: hasSel={GetF<bool>("_hasSelection")} full={GetF<bool>("_isFullRegion")}");
            L(p4 ? "PASS: screenshot re-applies default selection" : "FAIL: re-apply wrong");

            try { overlay.Close(); } catch { }
            L((p1 && p2 && p3 && p4) ? "ALL OVERLAY CHECKS PASSED" : "SOME OVERLAY CHECKS FAILED");
        }
        catch (Exception ex) { L("EXCEPTION: " + ex); }
        File.WriteAllText(Path.Combine(Dir, "overlay.txt"), log.ToString());
    }

    static async Task CaptureMergePreview()
    {
        // build two small sample PDFs from existing PNGs
        var pngs = Directory.GetFiles(Dir, "*.png").Where(p => !Path.GetFileName(p).StartsWith("_")).Take(5).ToArray();
        string pdfA = Path.Combine(Dir, "mp_a.pdf");
        string pdfB = Path.Combine(Dir, "mp_b.pdf");
        await FileToolsService.ImagesToPdfAsync(pngs.Take(3).ToArray(), pdfA);
        await FileToolsService.ImagesToPdfAsync(pngs.Skip(3).Take(2).ToArray(), pdfB);

        var ws = new ToolWorkspaceWindow("merge_pdf")
        {
            WindowState = WindowState.Normal, Width = 1380, Height = 880,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true
        };
        ws.Show(); ws.Activate();
        await Task.Delay(500);

        var t = typeof(ToolWorkspaceWindow);
        const System.Reflection.BindingFlags BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var files = (List<string>)t.GetField("_mergeFiles", BF)!.GetValue(ws)!;
        var pages = (Dictionary<string, int>)t.GetField("_mergePages", BF)!.GetValue(ws)!;
        var pw = (Dictionary<string, string?>)t.GetField("_mergePw", BF)!.GetValue(ws)!;
        foreach (var (path, n) in new[] { (pdfA, 3), (pdfB, 2) }) { files.Add(path); pages[path] = n; pw[path] = null; }
        t.GetMethod("RefreshAll", BF)!.Invoke(ws, null);
        await Task.Delay(1400); // let the preview + filmstrip render

        await Task.Delay(1500); // let thumbnails render
        ws.UpdateLayout();
        await Task.Delay(300);
        ThemeManager.Apply(ThemeManager.Dark, persist: false);
        await Task.Delay(200);
        ShotRtb(ws, "mergepreview_dark.png");
        ThemeManager.Apply(ThemeManager.Light, persist: false);
        await Task.Delay(200);
        ShotRtb(ws, "mergepreview_light.png");
        ws.Close();
    }

    static async Task CaptureMarkup(string name, string mode)
    {
        var w = new PdfMarkupWindow(mode)
        {
            WindowState = WindowState.Normal, Width = 1380, Height = 880,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true
        };
        w.Show(); w.Activate();
        await Task.Delay(500);
        w.UpdateLayout();
        await Task.Delay(300);
        if (Environment.GetEnvironmentVariable("LLAMASHOT_WS_ONLY") == "1") ShotRtb(w, name);
        else Shot(w, name);
        w.Close();
    }

    static async Task CaptureMergeList(string name, string toolId = "merge_pdf", (string path, int pages)[]? rows = null)
    {
        var ws = new ToolWorkspaceWindow(toolId)
        {
            WindowState = WindowState.Normal, Width = 1380, Height = 880,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true
        };
        ws.Show(); ws.Activate();
        await Task.Delay(500);

        var t = typeof(ToolWorkspaceWindow);
        const System.Reflection.BindingFlags BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var files = (System.Collections.Generic.List<string>)t.GetField("_mergeFiles", BF)!.GetValue(ws)!;
        var pages = (System.Collections.Generic.Dictionary<string, int>)t.GetField("_mergePages", BF)!.GetValue(ws)!;
        var pw = (System.Collections.Generic.Dictionary<string, string?>)t.GetField("_mergePw", BF)!.GetValue(ws)!;

        // use real existing files so FileInfo works; names/sizes are just for layout
        rows ??= new[] { (typeof(ToolWorkspaceWindow).Assembly.Location, 12), (typeof(Program).Assembly.Location, 5) };
        foreach (var (path, pg) in rows) { files.Add(path); pages[path] = pg; pw[path] = null; }

        t.GetMethod("RefreshAll", BF)!.Invoke(ws, null);
        await Task.Delay(1400); // let the preview + filmstrip render

        ws.UpdateLayout();
        await Task.Delay(300);
        if (Environment.GetEnvironmentVariable("LLAMASHOT_WS_ONLY") == "1") ShotRtb(ws, name);
        else Shot(ws, name);
        ws.Close();
    }

    static async Task CaptureWorkspace(string name, string toolId, bool busy)
    {
        var ws = new ToolWorkspaceWindow(toolId)
        {
            WindowState = WindowState.Normal, Width = 1380, Height = 880,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true
        };
        ws.Show(); ws.Activate();
        await Task.Delay(600);
        if (busy)
        {
            // peek the spinner overlay (private helper) for a static capture
            var m = typeof(ToolWorkspaceWindow).GetMethod("ShowBusy",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            m?.Invoke(ws, new object[] { "Compressing PDF…", "Optimizing pages and images" });
            await Task.Delay(250);
        }
        ws.UpdateLayout();
        await Task.Delay(300);
        if (Environment.GetEnvironmentVariable("LLAMASHOT_WS_ONLY") == "1") ShotRtb(ws, name);
        else Shot(ws, name);
        ws.Close();
    }

    /// <summary>Renders the window visual directly (no screen capture / z-order dependency).</summary>
    static void ShotRtb(Window win, string name)
    {
        try
        {
            win.UpdateLayout();
            int w = (int)win.ActualWidth, h = (int)win.ActualHeight;
            var rtb = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(win);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(Path.Combine(Dir, name));
            enc.Save(fs);
        }
        catch (Exception ex) { File.AppendAllText(Path.Combine(Dir, "err.txt"), $"shotrtb {name}: {ex}\n"); }
    }

    static void CapturePasswordDialog(Window owner, string name)
    {
        var timer = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(550) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var dlg = System.Linq.Enumerable.FirstOrDefault(
                System.Linq.Enumerable.OfType<PasswordDialog>(Application.Current.Windows));
            if (dlg != null) { dlg.UpdateLayout(); Shot(dlg, name); dlg.Close(); }
        };
        timer.Start();
        var d = new PasswordDialog { Owner = owner };
        d.ShowDialog();
    }

    static void CaptureAlert(Window owner, string name)
    {
        var timer = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(550) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var dlg = System.Linq.Enumerable.FirstOrDefault(
                System.Linq.Enumerable.OfType<ConfirmDialog>(Application.Current.Windows));
            if (dlg != null) { dlg.UpdateLayout(); Shot(dlg, name); dlg.Close(); }
        };
        timer.Start();
        ConfirmDialog.Alert(owner, "Can't Merge PDF",
            "Add at least 2 PDF files to merge — a single file has nothing to combine.");
    }

    static UIElement? FindByTag(DependencyObject root, string tag)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Border b && b.Tag is string s && s == tag) return b;
            var found = FindByTag(child, tag);
            if (found != null) return found;
        }
        return null;
    }

    static void Shot(Window win, string name)
    {
        try
        {
            int w = (int)win.ActualWidth, h = (int)win.ActualHeight;
            var tl = win.PointToScreen(new System.Windows.Point(0, 0));
            using var bmp = new SD.Bitmap(Math.Max(1, w), Math.Max(1, h));
            using (var g = SD.Graphics.FromImage(bmp))
                g.CopyFromScreen((int)tl.X, (int)tl.Y, 0, 0, new SD.Size(w, h));
            bmp.Save(Path.Combine(Dir, name), SD.Imaging.ImageFormat.Png);
        }
        catch (Exception ex) { File.AppendAllText(Path.Combine(Dir, "err.txt"), $"shot {name}: {ex}\n"); }
    }
}
