using System.Windows.Input;
using USBBlockManager.Security;

namespace USBBlockManager.ViewModels;

/// <summary>
/// ViewModel окна входа.
/// </summary>
public class LoginViewModel : ObservableObject
{
    private readonly AuthService _authService;
    private string _password = string.Empty;
    private string _passwordConfirm = string.Empty;
    private string? _error;
    private bool _isFirstRun;

    public LoginViewModel(AuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _isFirstRun = !_authService.IsPasswordSet;

        LoginCommand = new RelayCommand(Login, CanLogin);
        SetupCommand = new RelayCommand(Setup, CanSetup);
    }

    public bool IsFirstRun
    {
        get => _isFirstRun;
        private set
        {
            if (SetProperty(ref _isFirstRun, value))
            {
                OnPropertyChanged(nameof(IsLoginVisible));
                OnPropertyChanged(nameof(IsSetupVisible));
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public bool IsLoginVisible => !IsFirstRun;
    public bool IsSetupVisible => IsFirstRun;

    public string Title => IsFirstRun
        ? "USB Block Manager - первичная настройка"
        : "USB Block Manager - вход";

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                Error = null;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string PasswordConfirm
    {
        get => _passwordConfirm;
        set
        {
            if (SetProperty(ref _passwordConfirm, value))
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

    public RelayCommand LoginCommand { get; }
    public RelayCommand SetupCommand { get; }

    public event EventHandler? LoginSucceeded;

    private bool CanLogin() => !IsFirstRun && !string.IsNullOrEmpty(Password);

    private bool CanSetup() => IsFirstRun
                               && !string.IsNullOrEmpty(Password)
                               && Password.Length >= 6
                               && Password == PasswordConfirm;

    private void Login()
    {
        if (_authService.VerifyPassword(Password))
        {
            Password = string.Empty;
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Error = "Неверный пароль.";
            Password = string.Empty;
        }
    }

    private void Setup()
    {
        try
        {
            _authService.SetInitialPassword(Password);
            IsFirstRun = false;
            Password = string.Empty;
            PasswordConfirm = string.Empty;
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Error = $"Не удалось установить пароль: {ex.Message}";
        }
    }
}