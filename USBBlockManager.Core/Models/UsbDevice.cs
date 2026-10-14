namespace USBBlockManager.Models;

/// <summary>
/// USB-накопитель, обнаруженный в системе.
/// </summary>
public class UsbDevice
{
    /// <summary>Vendor ID, 4 hex-символа, например "0951".</summary>
    public string Vid { get; set; } = string.Empty;

    /// <summary>Product ID, 4 hex-символа, например "1666".</summary>
    public string Pid { get; set; } = string.Empty;

    /// <summary>Серийный номер — уникален для конкретного экземпляра.</summary>
    public string Serial { get; set; } = string.Empty;

    /// <summary>Человекочитаемое имя, например "Kingston DataTraveler".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Буква диска, если присвоена, например "E:".</summary>
    public string? DriveLetter { get; set; }

    /// <summary>Время обнаружения (UTC).</summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Уникальный строковый ключ для сопоставления с белым списком.</summary>
    public string IdentityKey => $"{Vid}:{Pid}:{Serial}";
}