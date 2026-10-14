using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using USBBlockManager.Ipc;
using USBBlockManager.Licensing;
using USBBlockManager.Models;
using USBBlockManager.Security;
using USBBlockManager.Services;
using USBBlockManager.Views;

namespace USBBlockManager.ViewModels;

/// <summary>
/// ViewModel главного окна. Работает через службу (IPC).
/// Раз в секунду опрашивает службу на наличие устройств,
/// ожидающих авторизации администратора.
/// </summary>
public class MainViewModel : ObservableObject, IDisposable
{
    private readonly ServiceClient _client;
    private readonly AuthService _authService;
    private readonly DispatcherTimer _pendingTimer;

    /// <summary>
    /// Ключи pending-устройств, для которых диалог уже показан.
    /// Защита от повторного показа диалога, пока служба не удалит pending.
    /// Сбрасывается после обработки диалога и при исчезновении pending.
    /// </summary>
    private readonly HashSet<string> _handledPendingKeys = new(StringComparer.OrdinalIgnoreCase);

    private UsbDevice? _selectedDevice;
    private WhitelistEntry? _selectedWhitelistEntry;
    private string _statusMessage = "Готово";
    private bool _isBlocked;
    private bool _isAddMode;
    private bool _serviceAvailable;
    private bool _isShowingPendingDialog;

