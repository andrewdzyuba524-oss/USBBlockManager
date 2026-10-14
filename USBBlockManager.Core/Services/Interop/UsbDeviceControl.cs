using System.Runtime.InteropServices;
using USBBlockManager.Models;

namespace USBBlockManager.Services.Interop;

/// <summary>
/// P/Invoke к CfgMgr32.dll и SetupAPI.dll для управления USB-устройствами.
/// </summary>
public static class UsbDeviceControl
{
    // ----- CfgMgr32 -----
    private const string CfgMgr32 = "cfgmgr32.dll";

    [DllImport(CfgMgr32, CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(
        out uint pdnDevInst,
        string pDeviceID,
        uint ulFlags);

    [DllImport(CfgMgr32)]
    private static extern int CM_Disable_DevNode(uint dnDevInst, uint ulFlags);

    [DllImport(CfgMgr32)]
    private static extern int CM_Enable_DevNode(uint dnDevInst, uint ulFlags);

    [DllImport(CfgMgr32)]
    private static extern int CM_Get_DevNode_Status(
        out uint pulStatus,
        out uint pulProblemNumber,
        uint dnDevInst,
        uint ulFlags);

    [DllImport(CfgMgr32)]
    private static extern int CM_Reenumerate_DevNode(uint dnDevInst, uint ulFlags);

    private const uint CM_LOCATE_DEVNODE_NORMAL = 0;
    private const uint CM_DISABLE_UI_NOT_OK = 0x00000004;
    private const uint CM_ENABLE_UI_NOT_OK = 0x00000000;
    private const int CR_SUCCESS = 0;

    private const uint DN_HAS_PROBLEM = 0x00000400;

    // ----- SetupAPI -----
    private const string SetupApi = "setupapi.dll";

    [DllImport(SetupApi, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(
        IntPtr classGuid,
        string enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport(SetupApi, SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport(SetupApi, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceIdW(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        System.Text.StringBuilder deviceInstanceId,
        uint deviceInstanceIdSize,
        out uint requiredSize);

    [DllImport(SetupApi, SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    private const uint DIGCF_ALLCLASSES = 0x00000004;
    private const uint DIGCF_PRESENT = 0x00000002;
    private const int ERROR_NO_MORE_ITEMS = 259;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    // ----- Публичные методы -----

    public static bool Disable(string deviceInstanceId)
    {
        var devInst = LocateDevice(deviceInstanceId);
        if (devInst == 0) return false;

        var result = CM_Disable_DevNode(devInst, CM_DISABLE_UI_NOT_OK);
        return result == CR_SUCCESS;
    }

    public static bool Enable(string deviceInstanceId)
    {
        var devInst = LocateDevice(deviceInstanceId);
        if (devInst == 0) return false;

        var result = CM_Enable_DevNode(devInst, CM_ENABLE_UI_NOT_OK);
        return result == CR_SUCCESS;
    }

    public static bool IsDisabled(string deviceInstanceId)
    {
        var devInst = LocateDevice(deviceInstanceId);
        if (devInst == 0) return false;

        CM_Get_DevNode_Status(out uint status, out _, devInst, 0);
        return (status & DN_HAS_PROBLEM) != 0;
    }

    public static string? FindDeviceInstanceId(string pnpDeviceId)
    {
        var handle = SetupDiGetClassDevsW(
            IntPtr.Zero,
            null!,
            IntPtr.Zero,
            DIGCF_ALLCLASSES | DIGCF_PRESENT);

        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            return null;

        try
        {
            var data = new SP_DEVINFO_DATA
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
            };

            for (uint i = 0; ; i++)
            {
                if (!SetupDiEnumDeviceInfo(handle, i, ref data))
                {
                    if (Marshal.GetLastWin32Error() == ERROR_NO_MORE_ITEMS)
                        break;
                    continue;
                }

                var sb = new System.Text.StringBuilder(512);
                if (SetupDiGetDeviceInstanceIdW(handle, ref data, sb, (uint)sb.Capacity, out _))
                {
                    var instanceId = sb.ToString();
                    if (instanceId.StartsWith(pnpDeviceId, StringComparison.OrdinalIgnoreCase))
                        return instanceId;
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(handle);
        }

        return null;
    }

    /// <summary>
    /// Найти instance ID даже для отключённого устройства.
    /// </summary>
    public static string? FindDeviceInstanceIdIncludingDisabled(string pnpDeviceId)
    {
        var handle = SetupDiGetClassDevsW(
            IntPtr.Zero,
            null!,
            IntPtr.Zero,
            DIGCF_ALLCLASSES);

        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            return null;

        try
        {
            var data = new SP_DEVINFO_DATA
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
            };

            for (uint i = 0; ; i++)
            {
                if (!SetupDiEnumDeviceInfo(handle, i, ref data))
                {
                    if (Marshal.GetLastWin32Error() == ERROR_NO_MORE_ITEMS)
                        break;
                    continue;
                }

                var sb = new System.Text.StringBuilder(512);
                if (SetupDiGetDeviceInstanceIdW(handle, ref data, sb, (uint)sb.Capacity, out _))
                {
                    var instanceId = sb.ToString();
                    if (instanceId.StartsWith(pnpDeviceId, StringComparison.OrdinalIgnoreCase))
                        return instanceId;
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(handle);
        }

        return null;
    }

    /// <summary>
    /// Переопрос USB-шины.
    /// </summary>
    public static void RescanUsbBus()
    {
        try
        {
            var handle = SetupDiGetClassDevsW(
                IntPtr.Zero,
                "USB",
                IntPtr.Zero,
                DIGCF_ALLCLASSES);

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                RescanAllDevices();
                return;
            }

            try
            {
                var data = new SP_DEVINFO_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
                };

                for (uint i = 0; ; i++)
                {
                    if (!SetupDiEnumDeviceInfo(handle, i, ref data))
                    {
                        if (Marshal.GetLastWin32Error() == ERROR_NO_MORE_ITEMS)
                            break;
                        continue;
                    }

                    CM_Reenumerate_DevNode(data.DevInst, 0);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(handle);
            }
        }
        catch
        {
            // Не критично
        }
    }

    /// <summary>
    /// Включает все устройства, которые сейчас отключены,
    /// но присутствуют в whitelist.
    /// </summary>
    public static int EnableWhitelistedDevices(IEnumerable<WhitelistEntry> whitelist)
    {
        int enabled = 0;

        try
        {
            var handle = SetupDiGetClassDevsW(
                IntPtr.Zero,
                "USBSTOR",
                IntPtr.Zero,
                DIGCF_ALLCLASSES);

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                return 0;

            try
            {
                var data = new SP_DEVINFO_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
                };

                var entries = whitelist.ToList();

                for (uint i = 0; ; i++)
                {
                    if (!SetupDiEnumDeviceInfo(handle, i, ref data))
                    {
                        if (Marshal.GetLastWin32Error() == ERROR_NO_MORE_ITEMS)
                            break;
                        continue;
                    }

                    var sb = new System.Text.StringBuilder(512);
                    if (!SetupDiGetDeviceInstanceIdW(handle, ref data, sb, (uint)sb.Capacity, out _))
                        continue;

                    var instanceId = sb.ToString();

                    var match = entries.Any(e =>
                        instanceId.Contains(e.Vid, StringComparison.OrdinalIgnoreCase)
                        && instanceId.Contains(e.Pid, StringComparison.OrdinalIgnoreCase)
                        && instanceId.Contains(e.Serial, StringComparison.OrdinalIgnoreCase));

                    if (!match)
                        continue;

                    CM_Get_DevNode_Status(out uint status, out _, data.DevInst, 0);
                    if ((status & DN_HAS_PROBLEM) != 0)
                    {
                        var rc = CM_Enable_DevNode(data.DevInst, CM_ENABLE_UI_NOT_OK);
                        if (rc == CR_SUCCESS)
                            enabled++;
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(handle);
            }
        }
        catch
        {
            // Игнорируем
        }

        return enabled;
    }

    /// <summary>
    /// Отключить все USBSTOR-устройства в системе.
    /// Используется при глобальной блокировке.
    /// </summary>
    public static int DisableAllUsbStorDevices()
    {
        return ToggleAllUsbStor(disable: true);
    }

    /// <summary>
    /// Включить все USBSTOR-устройства в системе.
    /// Используется при глобальной разблокировке.
    /// </summary>
    public static int EnableAllUsbStorDevices()
    {
        return ToggleAllUsbStor(disable: false);
    }

    /// <summary>
    /// Общий обход всех USBSTOR-устройств и их включение/отключение.
    /// DIGCF_ALLCLASSES без DIGCF_PRESENT — видим и отключённые.
    /// </summary>
    private static int ToggleAllUsbStor(bool disable)
    {
        int affected = 0;

        try
        {
            var handle = SetupDiGetClassDevsW(
                IntPtr.Zero,
                "USBSTOR",
                IntPtr.Zero,
                DIGCF_ALLCLASSES);

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                return 0;

            try
            {
                var data = new SP_DEVINFO_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
                };

                for (uint i = 0; ; i++)
                {
                    if (!SetupDiEnumDeviceInfo(handle, i, ref data))
                    {
                        if (Marshal.GetLastWin32Error() == ERROR_NO_MORE_ITEMS)
                            break;
                        continue;
                    }

                    var rc = disable
                        ? CM_Disable_DevNode(data.DevInst, CM_DISABLE_UI_NOT_OK)
                        : CM_Enable_DevNode(data.DevInst, CM_ENABLE_UI_NOT_OK);

                    if (rc == CR_SUCCESS)
                        affected++;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(handle);
            }
        }
        catch
        {
            // Игнорируем — вернём сколько успели
        }

        return affected;
    }

    // ----- Приватные -----

    private static void RescanAllDevices()
    {
        try
        {
            var handle = SetupDiGetClassDevsW(
                IntPtr.Zero,
                null!,
                IntPtr.Zero,
                DIGCF_ALLCLASSES);

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                return;

            try
            {
                var data = new SP_DEVINFO_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
                };

                if (SetupDiEnumDeviceInfo(handle, 0, ref data))
                {
                    CM_Reenumerate_DevNode(data.DevInst, 0);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(handle);
            }
        }
        catch
        {
            // Игнорируем
        }
    }

    private static uint LocateDevice(string deviceInstanceId)
    {
        var result = CM_Locate_DevNodeW(out uint devInst, deviceInstanceId, CM_LOCATE_DEVNODE_NORMAL);
        return result == CR_SUCCESS ? devInst : 0;
    }
}