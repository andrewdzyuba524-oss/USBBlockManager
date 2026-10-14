using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using USBBlockManager.Ipc;

namespace USBBlockService;

/// <summary>
/// Named Pipe сервер для связи GUI со службой.
/// Обрабатывает команды от GUI построчно (JSON in, JSON out).
/// </summary>
public class PipeServer
{
    private readonly Action<string> _log;
    private readonly Func<IpcRequest, IpcResponse> _handler;
    private CancellationTokenSource? _cts;

    public PipeServer(Action<string> log, Func<IpcRequest, IpcResponse> handler)
    {
        _log = log;
        _handler = handler;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoop(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
    }

    private async Task ListenLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreatePipeServer();

                await server.WaitForConnectionAsync(token);

                var serverLocal = server;
                server = null; // предотвращаем Dispose в finally

                _ = Task.Run(() => HandleClient(serverLocal, token), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log($"PipeServer listen error: {ex.Message}");
                await Task.Delay(1000, token);
            }
            finally
            {
                try { server?.Dispose(); } catch { }
            }
        }
    }

    private async Task HandleClient(NamedPipeServerStream server, CancellationToken token)
    {
        try
        {
            var buffer = new byte[8192];
            var lineBuffer = new StringBuilder();

            while (server.IsConnected && !token.IsCancellationRequested)
            {
                int bytesRead = await server.ReadAsync(buffer, 0, buffer.Length, token);
                if (bytesRead <= 0)
                    break;

                var chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                lineBuffer.Append(chunk);

                string text = lineBuffer.ToString();
                int newlineIndex;
                while ((newlineIndex = text.IndexOf('\n')) >= 0)
                {
                    var line = text.Substring(0, newlineIndex).TrimEnd('\r');
                    text = text.Substring(newlineIndex + 1);

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var response = ProcessLine(line);
                    var json = JsonSerializer.Serialize(response) + "\n";
                    var bytes = Encoding.UTF8.GetBytes(json);

                    try
                    {
                        await server.WriteAsync(bytes, 0, bytes.Length, token);
                        await server.FlushAsync(token);
                    }
                    catch (Exception ex)
                    {
                        // Клиент отключился в момент записи — это нормальная ситуация,
                        // не считаем её фатальной ошибкой.
                        _log($"PipeServer write error: {ex.Message}");
                        return;
                    }
                }

                lineBuffer.Clear();
                lineBuffer.Append(text);
            }
        }
        catch (OperationCanceledException)
        {
            // Нормальное завершение
        }
        catch (Exception ex)
        {
            _log($"PipeServer client error: {ex.Message}");
        }
        finally
        {
            try { server.Disconnect(); } catch { }
            try { server.Dispose(); } catch { }
        }
    }

    private IpcResponse ProcessLine(string line)
    {
        try
        {
            var request = JsonSerializer.Deserialize<IpcRequest>(line);
            if (request is null)
                return new IpcResponse { Success = false, Message = "Invalid request" };

            return _handler(request);
        }
        catch (Exception ex)
        {
            return new IpcResponse { Success = false, Message = ex.Message };
        }
    }

    /// <summary>
    /// Создаёт NamedPipeServerStream с разрешением доступа для всех пользователей.
    /// Без этого клиент от обычного пользователя получит Access Denied.
    /// </summary>
    private static NamedPipeServerStream CreatePipeServer()
    {
        var security = new PipeSecurity();

        // Everyone — полный доступ
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // Авторизованные пользователи — чтение/запись
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        // Администраторы — полный доступ
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            IpcConstants.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }
}