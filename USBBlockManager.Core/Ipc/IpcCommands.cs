namespace USBBlockManager.Ipc;

/// <summary>Имена команд.</summary>
public static class IpcCommands
{
    public const string Ping = "ping";
    public const string Status = "status";
    public const string ListDevices = "list-devices";
    public const string ListWhitelist = "list-whitelist";
    public const string ApplyWhitelist = "apply-whitelist";
    public const string AddToWhitelist = "add-to-whitelist";
    public const string RemoveFromWhitelist = "remove-from-whitelist";
    public const string BlockAll = "block-all";
    public const string UnblockAll = "unblock-all";

    // Новое для Этапа 4:
    public const string GetPending = "get-pending";
    public const string ApprovePending = "approve-pending";
    public const string RejectPending = "reject-pending";
    public const string SetAddMode = "set-add-mode";         // payload: "true" / "false"
    public const string GetAddMode = "get-add-mode";
}