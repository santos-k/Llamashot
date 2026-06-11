using System.Windows;
using System.Windows.Input;

namespace Llamashot.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

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
