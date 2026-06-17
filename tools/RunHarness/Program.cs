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

        if (Environment.GetEnvironmentVariable("LLAMASHOT_MERGEPREVIEW") == "1")
        {
            await CaptureMergePreview();
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
            var pngs = Directory.GetFiles(Dir, "*.png").Take(3).ToArray();
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
        t.GetMethod("BuildMergeList", BF)!.Invoke(ws, null);
        t.GetMethod("BuildInfo", BF)!.Invoke(ws, null);
        t.GetMethod("UpdateSelection", BF)!.Invoke(ws, null);
        t.GetMethod("ShowPreviewState", BF)!.Invoke(ws, null);

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

        t.GetMethod("BuildMergeList", BF)!.Invoke(ws, null);
        t.GetMethod("BuildInfo", BF)!.Invoke(ws, null);
        t.GetMethod("UpdateSelection", BF)!.Invoke(ws, null);
        t.GetMethod("ShowPreviewState", BF)!.Invoke(ws, null);

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
