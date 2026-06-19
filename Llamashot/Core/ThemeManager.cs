using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Llamashot.Core;

public static class ThemeManager
{
    public const string Light = "Light";
    public const string Dark = "Dark";
    public const string System = "System";

    /// <summary>The currently applied concrete theme ("Light" or "Dark").</summary>
    public static string Current { get; private set; } = Light;

    /// <summary>The user's stored preference ("Light" | "Dark" | "System").</summary>
    public static string Preference { get; private set; } = System;

    /// <summary>Raised after the active theme dictionary and/or accent has been swapped.</summary>
    public static event Action? ThemeChanged;

    private static ResourceDictionary? _active;
    private static bool _hookedSystemEvents;

    /// <summary>
    /// Applies the stored preference. "System" (or empty) follows the Windows app theme,
    /// and keeps following it live while the preference stays System.
    /// </summary>
    public static void Initialize(string preference)
    {
        Preference = string.IsNullOrWhiteSpace(preference) ? System : preference;
        string resolved = Preference == System ? DetectSystemTheme() : Preference;
        Apply(resolved, persist: false, notify: false);
        ApplyAccent(notify: false);

        if (!_hookedSystemEvents)
        {
            _hookedSystemEvents = true;
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        }
    }

    private static void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || Preference != System) return;
        var resolved = DetectSystemTheme();
        if (resolved == Current) return;
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Apply(resolved, persist: false, notify: false);
            ApplyAccent(notify: false);
            ThemeChanged?.Invoke();
        });
    }

    /// <summary>Sets the theme preference (Light | Dark | System) and applies it.</summary>
    public static void SetMode(string preference)
    {
        Preference = preference;
        AppSettings.Instance.FileToolsTheme = preference;
        AppSettings.Save();
        string resolved = preference == System ? DetectSystemTheme() : preference;
        Apply(resolved, persist: false, notify: false);
        ApplyAccent(notify: false);
        ThemeChanged?.Invoke();
    }

    /// <summary>Legacy 2-state flip (Light↔Dark). Used by the toolbar toggle buttons.</summary>
    public static void Toggle() => SetMode(Current == Light ? Dark : Light);

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
            AppSettings.Instance.FileToolsTheme = Preference;
            AppSettings.Save();
        }
        // Re-apply the accent overrides — swapping the dictionary reset them to the teal defaults.
        ApplyAccent(notify: false);
        if (notify) ThemeChanged?.Invoke();
    }

    // ==================================================================
    //  Accent / gradient customization
    // ==================================================================

    /// <summary>Applies the accent gradient stored in AppSettings.</summary>
    public static void ApplyAccent(bool notify = true)
    {
        var s = AppSettings.Instance;
        ApplyAccent(s.AccentStart, s.AccentEnd, s.GradientAngle, s.WindowTint, notify);
    }

    /// <summary>
    /// Overrides the accent + surface-gradient brushes at runtime so the whole suite
    /// re-colours instantly. Re-run after every theme swap.
    /// </summary>
    public static void ApplyAccent(string startHex, string endHex, int angle, bool tint, bool notify = true)
    {
        var res = Application.Current.Resources;
        Color start = Parse(startHex, Color.FromRgb(0x2D, 0xD4, 0xBF));
        Color end = Parse(endHex, Color.FromRgb(0x34, 0xD3, 0x99));

        res["AccentColor"] = start;
        res["Accent2Color"] = end;
        res["AccentBrush"] = Freeze(new SolidColorBrush(start));
        res["AccentTextBrush"] = Freeze(new SolidColorBrush(
            Luma(start) > 150 ? Color.FromRgb(0x06, 0x20, 0x1C) : Colors.White));
        res["AccentGradientBrush"] = MakeGradient(start, end, angle);

        Color win = BaseColor(res, "WindowBrush", Color.FromRgb(0x14, 0x15, 0x1A));
        Color hdr = BaseColor(res, "HeaderBrush", Color.FromRgb(0x1B, 0x1C, 0x24));
        Color srf = BaseColor(res, "SurfaceBrush", Color.FromRgb(0x1E, 0x20, 0x29));

        if (tint)
        {
            res["WindowGradientBrush"] = TintGradient(win, start, 0.08, 0.72);
            res["HeaderGradientBrush"] = TintGradient(hdr, start, 0.15, 0.80);
            res["SurfaceGradientBrush"] = TintGradient(srf, start, 0.07, 1.0);
        }
        else
        {
            // Drop the override so the dictionary's neutral gradient applies.
            res.Remove("WindowGradientBrush");
            res.Remove("HeaderGradientBrush");
            res.Remove("SurfaceGradientBrush");
        }

        if (notify) ThemeChanged?.Invoke();
    }

    /// <summary>Restores the teal factory defaults and persists them.</summary>
    public static void ResetAccent()
    {
        var s = AppSettings.Instance;
        s.AccentStart = AppSettings.DefaultAccentStart;
        s.AccentEnd = AppSettings.DefaultAccentEnd;
        s.GradientAngle = AppSettings.DefaultGradientAngle;
        s.WindowTint = true;
        AppSettings.Save();
        ApplyAccent();
    }

    /// <summary>Persists the supplied accent and applies it.</summary>
    public static void SaveAccent(string startHex, string endHex, int angle, bool tint)
    {
        var s = AppSettings.Instance;
        s.AccentStart = startHex;
        s.AccentEnd = endHex;
        s.GradientAngle = angle;
        s.WindowTint = tint;
        AppSettings.Save();
        ApplyAccent();
    }

    // ---- helpers --------------------------------------------------------

    private static LinearGradientBrush MakeGradient(Color a, Color b, int angle)
    {
        double rad = angle * Math.PI / 180.0;
        double dx = Math.Cos(rad), dy = Math.Sin(rad);
        var sp = new Point(0.5 - dx * 0.5, 0.5 - dy * 0.5);
        var ep = new Point(0.5 + dx * 0.5, 0.5 + dy * 0.5);
        var g = new LinearGradientBrush(a, b, sp, ep);
        g.Freeze();
        return g;
    }

    private static LinearGradientBrush TintGradient(Color baseColor, Color accent, double frac, double endOffset)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.7, 1) };
        g.GradientStops.Add(new GradientStop(Blend(baseColor, accent, frac), 0));
        g.GradientStops.Add(new GradientStop(baseColor, endOffset));
        g.Freeze();
        return g;
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R * (1 - t) + b.R * t),
        (byte)(a.G * (1 - t) + b.G * t),
        (byte)(a.B * (1 - t) + b.B * t));

    private static double Luma(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    private static Color BaseColor(ResourceDictionary res, string key, Color fallback)
        => res[key] is SolidColorBrush b ? b.Color : fallback;

    private static Color Parse(string hex, Color fallback)
    {
        try { return (Color)global::System.Windows.Media.ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }

    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

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
