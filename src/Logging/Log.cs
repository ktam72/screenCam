// REQ-008: ロギング (error/warn/info/debug)。起動時に .prev へローテートし証跡を残す
namespace ScreenCam.Logging;

using System.IO;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

public static class Log
{
    private static readonly object Gate = new();
    private static string? Path;
    private static LogLevel MinLevel = LogLevel.Info;

    public static void Init(string logDir, LogLevel minLevel)
    {
        MinLevel = minLevel;
        Directory.CreateDirectory(logDir);
        Path = System.IO.Path.Combine(logDir, "screenCam.log");

        // 異常終了の証跡が消えないよう、起動時に .prev へ退避する
        if (File.Exists(Path))
            File.Move(Path, Path + ".prev", overwrite: true);

        Write(LogLevel.Info, $"log start (level={minLevel}, file={Path})");
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Write(LogLevel level, string message)
    {
        if (level < MinLevel)
            return;

        // ログ書き込み自体の失敗を握りつぶさない: 標準エラーへ出してから例外を投げる
        try
        {
            lock (Gate)
            {
                if (Path == null)
                    return;
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level.ToString().ToUpperInvariant()}] {message}{Environment.NewLine}");
            }
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"log write failed: {ex.Message}");
            throw;
        }
    }
}
