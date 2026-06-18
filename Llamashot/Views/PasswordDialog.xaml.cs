using System.Windows;
using System.Windows.Input;
using Llamashot.Core;

namespace Llamashot.Views;

public partial class PasswordDialog : Window
{
    public PasswordDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PwBox.Focus();
    }

    public string Password => PwBox.Password;

    /// <summary>Returned by <see cref="UnlockAsync"/> when the user chooses "Recover" instead of entering a password.</summary>
    public const string RecoverSentinel = "\0__RECOVER__\0";

    private bool _recover;

    /// <summary>
    /// Ensures a PDF can be opened. Returns:
    ///  - "" when the PDF is not protected (no password needed),
    ///  - the working password when the user unlocks it,
    ///  - <see cref="RecoverSentinel"/> when the user clicks "Recover" (only when <paramref name="allowRecover"/>),
    ///  - null when the user cancels.
    /// Re-prompts on an incorrect password.
    /// </summary>
    public static async Task<string?> UnlockAsync(Window owner, string pdfPath, bool allowRecover = false)
    {
        if (!await FileToolsService.IsPdfEncryptedAsync(pdfPath))
            return "";

        string fileName = System.IO.Path.GetFileName(pdfPath);
        string? error = null;
        while (true)
        {
            var dlg = new PasswordDialog();
            if (owner.IsLoaded) dlg.Owner = owner;
            else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            dlg.TxtMessage.Text = $"“{fileName}” is password-protected. Enter its password to open it.";
            if (allowRecover) dlg.BtnRecover.Visibility = Visibility.Visible;
            if (error != null)
            {
                dlg.TxtError.Text = error;
                dlg.TxtError.Visibility = Visibility.Visible;
            }

            if (dlg.ShowDialog() != true)
                return null; // cancelled

            if (dlg._recover) return RecoverSentinel;

            string pw = dlg.Password;
            if (pw.Length > 0 && await FileToolsService.TryUnlockPdfAsync(pdfPath, pw))
                return pw;

            error = "Incorrect password. Please try again.";
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Recover_Click(object sender, RoutedEventArgs e) { _recover = true; DialogResult = true; }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void PwBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) DialogResult = true;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
