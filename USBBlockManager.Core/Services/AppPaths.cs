using System.IO;

namespace USBBlockManager.Services;

/// <summary>
/// Централизованные пути приложения.
/// Все общие данные лежат в %ProgramData%\USBBlockManager.
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "USBBlockManager";

    /// <summary>Корневая папка данных: %ProgramData%\USBBlockManager</summary>
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        AppFolderName);

    /// <summary>Папка конфигов: %ProgramData%\USBBlockManager\config</summary>
    public static string ConfigDir => Path.Combine(DataRoot, "config");

    /// <summary>Файл с хэшем пароля администратора.</summary>
    public static string AdminHashFile => Path.Combine(ConfigDir, "admin.hash");

    /// <summary>Папка белого списка.</summary>
    public static string WhitelistDir => Path.Combine(DataRoot, "whitelist");

    /// <summary>Файл белого списка.</summary>
    public static string WhitelistFile => Path.Combine(WhitelistDir, "whitelist.json");

    /// <summary>Папка логов.</summary>
    public static string LogsDir => Path.Combine(DataRoot, "logs");

    /// <summary>Папка лицензирования.</summary>
    public static string LicensingDir => Path.Combine(DataRoot, "licensing");

    /// <summary>Файл лицензии.</summary>
    public static string LicenseFile => Path.Combine(LicensingDir, "license.json");

    /// <summary>Файл пробного периода (trial).</summary>
    public static string TrialFile => Path.Combine(LicensingDir, "trial.json");

    /// <summary>Создать все нужные папки. Бросает исключение, если нет прав.</summary>
    public static void EnsureAll()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(WhitelistDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(LicensingDir);
    }

    /// <summary>
    /// Проверить, есть ли права на запись в DataRoot.
    /// Возвращает true, если можно писать.
    /// </summary>
    public static bool CanWrite()
    {
        try
        {
            Directory.CreateDirectory(DataRoot);
            var probe = Path.Combine(DataRoot, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}