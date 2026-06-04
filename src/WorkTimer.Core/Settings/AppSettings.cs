namespace WorkTimer.Core.Settings;

public class AppSettings
{
    /// <summary>悬停延迟（秒）</summary>
    public double HoverDelaySeconds { get; set; } = 2.0;

    /// <summary>窗口默认宽度</summary>
    public double WindowWidth { get; set; } = 220;

    /// <summary>窗口默认高度</summary>
    public double WindowHeight { get; set; } = 100;

    /// <summary>默认透明度（穿透态）</summary>
    public double DefaultOpacity { get; set; } = 0.15;

    /// <summary>交互态透明度</summary>
    public double InteractiveOpacity { get; set; } = 0.8;

    /// <summary>开机自启</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>心跳间隔（秒）</summary>
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>轮询间隔（毫秒）</summary>
    public int PollIntervalMs { get; set; } = 200;
}
