// REQ-006: 出力ファイル命名規則 <prefix>_<yyyyMMdd>_<HHmmss>.mp4
namespace ScreenCam.Domain;

using System.IO;

public static class FileNameBuilder
{
    // 同名衝突時の秒加算は最大60 (設計確定 2026-10-08)
    private const int MaxCollisionAttempts = 60;

    public static bool IsValidPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix) || prefix.Length > Config.MaxPrefixLength)
            return false;
        foreach (char c in prefix)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
                return false;
        }
        return true;
    }

    public static string Build(string prefix, DateTimeOffset now, string extension = "mp4")
    {
        if (!IsValidPrefix(prefix))
            throw new ArgumentException($"prefix が不正: {prefix}", nameof(prefix));
        return $"{prefix}_{now:yyyyMMdd}_{now:HHmmss}.{extension}";
    }

    // 録画中は _tmp を付けた一時ファイル、停止後に確定名へリネームする (REQ-006)
    public static string BuildTemp(string prefix, DateTimeOffset now, string extension = "mp4")
    {
        if (!IsValidPrefix(prefix))
            throw new ArgumentException($"prefix が不正: {prefix}", nameof(prefix));
        return $"{prefix}_{now:yyyyMMdd}_{now:HHmmss}_tmp.{extension}";
    }

    public static string ResolveCollision(string dir, string prefix, DateTimeOffset now, string extension = "mp4")
    {
        for (int i = 0; i < MaxCollisionAttempts; i++)
        {
            string candidate = Build(prefix, now.AddSeconds(i), extension);
            if (!File.Exists(Path.Combine(dir, candidate)))
                return candidate;
        }
        throw new IOException($"同名ファイルが {MaxCollisionAttempts} 件続いたため命名できません: {prefix}");
    }
}
