using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using WorkTimer.Core;
using WorkTimer.Core.Data;
using WorkTimer.Core.Services;
using WorkTimer.Core.Settings;
using WorkTimer.Overlay.Services;

namespace WorkTimer.Overlay;

public partial class MainWindow : Window
{
    // ─── Win32 ──────────────────────────────────────────
    private const int WM_NCHITTEST = 0x84;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTTRANSPARENT = -1;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    // ─── Colors ─────────────────────────────────────────

    // ─── Fields ─────────────────────────────────────────
    private readonly DatabaseService _db;
    private readonly SessionManager _sessionManager;
    private readonly TimerService _timerService;
    private readonly HeartbeatService _heartbeatService;
    private readonly ConfigService _configService;
    private readonly SettingsManager _settingsManager;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _blinkTimer;
    private readonly TaskbarIcon _trayIcon;
    private nint _windowHandle;
    private bool _inPassthrough = true;
    private bool _isMouseOver;
    private bool _interactiveMode;
    private int _hoverTickCount;
    private int _hoverThreshold;
    private bool _showContinuationPrompt;
    private bool _blinkState;

    public MainWindow(DatabaseService db, SessionManager sessionManager)
    {
        InitializeComponent();

        _db = db;
        _sessionManager = sessionManager;
        _timerService = new TimerService(sessionManager);
        _heartbeatService = new HeartbeatService(db);
        _configService = new ConfigService();
        _settingsManager = new SettingsManager();

        _timerService.Tick += OnTimerTick;
        _timerService.PauseStateChanged += OnPauseStateChanged;
        _hoverThreshold = (int)(_settingsManager.Data.HoverDelaySeconds * 1000 / 200);

        _pollTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _pollTimer.Tick += PollTimer_Tick;

        _blinkTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _blinkTimer.Tick += BlinkTimer_Tick;

        _trayIcon = new TaskbarIcon
        {
            Icon = CreateAppIcon(),
            ToolTipText = "WorkTimer - 工作中",
            Visibility = Visibility.Visible,
        };
        _trayIcon.TrayMouseDoubleClick += (_, _) => TogglePassthrough();
        _trayIcon.ContextMenu = CreateTrayMenu();

        Loaded += MainWindow_Loaded;
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) =>
        {
            _configService.Save();
            _trayIcon.Dispose();
        };

