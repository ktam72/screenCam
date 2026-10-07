// REQ-004 / REQ-006: FFmpeg へ rawvideo (BGRA) を pipe 書き出しし、録画中は .tmp.mp4 へ書く
namespace ScreenCam.Encode;

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using ScreenCam.Capture;
using ScreenCam.Domain;
using ScreenCam.Logging;

// FrameSource の IFrameSink 実装。FFmpeg の生命周期のみ責任を持つ (単一責任)
public sealed class FfmpegSession : IFrameSink, IDisposable
{
    private const int ExitTimeoutMs = 15000;
    private const int ProbeTimeoutMs = 10000;
    private const int StderrTailChars = 2000;

    // REQ-006: 録画中の一時ファイルは固定名 (単一インスタンスガードで競合しない。Recovery の走査も単純化)
    public const string TempVideoName = ".tmp.mp4";

    private readonly Config config;
    private readonly CropRect? crop;
    private readonly string ffmpegPath;
    private readonly string encoder;
    private readonly StringBuilder stderr = new();

    private Process? process;
    private Stream? stdin;
    private long bytesWritten;
    private bool warnedWriteFailure;
    private bool disposed;
    private bool stopped;
    private bool lastExitOk;

    public string TempPath { get; }

    // 起動失敗は null を返す (呼び出し側が warn して処理を終える)
    public static FfmpegSession? TryCreate(Config config, int frameWidth, int frameHeight, CropRect? crop)
    {
        string? ffmpegPath = FfmpegPath.Resolve(config);
        if (ffmpegPath == null)
        {
            Log.Error("REQ-004: ffmpeg が見つかりません。config.yaml の ffmpeg_path にフルパスを設定してください");
            return null;
        }

        try
        {
            Directory.CreateDirectory(config.OutputDir);
        }
        catch (IOException ex)
        {
            Log.Error($"REQ-006: 保存先を作成できない ({config.OutputDir}): {ex.Message}");
            return null;
        }

        // REQ-004: NVENC が使えない環境は libx264 へフォールバック (warn)
        string encoder = ResolveEncoder(config, ffmpegPath);

        FfmpegSession session = new(config, frameWidth, frameHeight, crop, encoder, ffmpegPath);
        if (!session.Start())
            return null;

        return session;
    }

    private FfmpegSession(Config config, int frameWidth, int frameHeight, CropRect? crop, string encoder, string ffmpegPath)
    {
        this.config = config;
        this.frameWidth = frameWidth;
        this.frameHeight = frameHeight;
        this.crop = crop;
        this.encoder = encoder;
        this.ffmpegPath = ffmpegPath;
        TempPath = Path.Combine(config.OutputDir, TempVideoName);
    }

    private readonly int frameWidth;
    private readonly int frameHeight;

    private bool Start()
    {
        ProcessStartInfo info = new(ffmpegPath, BuildArguments())
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            process = Process.Start(info);
        }
        catch (Exception ex)
        {
            Log.Error($"REQ-004: ffmpeg を起動できない: {ex.Message}");
            return false;
        }

        if (process == null)
        {
            Log.Error("REQ-004: ffmpeg の起動結果が null");
            return false;
        }

