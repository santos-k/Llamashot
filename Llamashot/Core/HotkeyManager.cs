using System.Windows;
using System.Windows.Interop;

namespace Llamashot.Core;

public class HotkeyManager : IDisposable
{
    private readonly Window _window;
    private readonly Dictionary<int, Action> _hotkeys = new();
    private HwndSource? _source;
    private int _nextId = 1;
    private bool _disposed;

    public HotkeyManager(Window window)
    {
        _window = window;
        _window.SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(_window);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);
    }

    public int Register(uint modifiers, uint vk, Action callback)
    {
        var helper = new WindowInteropHelper(_window);
        int id = _nextId++;

        if (helper.Handle != IntPtr.Zero)
        {
            if (!NativeMethods.RegisterHotKey(helper.Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, vk))
                return -1;
        }

        _hotkeys[id] = callback;
        return id;
    }

    public void Unregister(int id)
    {
        var helper = new WindowInteropHelper(_window);
        if (helper.Handle != IntPtr.Zero)
            NativeMethods.UnregisterHotKey(helper.Handle, id);
        _hotkeys.Remove(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_hotkeys.TryGetValue(id, out var callback))
            {
                callback.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var helper = new WindowInteropHelper(_window);
        foreach (var id in _hotkeys.Keys)
        {
            if (helper.Handle != IntPtr.Zero)
                NativeMethods.UnregisterHotKey(helper.Handle, id);
        }
        _hotkeys.Clear();
        _source?.RemoveHook(WndProc);
    }
}
