using Microsoft.Win32;

namespace WorkTimer.Overlay;

public static class AutoStartService
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WorkTimer";

    public static void Enable()
    {
        var path = Environment.ProcessPath;
        if (path != null)
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true);
            key?.SetValue(AppName, path);
        }
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true);
        key?.DeleteValue(AppName, false);
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
        return key?.GetValue(AppName) != null;
    }
}
