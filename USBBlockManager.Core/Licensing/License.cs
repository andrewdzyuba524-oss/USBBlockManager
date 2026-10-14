using System.Text.Json.Serialization;

namespace USBBlockManager.Licensing;

/// <summary>
/// Модель лицензии. Хранится в JSON-файле, подписывается приватным ключом
/// издателя. При проверке подпись сверяется с публичным ключом, зашитым в программу.
/// </summary>
public class License
{
    /// <summary>Версия формата лицензии (на будущее, для миграций).</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>Имя лицензиата (ФИО или название организации).</summary>
    [JsonPropertyName("user")]
    public string User { get; set; } = string.Empty;

    /// <summary>Email лицензиата (для связи).</summary>
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Hardware ID компьютера, к которому привязана лицензия.
    /// Если пусто — лицензия переносимая (без привязки).
    /// </summary>
    [JsonPropertyName("hardwareId")]
    public string HardwareId { get; set; } = string.Empty;

    /// <summary>Дата выпуска лицензии (UTC).</summary>
    [JsonPropertyName("issuedAt")]
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Дата истечения (UTC). null — бессрочная.</summary>
    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Тип лицензии: "trial", "standard", "pro", "enterprise".</summary>
    [JsonPropertyName("edition")]
    public string Edition { get; set; } = "standard";

    /// <summary>Свободный комментарий издателя.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>
    /// Подпись (Base64) — ECDsa-подпись canonical-представления лицензии
    /// без этого поля. Заполняется LicenseService при проверке/выпуске.
    /// </summary>
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    /// <summary>Истекла ли лицензия по сроку.</summary>
    [JsonIgnore]
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;

    /// <summary>Дней до истечения. null — бессрочная.</summary>
    [JsonIgnore]
    public int? DaysUntilExpiry => ExpiresAt.HasValue
        ? (int?)Math.Max(0, (ExpiresAt.Value - DateTime.UtcNow).TotalDays)
        : null;
}