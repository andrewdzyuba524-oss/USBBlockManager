using System.Text.Json.Serialization;

namespace USBBlockManager.Ipc;

/// <summary>Запрос от GUI к службе.</summary>
public class IpcRequest
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public string? Payload { get; set; }
}

/// <summary>Ответ службы GUI.</summary>
public class IpcResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }
}

/// <summary>Константы протокола.</summary>
public static class IpcConstants
{
    public const string PipeName = "usbblockmanager";
    public const string ServerName = "."; // local
}