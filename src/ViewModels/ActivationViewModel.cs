using System.IO;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using USBBlockManager.Licensing;
using USBBlockManager.Services;

namespace USBBlockManager.ViewModels;

/// <summary>
/// ViewModel окна активации.
/// Показывает статус лицензии, Hardware ID, кнопки действий.
/// </summary>
public class ActivationViewModel : ObservableObject
{
    private readonly string _licenseFilePath;
    private readonly string _trialFilePath;

    private LicenseStatus _status;
    private string _hardwareIdText = string.Empty;
    private string _statusMessage = string.Empty;
    private string _hardwareIdDetails = string.Empty;
    private bool _showTrialButton;

    public ActivationViewModel(LicenseStatus status)
    {
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _licenseFilePath = AppPaths.LicenseFile;
        _trialFilePath = AppPaths.TrialFile;

        LoadHardwareId();
        UpdateFromStatus();

        LoadFromFileCommand = new RelayCommand(LoadFromFile);
        PasteTextCommand = new RelayCommand(PasteText);
        CopyHwidCommand = new RelayCommand(CopyHwid);
        ContinueTrialCommand = new RelayCommand(ContinueTrial, () => _showTrialButton);
        ExitCommand = new RelayCommand(ExitApplication);
    }

    // ----- Свойства для binding -----

    /// <summary>
    /// Hardware ID — текстовое представление для отображения.
    /// Название с суффиксом Text, чтобы не конфликтовать
    /// с классом USBBlockManager.Licensing.HardwareId.
    /// </summary>
    public string HardwareIdText
    {
        get => _hardwareIdText;
        private set => SetProperty(ref _hardwareIdText, value);
    }

    public string HardwareIdDetails
    {
        get => _hardwareIdDetails;
        private set => SetProperty(ref _hardwareIdDetails, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool ShowTrialButton
    {
        get => _showTrialButton;
        private set => SetProperty(ref _showTrialButton, value);
    }

    public string WindowTitle => _status.Kind switch
    {
        LicenseStatusKind.Licensed => "USB Block Manager",
        LicenseStatusKind.Trial => "USB Block Manager — Пробный период",
        LicenseStatusKind.TrialExpired => "USB Block Manager — Требуется лицензия",
        LicenseStatusKind.InvalidLicense => "USB Block Manager — Лицензия недействительна",
        _ => "USB Block Manager — Активация",
    };

    public string StatusHeader => _status.Kind switch
    {
        LicenseStatusKind.Trial => $"Пробный период: осталось {_status.Trial?.DaysRemaining} дн.",
        LicenseStatusKind.TrialExpired => "Пробный период истёк",
        LicenseStatusKind.InvalidLicense => "Лицензия недействительна",
        _ => "Лицензия не найдена",
    };

    public string StatusDescription => _status.Kind switch
    {
        LicenseStatusKind.Trial =>
            "Вы можете пользоваться программой в пробном режиме. " +
            "Для полной версии зарегистрируйтесь и получите лицензию.",

        LicenseStatusKind.TrialExpired =>
            "Пробный период закончился. Для продолжения работы необходима лицензия.",

        LicenseStatusKind.InvalidLicense =>
            $"Лицензия недействительна: {_status.Message}\n\n" +
            "Получите новую лицензию у поставщика.",

        _ =>
            "Лицензия не найдена. Чтобы активировать программу, " +
            "отправьте поставщику Hardware ID вашего компьютера и получите файл лицензии.",
    };

    // ----- Команды -----

    public RelayCommand LoadFromFileCommand { get; }
    public RelayCommand PasteTextCommand { get; }
    public RelayCommand CopyHwidCommand { get; }
    public RelayCommand ContinueTrialCommand { get; }
    public RelayCommand ExitCommand { get; }

    /// <summary>true — лицензия принята, можно продолжать работу.</summary>
    public event EventHandler? ActivationSucceeded;

    /// <summary>true — пользователь выбрал выход или активация провалилась.</summary>
    public event EventHandler? ExitRequested;

    // ----- Реализация -----

    private void LoadHardwareId()
    {
        try
        {
            var details = USBBlockManager.Licensing.HardwareId.GetDetailed();
            HardwareIdText = details.ComputedId;
            HardwareIdDetails = details.ToString();
        }
        catch (Exception ex)
        {
            HardwareIdText = "(не удалось получить)";
            HardwareIdDetails = $"Ошибка: {ex.Message}";
        }
    }

    private void UpdateFromStatus()
    {
        StatusMessage = _status.Message;
        ShowTrialButton = _status.Kind == LicenseStatusKind.Trial;

        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(StatusHeader));
        OnPropertyChanged(nameof(StatusDescription));
    }

    private void LoadFromFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите файл лицензии",
            Filter = "Файлы лицензии (*.json)|*.json|Все файлы (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            ApplyLicenseJson(json);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось прочитать файл:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void PasteText()
    {
        var window = new USBBlockManager.Views.PasteLicenseWindow();
        if (Application.Current?.MainWindow is not null)
            window.Owner = Application.Current.MainWindow;

        if (window.ShowDialog() == true && !string.IsNullOrWhiteSpace(window.LicenseJson))
        {
            ApplyLicenseJson(window.LicenseJson);
        }
    }

    private void ApplyLicenseJson(string json)
    {
        var result = LicenseService.ValidateFromJson(json);

        if (!result.IsValid)
        {
            MessageBox.Show(
                $"Лицензия не принята:\n\n{result.Message}",
                "Недействительная лицензия",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Сохраняем лицензию на диск
        try
        {
            var dir = Path.GetDirectoryName(_licenseFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(_licenseFilePath, json);

            StatusMessage = $"Лицензия успешно активирована: {result.License!.User}";
            MessageBox.Show(
                $"Лицензия активирована.\n\n" +
                $"Пользователь: {result.License.User}\n" +
                (result.License.ExpiresAt.HasValue
                    ? $"Действует до: {result.License.ExpiresAt:yyyy-MM-dd}\n"
                    : "Действует: бессрочно\n") +
                (string.IsNullOrEmpty(result.License.HardwareId)
                    ? "Привязка: без привязки"
                    : $"Hardware ID: {result.License.HardwareId}"),
                "Успех",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            ActivationSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось сохранить лицензию:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CopyHwid()
    {
        try
        {
            System.Windows.Clipboard.SetText(HardwareIdText);
            MessageBox.Show(
                "Hardware ID скопирован в буфер обмена.",
                "USB Block Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch
        {
            // Игнорируем — Clipboard иногда не работает
        }
    }

    private void ContinueTrial()
    {
        ActivationSucceeded?.Invoke(this, EventArgs.Empty);
    }

    private void ExitApplication()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}