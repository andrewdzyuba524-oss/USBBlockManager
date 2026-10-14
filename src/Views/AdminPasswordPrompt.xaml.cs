using System.Windows;
using System.Windows.Controls;
using USBBlockManager.Security;

namespace USBBlockManager.Views;

/// <summary>
/// Диалог запроса пароля администратора перед выходом.
/// </summary>
public partial class AdminPasswordPrompt : Window
{
    private readonly AuthService _authService;
    private string _password = string.Empty;

    public AdminPasswordPrompt(AuthService authService)
    {
        InitializeComponent();
        _authService = authService;
        PasswordBox.Focus();
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _password = pb.Password;
    }

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_password))
        {
            ErrorText.Text = "Введите пароль.";
            return;
        }

        if (!_authService.VerifyPassword(_password))
        {
            ErrorText.Text = "Неверный пароль.";
            _password = string.Empty;
            PasswordBox.Clear();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}