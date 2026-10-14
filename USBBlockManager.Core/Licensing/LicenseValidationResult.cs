namespace USBBlockManager.Licensing;

/// <summary>
/// Результат проверки лицензии.
/// </summary>
public class LicenseValidationResult
{
    /// <summary>Лицензия действительна.</summary>
    public bool IsValid { get; init; }

    /// <summary>Причина отказа (если IsValid = false).</summary>
    public LicenseValidationError Error { get; init; } = LicenseValidationError.None;

    /// <summary>Человекочитаемое описание ошибки.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Загруженная лицензия (если удалось распарсить).</summary>
    public License? License { get; init; }

    /// <summary>HardwareId текущего компьютера.</summary>
    public string CurrentHardwareId { get; init; } = string.Empty;

    public static LicenseValidationResult Ok(License license, string hardwareId) => new()
    {
        IsValid = true,
        Error = LicenseValidationError.None,
        Message = "Лицензия действительна.",
        License = license,
        CurrentHardwareId = hardwareId,
    };

    public static LicenseValidationResult Fail(
        LicenseValidationError error,
        string message,
        License? license = null,
        string hardwareId = "") => new()
    {
        IsValid = false,
        Error = error,
        Message = message,
        License = license,
        CurrentHardwareId = hardwareId,
    };
}

/// <summary>
/// Причины отказа в лицензии.
/// </summary>
public enum LicenseValidationError
{
    None = 0,

    /// <summary>Файл лицензии не найден.</summary>
    FileNotFound,

    /// <summary>Файл не удалось прочитать.</summary>
    FileReadError,

    /// <summary>JSON не парсится или структура неверна.</summary>
    InvalidFormat,

    /// <summary>Подпись не совпадает с публичным ключом.</summary>
    InvalidSignature,

    /// <summary>Срок действия истёк.</summary>
    Expired,

    /// <summary>HardwareId не совпадает с указанным в лицензии.</summary>
    HardwareMismatch,

    /// <summary>Публичный ключ в программе не настроен.</summary>
    PublicKeyNotConfigured,
}