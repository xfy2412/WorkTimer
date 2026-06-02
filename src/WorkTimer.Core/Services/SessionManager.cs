using WorkTimer.Core.Data;
using WorkTimer.Core.Models;

namespace WorkTimer.Core.Services;

public class SessionManager
{
    private readonly DatabaseService _db;
    private int _logThrottle;

    public SessionManager(DatabaseService db)
    {
        _db = db;
    }

    public Session? CurrentSession { get; private set; }
    public long? CurrentPausePeriodId { get; private set; }
    public List<PausePeriod> PausePeriods { get; private set; } = [];

    /// <summary>
    /// 检查是否有活跃会话。返回 (session, 是否需要询问用户是否续接)。
    /// </summary>
    public async Task<(Session? session, bool needsContinuationPrompt)> StartupCheckAsync()
    {
        var active = await _db.GetActiveSessionAsync();

        if (active == null)
        {
            Logger.Write("[SessionManager] 无活跃会话，创建新会话");
            CurrentSession = await _db.CreateSessionAsync(new Session
            {
                StartTime = DateTime.UtcNow
            });
            PausePeriods = [];
            return (CurrentSession, false);
        }

        Logger.Write($"[SessionManager] 发现活跃会话 #{active.Id}，StartTime={active.StartTime:O}");
        CurrentSession = active;
        PausePeriods = await _db.GetPausePeriodsAsync(active.Id);
        Logger.Write($"[SessionManager] 已加载 {PausePeriods.Count} 条暂停记录");
        return (active, true);
    }

    /// <summary>
    /// 用户选择续接：将关机时段写入 pause_periods
    /// </summary>
    public async Task ContinueSessionAsync()
    {
        if (CurrentSession == null) return;

        Logger.Write($"[SessionManager] ═══ 续接会话 #{CurrentSession.Id} ═══");
        Logger.Write($"[SessionManager]   会话起点: {CurrentSession.StartTime:yyyy-MM-dd HH:mm:ss}");

        // ① 关闭所有未完成的暂停段（上一个会话退出时可能还在暂停中）
        var unfinished = PausePeriods.Where(p => !p.PauseEnd.HasValue).ToList();
        if (unfinished.Count > 0)
        {
            Logger.Write($"[SessionManager]   发现 {unfinished.Count} 个未完成暂停段，正在关闭...");
            foreach (var p in unfinished)
            {
                var end = DateTime.UtcNow;
                var dur = (long)(end - p.PauseStart).TotalSeconds;
                Logger.Write($"[SessionManager]     暂停段 #{p.Id}: {p.PauseStart:HH:mm:ss} → {end:HH:mm:ss} = {dur}s");
                p.PauseEnd = end;
                p.DurationSeconds = dur;
                await _db.UpdatePauseEndAsync(p.Id, end, dur);
            }
        }

        // ② 关机间隔作为新的暂停段
        var lastHeartbeat = await _db.GetLastHeartbeatAsync(CurrentSession.Id);
        var gapStart = lastHeartbeat?.Timestamp ?? CurrentSession.StartTime;
        var gapEnd = DateTime.UtcNow;
        var duration = (long)(gapEnd - gapStart).TotalSeconds;
        Logger.Write($"[SessionManager]   关机间隔: {gapStart:HH:mm:ss} → {gapEnd:HH:mm:ss} = {duration}s");

        if (duration > 0)
        {
            await _db.InsertPausePeriodAsync(new PausePeriod
            {
                SessionId = CurrentSession.Id,
                PauseStart = gapStart,
                PauseEnd = gapEnd,
                DurationSeconds = duration
            });
        }

        // ③ 重新加载全部暂停段并打印明细
        PausePeriods = await _db.GetPausePeriodsAsync(CurrentSession.Id);
        Logger.Write($"[SessionManager]   续接后共 {PausePeriods.Count} 条暂停段:");
        foreach (var p in PausePeriods)
            Logger.Write($"[SessionManager]     [#{p.Id}] {p.PauseStart:HH:mm:ss} → {p.PauseEnd:HH:mm:ss}  dur={p.DurationSeconds ?? 0}s");

        CurrentPausePeriodId = null;
    }

