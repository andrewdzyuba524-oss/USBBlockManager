namespace USBBlockManager.Models;

/// <summary>
/// Запись в белом списке USB-устройств.
/// </summary>
public class WhitelistEntry
{
    public string Vid { get; set; } = string.Empty;
    public string Pid { get; set; } = string.Empty;
    public string Serial { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    public string AddedBy { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public string? Comment { get; set; }

    /// <summary>Последняя известная буква диска (например, "D:").</summary>
    public string? LastDriveLetter { get; set; }

    public string IdentityKey => $"{Vid}:{Pid}:{Serial}";

    public static WhitelistEntry FromDevice(UsbDevice device, string addedBy, string? comment = null)
        => new()
        {
            Vid = device.Vid,
            Pid = device.Pid,
            Serial = device.Serial,
            Label = device.Label,
            AddedBy = addedBy,
            Comment = comment,
            AddedAt = DateTime.UtcNow,
            LastDriveLetter = device.DriveLetter,
        };
}