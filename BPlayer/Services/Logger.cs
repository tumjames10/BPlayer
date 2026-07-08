using System.IO;

namespace BPlayer.Services;

public static class Logger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BPlayer", "log.txt");

    static Logger()
    {
        var dir = Path.GetDirectoryName(LogPath);
        if (dir != null) Directory.CreateDirectory(dir);
        File.WriteAllText(LogPath, $"=== BPlayer Log {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);
    public static void Debug(string msg) => Write("DEBUG", msg);

    private static void Write(string level, string msg)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}\n");
        }
        catch { }
    }
}
