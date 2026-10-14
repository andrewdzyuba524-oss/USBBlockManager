using System.Windows.Input;
using USBBlockManager.Security;

namespace USBBlockManager.ViewModels;

/// <summary>
/// ViewModel диалога смены пароля администратора.
/// </summary>
public class ChangePasswordViewModel : ObservableObject
{
    private readonly AuthService _authService;

    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private string? _error;

    public ChangePasswordViewModel(AuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));

        ChangeCommand = new RelayCommand(Change, CanChange);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(this, false));
    }

    public string CurrentPassword
    {
        get => _currentPassword;
        set
        {
            if (SetProperty(ref _currentPassword, value))
            {
                Error = null;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string NewPassword
    {
        get => _newPassword;
        set
        {
            if (SetProperty(ref _newPassword, value))
            {
                Error = null;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set
        {
            if (SetProperty(ref _confirmPassword, value))
            {
                Error = null;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public RelayCommand ChangeCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>true — пароль сменён, false — отмена.</summary>
    public event EventHandler<bool>? RequestClose;

    private bool CanChange()
    {
        return !string.IsNullOrEmpty(CurrentPassword)
            && !string.IsNullOrEmpty(NewPassword)
            && !string.IsNullOrEmpty(ConfirmPassword)
            && NewPassword.Length >= 6
            && NewPassword == ConfirmPassword;
    }

    private void Change()
    {
        if (NewPassword != ConfirmPassword)
        {
            Error = "Новый пароль и подтверждение не совпадают.";
            return;
        }

        if (NewPassword.Length < 6)
        {
            Error = "Новый пароль должен содержать минимум 6 символов.";
            return;
        }

        if (!_authService.ChangePassword(CurrentPassword, NewPassword))
        {
            Error = "Неверный текущий пароль.";
            return;
        }

        RequestClose?.Invoke(this, true);
    }
}