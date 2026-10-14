using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using USBBlockManager.Ipc;
using USBBlockManager.Models;
using USBBlockManager.Security;
using USBBlockManager.Services;
using USBBlockManager.Services.Interop;

namespace USBBlockService;

/// <summary>
/// Основная служба USB Block Manager.
/// Слушает WMI-события USB, блокирует неразрешённые устройства,
/// включает whitelisted-устройства. Общается с GUI через Named Pipe.
/// </summary>
public class BlockService : BackgroundService
{
    private readonly ILogger<BlockService> _logger;
    private readonly string _logFile;

    private UsbMonitor _monitor = null!;
    private UsbBlocker _blocker = null!;
    private WhitelistManager _whitelist = null!;
    private UsbEventWatcher _watcher = null!;
    private AuthService _authService = null!;
    private PipeServer? _pipeServer;

    /// <summary>
    /// Режим добавления. Когда включён — новые USB-устройства
    /// не блокируются, чтобы админ мог добавить их в whitelist.
    /// </summary>
    private volatile bool _addMode;

    // Ожидающие авторизации устройства: identityKey -> PendingApproval
    private readonly object _pendingLock = new();
    private readonly Dictionary<string, PendingApproval> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    // Счётчик неудачных попыток ввода пароля: identityKey -> attempts
    private readonly Dictionary<string, int> _approveAttempts =
        new(StringComparer.OrdinalIgnoreCase);

    private const int MaxApproveAttempts = 3;

    public BlockService(ILogger<BlockService> logger)
    {
        _logger = logger;

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "USBBlockManager",
            "logs");

