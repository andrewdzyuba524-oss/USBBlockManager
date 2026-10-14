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
    private const int ResponseTimeoutMs = 5000;

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

        using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8);

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

    /// <summary>Удобные обёртки.</summary>
    public IpcResponse GetStatus() => SendCommand(IpcCommands.Status);
    public IpcResponse ListDevices() => SendCommand(IpcCommands.ListDevices);
    public IpcResponse ListWhitelist() => SendCommand(IpcCommands.ListWhitelist);
    public IpcResponse ApplyWhitelist() => SendCommand(IpcCommands.ApplyWhitelist);
    public IpcResponse BlockAll() => SendCommand(IpcCommands.BlockAll);
    public IpcResponse UnblockAll() => SendCommand(IpcCommands.UnblockAll);

    public IpcResponse AddToWhitelist(WhitelistEntry entry)
    {
        var payload = JsonSerializer.Serialize(entry);
        return SendCommand(IpcCommands.AddToWhitelist, payload);
    }
    


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



    public IpcResponse RemoveFromWhitelist(string identityKey)
        => SendCommand(IpcCommands.RemoveFromWhitelist, identityKey);
}