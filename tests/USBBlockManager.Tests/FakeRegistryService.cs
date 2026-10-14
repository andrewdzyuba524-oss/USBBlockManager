using USBBlockManager.Services;

namespace USBBlockManager.Tests;

/// <summary>
/// Тестовая реализация IRegistryService. Хранит данные в памяти,
/// не трогает реальный реестр.
/// </summary>
public class FakeRegistryService : IRegistryService
{
    private readonly Dictionary<string, Dictionary<string, int>> _data =
        new(StringComparer.OrdinalIgnoreCase);

    public int? ReadDword(string subKeyPath, string valueName)
    {
        if (_data.TryGetValue(subKeyPath, out var values)
            && values.TryGetValue(valueName, out int v))
        {
            return v;
        }
        return null;
    }

    public void WriteDword(string subKeyPath, string valueName, int value)
    {
        if (!_data.TryGetValue(subKeyPath, out var values))
        {
            values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _data[subKeyPath] = values;
        }
        values[valueName] = value;
    }

    public bool DeleteValue(string subKeyPath, string valueName)
    {
        if (!_data.TryGetValue(subKeyPath, out var values))
            return false;

        return values.Remove(valueName);
    }

    public bool KeyExists(string subKeyPath) => _data.ContainsKey(subKeyPath);

    public bool ValueExists(string subKeyPath, string valueName)
        => _data.TryGetValue(subKeyPath, out var values)
           && values.ContainsKey(valueName);
}