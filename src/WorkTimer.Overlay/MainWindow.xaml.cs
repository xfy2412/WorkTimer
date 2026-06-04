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
using WorkTimer.Core.IPC;
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
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    // ─── Colors ─────────────────────────────────────────
    private static readonly SolidColorBrush DarkBg = new(Color.FromRgb(0x2D, 0x2D, 0x2D));
    private static readonly SolidColorBrush AmberBg = new(Color.FromRgb(0xCC, 0x80, 0x00));
    private static readonly SolidColorBrush DarkLine = new(Color.FromRgb(0x44, 0x44, 0x44));
    private static readonly SolidColorBrush AmberLine = new(Color.FromRgb(0xE0, 0xA0, 0x30));

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
    private OverlayWindowState _state = OverlayWindowState.Passthrough;
    private int _hoverTickCount;
    private int _hoverThreshold;
    private double _defaultOpacity;
    private bool _blinkState;
    private bool _showContinuationPrompt;
    private bool _mouseWasOver;

    public MainWindow(DatabaseService db, SessionManager sessionManager)
    {
        InitializeComponent();

        _db = db;
        _sessionManager = sessionManager;
        _timerService = new TimerService(sessionManager);
        _heartbeatService = new HeartbeatService(db);
        _configService = new ConfigService();
        _settingsManager = new SettingsManager();
        _defaultOpacity = _settingsManager.Data.DefaultOpacity;

        _timerService.Tick += OnTimerTick;
        _timerService.PauseStateChanged += OnPauseStateChanged;
        _hoverThreshold = (int)(_settingsManager.Data.HoverDelaySeconds * 1000 / 200);

        _pollTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(200) };
        _pollTimer.Tick += PollTimer_Tick;

        _blinkTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(800) };
        _blinkTimer.Tick += BlinkTimer_Tick;

        _trayIcon = new TaskbarIcon
        {
            Icon = CreateAppIcon(),
            ToolTipText = "WorkTimer - 工作中",
            Visibility = Visibility.Visible,
        };
        _trayIcon.TrayMouseDoubleClick += (_, _) => TogglePassthrough();
        _trayIcon.TrayLeftMouseDown += (_, _) => GoFindMe();
        _trayIcon.ContextMenu = CreateTrayMenu();

        Loaded += MainWindow_Loaded;
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => { _configService.Save(); _trayIcon.Dispose(); };

        RestorePosition();
    }

    // ═══════════════════════════════════════════════════════
    //  状态管理 —— 所有状态变化走 TransitionTo
    // ═══════════════════════════════════════════════════════

    private bool InPassthrough =>
        _state is OverlayWindowState.Passthrough or OverlayWindowState.Hidden;

    /// <summary>设置/取消 WS_EX_TRANSPARENT，确保鼠标穿透</summary>
    private void ApplyWindowStyle()
    {
        if (_windowHandle == nint.Zero) return;
        var style = GetWindowLongPtr(_windowHandle, GWL_EXSTYLE);
        if (InPassthrough)
            style = new nint(style.ToInt64() | WS_EX_TRANSPARENT);
        else
            style = new nint(style.ToInt64() & ~WS_EX_TRANSPARENT);
        SetWindowLongPtr(_windowHandle, GWL_EXSTYLE, style);
    }

    /// <summary>统一状态切换：清旧态、设新态、调 UI</summary>
    private void TransitionTo(OverlayWindowState newState)
    {
        var old = _state;
        _state = newState;

        // 退出旧态的收尾工作
        if (old == OverlayWindowState.FindMe && newState != OverlayWindowState.FindMe)
        {
            TimeText.FontSize = 26;
            _timerService.Start();
            _pollTimer.Start();
        }
        if (old == OverlayWindowState.ContinuationPrompt && newState != OverlayWindowState.ContinuationPrompt)
            _showContinuationPrompt = false;

        // 进入新态
        switch (newState)
        {
            case OverlayWindowState.Hidden:
                Visibility = Visibility.Hidden;
                _blinkTimer.Stop();
                break;

            case OverlayWindowState.Passthrough:
                Visibility = Visibility.Visible;
                this.WindowState = System.Windows.WindowState.Normal;
                ResetColors();
                if (_sessionManager.IsPaused) SetAmber(blink: true);
                BeginStoryboard((System.Windows.Media.Animation.Storyboard)FindResource("FadeOut"));
                _hoverTickCount = 0;
                _pollTimer.Start();
                _timerService.Start();
                break;

            case OverlayWindowState.Interactive:
                Visibility = Visibility.Visible;
                ResetColors();
                if (_sessionManager.IsPaused) SetAmber(blink: true);
                BeginStoryboard((System.Windows.Media.Animation.Storyboard)FindResource("FadeIn"));
                break;

            case OverlayWindowState.FindMe:
                Visibility = Visibility.Visible;
                this.WindowState = System.Windows.WindowState.Normal;
                TimeText.Text = "我在这里";
                StatusText.FontSize = 12;
                StatusText.Text = "点击恢复";
                _timerService.Stop();
                _pollTimer.Stop();
                BeginStoryboard((System.Windows.Media.Animation.Storyboard)FindResource("FadeIn"));
                SetAmber(blink: true);
                Activate();
                break;

            case OverlayWindowState.ContinuationPrompt:
                Visibility = Visibility.Visible;
                this.WindowState = System.Windows.WindowState.Normal;
                Opacity = 0.85;
                SetAmber(blink: false);
                break;
        }
        ApplyWindowStyle();
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
        HamburgerIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
    }

    private void BlinkTimer_Tick(object? sender, EventArgs e)
    {
        _blinkState = !_blinkState;
        CardBorder.Background = _blinkState ? AmberBg : DarkBg;
        SeparatorLine.Background = _blinkState ? AmberLine : DarkLine;
    }

    // ═══════════════════════════════════════════════════════
    //  初始化
    // ═══════════════════════════════════════════════════════

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        if (_windowHandle != nint.Zero)
        {
            var source = HwndSource.FromHwnd(_windowHandle);
            source?.AddHook(WndProc);
            Opacity = _defaultOpacity;
            ApplyWindowStyle();
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Logger.Write("[MainWindow] 窗口加载完成");

        var uiDispatcher = Dispatcher;
        _ = Task.Run(() => SettingsIpc.StartServerAsync(
            msg => uiDispatcher.BeginInvoke(() => OnIpcMessage(msg))));

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

    // ═══════════════════════════════════════════════════════
    //  WM_NCHITTEST
    // ═══════════════════════════════════════════════════════

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WM_NCHITTEST) return nint.Zero;

        var x = (short)(lParam.ToInt64() & 0xFFFF);
        var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

        GetWindowRect(_windowHandle, out RECT win);
        var localY = y - win.Top;

        if (localY >= 0 && localY <= 20)
        {
            handled = true;
            return (nint)HTCAPTION;
        }

        handled = true;
        return (nint)(InPassthrough ? HTTRANSPARENT : HTCLIENT);
    }

    // ═══════════════════════════════════════════════════════
    //  鼠标轮询
    // ═══════════════════════════════════════════════════════

    private void PollTimer_Tick(object? sender, EventArgs e)
    {
        if (_state is OverlayWindowState.FindMe or OverlayWindowState.ContinuationPrompt)
            return;
        if (_windowHandle == nint.Zero) return;

        GetCursorPos(out POINT cursor);
        GetWindowRect(_windowHandle, out RECT win);

        bool over = cursor.X >= win.Left && cursor.X <= win.Right &&
                    cursor.Y >= win.Top && cursor.Y <= win.Bottom;

        if (over)
        {
            if (!_mouseWasOver)
            {
                _mouseWasOver = true;
                _hoverTickCount = 0;
            }

            if (_state == OverlayWindowState.Passthrough)
            {
                _hoverTickCount++;
                if (_hoverTickCount >= _hoverThreshold)
                    TransitionTo(OverlayWindowState.Interactive);
            }
        }
        else
        {
            _mouseWasOver = false;
            if (_state != OverlayWindowState.Passthrough)
                TransitionTo(OverlayWindowState.Passthrough);
            _hoverTickCount = 0;
        }
    }

    // ═══════════════════════════════════════════════════════
    //  续接提示
    // ═══════════════════════════════════════════════════════

    private void ShowContinuationPrompt()
    {
        var elapsed = _sessionManager.GetElapsed(DateTime.UtcNow);
        TimeText.Text = elapsed.TotalHours >= 1
            ? $"上次 {(int)elapsed.TotalHours}h{elapsed.Minutes}m"
            : $"上次 {elapsed.Minutes}m{elapsed.Seconds}s";
        StatusText.Text = "左键续接 · 右键重置";
        TransitionTo(OverlayWindowState.ContinuationPrompt);

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
            e.Handled = true; timer.Stop(); _ = ContinueSession(true);
        };
        PreviewMouseRightButtonDown += (_, e) =>
        {
            if (!_showContinuationPrompt) return;
            e.Handled = true; timer.Stop(); _ = ContinueSession(false);
        };
    }

    private async Task ContinueSession(bool continueLast)
    {
        Logger.Write($"[MainWindow] 续接选择: {(continueLast ? "继续" : "重置")}");
        _showContinuationPrompt = false;
        if (continueLast) await _sessionManager.ContinueSessionAsync();
        else await _sessionManager.ResetSessionAsync();
        TimeText.FontSize = 26;
        StatusText.Text = "工作中";
        TransitionTo(OverlayWindowState.Passthrough);
        _pollTimer.Start();
        _timerService.Start();
    }

    // ═══════════════════════════════════════════════════════
    //  查找模式
    // ═══════════════════════════════════════════════════════

    private void GoFindMe() => TransitionTo(OverlayWindowState.FindMe);
    private void ExitFindMe() => TransitionTo(OverlayWindowState.Passthrough);
    private void DragHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _windowHandle != nint.Zero)
            DragMove();
    }

    // ═══════════════════════════════════════════════════════
    //  计时 / 暂停
    // ═══════════════════════════════════════════════════════

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
        if (isPaused) SetAmber(blink: true);
        else if (_state != OverlayWindowState.FindMe) ResetColors();
    }

    // ═══════════════════════════════════════════════════════
    //  IPC
    // ═══════════════════════════════════════════════════════

    private void OnIpcMessage(IpcMessage msg)
    {
        switch (msg.Type)
        {
            case "set": ApplySetting(msg.Key, msg.Value); break;
            case "reload":
                _settingsManager.Load();
                _hoverThreshold = (int)(_settingsManager.Data.HoverDelaySeconds * 1000 / 200);
                _defaultOpacity = _settingsManager.Data.DefaultOpacity;
                if (InPassthrough) Opacity = _defaultOpacity;
                break;
        }
    }

    private void ApplySetting(string key, string value)
    {
        switch (key)
        {
            case "HoverDelaySeconds":
                if (double.TryParse(value, out var hd))
                { _hoverThreshold = (int)(hd * 1000 / 200); _settingsManager.Data.HoverDelaySeconds = hd; }
                break;
            case "DefaultOpacity":
                if (double.TryParse(value, out var op))
                { _defaultOpacity = op; _settingsManager.Data.DefaultOpacity = op; if (InPassthrough) Opacity = op; }
                break;
        }
    }

    // ═══════════════════════════════════════════════════════
    //  鼠标事件
    // ═══════════════════════════════════════════════════════

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_state == OverlayWindowState.FindMe) { ExitFindMe(); return; }
        if (_state is OverlayWindowState.ContinuationPrompt or OverlayWindowState.Passthrough) return;
        _ = TogglePauseAsync();
    }

    private async Task TogglePauseAsync()
    {
        if (_sessionManager.IsPaused)
        { await _sessionManager.ResumeAsync(); _timerService.NotifyPauseState(false); }
        else
        { await _sessionManager.PauseAsync(); _timerService.NotifyPauseState(true); }
    }

    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_state is OverlayWindowState.ContinuationPrompt or OverlayWindowState.Passthrough or OverlayWindowState.Hidden) return;
        if (ContextMenu != null) ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    // ═══════════════════════════════════════════════════════
    //  上下文菜单
    // ═══════════════════════════════════════════════════════

    private async void TogglePause_Click(object sender, RoutedEventArgs e) => await TogglePauseAsync();

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("确定要重置计时？当前会话数据将归档。", "重置",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _sessionManager.ResetSessionAsync();
        TimeText.Text = "00:00:00";
        StatusText.Text = "工作中";
    }

    private void OpenStats_Click(object sender, RoutedEventArgs e) => LaunchSettings("--view=stats");
    private void OpenSettings_Click(object sender, RoutedEventArgs e) => LaunchSettings("--view=settings");

    private static void LaunchSettings(string args)
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 5; i++)
            {
                if (File.Exists(Path.Combine(dir, "WorkTimer.sln"))) break;
                dir = Path.GetDirectoryName(dir);
                if (dir == null) { dir = AppContext.BaseDirectory; break; }
            }
            var settingsDir = Path.GetFullPath(Path.Combine(dir, "src", "WorkTimer.Settings", "bin",
                "Debug", "net9.0-windows10.0.26100.0"));
            var exe = Path.Combine(settingsDir, "WorkTimer.Settings.exe");
            if (!File.Exists(exe))
                exe = Path.Combine(settingsDir, "win-x64", "WorkTimer.Settings.exe");
            if (File.Exists(exe))
                Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
            else
                MessageBox.Show($"找不到设置程序，请先编译 Settings 项目。\n查找位置: {exe}", "错误");
        }
        catch (Exception ex) { MessageBox.Show($"无法启动设置窗口: {ex.Message}", "错误"); }
    }

    private void TogglePassthrough()
    {
        if (_state == OverlayWindowState.ContinuationPrompt) return;
        if (_state == OverlayWindowState.FindMe) { ExitFindMe(); return; }
        if (_state == OverlayWindowState.Hidden || Visibility != Visibility.Visible || Opacity < 0.1)
            TransitionTo(OverlayWindowState.Interactive);
        else
            TransitionTo(OverlayWindowState.Hidden);
    }

    // ═══════════════════════════════════════════════════════
    //  位置持久化
    // ═══════════════════════════════════════════════════════

    private void RestorePosition()
    {
        if (_configService.Data.WindowLeft >= 0 && _configService.Data.WindowTop >= 0)
        { Left = _configService.Data.WindowLeft; Top = _configService.Data.WindowTop; }
        else
        { var wa = SystemParameters.WorkArea; Left = wa.Right - 240; Top = wa.Bottom - 140; }
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (_configService != null && this.WindowState == System.Windows.WindowState.Normal)
        { _configService.Data.WindowLeft = Left; _configService.Data.WindowTop = Top; }
    }

    // ═══════════════════════════════════════════════════════
    //  托盘图标
    // ═══════════════════════════════════════════════════════

    private static System.Drawing.Icon CreateAppIcon()
    {
        var icoPath = Path.Combine(AppContext.BaseDirectory, "Resources", "worktimer-logo.ico");
        if (File.Exists(icoPath)) return new System.Drawing.Icon(icoPath);
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