    /// <summary>
    /// 用户选择重置：归档旧会话，开启新会话
    /// </summary>
    public async Task ResetSessionAsync()
    {
        if (CurrentSession == null) return;

        var lastHeartbeat = await _db.GetLastHeartbeatAsync(CurrentSession.Id);
        var endTime = lastHeartbeat?.Timestamp ?? DateTime.UtcNow;
        var elapsed = GetElapsed(endTime);

        await _db.EndSessionAsync(CurrentSession.Id, endTime, (long)elapsed.TotalSeconds);

        CurrentSession = await _db.CreateSessionAsync(new Session
        {
            StartTime = DateTime.UtcNow
        });
        PausePeriods = [];
        CurrentPausePeriodId = null;
    }

    /// <summary>
    /// 暂停计时
    /// </summary>
    public async Task PauseAsync()
    {
        if (CurrentSession == null || CurrentPausePeriodId != null) return;

        Logger.Write("[SessionManager] ── 暂停 ──");
        var period = await _db.InsertPausePeriodAsync(new PausePeriod
        {
            SessionId = CurrentSession.Id,
            PauseStart = DateTime.UtcNow
        });
        CurrentPausePeriodId = period.Id;
        PausePeriods.Add(period);
    }

    public async Task ResumeAsync()
    {
        if (CurrentSession == null || CurrentPausePeriodId == null) return;

        var now = DateTime.UtcNow;
        var period = PausePeriods.FirstOrDefault(p => p.Id == CurrentPausePeriodId);
        if (period != null)
        {
            var duration = (long)(now - period.PauseStart).TotalSeconds;
            Logger.Write($"[SessionManager] ── 恢复（暂停了 {duration}s）──");
            period.PauseEnd = now;
            period.DurationSeconds = duration;
            await _db.UpdatePauseEndAsync(CurrentPausePeriodId.Value, now, duration);
        }

        CurrentPausePeriodId = null;
    }

    /// <summary>
    /// 获取当前已用时长（实时计算，无累积误差）
    /// </summary>
    public TimeSpan GetElapsed(DateTime now)
    {
        if (CurrentSession == null) return TimeSpan.Zero;

        var totalPause = TimeSpan.Zero;
        foreach (var p in PausePeriods)
        {
            if (p.PauseEnd.HasValue && p.DurationSeconds.HasValue)
            {
                totalPause += TimeSpan.FromSeconds(p.DurationSeconds.Value);
            }
            else if (!p.PauseEnd.HasValue)
            {
                totalPause += now - p.PauseStart;
            }
        }

        var elapsed = now - CurrentSession.StartTime - totalPause;
        var result = elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
        if (++_logThrottle % 10 == 0)
        {
            var detail = string.Join(" ", PausePeriods.Select((p, i) =>
                $"P{i}=[{(p.PauseEnd.HasValue ? $"{p.DurationSeconds ?? 0}s" : "ACTIVE")}]"));
            Logger.Write($"[SM] ⏱ {result.TotalSeconds,6:F0}s | now={now:HH:mm:ss} start={CurrentSession.StartTime:HH:mm:ss} | pause+{totalPause.TotalSeconds,5:F0}s ({detail}) | paused={IsPaused}");
        }
        return result;
    }

    /// <summary>
    /// 关机/退出时归档会话
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (CurrentSession == null) return;

        // 如果正在暂停中，先结束暂停
        if (CurrentPausePeriodId != null)
            await ResumeAsync();

        var now = DateTime.UtcNow;
        var elapsed = GetElapsed(now);
        await _db.EndSessionAsync(CurrentSession.Id, now, (long)elapsed.TotalSeconds);

        CurrentSession = null;
    }

    /// <summary>
    /// 是否处于暂停状态
    /// </summary>
    public bool IsPaused => CurrentPausePeriodId != null;
}
