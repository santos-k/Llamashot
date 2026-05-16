using System.Windows.Input;

namespace Llamashot.Core;

public static class ShortcutHelper
{
    /// <summary>
    /// Check if the current key event matches a shortcut string like "Ctrl+S", "P", "Ctrl+Shift+Z"
    /// </summary>
    public static bool Matches(KeyEventArgs e, string shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) return false;

        var parts = shortcut.Split('+');
        bool needCtrl = false, needShift = false, needAlt = false;
        string keyPart = "";

        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) needCtrl = true;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) needShift = true;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) needAlt = true;
            else keyPart = p;
        }

        // Check modifiers match exactly
        bool ctrlDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shiftDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool altDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        if (ctrlDown != needCtrl || shiftDown != needShift || altDown != needAlt)
            return false;

        // Get the actual key pressed
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Map key name to Key enum
        var expectedKey = ParseKey(keyPart);
        return expectedKey != Key.None && key == expectedKey;
    }

    public static Key ParseKey(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName)) return Key.None;

        return keyName.ToUpperInvariant() switch
        {
            "PRINTSCREEN" => Key.Snapshot,
            "PRTSC" => Key.Snapshot,
            "PLUS" => Key.OemPlus,
            "MINUS" => Key.OemMinus,
            "TILDE" => Key.OemTilde,
            "BACKSPACE" => Key.Back,
            "ENTER" => Key.Return,
            "ESC" or "ESCAPE" => Key.Escape,
            "SPACE" => Key.Space,
            "TAB" => Key.Tab,
            "DELETE" or "DEL" => Key.Delete,
            "INSERT" or "INS" => Key.Insert,
            "HOME" => Key.Home,
            "END" => Key.End,
            "PAGEUP" => Key.PageUp,
            "PAGEDOWN" => Key.PageDown,
            "UP" => Key.Up,
            "DOWN" => Key.Down,
            "LEFT" => Key.Left,
            "RIGHT" => Key.Right,
            "D0" => Key.D0, "D1" => Key.D1, "D2" => Key.D2, "D3" => Key.D3, "D4" => Key.D4,
            "D5" => Key.D5, "D6" => Key.D6, "D7" => Key.D7, "D8" => Key.D8, "D9" => Key.D9,
            _ => Enum.TryParse<Key>(keyName, true, out var k) ? k : Key.None
        };
    }

    /// <summary>
    /// Parse a shortcut string to Win32 modifiers + virtual key for RegisterHotKey
    /// </summary>
    public static (uint modifiers, uint vk) ParseGlobalHotkey(string shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) return (0, 0);

        var parts = shortcut.Split('+');
        uint mods = NativeMethods.MOD_NOREPEAT;
        uint vk = 0;

        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
                mods |= NativeMethods.MOD_CONTROL;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                mods |= NativeMethods.MOD_SHIFT;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                mods |= NativeMethods.MOD_ALT;
            else
                vk = MapToVirtualKey(p);
        }

        return (mods, vk);
    }

    /// <summary>
    /// Check if a virtual key code (from low-level keyboard hook) matches a shortcut string.
    /// Only matches shortcuts with no modifiers (single key) since the hook doesn't track modifiers.
    /// </summary>
    public static bool MatchesVk(uint vkCode, string shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) return false;

        var parts = shortcut.Split('+');
        // If shortcut has modifiers, we can't match from low-level hook vkCode alone
        if (parts.Length > 1) return false;

        return MapToVirtualKey(parts[0].Trim()) == vkCode;
    }

    private static uint MapToVirtualKey(string keyName)
    {
        return keyName.ToUpperInvariant() switch
        {
            "PRINTSCREEN" or "PRTSC" => NativeMethods.VK_SNAPSHOT,
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
            "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
            "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            "SPACE" => 0x20, "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09, "ESCAPE" or "ESC" => 0x1B,
            "BACKSPACE" or "BACK" => 0x08,
            "DELETE" or "DEL" => 0x2E, "INSERT" or "INS" => 0x2D,
            "HOME" => 0x24, "END" => 0x23,
            "PAGEUP" => 0x21, "PAGEDOWN" => 0x22,
            "D0" => 0x30, "D1" => 0x31, "D2" => 0x32, "D3" => 0x33, "D4" => 0x34,
            "D5" => 0x35, "D6" => 0x36, "D7" => 0x37, "D8" => 0x38, "D9" => 0x39,
            _ => keyName.Length == 1 ? (uint)char.ToUpper(keyName[0]) : 0
        };
    }
}
