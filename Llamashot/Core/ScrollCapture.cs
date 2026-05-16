using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Llamashot.Core;

/// <summary>
/// Orchestrates scrolling screenshot capture in auto or manual mode.
/// </summary>
public class ScrollCapture
{
    private const int ScrollDelayMs = 350;
    private const int ScrollNotches = 2;
    private const int MaxIterations = 100;
    private const int MinFramesBeforeStop = 3; // Don't stop before capturing at least this many frames
    private const int ManualDebounceMs = 200;

    private readonly IntPtr _targetHwnd;
    private readonly List<BitmapSource> _frames = new();
    private bool _capturing;
    private DateTime _lastManualCapture = DateTime.MinValue;

    private IntPtr _mouseHook;
    private NativeMethods.LowLevelMouseProc? _mouseProc;

    public event Action<int>? FrameCaptured;
    public event Action? CaptureComplete;
    public int FrameCount => _frames.Count;
    public bool IsCapturing => _capturing;

    public ScrollCapture(IntPtr targetHwnd)
    {
        _targetHwnd = targetHwnd;
    }

    public async Task<BitmapSource?> AutoCaptureAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        _frames.Clear();
        _capturing = true;

        try
        {
            // Bring target window to foreground and let it settle
            NativeMethods.SetForegroundWindow(_targetHwnd);
            await Task.Delay(300);

            // Bring target window to foreground (no click — just focus for keyboard input)
            NativeMethods.SetForegroundWindow(_targetHwnd);
            await Task.Delay(300);

            // Scroll to top
            await ScrollToTop(ct);
            await Task.Delay(ScrollDelayMs);

            // Capture loop
            BitmapSource? prevFrame = null;
            int identicalCount = 0;

            for (int i = 0; i < MaxIterations; i++)
            {
                if (ct.IsCancellationRequested) break; // Stitch what we have instead of throwing

                var frame = CaptureWindow();
                if (frame == null) break;

                // Only check for identical frames after collecting minimum frames
                if (prevFrame != null && _frames.Count >= MinFramesBeforeStop
                    && ImageStitcher.AreFramesIdentical(prevFrame, frame))
                {
                    identicalCount++;
                    if (identicalCount >= 2) break;
                }
                else
                {
                    identicalCount = 0;
                }

                _frames.Add(frame);
                prevFrame = frame;
                FrameCaptured?.Invoke(_frames.Count);
                progress?.Report(_frames.Count);

                // Scroll down using Page Down for reliable page-sized scroll
                SendPageDown();
                try { await Task.Delay(ScrollDelayMs, ct); }
                catch (OperationCanceledException) { break; }
            }

            return ImageStitcher.Stitch(_frames);
        }
        finally
        {
            _capturing = false;
            CaptureComplete?.Invoke();
        }
    }

    public void StartManualCapture()
    {
        _frames.Clear();
        _capturing = true;

        NativeMethods.SetForegroundWindow(_targetHwnd);
        var initial = CaptureWindow();
        if (initial != null)
        {
            _frames.Add(initial);
            FrameCaptured?.Invoke(_frames.Count);
        }

        _mouseProc = ManualMouseHookCallback;
        var module = NativeMethods.GetModuleHandle(null);
        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
    }

    public BitmapSource? FinishManualCapture()
    {
        _capturing = false;
        UnhookMouse();
        CaptureComplete?.Invoke();
        return ImageStitcher.Stitch(_frames);
    }

    public void CaptureManualFrame()
    {
        if (!_capturing) return;
        var frame = CaptureWindow();
        if (frame != null)
        {
            _frames.Add(frame);
            FrameCaptured?.Invoke(_frames.Count);
        }
    }

    public void Cancel()
    {
        _capturing = false;
        UnhookMouse();
        _frames.Clear();
    }

    private void UnhookMouse()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        _mouseProc = null;
    }

    private IntPtr ManualMouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == NativeMethods.WM_MOUSEWHEEL && _capturing)
        {
            var now = DateTime.Now;
            if ((now - _lastManualCapture).TotalMilliseconds >= ManualDebounceMs)
            {
                _lastManualCapture = now;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(ManualDebounceMs);
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (!_capturing) return;
                        var frame = CaptureWindow();
                        if (frame != null)
                        {
                            _frames.Add(frame);
                            FrameCaptured?.Invoke(_frames.Count);
                        }
                    });
                });
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private BitmapSource? CaptureWindow()
    {
        NativeMethods.GetWindowRect(_targetHwnd, out var rect);

        // Clip bottom to work area to exclude taskbar
        // Use screen metrics directly (pixel values, no DPI conversion needed)
        int screenBottom = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN)
                         + NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        // Get taskbar height: difference between full screen and work area
        // WorkArea is in DIPs but for primary monitor at 100% it matches pixels
        // Use a simpler approach: capture the window rect but cap at screen height minus ~48px for taskbar
        int taskbarHeight = 48; // Safe estimate for most taskbar sizes
        int maxBottom = screenBottom - taskbarHeight;

        int left = rect.Left;
        int top = rect.Top;
        int right = rect.Right;
        int bottom = Math.Min(rect.Bottom, maxBottom);
        int w = right - left;
        int h = bottom - top;
        if (w <= 0 || h <= 0) return null;

        return ScreenCapture.CaptureRegion(left, top, w, h);
    }

    private async Task ScrollToTop(CancellationToken ct)
    {
        var inputs = new NativeMethods.INPUT[4];
        inputs[0].type = NativeMethods.INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = 0x11; // VK_CONTROL
        inputs[1].type = NativeMethods.INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = 0x24; // VK_HOME
        inputs[2].type = NativeMethods.INPUT_KEYBOARD;
        inputs[2].u.ki.wVk = 0x24;
        inputs[2].u.ki.dwFlags = 0x0002; // KEYEVENTF_KEYUP
        inputs[3].type = NativeMethods.INPUT_KEYBOARD;
        inputs[3].u.ki.wVk = 0x11;
        inputs[3].u.ki.dwFlags = 0x0002;

        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        await Task.Delay(500, ct);
    }

    private static void ClickAt(int x, int y)
    {
        NativeMethods.SetCursorPos(x, y);
        var inputs = new NativeMethods.INPUT[2];
        inputs[0].type = NativeMethods.INPUT_MOUSE;
        inputs[0].u.mi.dwFlags = 0x0002; // MOUSEEVENTF_LEFTDOWN
        inputs[1].type = NativeMethods.INPUT_MOUSE;
        inputs[1].u.mi.dwFlags = 0x0004; // MOUSEEVENTF_LEFTUP
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public static void SendPageDownPublic() => SendPageDown();

    private static void SendPageDown()
    {
        var inputs = new NativeMethods.INPUT[2];
        inputs[0].type = NativeMethods.INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = 0x22; // VK_NEXT (Page Down)
        inputs[1].type = NativeMethods.INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = 0x22;
        inputs[1].u.ki.dwFlags = 0x0002; // KEYEVENTF_KEYUP
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void SendMouseWheel(int notches)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.INPUTUNION
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    mouseData = notches * NativeMethods.WHEEL_DELTA,
                    dwFlags = NativeMethods.MOUSEEVENTF_WHEEL
                }
            }
        };
        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }
}
