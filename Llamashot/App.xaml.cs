using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Llamashot.Core;
using Llamashot.Views;
using WinForms = System.Windows.Forms;

namespace Llamashot;

public partial class App : Application
{
    private Mutex? _mutex;
    private WinForms.NotifyIcon? _trayIcon;
    private HotkeyManager? _hotkeyManager;
    private Window? _hiddenWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance check
        _mutex = new Mutex(true, "LlamashotAppMutex", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("Llamashot is already running.", "Llamashot", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Load settings
        AppSettings.Load();
        HistoryManager.Load();

        // Create hidden window for hotkey handling
        _hiddenWindow = new Window
        {
            Width = 0, Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Visibility = Visibility.Hidden
        };
        _hiddenWindow.Show();
        _hiddenWindow.Hide();

        // Register hotkeys
        _hotkeyManager = new HotkeyManager(_hiddenWindow);
        RegisterHotkeys();

        // System tray
        SetupTrayIcon();
    }

    private void RegisterHotkeys()
    {
        if (_hotkeyManager == null) return;

        // PrintScreen → region capture
        _hotkeyManager.Register(NativeMethods.MOD_NONE, NativeMethods.VK_SNAPSHOT, StartRegionCapture);

        // Shift+PrintScreen → fullscreen save
        _hotkeyManager.Register(NativeMethods.MOD_SHIFT, NativeMethods.VK_SNAPSHOT, FullscreenSave);

        // Ctrl+PrintScreen → fullscreen to clipboard
        _hotkeyManager.Register(NativeMethods.MOD_CONTROL, NativeMethods.VK_SNAPSHOT, FullscreenClipboard);
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = CreateDefaultIcon(),
            Text = "Llamashot - Press PrintScreen to capture",
            Visible = true
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Take Screenshot", null, (s, e) => StartRegionCapture());
        menu.Items.Add("Delayed Capture...", null, (s, e) => ShowDelayedCapture());
        menu.Items.Add("Fullscreen to Clipboard", null, (s, e) => FullscreenClipboard());
        menu.Items.Add("-");
        menu.Items.Add("History", null, (s, e) => ShowHistory());
        menu.Items.Add("Settings", null, (s, e) => ShowSettings());
        menu.Items.Add("About", null, (s, e) => ShowAbout());
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (s, e) => ExitApp());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.MouseClick += (s, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                StartRegionCapture();
        };
        _trayIcon.DoubleClick += (s, e) => ShowSettings();
    }

    private System.Drawing.Icon CreateDefaultIcon()
    {
        var bmp = new System.Drawing.Bitmap(32, 32);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var bgBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(33, 150, 243));
        g.FillEllipse(bgBrush, 2, 2, 28, 28);

        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2f);
        g.DrawRectangle(pen, 8, 10, 16, 12);
        g.DrawLine(pen, 16, 7, 16, 10);
        g.DrawLine(pen, 14, 8, 18, 8);
        g.DrawEllipse(pen, 12, 13, 8, 6);

        var handle = bmp.GetHicon();
        return System.Drawing.Icon.FromHandle(handle);
    }

    private void StartRegionCapture()
    {
        Dispatcher.Invoke(() =>
        {
            var overlay = new OverlayWindow();
            overlay.StartCapture();
        });
    }

    private void FullscreenSave()
    {
        Dispatcher.Invoke(() =>
        {
            var screenshot = ScreenCapture.CaptureFullScreen();
            var settings = AppSettings.Instance;

            var dir = settings.LastSaveDirectory;
            Directory.CreateDirectory(dir);

            var ext = settings.DefaultSaveFormat.ToLower();
            var path = Path.Combine(dir, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}");

            BitmapEncoder encoder = ext switch
            {
                "jpg" or "jpeg" => new JpegBitmapEncoder { QualityLevel = settings.JpegQuality },
                "bmp" => new BmpBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };

            encoder.Frames.Add(BitmapFrame.Create(screenshot));
            using var fs = File.Create(path);
            encoder.Save(fs);

            HistoryManager.AddRecord(screenshot, path);

            if (settings.ShowNotifications)
                _trayIcon?.ShowBalloonTip(2000, "Llamashot", $"Saved to {path}", WinForms.ToolTipIcon.Info);
        });
    }

    private void FullscreenClipboard()
    {
        Dispatcher.Invoke(() =>
        {
            var screenshot = ScreenCapture.CaptureFullScreen();
            Clipboard.SetImage(screenshot);

            if (AppSettings.Instance.ShowNotifications)
                _trayIcon?.ShowBalloonTip(2000, "Llamashot", "Screenshot copied to clipboard", WinForms.ToolTipIcon.Info);
        });
    }

    private void ShowDelayedCapture()
    {
        Dispatcher.Invoke(() =>
        {
            var window = new DelayedCaptureWindow();
            window.Show();
        });
    }

    private void ShowHistory()
    {
        Dispatcher.Invoke(() =>
        {
            var window = new HistoryWindow();
            window.Show();
        });
    }

    private void ShowSettings()
    {
        Dispatcher.Invoke(() =>
        {
            var window = new SettingsWindow();
            if (window.ShowDialog() == true)
            {
                // Re-register hotkeys in case they changed
                _hotkeyManager?.Dispose();
                _hotkeyManager = new HotkeyManager(_hiddenWindow!);
                RegisterHotkeys();
            }
        });
    }

    private void ShowAbout()
    {
        Dispatcher.Invoke(() =>
        {
            var window = new AboutWindow();
            window.ShowDialog();
        });
    }

    private void ExitApp()
    {
        _hotkeyManager?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyManager?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
