// REQ-007: 設定ウィンドウの ViewModel (Model = Config, View = SettingsWindow)。View はこの VM だけを見る
namespace ScreenCam.Ui;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using ScreenCam.Domain;
using ScreenCam.Encode;
using ScreenCam.Logging;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly Config config;
    private readonly ConfigStore store;

    public SettingsViewModel(Config config, ConfigStore store)
    {
        this.config = config;
        this.store = store;

        // 選択肢は Domain 層の許可集合から取る (UI 側で再定義しない = DRY)
        Encoders = new List<string>(Config.AllowedEncoders);
        Presets = new List<string>(Config.AllowedPresets);
        Modes = new List<string>(Enum.GetNames<CaptureMode>());
        LogLevels = new List<string>(Enum.GetNames<LogLevel>());
        AudioDevices = AudioDeviceList.RenderDevices();
        selectedAudioDevice = AudioDevices.FirstOrDefault(d => d.Id == config.AudioDeviceId)
                             ?? AudioDevices.FirstOrDefault();

        // 表示上の選択と Model を最初から一致させる (未設定なら既定 endpoint を Model へ反映)
        if (config.AudioDeviceId == null && selectedAudioDevice != null)
            config.AudioDeviceId = selectedAudioDevice.Id;

        SaveCommand = new RelayCommand(Save);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand SaveCommand { get; }

    public IReadOnlyList<string> Encoders { get; }
    public IReadOnlyList<string> Presets { get; }
    public IReadOnlyList<string> Modes { get; }
    public IReadOnlyList<string> LogLevels { get; }
    public IReadOnlyList<AudioDevice> AudioDevices { get; }

    // Config.AudioDeviceId は device Id (NAudio の MMDevice.Id)。未検出なら null (録画は音声なしで継続)
    public AudioDevice? SelectedAudioDevice
    {
        get => selectedAudioDevice;
        set
        {
            if (selectedAudioDevice == value)
                return;

            selectedAudioDevice = value;
            if (value != null)
                config.AudioDeviceId = value.Id;

            OnPropertyChanged();
        }
    }

    private AudioDevice? selectedAudioDevice;
    public IReadOnlyList<string> Errors { get; private set; } = Array.Empty<string>();

    public string ConfigPath => store.Path;

    public string Prefix
    {
        get => config.Prefix;
        set => Set(config.Prefix, value, v => config.Prefix = v);
    }

    public string OutputDir
    {
        get => config.OutputDir;
        set => Set(config.OutputDir, value, v => config.OutputDir = v);
    }

    public string FfmpegPath
    {
        get => config.FfmpegPath;
        set => Set(config.FfmpegPath, value, v => config.FfmpegPath = v);
    }

    public int Fps
    {
        get => config.Fps;
        set => Set(config.Fps, value, v => config.Fps = v);
    }

    public int Width
    {
        get => config.Width;
        set => Set(config.Width, value, v => config.Width = v);
    }

    public int Height
    {
        get => config.Height;
        set => Set(config.Height, value, v => config.Height = v);
    }

    public int Cq
    {
        get => config.Cq;
        set => Set(config.Cq, value, v => config.Cq = v);
    }

    public int MonitorIndex
    {
        get => config.MonitorIndex;
        set => Set(config.MonitorIndex, value, v => config.MonitorIndex = v);
    }

    public string VideoEncoder
    {
        get => config.VideoEncoder;
        set => Set(config.VideoEncoder, value, v => config.VideoEncoder = v);
    }

    public string NvencPreset
    {
        get => config.NvencPreset;
        set => Set(config.NvencPreset, value, v => config.NvencPreset = v);
    }

    public string CaptureModeName
    {
        get => config.CaptureMode.ToString();
        set => Set(config.CaptureMode.ToString(), value, v => config.CaptureMode = Enum.Parse<CaptureMode>(v));
    }

    public string LogLevelName
    {
        get => config.LogLevel.ToString();
        set => Set(config.LogLevel.ToString(), value, v => config.LogLevel = Enum.Parse<LogLevel>(v));
    }

    public bool AudioEnabled
    {
        get => config.AudioEnabled;
        set => Set(config.AudioEnabled, value, v => config.AudioEnabled = v);
    }

    public bool AutoStart
    {
        get => config.AutoStart;
        set => Set(config.AutoStart, value, v => config.AutoStart = v);
    }

    public string HotkeyStartStop
    {
        get => config.Hotkeys.StartStop;
        set => Set(config.Hotkeys.StartStop, value, v => config.Hotkeys.StartStop = v);
    }

    public string HotkeyRegionSelect
    {
        get => config.Hotkeys.RegionSelect;
        set => Set(config.Hotkeys.RegionSelect, value, v => config.Hotkeys.RegionSelect = v);
    }

    public string HotkeyWindowPicker
    {
        get => config.Hotkeys.WindowPicker;
        set => Set(config.Hotkeys.WindowPicker, value, v => config.Hotkeys.WindowPicker = v);
    }

    // REQ-007: 保存前に必ず検証し、不正値は画面へ出す (REQ-008 のビジネスエラー)
    private void Save()
    {
        List<string> errors = config.Validate();
        Errors = errors;
        OnPropertyChanged(nameof(Errors));

        if (errors.Count > 0)
        {
            Log.Warn($"REQ-007: 設定に不正な値があり保存しませんでした: {string.Join(" / ", errors)}");
            return;
        }

        try
        {
            store.Save(config);
            Log.Info($"REQ-007: config を保存しました ({store.Path})");
        }
        catch (Exception ex)
        {
            Log.Error($"REQ-007: config を保存できません: {ex.Message}");
        }
    }

    private void Set<T>(T current, T value, Action<T> apply, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
            return;

        apply(value);
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