        RestorePosition();
    }

    // ─── 初始化 ─────────────────────────────────────────
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        if (_windowHandle != nint.Zero)
        {
            var source = HwndSource.FromHwnd(_windowHandle);
            source?.AddHook(WndProc);
            Opacity = 0.15;
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Logger.Write("[MainWindow] 窗口加载完成");
        var (session, needsPrompt) = await _sessionManager.StartupCheckAsync();

        if (session != null)
            _heartbeatService.Start(session.Id);

        if (needsPrompt)
        {
            _showContinuationPrompt = true;
            ShowContinuationPrompt();
        }
        else
        {
            _pollTimer.Start();
            _timerService.Start();
        }
    }

    // ─── WM_NCHITTEST（支持拖拽手柄穿透点击）───────────
    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WM_NCHITTEST) return nint.Zero;

        var x = (short)(lParam.ToInt64() & 0xFFFF);
        var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

        GetWindowRect(_windowHandle, out RECT win);
        var localY = y - win.Top;

        // 拖拽手柄区（顶部 20px）始终返回 HTCAPTION，穿透模式下也可拖拽
        if (localY >= 0 && localY <= 20)
        {
            handled = true;
            return (nint)HTCAPTION;
        }

        // 其余区域：穿透模式返回 HTTRANSPARENT，交互模式返回 HTCLIENT
        handled = true;
        return (nint)(_inPassthrough ? HTTRANSPARENT : HTCLIENT);
    }

    // ─── 鼠标轮询（每 200ms）─────────────────────────────
    private void PollTimer_Tick(object? sender, EventArgs e)
    {
        if (_showContinuationPrompt) return;
        if (_windowHandle == nint.Zero) return;

        GetCursorPos(out POINT cursor);
        GetWindowRect(_windowHandle, out RECT win);

        bool over = cursor.X >= win.Left && cursor.X <= win.Right &&
                    cursor.Y >= win.Top && cursor.Y <= win.Bottom;

        if (over)
        {
            if (!_isMouseOver)
            {
                _isMouseOver = true;
                _hoverTickCount = 0;
                _interactiveMode = false;
            }

            if (!_interactiveMode)
            {
                _hoverTickCount++;
                if (_hoverTickCount >= _hoverThreshold)
                {
                    _interactiveMode = true;
                    _inPassthrough = false;
                    BeginStoryboard((System.Windows.Media.Animation.Storyboard)FindResource("FadeIn"));
                }
            }
        }
        else
        {
            if (_isMouseOver)
            {
                _isMouseOver = false;
                _hoverTickCount = 0;
                _interactiveMode = false;
                _inPassthrough = true;
                BeginStoryboard((System.Windows.Media.Animation.Storyboard)FindResource("FadeOut"));
            }
        }
    }

    // ─── 琥珀色闪烁 ──────────────────────────────────────
    private static readonly SolidColorBrush DarkBg = new(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D));
    private static readonly SolidColorBrush AmberBg = new(System.Windows.Media.Color.FromRgb(0xCC, 0x80, 0x00));

    private static readonly SolidColorBrush DarkLine = new(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44));
    private static readonly SolidColorBrush AmberLine = new(System.Windows.Media.Color.FromRgb(0xE0, 0xA0, 0x30));

    private void BlinkTimer_Tick(object? sender, EventArgs e)
    {
        _blinkState = !_blinkState;
        CardBorder.Background = _blinkState ? AmberBg : DarkBg;
        SeparatorLine.Background = _blinkState ? AmberLine : DarkLine;
    }

    private void SetAmber(bool blink)
    {
        if (blink)
        {
            _blinkState = true;
            CardBorder.Background = AmberBg;
            SeparatorLine.Background = AmberLine;
            _blinkTimer.Start();
        }
        else
        {
            _blinkTimer.Stop();
            CardBorder.Background = AmberBg;
            SeparatorLine.Background = AmberLine;
        }
    }

    private void ResetColors()
    {
        _blinkTimer.Stop();
        CardBorder.Background = DarkBg;
        SeparatorLine.Background = DarkLine;
        HamburgerIcon.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66));
    }

    // ─── 续接提示 ───────────────────────────────────────
    private void ShowContinuationPrompt()
    {
        var elapsed = _sessionManager.GetElapsed(DateTime.UtcNow);
        TimeText.Text = elapsed.TotalHours >= 1
            ? $"上次 {(int)elapsed.TotalHours}h{elapsed.Minutes}m"
            : $"上次 {elapsed.Minutes}m{elapsed.Seconds}s";
        StatusText.Text = "左键续接 · 右键重置";

        SetAmber(blink: false); // 琥珀色常亮
        _inPassthrough = false;
        Opacity = 0.85;

        var countdown = 10;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            countdown--;
            if (countdown <= 0) { timer.Stop(); await ContinueSession(false); }
        };
        timer.Start();

        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!_showContinuationPrompt) return;
            e.Handled = true;
            timer.Stop();
            _ = ContinueSession(true);
        };
        PreviewMouseRightButtonDown += (_, e) =>
        {
            if (!_showContinuationPrompt) return;
            e.Handled = true;
            timer.Stop();
            _ = ContinueSession(false);
        };
    }

    private async Task ContinueSession(bool continueLast)
    {
        Logger.Write($"[MainWindow] 续接选择: {(continueLast ? "继续" : "重置")}");
        _showContinuationPrompt = false;
        if (continueLast)
        {
            await _sessionManager.ContinueSessionAsync();
            StatusText.Text = "工作中";
        }
        else
        {
            await _sessionManager.ResetSessionAsync();
            StatusText.Text = "工作中";
        }

        ResetColors();
        TimeText.FontSize = 26;
        _interactiveMode = false;
        _hoverTickCount = 0;
        _inPassthrough = true;
        Opacity = 0.15;
        _isMouseOver = false;

        _pollTimer.Start();
        _timerService.Start();
    }

    // ─── 拖拽手柄 ────────────────────────────────────────
    private void DragHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // WM_NCHITTEST 已经处理了拖拽逻辑，这里仅做安全兜底
        if (e.LeftButton == MouseButtonState.Pressed && _windowHandle != nint.Zero)
            DragMove();
    }

    // ─── 计时 UI ────────────────────────────────────────
    private void OnTimerTick(TimeSpan elapsed)
    {
        TimeText.Text = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
            : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }

    private void OnPauseStateChanged(bool isPaused)
    {
        StatusText.Text = isPaused ? "已暂停" : "工作中";
        _trayIcon.ToolTipText = isPaused ? "WorkTimer - 已暂停" : "WorkTimer - 工作中";

        if (isPaused)
            SetAmber(blink: true);    // 暂停 → 琥珀色闪烁
        else
            ResetColors();            // 恢复 → 白色
    }

    // ─── 左键（暂停/继续）────────────────────────────────
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_showContinuationPrompt || !_interactiveMode) return;
        _ = TogglePauseAsync();
    }

    private async Task TogglePauseAsync()
    {
        if (_sessionManager.IsPaused)
        {
            await _sessionManager.ResumeAsync();
            _timerService.NotifyPauseState(false);
        }
        else
        {
            await _sessionManager.PauseAsync();
            _timerService.NotifyPauseState(true);
        }
    }

    // ─── XAML 上下文菜单 ─────────────────────────────────
    private async void TogglePause_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionManager.IsPaused)
        {
            await _sessionManager.ResumeAsync();
            _timerService.NotifyPauseState(false);
        }
        else
        {
            await _sessionManager.PauseAsync();
            _timerService.NotifyPauseState(true);
        }
    }

    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_showContinuationPrompt || !_interactiveMode) return;
        if (ContextMenu != null) ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("确定要重置计时？当前会话数据将归档。", "重置",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _sessionManager.ResetSessionAsync();
        ResetColors();
        TimeText.Text = "00:00:00";
        StatusText.Text = "工作中";
    }

    private void OpenStats_Click(object sender, RoutedEventArgs e) => LaunchSettings("--view=stats");
    private void OpenSettings_Click(object sender, RoutedEventArgs e) => LaunchSettings("--view=settings");

    private static void LaunchSettings(string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                Path.Combine(AppContext.BaseDirectory, "WorkTimer.Settings.exe"), args)
            { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法启动设置窗口: {ex.Message}", "错误");
        }
    }

    private void TogglePassthrough()
    {
        if (_showContinuationPrompt) return;
        if (!_interactiveMode)
        {
            _interactiveMode = true;
            _inPassthrough = false;
            _isMouseOver = true;
            _hoverTickCount = 99;
            Opacity = 0.8;
            Activate();
        }
        else
        {
            _interactiveMode = false;
            _inPassthrough = true;
            _hoverTickCount = 0;
            _isMouseOver = false;
            Opacity = 0.15;
        }
    }

    // ─── 位置持久化 ──────────────────────────────────────
    private void RestorePosition()
    {
        if (_configService.Data.WindowLeft >= 0 && _configService.Data.WindowTop >= 0)
        {
            Left = _configService.Data.WindowLeft;
            Top = _configService.Data.WindowTop;
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - 240;
            Top = wa.Bottom - 140;
        }
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (_configService != null && WindowState == WindowState.Normal)
        {
            _configService.Data.WindowLeft = Left;
            _configService.Data.WindowTop = Top;
        }
    }

    // ─── 托盘图标 ───────────────────────────────────────
    private static System.Drawing.Icon CreateAppIcon()
    {
        var icoPath = Path.Combine(AppContext.BaseDirectory, "Resources", "worktimer-logo.ico");
        if (File.Exists(icoPath))
            return new System.Drawing.Icon(icoPath);
        var bmp = new System.Drawing.Bitmap(16, 16);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.DodgerBlue);
            g.FillEllipse(brush, 0, 0, 15, 15);
            using var font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold);
            g.DrawString("T", font, System.Drawing.Brushes.White, 4, 3);
        }
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private ContextMenu CreateTrayMenu()
    {
        var menu = new ContextMenu();
        var showItem = new MenuItem { Header = "显示/隐藏" };
        showItem.Click += (_, _) => TogglePassthrough();
        menu.Items.Add(showItem);
        menu.Items.Add(new Separator());
        var statsItem = new MenuItem { Header = "查看统计" };
        statsItem.Click += OpenStats_Click;
        menu.Items.Add(statsItem);
        var settingsItem = new MenuItem { Header = "设置" };
        settingsItem.Click += OpenSettings_Click;
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Separator());
        var exitItem = new MenuItem { Header = "退出" };
        exitItem.Click += Exit_Click;
        menu.Items.Add(exitItem);
        return menu;
    }

    private async void Exit_Click(object sender, RoutedEventArgs e)
    {
        _blinkTimer.Stop();
        _pollTimer.Stop();
        _timerService.Stop();
        if (_sessionManager.CurrentSession != null)
            await _db.InsertHeartbeatAsync(_sessionManager.CurrentSession.Id, DateTime.UtcNow);
        _heartbeatService.Stop();
        _configService.Save();
        Application.Current.Shutdown();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _pollTimer.Stop();
        _blinkTimer.Stop();
        _timerService.Dispose();
        _heartbeatService.Dispose();
        _trayIcon.Dispose();
        _configService.Save();
        base.OnClosing(e);
    }
}
