using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Llamashot.Core;
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

        // Workspace-only mode: skip the flaky FileToolsWindow/alert/password captures.
        if (Environment.GetEnvironmentVariable("LLAMASHOT_WS_ONLY") == "1")
        {
            // warm-up: pump the message loop with a throwaway window so the first real capture paints
            await CaptureWorkspace("_warmup.png", "pdf_to_images", false);
            await Task.Delay(300);

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
            ThemeManager.Apply(ThemeManager.Light, persist: false);
            await Task.Delay(200);
            await CaptureMergeList("i2p_light.png", "images_to_pdf", imgRows);
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
