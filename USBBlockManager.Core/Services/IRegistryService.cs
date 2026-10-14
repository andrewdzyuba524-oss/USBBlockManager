namespace USBBlockManager.Services;

/// <summary>
/// Абстракция над реестром Windows.
/// Позволяет подменять реальный реестр в тестах.
/// </summary>
public interface IRegistryService
{
    /// <summary>Прочитать DWORD-значение. Возвращает null, если ключ/значение не найдено.</summary>
    int? ReadDword(string subKeyPath, string valueName);

    /// <summary>Записать DWORD-значение.</summary>
    void WriteDword(string subKeyPath, string valueName, int value);

    /// <summary>Удалить значение. Возвращает true, если оно было.</summary>
    bool DeleteValue(string subKeyPath, string valueName);

    /// <summary>Существует ли подключ.</summary>
    bool KeyExists(string subKeyPath);

    /// <summary>Существует ли значение.</summary>
    bool ValueExists(string subKeyPath, string valueName);
}