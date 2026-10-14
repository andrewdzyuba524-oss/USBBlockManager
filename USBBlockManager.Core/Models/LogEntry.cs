namespace USBBlockManager.Models;

public enum LogLevel
{
    Info,
    Warning,
    Error,
    Security,
}

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public LogLevel Level { get; set; } = LogLevel.Info;
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? User { get; set; }
    public string? DeviceIdentity { get; set; }
}