using USBBlockManager.Models;
using USBBlockManager.Services.Interop;
using System.ServiceProcess;

namespace USBBlockManager.Services;

/// <summary>Результат применения белого списка.</summary>
public record WhitelistResult(int Disabled, int Enabled, int Errors);

/// <summary>
/// Блокировка и разблокировка USB-накопителей через реестр.
/// Управляет значением USBSTOR\Start:
///   3 = обычная работа USBSTOR (разблокировано)
///   4 = драйвер отключён (заблокировано)
/// </summary>
public class UsbBlocker
{
    public const string UsbStorKey = @"Services\USBSTOR";
    public const string StartValueName = "Start";

    public const string BlockMarkerKey = @"Services\USBBlockManager";
    public const string BlockMarkerValue = "BlockedByApp";

    public const string OriginalStartValue = "OriginalStart";

    private const int StartEnabled = 3;
    private const int StartDisabled = 4;

    private readonly IRegistryService _registry;

    public UsbBlocker(IRegistryService registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>Активна ли блокировка, установленная нашим приложением.</summary>
    public bool IsBlocked()
    {
        var marker = _registry.ReadDword(BlockMarkerKey, BlockMarkerValue);
        if (marker != 1)
            return false;

        var start = _registry.ReadDword(UsbStorKey, StartValueName);
        return start == StartDisabled;
    }

    /// <summary>
    /// Заблокировать USB. Возвращает true, если блокировка установлена впервые.
    /// </summary>
    public bool Block()
    {
        if (IsBlocked())
            return false;

        // Сохраняем оригинальное значение Start, если ещё не сохраняли
        if (_registry.ReadDword(BlockMarkerKey, OriginalStartValue) is null)
        {
            var currentStart = _registry.ReadDword(UsbStorKey, StartValueName) ?? StartEnabled;
            _registry.WriteDword(BlockMarkerKey, OriginalStartValue, currentStart);
        }

        // 1. Ставим Start = 4 (USBSTOR отключён)
        _registry.WriteDword(UsbStorKey, StartValueName, StartDisabled);

        // 2. Просим Windows перечитать параметры USBSTOR
        RestartUsbStorService();

        // 3. Принудительно отключаем все USBSTOR-устройства (чтобы «дохлые»
        //    не остались в системе до перезагрузки)
        try
        {
            UsbDeviceControl.DisableAllUsbStorDevices();
        }
        catch
        {
            // Не критично — сам Start=4 должен сработать
        }

        _registry.WriteDword(BlockMarkerKey, BlockMarkerValue, 1);
        return true;
    }

    /// <summary>
    /// Разблокировать USB. Возвращает true, если блокировка была снята.
    /// </summary>
    public bool Unblock()
    {
        var wasBlocked = IsBlocked();

        if (!wasBlocked)
        {
            // Всё равно чистим маркеры и пробуем включить всё обратно —
            // на случай, если остались «залипшие» отключённые устройства.
            CleanupMarkers();
            try
            {
                RestartUsbStorService();
                UsbDeviceControl.EnableAllUsbStorDevices();
            }
            catch { }
            return false;
        }

        // Восстанавливаем оригинальное значение Start
        var originalStart = _registry.ReadDword(BlockMarkerKey, OriginalStartValue)
                            ?? StartEnabled;

        if (originalStart == StartDisabled)
            originalStart = StartEnabled;

        _registry.WriteDword(UsbStorKey, StartValueName, originalStart);

        // 1. Перезапускаем USBSTOR, чтобы Windows подхватила новый Start
        RestartUsbStorService();

        // 2. Включаем все USBSTOR-устройства, которые могли быть отключены
        try
        {
            UsbDeviceControl.EnableAllUsbStorDevices();
        }
        catch
        {
            // Не критично — большинство устройств включится само
        }

        CleanupMarkers();
        return true;
    }

    /// <summary>
    /// Перезапустить сервис USBSTOR — заставляет Windows перечитать Start.
    /// </summary>
    private static void RestartUsbStorService()
    {
        try
        {
            using var sc = new ServiceController("USBSTOR");

            if (sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped,
                    TimeSpan.FromSeconds(10));
            }

            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running,
                TimeSpan.FromSeconds(10));
        }
        catch
        {
            // Если сервис не запущен или его нет — пропускаем.
            // Установка Start=3 в реестре всё равно применится при следующем
            // переподключении устройства или перезагрузке.
        }
    }

    /// <summary>Вернуть список подключённых устройств, которых нет в белом списке.</summary>
    public List<UsbDevice> GetUnlistedDevices(
        IEnumerable<UsbDevice> connectedDevices,
        WhitelistManager whitelist)
    {
        if (connectedDevices is null) throw new ArgumentNullException(nameof(connectedDevices));
        if (whitelist is null) throw new ArgumentNullException(nameof(whitelist));

        return connectedDevices
            .Where(d => !whitelist.Contains(d))
            .ToList();
    }

    /// <summary>
    /// Применить белый список: включить whitelisted-устройства,
    /// отключить всё остальное через SetupAPI (CM_Disable_DevNode).
    /// </summary>
    public WhitelistResult ApplyWhitelist(WhitelistManager whitelist)
    {
        if (whitelist is null) throw new ArgumentNullException(nameof(whitelist));

        var monitor = new UsbMonitor();
        var devices = monitor.GetConnectedDevices();
        var entries = whitelist.LoadAll();
        var entryKeys = new HashSet<string>(
            entries.Select(e => e.IdentityKey),
            StringComparer.OrdinalIgnoreCase);

        int disabled = 0;
        int enabled = 0;
        int errors = 0;

        foreach (var device in devices)
        {
            try
            {
                var instanceId = UsbDeviceControl.FindDeviceInstanceId(device.IdentityKey);
                if (instanceId is null)
                    continue;

                var isWhitelisted = entryKeys.Contains(device.IdentityKey);

                if (isWhitelisted)
                {
                    if (UsbDeviceControl.Enable(instanceId))
                        enabled++;
                }
                else
                {
                    if (UsbDeviceControl.Disable(instanceId))
                        disabled++;
                }
            }
            catch
            {
                errors++;
            }
        }

        return new WhitelistResult(disabled, enabled, errors);
    }

    // ----- приватные -----

    private void CleanupMarkers()
    {
        _registry.DeleteValue(BlockMarkerKey, BlockMarkerValue);
        _registry.DeleteValue(BlockMarkerKey, OriginalStartValue);
    }
}