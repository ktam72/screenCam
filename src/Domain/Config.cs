// REQ-007: 設定モデル (flat なキーのみ。config.yaml は個人設定を含むため .gitignore)
namespace ScreenCam.Domain;

using System.IO;
using ScreenCam.Logging;

public sealed class Config
{
    public const int MaxPrefixLength = 16;

    public string Prefix { get; set; } = "sc";
    public string OutputDir { get; set; } = DefaultOutputDir();
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Fps { get; set; } = 30;
    public string VideoEncoder { get; set; } = "h264_nvenc";
    public string NvencPreset { get; set; } = "p4";
    public int Cq { get; set; } = 23;
    public bool AudioEnabled { get; set; } = true;
    public string? AudioDeviceId { get; set; }
    // REQ-004: FFmpeg は PATH が効かない環境があるためフルパスを許容。空なら PATH 検索 (Encode 側)
    public string FfmpegPath { get; set; } = string.Empty;
    public CaptureMode CaptureMode { get; set; } = CaptureMode.Monitor;
    public int MonitorIndex { get; set; }
    public bool AutoStart { get; set; } = true;
    public LogLevel LogLevel { get; set; } = LogLevel.Info;

    // 環境固有値をハードコードしない (ルール): 解決できない場合は相対パスへ退避
    private static string DefaultOutputDir()
    {
        string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return string.IsNullOrEmpty(videos) ? "recordings" : Path.Combine(videos, "screenCam");
    }

    // REQ-007: 読み込み後必ず検証する。不正値は default へ戻し呼び出し側が warn を出す
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (!FileNameBuilder.IsValidPrefix(Prefix))
            errors.Add($"prefix は半角英数字と _ のみ、1〜{MaxPrefixLength} 文字: '{Prefix}'");
        if (string.IsNullOrWhiteSpace(OutputDir))
            errors.Add("output_dir が空");
        if (Fps < 1 || Fps > 60)
            errors.Add($"fps が範囲外 (1-60): {Fps}");
        if (Width < 16 || Width > 8192 || Height < 16 || Height > 8192)
            errors.Add($"width/height が範囲外 (16-8192): {Width}x{Height}");
        if (Cq < 1 || Cq > 51)
            errors.Add($"cq が範囲外 (1-51): {Cq}");
        if (!AllowedEncoders.Contains(VideoEncoder))
            errors.Add($"video_encoder が不明: {VideoEncoder}");
        if (!AllowedPresets.Contains(NvencPreset))
            errors.Add($"nvenc_preset が不明: {NvencPreset}");
        if (!string.IsNullOrWhiteSpace(FfmpegPath) && !File.Exists(FfmpegPath))
            errors.Add($"ffmpeg_path が存在しない: {FfmpegPath}");

        return errors;
    }

    // REQ-007: ViewModel の combo とも共有する (DRY)。UI 側で選択肢を再定義しない
    public static readonly HashSet<string> AllowedEncoders =
        new(StringComparer.OrdinalIgnoreCase) { "h264_nvenc", "hevc_nvenc", "libx264" };

    public static readonly HashSet<string> AllowedPresets =
        new(StringComparer.OrdinalIgnoreCase) { "p1", "p2", "p3", "p4", "p5", "p6", "p7" };
}
