using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
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
        var win = new FileToolsWindow { Width = 1300, Height = 860,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
        win.Show(); win.Activate();
        await Task.Delay(900);
        win.UpdateLayout();
        await Task.Delay(400);
        Shot(win, "home_pdf_tools.png");
        win.Hide();
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
