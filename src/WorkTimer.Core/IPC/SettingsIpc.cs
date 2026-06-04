using System.IO.Pipes;
using System.Text;

namespace WorkTimer.Core.IPC;

public class IpcMessage
{
    public string Type { get; set; } = "";     // "set" | "reload"
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public static class SettingsIpc
{
    private const string PipeName = "WorkTimer-Settings-IPC";

    public delegate void MessageHandler(IpcMessage msg);

    /// <summary>在服务端（Overlay）启动时调用，开始监听</summary>
    public static async Task StartServerAsync(MessageHandler handler, CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Message);

                await server.WaitForConnectionAsync(ct);

                var buffer = new byte[1024];
                var read = await server.ReadAsync(buffer, ct);
                var raw = Encoding.UTF8.GetString(buffer, 0, read);

                var parts = raw.Split(':', 3);
                if (parts.Length >= 1)
                {
                    handler(new IpcMessage
                    {
                        Type = parts[0],
                        Key = parts.Length >= 2 ? parts[1] : "",
                        Value = parts.Length >= 3 ? parts[2] : ""
                    });
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    /// <summary>实时推送单条配置变更</summary>
    public static async Task NotifySetAsync(string key, string value)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            await client.ConnectAsync(500);
            var data = Encoding.UTF8.GetBytes($"set:{key}:{value}");
            await client.WriteAsync(data);
        }
        catch { }
    }

    /// <summary>通知 Overlay 从文件重载全部配置</summary>
    public static async Task NotifyReloadAsync()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            await client.ConnectAsync(1000);
            var data = Encoding.UTF8.GetBytes("reload");
            await client.WriteAsync(data);
        }
        catch { }
    }
}
