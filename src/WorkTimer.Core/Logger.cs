using System.Diagnostics;

namespace WorkTimer.Core;

public static class Logger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkTimer", "log.txt");

    private static readonly object _lock = new();
    private static int _lineCount;

    static Logger()
    {
        var dir = Path.GetDirectoryName(LogPath);
        if (dir != null) Directory.CreateDirectory(dir);
        if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 500 * 1024)
            File.WriteAllText(LogPath, $"--- Log truncated at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---{Environment.NewLine}");
    }

    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        lock (_lock)
        {
            File.AppendAllText(LogPath, line + Environment.NewLine);
            _lineCount++;
        }
        Debug.WriteLine(line);
    }

    public static string ReadRecent(int lines = 100)
    {
        lock (_lock)
        {
            if (!File.Exists(LogPath)) return "（无日志）";
            var all = File.ReadAllLines(LogPath);
            var recent = all.Length > lines ? all[^lines..] : all;
            return string.Join(Environment.NewLine, recent);
        }
    }
}
