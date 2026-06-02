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
        _timer = new System.Timers.Timer(30_000); // 30秒
        _timer.Elapsed += OnElapsed;
    }

    public void Start(long sessionId)
    {
        _sessionId = sessionId;
        _running = true;
        // 立即写入第一条
        _db.InsertHeartbeatAsync(_sessionId, DateTime.UtcNow).ConfigureAwait(false);
        _timer.Start();
    }

    public void Stop()
    {
        _running = false;
        _timer.Stop();
    }

    private void OnElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!_running) return;
        _db.InsertHeartbeatAsync(_sessionId, DateTime.UtcNow).ConfigureAwait(false);
    }

    public void Dispose()
    {
        Stop();
        _timer.Elapsed -= OnElapsed;
        _timer.Dispose();
    }
}
