// REQ-004: ffmpeg パス解決 (config のフルパス優先, 無ければ PATH を検索)。FfmpegSession / Muxer / Recovery で共有
namespace ScreenCam.Encode;

using System;
using System.IO;
using ScreenCam.Domain;

public static class FfmpegPath
{
    public static string? Resolve(Config config)
    {
        if (!string.IsNullOrWhiteSpace(config.FfmpegPath))
            return File.Exists(config.FfmpegPath) ? config.FfmpegPath : null;

        string pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir))
                continue;

            string candidate = Path.Combine(dir, "ffmpeg.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