        Directory.CreateDirectory(dataDir);
        _logFile = Path.Combine(dataDir, "service.log");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log($"=== USB Block Manager Service started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

        try
        {
            InitializeServices();

            _watcher.DeviceInserted += OnUsbInserted;
            _watcher.Start();

            Log("USB event watcher started.");

            ApplyWhitelistOnStartup();

            _pipeServer = new PipeServer(Log, HandleIpcRequest);
            _pipeServer.Start();
            Log("Named pipe server started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Нормальное завершение
        }
        catch (Exception ex)
        {
            Log($"FATAL: {ex}");
        }
        finally
        {
            try
            {
                _pipeServer?.Stop();
                Log("Named pipe server stopped.");
            }
            catch { }

            try
            {
                if (_watcher is not null)
                {
                    _watcher.DeviceInserted -= OnUsbInserted;
                    _watcher.Dispose();
                }
            }
            catch { }

            Log($"=== USB Block Manager Service stopped at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        }
    }

    private void InitializeServices()
    {
        _monitor = new UsbMonitor();
        var registry = new WindowsRegistryService();
        _blocker = new UsbBlocker(registry);
        _whitelist = new WhitelistManager(AppPaths.WhitelistFile);
        _authService = new AuthService(AppPaths.AdminHashFile);
        _watcher = new UsbEventWatcher();

        Log($"Initialized. Whitelist entries: {_whitelist.Count}");
    }

    private void ApplyWhitelistOnStartup()
    {
        try
        {
            var result = _blocker.ApplyWhitelist(_whitelist);
            Log($"Whitelist applied on startup: disabled={result.Disabled}, enabled={result.Enabled}, errors={result.Errors}");
        }
        catch (Exception ex)
        {
            Log($"Error applying whitelist on startup: {ex.Message}");
        }
    }

    /// <summary>
    /// Обработка вставки USB-устройства.
    /// Если включён режим добавления — не блокируем.
    /// Если устройство в whitelist — включаем.
    /// Иначе — отключаем и регистрируем как pending.
    /// </summary>
    private void OnUsbInserted(object? sender, UsbEventArgs e)
    {
        try
        {
            var pnpId = e.PnpDeviceId;
            if (string.IsNullOrWhiteSpace(pnpId))
                return;

            var (vid, pid, serial) = UsbMonitor.ParsePnpDeviceId(pnpId);
            if (string.IsNullOrEmpty(serial))
            {
                Log($"USB device inserted but couldn't parse: {pnpId}");
                return;
            }

            var identityKey = $"{vid}:{pid}:{serial}";
            Log($"USB inserted: {identityKey}");

            // ---- Режим добавления: не блокируем новые устройства ----
            if (_addMode)
            {
                Log($"Add mode active, skipping block for: {identityKey}");
                return;
            }

            var instanceId = UsbDeviceControl.FindDeviceInstanceId(pnpId);

            var whitelistEntry = _whitelist.LoadAll()
                .FirstOrDefault(x => string.Equals(x.IdentityKey, identityKey, StringComparison.OrdinalIgnoreCase));

            if (whitelistEntry is not null)
            {
                if (instanceId is not null)
                {
                    var enabled = UsbDeviceControl.Enable(instanceId);
                    Log($"Allowed whitelisted device: {identityKey}, enable={enabled}");
                }
                else
                {
                    Log($"Allowed whitelisted device: {identityKey}, instanceId not found");
                }
                return;
            }

            if (instanceId is null)
            {
                Log($"Could not find instance ID for {identityKey}");
                return;
            }

            // Отключаем устройство
            var disabled = UsbDeviceControl.Disable(instanceId);
            Log($"Disabled unlisted device: {identityKey}, result={disabled}");

            if (!disabled)
            {
                Log($"WARNING: CM_Disable_DevNode failed for {identityKey}");
            }

            // Размонтируем том — с несколькими попытками, потому что буква диска
            // может назначаться с задержкой. Без размонтирования данные
            // могут быть доступны из кэша, даже если устройство отключено.
            DismountVolumeWithRetries(pnpId, identityKey);

            // Регистрируем как ожидающее авторизации
            var pending = new PendingApproval
            {
                IdentityKey = identityKey,
                PnpId = pnpId,
                Vid = vid,
                Pid = pid,
                Serial = serial,
                Label = BuildLabel(vid, pid),
                DetectedAt = DateTime.UtcNow,
            };

            lock (_pendingLock)
            {
                _pending[identityKey] = pending;
                _approveAttempts.Remove(identityKey); // сбрасываем счётчик при новой вставке
            }

            Log($"Registered pending approval: {identityKey}");
        }
        catch (Exception ex)
        {
            Log($"Error in OnUsbInserted: {ex.Message}");
        }
    }

    /// <summary>
    /// Многократная попытка размонтировать том.
    /// Буква диска может назначаться через 100–500 мс после вставки.
    /// </summary>
    private void DismountVolumeWithRetries(string pnpId, string identityKey)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            string? letter = null;
            try
            {
                letter = _monitor.GetDriveLetterByPnpDeviceId(pnpId);
            }
            catch (Exception ex)
            {
                Log($"GetDriveLetterByPnpDeviceId error (attempt {attempt + 1}): {ex.Message}");
            }

            if (string.IsNullOrEmpty(letter))
            {
                Thread.Sleep(200);
                continue;
            }

            var dismounted = false;
            try
            {
                dismounted = VolumeControl.DismountVolume(letter);
            }
            catch (Exception ex)
            {
                Log($"DismountVolume error (attempt {attempt + 1}): {ex.Message}");
            }

            Log($"Dismount attempt {attempt + 1} for {letter} ({identityKey}): result={dismounted}");

            if (dismounted)
                return;

            Thread.Sleep(200);
        }
    }

    private static string BuildLabel(string vid, string pid)
    {
        var v = string.IsNullOrEmpty(vid) ? "?" : vid;
        var p = string.IsNullOrEmpty(pid) ? "?" : pid;
        return $"{v} {p}";
    }

    // ----- IPC -----

    private IpcResponse HandleIpcRequest(IpcRequest request)
    {
        Log($"IPC command: {request.Command}");

        try
        {
            switch (request.Command)
            {
                case IpcCommands.Ping:
                    return Ok("pong");

                case IpcCommands.Status:
                    return Ok(data: JsonSerializer.Serialize(BuildStatus()));

                case IpcCommands.ListDevices:
                    return Ok(data: JsonSerializer.Serialize(_monitor.GetConnectedDevices()));

                case IpcCommands.ListWhitelist:
                    return Ok(data: JsonSerializer.Serialize(_whitelist.LoadAll()));

                case IpcCommands.ApplyWhitelist:
                    return HandleApplyWhitelist();

                case IpcCommands.AddToWhitelist:
                    return HandleAddToWhitelist(request.Payload);

                case IpcCommands.RemoveFromWhitelist:
                    return HandleRemoveFromWhitelist(request.Payload);

                case IpcCommands.BlockAll:
                    return HandleBlockAll();

                case IpcCommands.UnblockAll:
                    return HandleUnblockAll();

                case IpcCommands.GetPending:
                    return HandleGetPending();

                case IpcCommands.ApprovePending:
                    return HandleApprovePending(request.Payload);

                case IpcCommands.RejectPending:
                    return HandleRejectPending(request.Payload);

                case IpcCommands.SetAddMode:
                    return HandleSetAddMode(request.Payload);

                case IpcCommands.GetAddMode:
                    return Ok(data: _addMode ? "true" : "false");

                default:
                    return Fail($"Unknown command: {request.Command}");
            }
        }
        catch (Exception ex)
        {
            Log($"IPC error: {ex}");
            return Fail(ex.Message);
        }
    }

    private IpcResponse Ok(string? message = null, string? data = null)
        => new() { Success = true, Message = message, Data = data };

    private IpcResponse Fail(string message)
        => new() { Success = false, Message = message };

    private object BuildStatus()
    {
        int devicesCount = 0;
        try { devicesCount = _monitor.GetConnectedDevices().Count; } catch { }

        return new
        {
            devicesCount,
            whitelistCount = _whitelist.Count,
            isBlocked = _blocker.IsBlocked(),
            serviceRunning = true,
            pendingCount = _pending.Count,
            addMode = _addMode,
        };
    }

    private IpcResponse HandleApplyWhitelist()
    {
        var result = _blocker.ApplyWhitelist(_whitelist);
        Log($"ApplyWhitelist: disabled={result.Disabled}, enabled={result.Enabled}, errors={result.Errors}");

        return Ok(message: $"Отключено: {result.Disabled}, включено: {result.Enabled}, ошибок: {result.Errors}");
    }

    private IpcResponse HandleAddToWhitelist(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return Fail("Пустой payload");

        var entry = JsonSerializer.Deserialize<WhitelistEntry>(payload);
        if (entry is null)
            return Fail("Не удалось разобрать запись");

        if (!_whitelist.Add(entry))
            return Fail("Устройство уже в белом списке");

        EnableDeviceByEntry(entry);

        Log($"Added to whitelist: {entry.IdentityKey}");
        return Ok(message: $"Добавлено: {entry.Label}");
    }

    private IpcResponse HandleRemoveFromWhitelist(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return Fail("Пустой payload");

        var identityKey = payload.Trim();
        var entry = _whitelist.LoadAll()
            .FirstOrDefault(e => string.Equals(e.IdentityKey, identityKey, StringComparison.OrdinalIgnoreCase));

        if (!_whitelist.Remove(identityKey))
            return Fail("Запись не найдена");

        if (entry is not null)
            DisableDeviceByEntry(entry);

        Log($"Removed from whitelist: {identityKey}");
        return Ok(message: $"Удалено: {identityKey}");
    }

    private IpcResponse HandleBlockAll()
    {
        var changed = _blocker.Block();
        Log($"BlockAll: changed={changed}");
        return Ok(message: changed ? "USB заблокированы" : "USB уже были заблокированы");
    }

    private IpcResponse HandleUnblockAll()
    {
        var changed = _blocker.Unblock();
        Log($"UnblockAll: changed={changed}");
        return Ok(message: changed ? "USB разблокированы" : "USB уже были разблокированы");
    }

    private IpcResponse HandleSetAddMode(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return Fail("Пустой payload");

        var enabled = string.Equals(payload.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        _addMode = enabled;

        Log($"Add mode set to: {enabled}");
        return Ok(message: enabled ? "Режим добавления: ВКЛ" : "Режим добавления: ВЫКЛ");
    }

    // ----- Pending approval -----

    private IpcResponse HandleGetPending()
    {
        PendingApproval? pending;
        lock (_pendingLock)
        {
            pending = _pending.Values.FirstOrDefault();
        }

        if (pending is null)
            return Ok(data: "null");

        return Ok(data: JsonSerializer.Serialize(pending));
    }

    private IpcResponse HandleApprovePending(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return Fail("Пустой payload");

        ApprovePayload? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ApprovePayload>(payload);
        }
        catch (Exception ex)
        {
            return Fail($"Не удалось разобрать payload: {ex.Message}");
        }

        if (parsed is null || string.IsNullOrEmpty(parsed.IdentityKey))
            return Fail("Некорректный payload");

        PendingApproval? pending;
        lock (_pendingLock)
        {
            if (!_pending.TryGetValue(parsed.IdentityKey, out pending))
                return Fail("Устройство не найдено в списке ожидания");
        }

        // ---- Проверка пароля ----
        if (!_authService.VerifyPassword(parsed.Password ?? string.Empty))
        {
            int attempts;
            lock (_pendingLock)
            {
                _approveAttempts.TryGetValue(parsed.IdentityKey, out attempts);
                attempts++;
                _approveAttempts[parsed.IdentityKey] = attempts;
            }

            Log($"Approve rejected (bad password, attempt {attempts} of {MaxApproveAttempts}): {parsed.IdentityKey}");

            if (attempts >= MaxApproveAttempts)
            {
                // Слишком много попыток — блокируем окончательно
                lock (_pendingLock)
                {
                    _pending.Remove(parsed.IdentityKey);
                    _approveAttempts.Remove(parsed.IdentityKey);
                }

                Log($"Too many attempts, pending removed: {parsed.IdentityKey}");

                // Убеждаемся, что устройство отключено
                var instanceToKeepDisabled = UsbDeviceControl.FindDeviceInstanceId(pending.PnpId)
                                             ?? UsbDeviceControl.FindDeviceInstanceIdIncludingDisabled(pending.PnpId);
                if (instanceToKeepDisabled is not null)
                {
                    UsbDeviceControl.Disable(instanceToKeepDisabled);
                    Log($"Force-disabled after too many attempts: {parsed.IdentityKey}");
                }

                // Размонтируем том на всякий случай
                DismountVolumeWithRetries(pending.PnpId, parsed.IdentityKey);

                return Fail("Превышено число попыток. Устройство заблокировано окончательно.");
            }

            return Fail($"Неверный пароль (попытка {attempts} из {MaxApproveAttempts})");
        }

        // ---- Пароль верный ----
        Log($"Approve accepted: {parsed.IdentityKey}");

        // Сбрасываем счётчик
        lock (_pendingLock)
        {
            _approveAttempts.Remove(parsed.IdentityKey);
        }

        // Включаем устройство
        var instanceId = UsbDeviceControl.FindDeviceInstanceId(pending.PnpId)
                         ?? UsbDeviceControl.FindDeviceInstanceIdIncludingDisabled(pending.PnpId);

        if (instanceId is not null)
        {
            var enabled = UsbDeviceControl.Enable(instanceId);
            Log($"Enabled approved device: {parsed.IdentityKey}, result={enabled}");

            if (!enabled)
            {
                Log($"WARNING: CM_Enable_DevNode failed for {parsed.IdentityKey}");
            }
        }
        else
        {
            Log($"WARNING: instanceId not found for approved device: {parsed.IdentityKey}");
        }

        // Добавляем в whitelist, если отмечено
        if (parsed.AddToWhitelist == true)
        {
            // Пытаемся получить текущую букву диска, чтобы сохранить её в whitelist
            string? currentLetter = null;
            try
            {
                currentLetter = _monitor.GetDriveLetterByPnpDeviceId(pending.PnpId);
            }
            catch (Exception ex)
            {
                Log($"GetDriveLetterByPnpDeviceId (approve) error: {ex.Message}");
            }

            var entry = new WhitelistEntry
            {
                Vid = pending.Vid,
                Pid = pending.Pid,
                Serial = pending.Serial,
                Label = pending.Label,
                AddedBy = "admin",
                AddedAt = DateTime.UtcNow,
                LastDriveLetter = currentLetter,
            };

            if (_whitelist.Add(entry))
                Log($"Added to whitelist: {parsed.IdentityKey}, lastDriveLetter={currentLetter}");
        }

        lock (_pendingLock)
        {
            _pending.Remove(parsed.IdentityKey);
        }

        return Ok("Устройство разрешено");
    }

    private IpcResponse HandleRejectPending(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return Fail("Пустой payload");

        var identityKey = payload.Trim();

        PendingApproval? pending = null;
        lock (_pendingLock)
        {
            if (_pending.TryGetValue(identityKey, out pending))
                _pending.Remove(identityKey);
            _approveAttempts.Remove(identityKey);
        }

        // Убеждаемся, что устройство отключено
        if (pending is not null)
        {
            var instanceId = UsbDeviceControl.FindDeviceInstanceId(pending.PnpId)
                             ?? UsbDeviceControl.FindDeviceInstanceIdIncludingDisabled(pending.PnpId);

            if (instanceId is not null)
            {
                UsbDeviceControl.Disable(instanceId);
                Log($"Force-disabled on reject: {identityKey}");
            }

            DismountVolumeWithRetries(pending.PnpId, identityKey);
        }

        Log($"Rejected pending: {identityKey}");
        return Ok("Устройство заблокировано");
    }

    // ----- Helpers для работы с устройством -----

    private void EnableDeviceByEntry(WhitelistEntry entry)
    {
        try
        {
            var device = _monitor.GetConnectedDevices()
                .FirstOrDefault(d => string.Equals(d.IdentityKey, entry.IdentityKey, StringComparison.OrdinalIgnoreCase));

            if (device is null)
                return;

            var pnpId = _monitor.GetPnpDeviceIdByDriveLetter(device.DriveLetter ?? string.Empty)
                        ?? _monitor.GetPnpDeviceIdByDriveLetter(entry.LastDriveLetter ?? string.Empty);

            if (string.IsNullOrEmpty(pnpId))
                return;

            var instanceId = UsbDeviceControl.FindDeviceInstanceId(pnpId)
                            ?? UsbDeviceControl.FindDeviceInstanceIdIncludingDisabled(pnpId);

            if (instanceId is not null)
            {
                var enabled = UsbDeviceControl.Enable(instanceId);
                Log($"Enabled device: {entry.IdentityKey}, result={enabled}");
            }
        }
        catch (Exception ex)
        {
            Log($"EnableDeviceByEntry error: {ex.Message}");
        }
    }

    private void DisableDeviceByEntry(WhitelistEntry entry)
    {
        try
        {
            var device = _monitor.GetConnectedDevices()
                .FirstOrDefault(d => string.Equals(d.IdentityKey, entry.IdentityKey, StringComparison.OrdinalIgnoreCase));

            if (device is null)
                return;

            var pnpId = _monitor.GetPnpDeviceIdByDriveLetter(device.DriveLetter ?? string.Empty)
                        ?? _monitor.GetPnpDeviceIdByDriveLetter(entry.LastDriveLetter ?? string.Empty);

            if (string.IsNullOrEmpty(pnpId))
                return;

            var instanceId = UsbDeviceControl.FindDeviceInstanceId(pnpId);
            if (instanceId is null)
                return;

            var disabled = UsbDeviceControl.Disable(instanceId);
            Log($"Disabled device: {entry.IdentityKey}, result={disabled}");

            // Размонтируем том с несколькими попытками и логированием
            DismountVolumeWithRetries(pnpId, entry.IdentityKey);
        }
        catch (Exception ex)
        {
            Log($"DisableDeviceByEntry error: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        try
        {
            File.AppendAllText(_logFile, line + Environment.NewLine);
        }
        catch
        {
            // Не критично
        }

        _logger.LogInformation(message);
    }

    /// <summary>DTO для команды approve-pending.</summary>
    private class ApprovePayload
    {
        [JsonPropertyName("identityKey")]
        public string? IdentityKey { get; set; }

        [JsonPropertyName("password")]
        public string? Password { get; set; }

        [JsonPropertyName("addToWhitelist")]
        public bool? AddToWhitelist { get; set; }
    }
}