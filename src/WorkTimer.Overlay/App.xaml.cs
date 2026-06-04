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
            // 已有实例运行，激活它
            var processes = Process.GetProcessesByName("WorkTimer.Overlay")
                .Where(p => p.Id != Environment.ProcessId)
                .ToList();

            foreach (var proc in processes)
            {
                try
                {
                    var hWnd = proc.MainWindowHandle;
                    if (hWnd != nint.Zero)
                    {
                        _ = NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_RESTORE);
                        _ = NativeMethods.SetForegroundWindow(hWnd);
                    }
                }
                catch { /* 进程可能已结束 */ }
            }

            Shutdown();
            return;
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
