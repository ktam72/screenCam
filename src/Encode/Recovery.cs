// REQ-008: 起動時に保存先の .tmp.* を走査して recovery する (設計確定 2026-10-08)
namespace ScreenCam.Encode;

using System;
using System.IO;
using ScreenCam.Domain;
using ScreenCam.Logging;

public static class Recovery
{
    private const string RecoveredPrefix = "recovered_";

    public static void Run(Config config)
    {
        if (!Directory.Exists(config.OutputDir))
            return;

        string video = Path.Combine(config.OutputDir, FfmpegSession.TempVideoName);
        string audio = Path.Combine(config.OutputDir, AudioRecorder.TempAudioName);

        if (!File.Exists(video) && !File.Exists(audio))
            return;

        // 一時ファイルは固定名なので元の接頭辞は残っていない。作成時刻で命名する (REQ-006)
        DateTimeOffset stamp = File.Exists(video) ? File.GetCreationTime(video) : File.GetCreationTime(audio);
        string baseName = $"{RecoveredPrefix}{stamp:yyyyMMdd}_{stamp:HHmmss}";

        // 揃っていれば mux 試行。クラッシュで moov が無い mp4 は失敗し、単独保存へ落ちる
        if (File.Exists(video) && File.Exists(audio))
        {
            string final = Path.Combine(config.OutputDir, baseName + ".mp4");
            if (Muxer.Mux(config, video, audio, final))
            {
                Log.Warn($"REQ-008: 中断された録画を mux して recovery: {baseName}.mp4");
                return;
            }
        }

        // mux 不可: 単独で残す (REQ-008)
        if (File.Exists(audio))
        {
            Log.Warn($"REQ-008: 音声のみ recovery: {baseName}.wav");
            Muxer.Rename(audio, Path.Combine(config.OutputDir, baseName + ".wav"));
        }

        if (File.Exists(video))
        {
            Log.Warn($"REQ-008: 映像のみ recovery: {baseName}.mp4");
            Muxer.Rename(video, Path.Combine(config.OutputDir, baseName + ".mp4"));
        }
    }
}
