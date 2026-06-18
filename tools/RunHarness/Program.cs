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
