namespace WorkTimer.Core.Models;

public class Heartbeat
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public DateTime Timestamp { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
