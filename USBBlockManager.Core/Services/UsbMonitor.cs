using System.Management;
using USBBlockManager.Models;

namespace USBBlockManager.Services;

/// <summary>
/// Получение списка USB-накопителей через WMI.
/// </summary>
public class UsbMonitor
{
    /// <summary>Список всех USB-накопителей, подключённых сейчас.</summary>
    public List<UsbDevice> GetConnectedDevices()
    {
        var result = new List<UsbDevice>();

        const string diskQuery =
            "SELECT * FROM Win32_DiskDrive WHERE InterfaceType = 'USB'";

        using var diskSearcher = new ManagementObjectSearcher(diskQuery);
        using var disks = diskSearcher.Get();

        foreach (ManagementObject disk in disks)
        {
            try
            {
                var device = ParseDisk(disk);
                if (device is not null)
                    result.Add(device);
            }
            catch
            {
                // Пропускаем сбойные устройства
            }
        }

        return result;
    }

    /// <summary>Найти USB-накопитель по букве диска (например, "E:").</summary>
    public UsbDevice? GetDeviceByDriveLetter(string driveLetter)
    {
        if (string.IsNullOrWhiteSpace(driveLetter))
            return null;

        var normalized = driveLetter.TrimEnd('\\').TrimEnd(':') + ":";
        return GetConnectedDevices()
            .FirstOrDefault(d => string.Equals(d.DriveLetter, normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Получить PNPDeviceID диска по букве (например, "E:").
    /// Возвращает строку вида "USBSTOR\DISK&VEN_...&PROD_...\SERIAL&0".
    /// </summary>
    public string? GetPnpDeviceIdByDriveLetter(string driveLetter)
    {
        if (string.IsNullOrWhiteSpace(driveLetter))
            return null;

        var normalized = driveLetter.TrimEnd('\\').TrimEnd(':') + ":";

        const string query = "SELECT * FROM Win32_DiskDrive";

        using var searcher = new ManagementObjectSearcher(query);
        using var disks = searcher.Get();

        foreach (ManagementObject disk in disks)
        {
            try
            {
                foreach (ManagementObject partition in disk.GetRelated("Win32_DiskPartition"))
                {
                    foreach (ManagementObject logical in partition.GetRelated("Win32_LogicalDisk"))
                    {
                        if (logical["DeviceID"] is string letter
                            && string.Equals(letter, normalized, StringComparison.OrdinalIgnoreCase))
                        {
                            return disk["PNPDeviceID"] as string;
                        }
                    }
                }
            }
            catch
            {
                // пропускаем сбойные устройства
            }
        }

        return null;
    }

    /// <summary>
    /// Получить букву диска (например, "E:") по PNPDeviceID.
    /// Возвращает null, если диск ещё не смонтирован.
    /// </summary>
    public string? GetDriveLetterByPnpDeviceId(string pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId))
            return null;

        const string query = "SELECT * FROM Win32_DiskDrive";

        using var searcher = new ManagementObjectSearcher(query);
        using var disks = searcher.Get();

        foreach (ManagementObject disk in disks)
        {
            try
            {
                var diskPnp = disk["PNPDeviceID"] as string;
                if (string.IsNullOrEmpty(diskPnp))
                    continue;

                if (!diskPnp.StartsWith(pnpDeviceId, StringComparison.OrdinalIgnoreCase)
                    && !pnpDeviceId.StartsWith(diskPnp, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (ManagementObject partition in disk.GetRelated("Win32_DiskPartition"))
                {
                    foreach (ManagementObject logical in partition.GetRelated("Win32_LogicalDisk"))
                    {
                        if (logical["DeviceID"] is string letter)
                            return letter;
                    }
                }
            }
            catch
            {
                // Пропускаем сбойные устройства
            }
        }

        return null;
    }

    /// <summary>
    /// Получить Volume GUID Path по PNPDeviceID.
    /// Возвращает строку вида "\\?\Volume{...}\" или null.
    /// Используется для восстановления буквы диска через Win32_Volume.AddMountPoint.
    /// </summary>
    public string? GetVolumeGuidByPnpDeviceId(string pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId))
            return null;

        const string query = "SELECT * FROM Win32_DiskDrive";

        using var searcher = new ManagementObjectSearcher(query);
        using var disks = searcher.Get();

        foreach (ManagementObject disk in disks)
        {
            try
            {
                var diskPnp = disk["PNPDeviceID"] as string;
                if (string.IsNullOrEmpty(diskPnp))
                    continue;

                if (!diskPnp.StartsWith(pnpDeviceId, StringComparison.OrdinalIgnoreCase)
                    && !pnpDeviceId.StartsWith(diskPnp, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Ищем раздел — у него есть Volume GUID
                foreach (ManagementObject partition in disk.GetRelated("Win32_DiskPartition"))
                {
                    // Win32_DiskPartition → Win32_Volume через associator
                    foreach (ManagementObject volume in partition.GetRelated("Win32_Volume"))
                    {
                        var deviceId = volume["DeviceID"] as string; // "\\?\Volume{...}\"
                        if (!string.IsNullOrEmpty(deviceId))
                            return deviceId;
                    }
                }
            }
            catch
            {
                // Пропускаем сбойные устройства
            }
        }

        return null;
    }

    // ----- приватные -----

    private static UsbDevice? ParseDisk(ManagementObject disk)
    {
        string pnpDeviceId = (disk["PNPDeviceID"] as string) ?? string.Empty;
        if (string.IsNullOrEmpty(pnpDeviceId))
            return null;

        var (vid, pid, serial) = ParsePnpDeviceId(pnpDeviceId);
        if (string.IsNullOrEmpty(serial))
            return null;

        var label = (disk["Model"] as string) ?? string.Empty;
        var driveLetter = GetDriveLetterForDisk(disk);

        return new UsbDevice
        {
            Vid = vid,
            Pid = pid,
            Serial = serial,
            Label = label,
            DriveLetter = driveLetter,
            DetectedAt = DateTime.UtcNow,
        };
    }

    /// <summary>Разбор PNPDeviceID USBSTOR.</summary>
    public static (string Vid, string Pid, string Serial) ParsePnpDeviceId(string pnpDeviceId)
    {
        string vid = string.Empty;
        string pid = string.Empty;
        string serial = string.Empty;

        var parts = pnpDeviceId.Split('\\');
        if (parts.Length >= 3)
        {
            var idPart = parts[1];
            var tokens = idPart.Split('&');
            foreach (var token in tokens)
            {
                if (token.StartsWith("VEN_", StringComparison.OrdinalIgnoreCase))
                    vid = token.Substring(4);
                else if (token.StartsWith("PROD_", StringComparison.OrdinalIgnoreCase))
                    pid = token.Substring(5);
            }

            var serialPart = parts[2];
            var ampIndex = serialPart.IndexOf('&');
            serial = ampIndex >= 0 ? serialPart.Substring(0, ampIndex) : serialPart;
        }

        return (vid, pid, serial);
    }

    private static string? GetDriveLetterForDisk(ManagementObject disk)
    {
        try
        {
            foreach (ManagementObject partition in disk.GetRelated("Win32_DiskPartition"))
            {
                foreach (ManagementObject logical in partition.GetRelated("Win32_LogicalDisk"))
                {
                    if (logical["DeviceID"] is string letter)
                        return letter;
                }
            }
        }
        catch
        {
            // Игнорируем — у некоторых устройств нет тома
        }

        return null;
    }
}