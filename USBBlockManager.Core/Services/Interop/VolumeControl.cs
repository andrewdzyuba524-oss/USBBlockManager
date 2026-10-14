using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace USBBlockManager.Services.Interop;

/// <summary>
/// P/Invoke для принудительного размонтирования тома Windows,
/// удаления и назначения буквы диска.
/// Используется, чтобы мгновенно закрыть доступ к содержимому USB-диска
/// и затем корректно вернуть устройство в работу.
/// </summary>
public static class VolumeControl
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    private const uint FSCTL_LOCK_VOLUME = 0x00090018;
    private const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
    private const uint FSCTL_UNLOCK_VOLUME = 0x0009001C;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    /// <summary>
    /// Размонтировать том по букве диска ("E:").
    /// Сначала блокирует том (сбрасывает кэш), затем размонтирует.
    /// </summary>
    public static bool DismountVolume(string driveLetter)
    {
        if (string.IsNullOrWhiteSpace(driveLetter))
            return false;

        var normalized = driveLetter.TrimEnd('\\').TrimEnd(':') + ":";
        var path = $@"\\.\{normalized}";

        using var handle = CreateFileW(
            path,
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid || handle.IsClosed)
            return false;

        // 1. Блокируем том (сбрасывает кэш, запрещает новые открытия)
        if (!DeviceIoControl(handle, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0,
                             IntPtr.Zero, 0, out _, IntPtr.Zero))
            return false;

        // 2. Размонтируем том
        var ok = DeviceIoControl(handle, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0,
                                 IntPtr.Zero, 0, out _, IntPtr.Zero);

        // 3. Отпускаем блокировку (том уже размонтирован)
        DeviceIoControl(handle, FSCTL_UNLOCK_VOLUME, IntPtr.Zero, 0,
                        IntPtr.Zero, 0, out _, IntPtr.Zero);

        return ok;
    }

    /// <summary>
    /// Назначить букву диска тому по Volume GUID Path.
    /// Возвращает true, если буква успешно назначена.
    /// </summary>
    /// <param name="volumeGuidPath">Строка вида "\\?\Volume{...}\"</param>
    /// <param name="driveLetter">Буква без двоеточия, например 'D'.</param>
    public static bool AssignDriveLetter(string volumeGuidPath, char driveLetter)
    {
        if (string.IsNullOrWhiteSpace(volumeGuidPath))
            return false;

        if (!char.IsLetter(driveLetter))
            return false;

        try
        {
            // Экранируем обратные слэши для WQL
            var escaped = volumeGuidPath.Replace("\\", "\\\\");
            var query = $"SELECT * FROM Win32_Volume WHERE DeviceID = '{escaped}'";

            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementObject volume in searcher.Get())
            {
                var inParams = volume.GetMethodParameters("AddMountPoint");
                inParams["DriveLetter"] = $"{char.ToUpperInvariant(driveLetter)}:";

                var outParams = volume.InvokeMethod("AddMountPoint", inParams, null);
                var returnValue = Convert.ToUInt32(outParams?["ReturnValue"] ?? 0u);

                return returnValue == 0; // 0 = success
            }
        }
        catch
        {
            // Игнорируем — буква не назначена
        }

        return false;
    }
}