using System.Text.Json.Serialization;

namespace USBBlockManager.Licensing;

/// <summary>
/// Информация о пробном периоде.
/// Дублируется в реестр HKLM и подписывается HMAC.
/// </summary>
public class TrialInfo
{
    /// <summary>Когда пользователь впервые запустил программу (UTC).</summary>
    [JsonPropertyName("firstRunAt")]
    public DateTime FirstRunAt { get; set; } = DateTime.UtcNow;

    /// <summary>Длительность пробного периода в днях.</summary>
    [JsonPropertyName("trialDays")]
    public int TrialDays { get; set; } = 7;

    /// <summary>Последний известный запуск (UTC). Защита от отката часов.</summary>
    [JsonPropertyName("lastRunAt")]
    public DateTime LastRunAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// HMAC-SHA256 подпись содержимого (без этого поля).
    /// Защищает от ручного редактирования.
    /// </summary>
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    /// <summary>Истёк ли пробный период.</summary>
    [JsonIgnore]
    public bool IsExpired => DateTime.UtcNow > FirstRunAt.AddDays(TrialDays);

    /// <summary>Сколько дней осталось.</summary>
    [JsonIgnore]
    public int DaysRemaining => Math.Max(0,
        (int)Math.Ceiling((FirstRunAt.AddDays(TrialDays) - DateTime.UtcNow).TotalDays));

    /// <summary>Дата окончания пробного периода (UTC).</summary>
    [JsonIgnore]
    public DateTime ExpiresAt => FirstRunAt.AddDays(TrialDays);
}