using System.Text.Json;

namespace USBBlockManager.Licensing;

/// <summary>
/// Высокоуровневая проверка лицензии: комбинирует LicenseService и TrialInfo.
/// Определяет итоговый статус программы при запуске.
/// </summary>
public static class LicenseChecker
{
    /// <summary>
    /// Полная проверка: лицензия → триал → ничего.
    /// Возвращает итоговый статус и детали.
    /// </summary>
    /// <param name="licenseFilePath">Путь к файлу лицензии.</param>
    /// <param name="trialFilePath">Путь к файлу пробного периода.</param>
    public static LicenseStatus Check(string licenseFilePath, string trialFilePath)
    {
        // 1. Проверяем лицензию, если файл существует
        if (!string.IsNullOrWhiteSpace(licenseFilePath) && File.Exists(licenseFilePath))
        {
            var result = LicenseService.ValidateFromFile(licenseFilePath);
            if (result.IsValid)
            {
                return LicenseStatus.CreateLicensed(result.License!, result.Message);
            }
            // Лицензия есть, но невалидна — возвращаем причину
            return LicenseStatus.CreateInvalidLicense(result);
        }

        // 2. Лицензии нет — проверяем триал через TrialProtection (HMAC + реестр)
        var trial = LoadOrCreateTrial(trialFilePath);
        if (trial is null)
        {
            // Не удалось создать триал (нет прав?) — работаем без него
            return LicenseStatus.CreateNoLicense(
                "Лицензия не найдена, пробный период недоступен.");
        }

        if (trial.IsExpired)
        {
            return LicenseStatus.CreateTrialExpired(trial);
        }

        // Обновляем lastRunAt и сохраняем с новой HMAC-подписью
        trial.LastRunAt = DateTime.UtcNow;
        TrialProtection.Save(trialFilePath, trial);

        return LicenseStatus.CreateTrial(trial);
    }

    /// <summary>
    /// Загрузить или создать файл триала с защитой (HMAC + дублирование в реестр).
    /// </summary>
    private static TrialInfo? LoadOrCreateTrial(string trialFilePath)
    {
        try
        {
            return TrialProtection.LoadOrCreate(trialFilePath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Сбросить триал (для тестов).</summary>
    internal static void ResetTrial(string trialFilePath)
    {
        try
        {
            if (File.Exists(trialFilePath))
                File.Delete(trialFilePath);
        }
        catch { }
    }
}

/// <summary>
/// Итоговый статус лицензии при запуске.
/// </summary>
public class LicenseStatus
{
    public LicenseStatusKind Kind { get; init; }

    /// <summary>Лицензия (если есть).</summary>
    public License? License { get; init; }

    /// <summary>Информация о триале (если в режиме триала).</summary>
    public TrialInfo? Trial { get; init; }

    /// <summary>Причина отказа (если лицензия есть, но невалидна).</summary>
    public LicenseValidationResult? ValidationResult { get; init; }

    /// <summary>Человекочитаемое сообщение.</summary>
    public string Message { get; init; } = string.Empty;

    // --- Фабричные методы ---
    // Названия с префиксом Create, чтобы не конфликтовать
    // со свойствами License и Trial.

    public static LicenseStatus CreateLicensed(License license, string message) => new()
    {
        Kind = LicenseStatusKind.Licensed,
        License = license,
        Message = message,
    };

    public static LicenseStatus CreateTrial(TrialInfo trial) => new()
    {
        Kind = LicenseStatusKind.Trial,
        Trial = trial,
        Message = $"Пробный период: осталось {trial.DaysRemaining} дн. " +
                  $"(до {trial.ExpiresAt:yyyy-MM-dd})",
    };

    public static LicenseStatus CreateTrialExpired(TrialInfo trial) => new()
    {
        Kind = LicenseStatusKind.TrialExpired,
        Trial = trial,
        Message = $"Пробный период истёк {trial.ExpiresAt:yyyy-MM-dd}. " +
                  $"Требуется лицензия.",
    };

    public static LicenseStatus CreateNoLicense(string message) => new()
    {
        Kind = LicenseStatusKind.NoLicense,
        Message = message,
    };

    public static LicenseStatus CreateInvalidLicense(LicenseValidationResult result) => new()
    {
        Kind = LicenseStatusKind.InvalidLicense,
        License = result.License,
        ValidationResult = result,
        Message = result.Message,
    };
}

public enum LicenseStatusKind
{
    /// <summary>Лицензия действительна.</summary>
    Licensed = 0,

    /// <summary>Идёт пробный период.</summary>
    Trial = 1,

    /// <summary>Пробный период истёк.</summary>
    TrialExpired = 2,

    /// <summary>Лицензии нет вообще (и триал не создан).</summary>
    NoLicense = 3,

    /// <summary>Лицензия есть, но невалидна.</summary>
    InvalidLicense = 4,
}