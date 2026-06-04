using System.Diagnostics;
using System.Windows;
using WorkTimer.Core;
using WorkTimer.Core.Data;
using WorkTimer.Core.Services;

namespace WorkTimer.Overlay;

public partial class App : Application
{
    private const string MutexName = "WorkTimer-Overlay-{B4E7F8A2-3D1C-4E5F-9A8B-7C6D5E4F3A2}";
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out var isNew);

        if (!isNew)
        {
            // 检查是否真有另一个进程在运行
            var running = Process.GetProcessesByName("WorkTimer.Overlay")
                .Any(p => p.Id != Environment.ProcessId);

            if (running)
            {
                new DarkDialog().ShowDialog();
                Shutdown();
                return;
            }
            // 没有实际进程 → Mutex 是残留的，忽略它
            _mutex.Dispose();
            _mutex = new Mutex(true, MutexName, out _);
        }

        // 初始化服务
        var db = new DatabaseService();
        var sessionManager = new SessionManager(db);

        // 注册自启动（仅首次）
        if (!AutoStartService.IsEnabled())
            AutoStartService.Enable();

        var mainWindow = new MainWindow(db, sessionManager);
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        base.OnExit(e);
    }
}

internal static class NativeMethods
{
    internal const int SW_RESTORE = 9;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool ShowWindowAsync(nint hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hWnd);
}
