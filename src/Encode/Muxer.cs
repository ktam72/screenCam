// REQ-005: 停止後に mux (映像 stream copy + AAC)。音声なし・ffmpeg 不在は確定名へリネームのみ
namespace ScreenCam.Encode;

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using ScreenCam.Domain;
using ScreenCam.Logging;

public static class Muxer
{
    private const int MuxTimeoutMs = 60000;
    private const int StderrTailChars = 2000;
    private const string AudioBitrate = "128k";

    // finalPath は呼び出し側 (FileNameBuilder.ResolveCollision) が決める (REQ-006)
    public static bool Mux(Config config, string videoTemp, string? audioTemp, string finalPath)
    {
        if (!File.Exists(videoTemp))
        {
            Log.Error($"REQ-005: 映像一時ファイルが無い: {videoTemp}");
            return false;
        }

        bool hasAudio = audioTemp != null && File.Exists(audioTemp) && new FileInfo(audioTemp).Length > AudioRecorder.WavHeaderBytes;

        if (!hasAudio)
        {
            Log.Info("REQ-005: 音声なし。映像を確定名へリネーム");
            return Rename(videoTemp, finalPath);
        }

        string? ffmpegPath = FfmpegPath.Resolve(config);
        if (ffmpegPath == null)
        {
            Log.Warn("REQ-005: ffmpeg が見つからない。mux をスキップして映像を確定名へリネーム");
            return Rename(videoTemp, finalPath);
        }

        string args = "-y -hide_banner -loglevel warning" +
            $" -i \"{videoTemp}\" -i \"{audioTemp}\" -c:v copy -c:a aac -b:a {AudioBitrate} \"{finalPath}\"";

        int code = Run(ffmpegPath, args);
        if (code != 0)
        {
            Log.Error($"REQ-005: mux 失敗 (code={code})。映像を確定名へ残す");
            return Rename(videoTemp, finalPath);
        }

        RemoveTemp(videoTemp);
        RemoveTemp(audioTemp!);
        Log.Info($"REQ-005: mux done -> {finalPath}");
        return true;
    }

    private static int Run(string ffmpegPath, string args)
    {
        StringBuilder stderr = new();

        ProcessStartInfo info = new(ffmpegPath, args)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (Exception ex)
        {
            Log.Error($"REQ-005: ffmpeg を起動できない: {ex.Message}");
            return -1;
        }

        if (process == null)
        {
            Log.Error("REQ-005: ffmpeg の起動結果が null");
            return -1;
        }

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            if (stderr.Length < StderrTailChars)
                stderr.Append(e.Data).Append('\n');
        };
        process.BeginErrorReadLine();

        if (!process.WaitForExit(MuxTimeoutMs))
        {
            Log.Error($"REQ-005: mux が {MuxTimeoutMs}ms 内に終了しない (kill)");
            try
            {
                process.Kill();
            }
            catch (Exception ex)
            {
                Log.Error($"REQ-005: ffmpeg を kill できない: {ex.Message}");
            }
            return -1;
        }

        int code = process.ExitCode;
        if (code != 0)
            Log.Warn($"REQ-005: mux stderr: {stderr}");

        return code;
    }

    // REQ-006: 停止後に確定名へリネームする
    internal static bool Rename(string source, string target)
    {
        try
        {
            File.Move(source, target);
            Log.Info($"REQ-006: {Path.GetFileName(source)} -> {Path.GetFileName(target)}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"REQ-006: リネーム失敗 ({source} -> {target}): {ex.Message}");
            return false;
        }
    }

    private static void RemoveTemp(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            Log.Warn($"REQ-006: 一時ファイルを消せない ({path}): {ex.Message}");
        }
    }
}
