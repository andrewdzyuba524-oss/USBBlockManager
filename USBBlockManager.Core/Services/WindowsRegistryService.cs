using Microsoft.Win32;

namespace USBBlockManager.Services;

/// <summary>
/// Реальная реализация IRegistryService для Windows.
/// Все операции выполняются в HKLM (HKEY_LOCAL_MACHINE).
/// </summary>
public class WindowsRegistryService : IRegistryService
{
    private const string RootPath = @"SYSTEM\CurrentControlSet";

    public int? ReadDword(string subKeyPath, string valueName)
    {
        if (subKeyPath is null) throw new ArgumentNullException(nameof(subKeyPath));
        if (valueName is null) throw new ArgumentNullException(nameof(valueName));

        using var key = Registry.LocalMachine.OpenSubKey(
            Combine(RootPath, subKeyPath), writable: false);

        if (key is null) return null;

        var value = key.GetValue(valueName);
        return value switch
        {
            null => null,
            int i => i,
            _ => Convert.ToInt32(value),
        };
    }

    public void WriteDword(string subKeyPath, string valueName, int value)
    {
        if (subKeyPath is null) throw new ArgumentNullException(nameof(subKeyPath));
        if (valueName is null) throw new ArgumentNullException(nameof(valueName));

        using var key = Registry.LocalMachine.CreateSubKey(
            Combine(RootPath, subKeyPath), writable: true);

        if (key is null)
            throw new InvalidOperationException(
                $"Не удалось открыть/создать ключ реестра: {subKeyPath}");

        key.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    public bool DeleteValue(string subKeyPath, string valueName)
    {
        if (subKeyPath is null) throw new ArgumentNullException(nameof(subKeyPath));
        if (valueName is null) throw new ArgumentNullException(nameof(valueName));

        using var key = Registry.LocalMachine.OpenSubKey(
            Combine(RootPath, subKeyPath), writable: true);

        if (key is null) return false;
        if (key.GetValue(valueName) is null) return false;

        key.DeleteValue(valueName, throwOnMissingValue: false);
        return true;
    }

    public bool KeyExists(string subKeyPath)
    {
        if (subKeyPath is null) throw new ArgumentNullException(nameof(subKeyPath));

        using var key = Registry.LocalMachine.OpenSubKey(
            Combine(RootPath, subKeyPath), writable: false);

        return key is not null;
    }

    public bool ValueExists(string subKeyPath, string valueName)
    {
        if (subKeyPath is null) throw new ArgumentNullException(nameof(subKeyPath));
        if (valueName is null) throw new ArgumentNullException(nameof(valueName));

        using var key = Registry.LocalMachine.OpenSubKey(
            Combine(RootPath, subKeyPath), writable: false);

        return key?.GetValue(valueName) is not null;
    }

    private static string Combine(string root, string sub)
        => sub.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? sub
            : $@"{root}\{sub}";
}