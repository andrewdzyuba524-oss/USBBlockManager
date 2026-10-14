namespace USBBlockManager.Ipc;

/// <summary>
/// Запись об устройстве, ожидающем авторизации администратора.
/// </summary>
public class PendingApproval
{
    public string IdentityKey { get; set; } = string.Empty;
    public string PnpId { get; set; } = string.Empty;
    public string Vid { get; set; } = string.Empty;
    public string Pid { get; set; } = string.Empty;
    public string Serial { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}