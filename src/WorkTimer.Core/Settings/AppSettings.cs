using System.ComponentModel;

namespace WorkTimer.Core.Settings;

public class AppSettings : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private double _hoverDelaySeconds = 1.0;
    private double _windowWidth = 220;
    private double _windowHeight = 100;
    private double _defaultOpacity = 0.15;
    private double _interactiveOpacity = 0.8;
    private bool _autoStart = true;
    private int _heartbeatIntervalSeconds = 30;
    private int _pollIntervalMs = 200;

    public double HoverDelaySeconds
    {
        get => _hoverDelaySeconds;
        set { _hoverDelaySeconds = value; PropertyChanged?.Invoke(this, new(nameof(HoverDelaySeconds))); }
    }

    public double WindowWidth
    {
        get => _windowWidth;
        set { _windowWidth = value; PropertyChanged?.Invoke(this, new(nameof(WindowWidth))); }
    }

    public double WindowHeight
    {
        get => _windowHeight;
        set { _windowHeight = value; PropertyChanged?.Invoke(this, new(nameof(WindowHeight))); }
    }

    public double DefaultOpacity
    {
        get => _defaultOpacity;
        set { _defaultOpacity = value; PropertyChanged?.Invoke(this, new(nameof(DefaultOpacity))); }
    }

    public double InteractiveOpacity
    {
        get => _interactiveOpacity;
        set { _interactiveOpacity = value; PropertyChanged?.Invoke(this, new(nameof(InteractiveOpacity))); }
    }

    public bool AutoStart
    {
        get => _autoStart;
        set { _autoStart = value; PropertyChanged?.Invoke(this, new(nameof(AutoStart))); }
    }

    public int HeartbeatIntervalSeconds
    {
        get => _heartbeatIntervalSeconds;
        set { _heartbeatIntervalSeconds = value; PropertyChanged?.Invoke(this, new(nameof(HeartbeatIntervalSeconds))); }
    }

    public int PollIntervalMs
    {
        get => _pollIntervalMs;
        set { _pollIntervalMs = value; PropertyChanged?.Invoke(this, new(nameof(PollIntervalMs))); }
    }
}
