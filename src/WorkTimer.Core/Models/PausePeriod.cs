namespace WorkTimer.Core.Models;

public class PausePeriod
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public DateTime PauseStart { get; set; }
    public DateTime? PauseEnd { get; set; }
    public long? DurationSeconds { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