        // stderr を非同期で貯める。握りつぶさない (停止時に tail をログへ)
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            if (stderr.Length < StderrTailChars)
                stderr.Append(e.Data).Append('\n');
        };
        process.BeginErrorReadLine();

        stdin = process.StandardInput.BaseStream;

        Log.Info($"REQ-004: ffmpeg started (encoder={encoder}, size={frameWidth}x{frameHeight}, out={TempPath})");
        return true;
    }

    // rawvideo の -s は実フレームサイズ。config の解像度と違えば scale で揃える (REQ-004)
    private string BuildArguments()
    {
        StringBuilder filters = new();

        bool cropped = crop != null && !crop.IsFullFrame(frameWidth, frameHeight);

        if (cropped)
        {
            // REQ-003: region モード (crop が効く) は crop サイズで出す。config へアップスケールしない (案A 承認 2026-10-08)
            filters.Append($"crop={crop!.Width}:{crop!.Height}:{crop!.X}:{crop!.Y}");
        }
        else if (config.Width != frameWidth || config.Height != frameHeight)
        {
            filters.Append($"scale={config.Width}:{config.Height}");
        }

        StringBuilder args = new("-y -hide_banner -loglevel warning");
        args.Append($" -f rawvideo -pix_fmt bgra -s {frameWidth}x{frameHeight} -r {config.Fps} -i pipe:0");

        if (filters.Length > 0)
            args.Append($" -vf {filters}");

        // FFmpeg 9.0.2 では h264_nvenc の -rc に "cq" を渡せない (Undefined constant)。-cq 単独が品質基準 rc になる (実測)
        if (encoder.Contains("nvenc"))
            args.Append($" -c:v {encoder} -preset {config.NvencPreset} -cq {config.Cq}");
        else
            args.Append($" -c:v {encoder} -preset veryfast -crf {config.Cq}");

        args.Append($" -f mp4 \"{TempPath}\"");
        return args.ToString();
    }

    // REQ-004: 1フレーム1回だけ pipe へ書く (NFR-001)
    public void Write(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (disposed || stdin == null || process == null)
            return;

        if (process.HasExited)
        {
            WarnWriteFailure("ffmpeg が既に終了している");
            return;
        }

        try
        {
            stdin.Write(bgra);
            bytesWritten += bgra.Length;
        }
        catch (Exception ex)
        {
            WarnWriteFailure(ex.Message);
        }
    }

    // stdin を閉じることで FFmpeg が mp4 を確定させる ( trailer 書き込み )
    public bool Stop()
    {
        if (process == null)
            return false;

        // 二重 Stop (Dispose 経由) で closed stream を触らない
        if (stopped)
            return lastExitOk;

        stopped = true;

        try
        {
            stdin?.Flush();
            stdin?.Close();
        }
        catch (IOException ex)
        {
            Log.Warn($"REQ-004: stdin を閉じられない: {ex.Message}");
        }

        if (!process.WaitForExit(ExitTimeoutMs))
        {
            Log.Error($"REQ-004: ffmpeg が {ExitTimeoutMs}ms 内に終了しない (kill)");
            try
            {
                process.Kill();
            }
            catch (Exception ex)
            {
                Log.Error($"REQ-004: ffmpeg を kill できない: {ex.Message}");
            }
            return false;
        }

        int code = process.ExitCode;
        Log.Info($"REQ-004: ffmpeg exited code={code} bytes={bytesWritten}");

        if (code != 0)
            Log.Warn($"REQ-004: ffmpeg stderr: {Tail()}");

        lastExitOk = code == 0;
        return lastExitOk;
    }

    private void WarnWriteFailure(string reason)
    {
        if (warnedWriteFailure)
            return;

        warnedWriteFailure = true;
        Log.Warn($"REQ-004: pipe 書き出し失敗 (以降同種は省略): {reason}");
    }

    private string Tail()
    {
        string text = stderr.ToString();
        return text.Length > StderrTailChars ? text.Substring(text.Length - StderrTailChars) : text;
    }

    // REQ-004: encoder を実機で probe して使えなければ libx264 へフォールバック
    private static string ResolveEncoder(Config config, string ffmpegPath)
    {
        if (!config.VideoEncoder.Contains("nvenc"))
            return config.VideoEncoder;

        // lavfi の 1フレームで encoder が初期化できるか確認する (実符号: -f null -)
        // NVENC は 64x64 だと "Frame Dimension less than the minimum supported value" で失敗するため 256x256 を使う (実測)
        ProcessStartInfo info = new(ffmpegPath,
            "-hide_banner -loglevel error -f lavfi -i color=c=black:s=256x256:d=0.1 -c:v " + config.VideoEncoder + " -f null -")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            Process? probe = Process.Start(info);
            if (probe == null)
                return "libx264";

            if (!probe.WaitForExit(ProbeTimeoutMs))
            {
                probe.Kill();
                Log.Warn($"REQ-004: NVENC probe がタイムアウト。libx264 を使う");
                return "libx264";
            }

            if (probe.ExitCode == 0)
                return config.VideoEncoder;

            Log.Warn($"REQ-004: NVENC probe 失敗 (code={probe.ExitCode})。libx264 へフォールバック (CPU 負荷上昇)");
            return "libx264";
        }
        catch (Exception ex)
        {
            Log.Warn($"REQ-004: NVENC probe で例外: {ex.Message}。libx264 を使う");
            return "libx264";
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Stop();
        process?.Dispose();
    }
}
