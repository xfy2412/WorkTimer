using System.Windows.Threading;
using WorkTimer.Core.Services;

namespace WorkTimer.Overlay.Services;

public class TimerService : IDisposable
{
    private readonly SessionManager _sessionManager;
    private readonly DispatcherTimer _timer;
    private int _tickCount;

    public event Action<TimeSpan>? Tick;
    public event Action<bool>? PauseStateChanged;

    public TimerService(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        _tickCount = 0;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_sessionManager.CurrentSession == null) return;

        _tickCount++;
        var elapsed = _sessionManager.GetElapsed(DateTime.UtcNow);
        Tick?.Invoke(elapsed);
    }

    public void NotifyPauseState(bool isPaused)
    {
        PauseStateChanged?.Invoke(isPaused);
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= OnTick;
    }
}
