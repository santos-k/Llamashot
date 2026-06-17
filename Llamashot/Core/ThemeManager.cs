using System.Windows;
using Microsoft.Win32;

namespace Llamashot.Core;

public static class ThemeManager
{
    public const string Light = "Light";
    public const string Dark = "Dark";
    public const string System = "System";

    /// <summary>The currently applied concrete theme ("Light" or "Dark").</summary>
    public static string Current { get; private set; } = Light;

    /// <summary>Raised after the active theme dictionary has been swapped.</summary>
    public static event Action? ThemeChanged;

    private static ResourceDictionary? _active;

    /// <summary>
    /// Applies the stored preference. "System" (or empty) follows the Windows app theme.
    /// </summary>
    public static void Initialize(string preference)
    {
        string resolved = (string.IsNullOrWhiteSpace(preference) || preference == System)
            ? DetectSystemTheme()
            : preference;
        Apply(resolved, persist: false, notify: false);
    }

    public static void Toggle()
    {
        Apply(Current == Light ? Dark : Light);
    }

    public static void Apply(string theme, bool persist = true, bool notify = true)
    {
        theme = theme == Dark ? Dark : Light;
        var uri = new Uri($"pack://application:,,,/Llamashot;component/Themes/{theme}Theme.xaml", UriKind.Absolute);
        var dict = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_active != null) merged.Remove(_active);
        // Add at the END so the active theme has the highest precedence over any other merged dictionary.
        merged.Add(dict);
        _active = dict;
        Current = theme;

        if (persist)
        {
            AppSettings.Instance.FileToolsTheme = theme;
            AppSettings.Save();
        }
        if (notify) ThemeChanged?.Invoke();
    }

    /// <summary>Reads the Windows "apps use light theme" setting (1 = light, 0 = dark).</summary>
    public static string DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v)
                return v == 0 ? Dark : Light;
        }
        catch { /* fall through to default */ }
        return Light;
    }
}
