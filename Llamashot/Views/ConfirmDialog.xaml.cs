using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Llamashot.Views;

public partial class ConfirmDialog : Window
{
    public enum AlertKind { Warning, Error, Info, Success }

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>Two-button confirm/cancel dialog. Returns true if confirmed.</summary>
    public static bool Show(Window owner, string title, string message,
        string confirmText = "Leave Anyway", string cancelText = "Stay")
    {
        var dlg = new ConfirmDialog { Owner = owner };
        dlg.TxtTitle.Text = title;
        dlg.TxtMessage.Text = message;
        dlg.BtnConfirm.Content = confirmText;
        dlg.BtnCancel.Content = cancelText;
        return dlg.ShowDialog() == true;
    }

    /// <summary>Single-button themed alert (replacement for MessageBox).</summary>
    public static void Alert(Window? owner, string title, string message,
        AlertKind kind = AlertKind.Warning, string okText = "OK")
    {
        var dlg = new ConfirmDialog();
        if (owner != null && owner.IsLoaded) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dlg.TxtTitle.Text = title;
        dlg.TxtMessage.Text = message;
        dlg.BtnCancel.Visibility = Visibility.Collapsed;
        dlg.BtnConfirm.Content = okText;
        // Neutral accent OK button (the destructive red is only for confirm/cancel actions).
        dlg.BtnConfirm.SetResourceReference(BackgroundProperty, "AccentBrush");
        dlg.BtnConfirm.SetResourceReference(ForegroundProperty, "AccentTextBrush");
        dlg.ApplyKind(kind);
        dlg.ShowDialog();
    }

    private void ApplyKind(AlertKind kind)
    {
        (string glyph, string hex) = kind switch
        {
            AlertKind.Error => ("✕", "#FF5C5C"),
            AlertKind.Info => ("ℹ", "#6E8BFF"),
            AlertKind.Success => ("✓", "#43C59E"),
            _ => ("⚠", "#FFA726"),
        };
        var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        IconGlyph.Text = glyph;
        IconGlyph.Foreground = new SolidColorBrush(color);
        IconCircle.Background = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B));
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