    public MainViewModel(ServiceClient client, AuthService authService)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));

        Devices = new ObservableCollection<UsbDevice>();
        Whitelist = new ObservableCollection<WhitelistEntry>();

        RefreshCommand = new RelayCommand(Refresh);
        BlockCommand = new RelayCommand(Block, () => !_isBlocked && _serviceAvailable);
        UnblockCommand = new RelayCommand(Unblock, () => _isBlocked && _serviceAvailable);
        AddToWhitelistCommand = new RelayCommand(AddSelectedToWhitelist, () => _selectedDevice is not null && _serviceAvailable);
        RemoveFromWhitelistCommand = new RelayCommand(RemoveSelectedFromWhitelist, () => _selectedWhitelistEntry is not null && _serviceAvailable);
        ApplyWhitelistCommand = new RelayCommand(ApplyWhitelist, () => _serviceAvailable);
        ToggleAddModeCommand = new RelayCommand(ToggleAddMode, () => _serviceAvailable);

        CheckServiceAndRefresh();

        _pendingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _pendingTimer.Tick += (_, _) => CheckPendingApproval();
        _pendingTimer.Start();
    }

    public ObservableCollection<UsbDevice> Devices { get; }
    public ObservableCollection<WhitelistEntry> Whitelist { get; }

    public UsbDevice? SelectedDevice
    {
        get => _selectedDevice;
        set => SetProperty(ref _selectedDevice, value);
    }

    public WhitelistEntry? SelectedWhitelistEntry
    {
        get => _selectedWhitelistEntry;
        set => SetProperty(ref _selectedWhitelistEntry, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBlocked
    {
        get => _isBlocked;
        private set
        {
            if (SetProperty(ref _isBlocked, value))
                OnPropertyChanged(nameof(BlockStatusText));
        }
    }

    public string BlockStatusText => IsBlocked
        ? "USB заблокированы"
        : "USB разблокированы";

    public bool IsAddMode
    {
        get => _isAddMode;
        set
        {
            if (SetProperty(ref _isAddMode, value))
                OnPropertyChanged(nameof(AddModeText));
        }
    }

    public string AddModeText => IsAddMode
        ? "Режим добавления: ВКЛ"
        : "Режим добавления: ВЫКЛ";

    public bool ServiceAvailable
    {
        get => _serviceAvailable;
        private set => SetProperty(ref _serviceAvailable, value);
    }

    /// <summary>
    /// Текущий статус лицензии. Устанавливается App'ом при старте.
    /// </summary>
    public LicenseStatus? LicenseStatus { get; set; }

    /// <summary>
    /// Текст для отображения в статус-баре — показывает статус лицензии.
    /// </summary>
    public string LicenseStatusText => LicenseStatus?.Kind switch
    {
        LicenseStatusKind.Licensed => "Лицензия активна",
        LicenseStatusKind.Trial => $"Пробный режим: {LicenseStatus.Trial?.DaysRemaining} дн.",
        LicenseStatusKind.TrialExpired => "Пробный период истёк",
        LicenseStatusKind.InvalidLicense => "Лицензия недействительна",
        _ => "",
    };

    /// <summary>
    /// Показывать ли статус лицензии в UI.
    /// </summary>
    public bool ShowLicenseStatus =>
        LicenseStatus is not null &&
        LicenseStatus.Kind != LicenseStatusKind.Licensed;

    public RelayCommand RefreshCommand { get; }
    public RelayCommand BlockCommand { get; }
    public RelayCommand UnblockCommand { get; }
    public RelayCommand AddToWhitelistCommand { get; }
    public RelayCommand RemoveFromWhitelistCommand { get; }
    public RelayCommand ApplyWhitelistCommand { get; }
    public RelayCommand ToggleAddModeCommand { get; }

    private void CheckServiceAndRefresh()
    {
        ServiceAvailable = _client.IsServiceAvailable();

        if (!ServiceAvailable)
        {
            StatusMessage = "Служба USBBlockService не запущена. Запустите её: sc start USBBlockService";
            return;
        }

        Refresh();
    }

    public void Refresh()
    {
        if (!ServiceAvailable)
            return;

        try
        {
            var devicesResp = _client.ListDevices();
            Devices.Clear();
            if (devicesResp.Success && !string.IsNullOrEmpty(devicesResp.Data))
            {
                var list = JsonSerializer.Deserialize<List<UsbDevice>>(devicesResp.Data);
                if (list is not null)
                    foreach (var d in list)
                        Devices.Add(d);
            }

            var wlResp = _client.ListWhitelist();
            Whitelist.Clear();
            if (wlResp.Success && !string.IsNullOrEmpty(wlResp.Data))
            {
                var list = JsonSerializer.Deserialize<List<WhitelistEntry>>(wlResp.Data);
                if (list is not null)
                    foreach (var e in list)
                        Whitelist.Add(e);
            }

            var statusResp = _client.GetStatus();
            if (statusResp.Success && !string.IsNullOrEmpty(statusResp.Data))
            {
                using var doc = JsonDocument.Parse(statusResp.Data);
                if (doc.RootElement.TryGetProperty("isBlocked", out var blocked))
                    IsBlocked = blocked.GetBoolean();
            }

            // Синхронизируем режим добавления со службой
            try
            {
                var addModeResp = _client.GetAddMode();
                if (addModeResp.Success && !string.IsNullOrEmpty(addModeResp.Data))
                {
                    IsAddMode = string.Equals(addModeResp.Data.Trim(), "true",
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }

            StatusMessage = $"Устройств: {Devices.Count}, в белом списке: {Whitelist.Count}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка связи со службой: {ex.Message}";
            ServiceAvailable = false;
        }
    }

    private void Block()
    {
        var resp = _client.BlockAll();
        StatusMessage = resp.Message ?? (resp.Success ? "Готово" : "Ошибка");
        Refresh();
    }

    private void Unblock()
    {
        var resp = _client.UnblockAll();
        StatusMessage = resp.Message ?? (resp.Success ? "Готово" : "Ошибка");
        Refresh();
    }

    private void ApplyWhitelist()
    {
        var resp = _client.ApplyWhitelist();
        StatusMessage = resp.Message ?? (resp.Success ? "Готово" : "Ошибка");
        Refresh();
    }

    private void AddSelectedToWhitelist()
    {
        if (_selectedDevice is null) return;

        var entry = WhitelistEntry.FromDevice(_selectedDevice, "admin");
        var resp = _client.AddToWhitelist(entry);
        StatusMessage = resp.Message ?? (resp.Success ? "Готово" : "Ошибка");
        Refresh();
    }

    private void RemoveSelectedFromWhitelist()
    {
        if (_selectedWhitelistEntry is null) return;

        var key = _selectedWhitelistEntry.IdentityKey;
        var resp = _client.RemoveFromWhitelist(key);
        StatusMessage = resp.Message ?? (resp.Success ? "Готово" : "Ошибка");
        Refresh();
    }

    /// <summary>
    /// Переключить режим добавления через службу.
    /// </summary>
    private void ToggleAddMode()
    {
        var newValue = !IsAddMode;
        var resp = _client.SetAddMode(newValue);

        if (resp.Success)
        {
            IsAddMode = newValue;
            StatusMessage = resp.Message ?? (newValue
                ? "Режим добавления: ВКЛ. Новые USB не блокируются."
                : "Режим добавления: ВЫКЛ. Новые USB блокируются.");
        }
        else
        {
            StatusMessage = $"Ошибка: {resp.Message}";
        }
    }

    /// <summary>
    /// Раз в секунду проверяем, есть ли устройства, ожидающие авторизации.
    /// Защита от повторного показа диалога — через <see cref="_handledPendingKeys"/>.
    /// Ключ сбрасывается после обработки, чтобы повторная вставка того же
    /// устройства снова вызвала диалог.
    /// </summary>
    private void CheckPendingApproval()
    {
        if (!ServiceAvailable) return;
        if (_isShowingPendingDialog) return;

        try
        {
            var response = _client.GetPending();

            // Если pending нет — сбрасываем список обработанных
            if (!response.Success || string.IsNullOrEmpty(response.Data) || response.Data == "null")
            {
                _handledPendingKeys.Clear();
                return;
            }

            var pending = JsonSerializer.Deserialize<PendingApproval>(response.Data);
            if (pending is null)
            {
                _handledPendingKeys.Clear();
                return;
            }

            // Защита от повторного показа того же устройства в рамках одной обработки
            if (_handledPendingKeys.Contains(pending.IdentityKey))
                return;

            _handledPendingKeys.Add(pending.IdentityKey);

            _isShowingPendingDialog = true;
            try
            {
                ShowPendingDialog(pending);
            }
            finally
            {
                _isShowingPendingDialog = false;

                // Сбрасываем ключ: если пользователь отказал, а служба
                // почему-то не удалила pending, диалог покажется снова
                // через секунду, а не застрянет навсегда.
                _handledPendingKeys.Remove(pending.IdentityKey);
            }
        }
        catch
        {
            // Игнорируем ошибки опроса — следующая попытка через секунду
        }
    }

    private void ShowPendingDialog(PendingApproval pending)
    {
        var device = new UsbDevice
        {
            Vid = pending.Vid,
            Pid = pending.Pid,
            Serial = pending.Serial,
            Label = pending.Label,
        };

        var dialog = new UsbApprovalWindow(device);
        var owner = Application.Current?.MainWindow;
        if (owner is not null)
            dialog.Owner = owner;

        var result = dialog.ShowDialog();

        if (result == true)
        {
            var response = _client.ApprovePending(
                pending.IdentityKey,
                dialog.Password,
                dialog.AddToWhitelist);

            StatusMessage = response.Success
                ? $"Устройство разрешено: {pending.Label}"
                : $"Ошибка: {response.Message}";

            Refresh();
        }
        else
        {
            var response = _client.RejectPending(pending.IdentityKey);
            StatusMessage = response.Success
                ? $"Устройство заблокировано: {pending.Label}"
                : $"Ошибка: {response.Message}";
        }
    }

    public void Dispose()
    {
        try
        {
            _pendingTimer.Stop();
        }
        catch { }
    }
}