using WorkTimer.Core;
using WorkTimer.Core.Data;

namespace WorkTimer.Overlay.Services;

public class HeartbeatService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly System.Timers.Timer _timer;
    private long _sessionId;
    private bool _running;

    public HeartbeatService(DatabaseService db)
    {
        _db = db;
        _timer = new System.Timers.Timer(30_000);
        _timer.Elapsed += OnElapsed;
    }

    public void Start(long sessionId)
    {
        _sessionId = sessionId;
        _running = true;
        _ = WriteHeartbeatAsync(); // 立即写入第一条
        _timer.Start();
    }

    public void Stop()
    {
        _running = false;
        _timer.Stop();
    }

    private async void OnElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!_running) return;
        await WriteHeartbeatAsync();
    }

    private async Task WriteHeartbeatAsync()
    {
        try
        {
            await _db.InsertHeartbeatAsync(_sessionId, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            Logger.Write($"[Heartbeat] 写入失败: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        _timer.Elapsed -= OnElapsed;
        _timer.Dispose();
    }
}
