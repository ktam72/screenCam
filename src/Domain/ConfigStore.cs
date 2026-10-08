// REQ-007: config.yaml の読み書き (YamlDotNet)。flat なキーのみとし型変換の歧路を避ける
namespace ScreenCam.Domain;

using System.IO;
using ScreenCam.Logging;
using YamlDotNet.Serialization;

public sealed class ConfigException : Exception
{
    public ConfigException(string message) : base(message) { }
}

public sealed class ConfigStore
{
    private readonly string _path;
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();
    private static readonly ISerializer Serializer = new SerializerBuilder().Build();

    public ConfigStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ConfigException("config path が空");
        _path = path;
    }

    public string Path => _path;

    // 不存在時は default を書き出して返す。パース失敗は warn して default で継続 (起動を止めない)
    public Config Load()
    {
        if (!File.Exists(_path))
        {
            var created = new Config();
            Save(created);
            return created;
        }

        Dictionary<string, string?> map;
        try
        {
            map = Deserializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(_path))
                  ?? new Dictionary<string, string?>();
        }
        catch (Exception ex)
        {
            Log.Warn($"config.yaml を解析できません: {ex.Message}。default を使用");
            return new Config();
        }

        var cfg = FromMap(map);
        foreach (string error in cfg.Validate())
            Log.Warn(error);
        return cfg;
    }

    public void Save(Config cfg)
    {
        string? dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var writer = new StreamWriter(_path);
        Serializer.Serialize(writer, ToMap(cfg));
    }

    private static Config FromMap(Dictionary<string, string?> map)
    {
        var defaults = new Config();
        var cfg = new Config
        {
            Prefix = GetString(map, "prefix", defaults.Prefix),
            OutputDir = GetString(map, "output_dir", defaults.OutputDir),
            Width = GetInt(map, "width", defaults.Width),
            Height = GetInt(map, "height", defaults.Height),
            Fps = GetInt(map, "fps", defaults.Fps),
            VideoEncoder = GetString(map, "video_encoder", defaults.VideoEncoder),
            NvencPreset = GetString(map, "nvenc_preset", defaults.NvencPreset),
            Cq = GetInt(map, "cq", defaults.Cq),
            AudioEnabled = GetBool(map, "audio_enabled", defaults.AudioEnabled),
            AudioDeviceId = GetNullableString(map, "audio_device_id"),
            FfmpegPath = GetString(map, "ffmpeg_path", defaults.FfmpegPath),
            CaptureMode = GetEnum(map, "capture_mode", defaults.CaptureMode),
            MonitorIndex = GetInt(map, "monitor_index", defaults.MonitorIndex),
            AutoStart = GetBool(map, "autostart", defaults.AutoStart),
            LogLevel = GetEnum(map, "log_level", defaults.LogLevel),
        };

        return cfg;
    }

    private static Dictionary<string, string> ToMap(Config cfg) => new()
    {
        ["prefix"] = cfg.Prefix,
        ["output_dir"] = cfg.OutputDir,
        ["width"] = cfg.Width.ToString(),
        ["height"] = cfg.Height.ToString(),
        ["fps"] = cfg.Fps.ToString(),
        ["video_encoder"] = cfg.VideoEncoder,
        ["nvenc_preset"] = cfg.NvencPreset,
        ["cq"] = cfg.Cq.ToString(),
        ["audio_enabled"] = cfg.AudioEnabled.ToString(),
        ["audio_device_id"] = cfg.AudioDeviceId ?? "",
        ["ffmpeg_path"] = cfg.FfmpegPath,
        ["capture_mode"] = cfg.CaptureMode.ToString(),
        ["monitor_index"] = cfg.MonitorIndex.ToString(),
        ["autostart"] = cfg.AutoStart.ToString(),
        ["log_level"] = cfg.LogLevel.ToString(),
    };

    private static string GetString(Dictionary<string, string?> map, string key, string fallback)
    {
        if (!map.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
            return fallback;
        return value;
    }

    private static string? GetNullableString(Dictionary<string, string?> map, string key)
    {
        if (!map.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
            return null;
        return value;
    }

    private static int GetInt(Dictionary<string, string?> map, string key, int fallback)
    {
        if (!map.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
            return fallback;
        if (!int.TryParse(value, out int parsed))
        {
            Log.Warn($"{key} が数値ではありません: '{value}'。{fallback} を使用");
            return fallback;
        }
        return parsed;
    }

    private static bool GetBool(Dictionary<string, string?> map, string key, bool fallback)
    {
        if (!map.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
            return fallback;
        if (!bool.TryParse(value, out bool parsed))
        {
            Log.Warn($"{key} が true/false ではありません: '{value}'。{fallback} を使用");
            return fallback;
        }
        return parsed;
    }

    private static T GetEnum<T>(Dictionary<string, string?> map, string key, T fallback) where T : struct, Enum
    {
        if (!map.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
            return fallback;
        if (!Enum.TryParse<T>(value, ignoreCase: true, out T parsed))
        {
            Log.Warn($"{key} が不明な値です: '{value}'。{fallback} を使用");
            return fallback;
        }
        return parsed;
    }
}
