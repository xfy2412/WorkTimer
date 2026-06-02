using Microsoft.Data.Sqlite;
using WorkTimer.Core.Models;

namespace WorkTimer.Core.Data;

public class DatabaseService : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DatabaseService()
    {
        var dbDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkTimer");
        Directory.CreateDirectory(dbDir);

        _connection = new SqliteConnection($"Data Source={Path.Combine(dbDir, "worktimer.db")}");
        _connection.Open();

        InitializeTables();
    }

    private void InitializeTables()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                start_time TEXT NOT NULL,
                end_time TEXT,
                total_seconds INTEGER,
                note TEXT,
                created_at TEXT DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS pause_periods (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id INTEGER NOT NULL REFERENCES sessions(id),
                pause_start TEXT NOT NULL,
                pause_end TEXT,
                duration_seconds INTEGER,
                created_at TEXT DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS heartbeats (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id INTEGER NOT NULL REFERENCES sessions(id),
                timestamp TEXT NOT NULL,
                created_at TEXT DEFAULT (datetime('now'))
            );

            CREATE INDEX IF NOT EXISTS idx_sessions_active ON sessions(end_time);
            CREATE INDEX IF NOT EXISTS idx_pause_periods_session ON pause_periods(session_id);
            CREATE INDEX IF NOT EXISTS idx_heartbeats_session ON heartbeats(session_id);
            """;
        cmd.ExecuteNonQuery();
    }

    // ─── Session ────────────────────────────────────────

    public async Task<Session> CreateSessionAsync(Session session)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sessions (start_time, end_time, total_seconds, note)
                VALUES ($start, $end, $total, $note);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$start", session.StartTime.ToString("O"));
            cmd.Parameters.AddWithValue("$end", DBNull.Value);
            cmd.Parameters.AddWithValue("$total", DBNull.Value);
            cmd.Parameters.AddWithValue("$note", DBNull.Value);

            var result = await cmd.ExecuteScalarAsync();
            session.Id = (long)(result ?? 0);
            return session;
        }
        finally { _lock.Release(); }
    }

    public async Task<Session?> GetActiveSessionAsync()
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, start_time, end_time, total_seconds, note, created_at FROM sessions WHERE end_time IS NULL ORDER BY id DESC LIMIT 1";
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return ReadSession(reader);
            return null;
        }
        finally { _lock.Release(); }
    }

    public async Task EndSessionAsync(long sessionId, DateTime endTime, long totalSeconds)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE sessions SET end_time = $end, total_seconds = $total WHERE id = $id";
            cmd.Parameters.AddWithValue("$end", endTime.ToString("O"));
            cmd.Parameters.AddWithValue("$total", totalSeconds);
            cmd.Parameters.AddWithValue("$id", sessionId);
            await cmd.ExecuteNonQueryAsync();
        }
        finally { _lock.Release(); }
    }

    public async Task<List<Session>> GetCompletedSessionsAsync(DateTime from, DateTime to)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, start_time, end_time, total_seconds, note, created_at FROM sessions WHERE end_time IS NOT NULL AND start_time >= $from AND start_time <= $to ORDER BY start_time DESC";
            cmd.Parameters.AddWithValue("$from", from.ToString("O"));
            cmd.Parameters.AddWithValue("$to", to.ToString("O"));

            var sessions = new List<Session>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                sessions.Add(ReadSession(reader));
            return sessions;
        }
        finally { _lock.Release(); }
    }

    // ─── PausePeriod ────────────────────────────────────

    public async Task<PausePeriod> InsertPausePeriodAsync(PausePeriod period)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO pause_periods (session_id, pause_start, pause_end, duration_seconds)
                VALUES ($sid, $start, $end, $dur);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$sid", period.SessionId);
            cmd.Parameters.AddWithValue("$start", period.PauseStart.ToString("O"));
            cmd.Parameters.AddWithValue("$end", period.PauseEnd?.ToString("O") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$dur", period.DurationSeconds ?? (object)DBNull.Value);

            var result = await cmd.ExecuteScalarAsync();
            period.Id = (long)(result ?? 0);
            return period;
        }
        finally { _lock.Release(); }
    }

    public async Task UpdatePauseEndAsync(long periodId, DateTime pauseEnd, long durationSeconds)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE pause_periods SET pause_end = $end, duration_seconds = $dur WHERE id = $id";
            cmd.Parameters.AddWithValue("$end", pauseEnd.ToString("O"));
            cmd.Parameters.AddWithValue("$dur", durationSeconds);
            cmd.Parameters.AddWithValue("$id", periodId);
            await cmd.ExecuteNonQueryAsync();
        }
        finally { _lock.Release(); }
    }

    public async Task<List<PausePeriod>> GetPausePeriodsAsync(long sessionId)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, session_id, pause_start, pause_end, duration_seconds, created_at FROM pause_periods WHERE session_id = $sid ORDER BY id ASC";
            cmd.Parameters.AddWithValue("$sid", sessionId);

            var periods = new List<PausePeriod>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                periods.Add(ReadPausePeriod(reader));
            return periods;
        }
        finally { _lock.Release(); }
    }

    // ─── Heartbeat ──────────────────────────────────────

    public async Task InsertHeartbeatAsync(long sessionId, DateTime timestamp)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT INTO heartbeats (session_id, timestamp) VALUES ($sid, $ts)";
            cmd.Parameters.AddWithValue("$sid", sessionId);
            cmd.Parameters.AddWithValue("$ts", timestamp.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }
        finally { _lock.Release(); }
    }

    public async Task<Heartbeat?> GetLastHeartbeatAsync(long sessionId)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, session_id, timestamp, created_at FROM heartbeats WHERE session_id = $sid ORDER BY id DESC LIMIT 1";
            cmd.Parameters.AddWithValue("$sid", sessionId);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return ReadHeartbeat(reader);
            return null;
        }
        finally { _lock.Release(); }
    }

    // ─── Readers ────────────────────────────────────────

    private static Session ReadSession(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        StartTime = DateTime.Parse(r.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
        EndTime = r.IsDBNull(2) ? null : DateTime.Parse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
        TotalSeconds = r.IsDBNull(3) ? null : r.GetInt64(3),
        Note = r.IsDBNull(4) ? null : r.GetString(4),
        CreatedAt = DateTime.Parse(r.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
    };

    private static PausePeriod ReadPausePeriod(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        SessionId = r.GetInt64(1),
        PauseStart = DateTime.Parse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
        PauseEnd = r.IsDBNull(3) ? null : DateTime.Parse(r.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
        DurationSeconds = r.IsDBNull(4) ? null : r.GetInt64(4),
        CreatedAt = DateTime.Parse(r.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
    };

    private static Heartbeat ReadHeartbeat(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        SessionId = r.GetInt64(1),
        Timestamp = DateTime.Parse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
        CreatedAt = DateTime.Parse(r.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
    };

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
        _lock?.Dispose();
    }
}
