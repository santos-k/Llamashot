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

    private bool _silentStart;

    // Global keyboard hook (double-Esc kill + recording shortcuts)
    private IntPtr _keyboardHook;
    private NativeMethods.LowLevelKeyboardProc? _keyboardProc;
    private DateTime _lastEscTime = DateTime.MinValue;
    internal static Views.RecordingOverlay? ActiveRecordingOverlay { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _silentStart = e.Args.Contains("--silent");

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
            if (!_silentStart)
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

            // Global double-Esc kill switch
            InstallEscapeHook();

            // Check for updates in background (result cached in UpdateChecker.LatestUpdate)
            _ = CheckForUpdatesAsync();
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

    private void InstallEscapeHook()
    {
        _keyboardProc = EscapeHookCallback;
        var module = NativeMethods.GetModuleHandle(null);
        _keyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);
    }

    private IntPtr EscapeHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == NativeMethods.WM_KEYDOWN)
        {
            uint vkCode = (uint)System.Runtime.InteropServices.Marshal.ReadInt32(lParam);

            // Double-Esc: dismiss all overlays
            if (vkCode == NativeMethods.VK_ESCAPE)
            {
                var now = DateTime.Now;
                if ((now - _lastEscTime).TotalMilliseconds < 500)
                {
                    _lastEscTime = DateTime.MinValue;
                    Dispatcher.BeginInvoke(DismissAllOverlays);
                }
                else
                {
                    _lastEscTime = now;
                }
            }

            // Recording shortcuts (only when a Llamashot window is in foreground)
            if (ActiveRecordingOverlay != null && IsOurWindowInForeground())
            {
                char key = vkCode switch
                {
                    0x50 => 'P', // P - Pen
                    0x41 => 'A', // A - Arrow
                    0x52 => 'R', // R - Rectangle
                    0x54 => 'T', // T - Text
                    0x43 => 'C', // C - Clear
                    0x4D => 'M', // M - Mic toggle
                    0x53 => 'S', // S - System audio toggle
                    0x20 => ' ', // Space - Pause/Resume
                    0x51 => 'Q', // Q - Stop
                    _ => '\0'
                };
                if (key != '\0')
                    Dispatcher.BeginInvoke(() => ActiveRecordingOverlay?.HandleShortcut(key));
            }
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static bool IsOurWindowInForeground()
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(fg, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    private void DismissAllOverlays()
    {
        // Close all overlay windows (screenshot, recording, annotation, borders)
        var windows = Windows.Cast<Window>().ToList();
        foreach (var w in windows)
        {
            if (w is Views.OverlayWindow or Views.RecordingOverlay
                or Views.RecordingAnnotation or Views.RecordingBorder)
            {
                try { w.Close(); } catch { }
            }
        }
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

    private static async Task CheckForUpdatesAsync()
    {
        try { await UpdateChecker.CheckForUpdateAsync(); }
        catch { /* silent background check */ }
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
            overlay.StartCapture(OverlayWindow.CaptureMode.Video);
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
        if (_keyboardHook != IntPtr.Zero)
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
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
