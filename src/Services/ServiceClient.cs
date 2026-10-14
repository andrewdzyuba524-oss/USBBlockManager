using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using USBBlockManager.Ipc;
using USBBlockManager.Models;

namespace USBBlockManager.Services;

/// <summary>
/// Клиент для общения со службой USBBlockService через Named Pipe.
/// </summary>
public class ServiceClient
{
    private const int ConnectTimeoutMs = 3000;

    /// <summary>Проверить, доступна ли служба.</summary>
    public bool IsServiceAvailable()
    {
        try
        {
            var response = SendCommand(IpcCommands.Ping);
            return response is { Success: true };
        }
        catch
        {
            return false;
        }
    }

    public IpcResponse SendCommand(string command, string? payload = null)
    {
        using var pipe = new NamedPipeClientStream(
            IpcConstants.ServerName,
            IpcConstants.PipeName,
            PipeDirection.InOut,
            PipeOptions.None);

        pipe.Connect(ConnectTimeoutMs);

        // leaveOpen: true — StreamReader/StreamWriter не закрывают pipe при Dispose.
        // Иначе writer.Dispose() закроет pipe, а reader.Dispose() получит "closed pipe".
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        var request = new IpcRequest { Command = command, Payload = payload };
        var json = JsonSerializer.Serialize(request);

        writer.WriteLine(json);
        writer.Flush();

        var responseLine = reader.ReadLine();
        if (string.IsNullOrEmpty(responseLine))
            return new IpcResponse { Success = false, Message = "Empty response" };

        var response = JsonSerializer.Deserialize<IpcResponse>(responseLine);
        return response ?? new IpcResponse { Success = false, Message = "Invalid response" };
    }

    // ----- Обёртки команд -----

    public IpcResponse GetStatus() => SendCommand(IpcCommands.Status);
    public IpcResponse ListDevices() => SendCommand(IpcCommands.ListDevices);
    public IpcResponse ListWhitelist() => SendCommand(IpcCommands.ListWhitelist);
    public IpcResponse ApplyWhitelist() => SendCommand(IpcCommands.ApplyWhitelist);
    public IpcResponse BlockAll() => SendCommand(IpcCommands.BlockAll);
    public IpcResponse UnblockAll() => SendCommand(IpcCommands.UnblockAll);
    public IpcResponse SetAddMode(bool enabled)
    => SendCommand(IpcCommands.SetAddMode, enabled ? "true" : "false");

    public IpcResponse GetAddMode()
        => SendCommand(IpcCommands.GetAddMode);
    public IpcResponse AddToWhitelist(WhitelistEntry entry)
    {
        var payload = JsonSerializer.Serialize(entry);
        return SendCommand(IpcCommands.AddToWhitelist, payload);
    }

    public IpcResponse RemoveFromWhitelist(string identityKey)
        => SendCommand(IpcCommands.RemoveFromWhitelist, identityKey);

    // ----- Этап 4: диалог авторизации -----

    public IpcResponse GetPending() => SendCommand(IpcCommands.GetPending);

    public IpcResponse ApprovePending(string identityKey, string password, bool addToWhitelist)
    {
        var payload = JsonSerializer.Serialize(new
        {
            identityKey,
            password,
            addToWhitelist,
        });
        return SendCommand(IpcCommands.ApprovePending, payload);
    }

    public IpcResponse RejectPending(string identityKey)
        => SendCommand(IpcCommands.RejectPending, identityKey);
}