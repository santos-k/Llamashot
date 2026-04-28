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

        // Global exception handler - log to file, never show modal dialogs
        // (modal MessageBox behind a fullscreen overlay = system deadlock)
        DispatcherUnhandledException += (s, ex) =>
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Llamashot");
                Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, "crash.log");
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.Exception.Message}\n{ex.Exception.StackTrace}\n\n");
            }
            catch { }
            ex.Handled = true;
        };

        // Single instance check
        _mutex = new Mutex(true, "LlamashotAppMutex", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("Llamashot is already running.", "Llamashot", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
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

            // Check for updates in background
            if (AppSettings.Instance.AutoCheckUpdates)
                _ = CheckForUpdatesAsync(silent: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Llamashot failed to start:\n\n{ex.Message}",
                "Llamashot Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void RegisterHotkeys()
    {
        if (_hotkeyManager == null) return;
        var s = AppSettings.Instance;

        RegisterFromString(s.CaptureHotkey, StartRegionCapture);
        RegisterFromString(s.FullscreenSaveHotkey, FullscreenSave);
        RegisterFromString(s.FullscreenClipboardHotkey, FullscreenClipboard);
    }

    private void RegisterFromString(string shortcut, Action callback)
    {
        var (mods, vk) = ShortcutHelper.ParseGlobalHotkey(shortcut);
        if (vk != 0)
            _hotkeyManager!.Register(mods, vk, callback);
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = CreateDefaultIcon(),
            Text = $"Llamashot - Press {AppSettings.Instance.CaptureHotkey} to capture",
            Visible = true
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Take Screenshot", null, (s, e) => StartRegionCapture());
        menu.Items.Add("Record Screen", null, (s, e) => StartRecordCapture());
        menu.Items.Add("Delayed Capture...", null, (s, e) => ShowDelayedCapture());
        menu.Items.Add("Fullscreen to Clipboard", null, (s, e) => FullscreenClipboard());
        menu.Items.Add("-");
        menu.Items.Add("History", null, (s, e) => ShowHistory());
        menu.Items.Add("Settings", null, (s, e) => ShowSettings());
        menu.Items.Add("Check for Updates", null, (s, e) => _ = CheckForUpdatesAsync(silent: false));
        menu.Items.Add("About", null, (s, e) => ShowAbout());
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (s, e) => ExitApp());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.MouseClick += (s, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                StartRegionCapture();
        };
    }

    private System.Drawing.Icon CreateDefaultIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/icon32.png", UriKind.Absolute);
            var sri = GetResourceStream(uri);
            if (sri != null)
            {
                using var bmp = new System.Drawing.Bitmap(sri.Stream);
                var handle = bmp.GetHicon();
                return System.Drawing.Icon.FromHandle(handle);
            }
        }
        catch { }

        // Fallback: simple generated icon
        var fallback = new System.Drawing.Bitmap(32, 32);
        using var g = System.Drawing.Graphics.FromImage(fallback);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var bgBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(33, 150, 243));
        g.FillEllipse(bgBrush, 2, 2, 28, 28);
        var h = fallback.GetHicon();
        return System.Drawing.Icon.FromHandle(h);
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        try
        {
            var result = await UpdateChecker.CheckForUpdateAsync();
            if (result != null)
            {
                Dispatcher.Invoke(() =>
                {
                    _trayIcon?.ShowBalloonTip(5000, "Llamashot Update Available",
                        $"Version {result.Version} is available. Right-click tray icon > Check for Updates to download.",
                        WinForms.ToolTipIcon.Info);

                    _trayIcon!.BalloonTipClicked += (s, e) => _ = DownloadAndInstallUpdateAsync(result);
                });
            }
            else if (!silent)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("You're running the latest version of Llamashot.",
                        "Llamashot", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Failed to check for updates:\n{ex.Message}",
                        "Llamashot", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
        }
    }

    private async Task DownloadAndInstallUpdateAsync(UpdateChecker.UpdateInfo update)
    {
        try
        {
            _trayIcon?.ShowBalloonTip(3000, "Llamashot", "Downloading update...", WinForms.ToolTipIcon.Info);

            var installerPath = await UpdateChecker.DownloadUpdateAsync(update);
            if (installerPath != null)
            {
                var result = MessageBox.Show(
                    $"Llamashot {update.Version} has been downloaded.\n\nInstall now? The application will close.",
                    "Llamashot Update", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = installerPath,
                        UseShellExecute = true
                    });
                    ExitApp();
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to download update:\n{ex.Message}",
                "Llamashot", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StartRegionCapture()
    {
        Dispatcher.Invoke(() =>
        {
            var overlay = new OverlayWindow();
            overlay.StartCapture();
        });
    }

    private void StartRecordCapture()
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
            HistoryManager.AddClipRecord(screenshot);

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

                // Update tray tooltip with current hotkey
                if (_trayIcon != null)
                    _trayIcon.Text = $"Llamashot - Press {AppSettings.Instance.CaptureHotkey} to capture";
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
