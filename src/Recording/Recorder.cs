// REQ-003/004/005/006: 録画のオーケストレーター。Ui 依存なし (TrayIcon はこれを呼ぶだけ)
namespace ScreenCam.Recording;

using System;
using System.IO;
using Windows.Graphics.Capture;
using ScreenCam.Capture;
using ScreenCam.Domain;
using ScreenCam.Encode;
using ScreenCam.Logging;

public enum RecorderState
{
    Idle,
    Recording,
}

public sealed class RecordingFinishedEventArgs : EventArgs
{
    public bool Ok { get; }
    public string? OutputPath { get; }

    public RecordingFinishedEventArgs(bool ok, string? outputPath)
    {
        Ok = ok;
        OutputPath = outputPath;
    }
}

public sealed class Recorder : IDisposable
{
    private readonly Config config;
    private readonly CaptureItemFactory factory = new();

    private FfmpegSession? encoder;
    private AudioRecorder? audio;
    private FrameSource? source;

    private bool disposed;

    public RecorderState State { get; private set; } = RecorderState.Idle;
    public string? LastOutputPath { get; private set; }

    // TrayIcon はこのイベントだけで状態を描ける (Recorder は UI を知らない)
    public event EventHandler<RecordingFinishedEventArgs>? Finished;

    public static Recorder? TryCreate(Config config)
    {
        if (config.Validate().Count > 0)
        {
            Log.Warn("REQ-007: config が不正。Recorder は作成しない");
            return null;
        }

        return new Recorder(config);
    }

    private Recorder(Config config)
    {
        this.config = config;
    }

    public bool Start(CaptureTarget target)
    {
        if (disposed)
        {
            Log.Warn("REQ-003: disposed な Recorder の Start");
            return false;
        }

        if (State == RecorderState.Recording)
        {
            Log.Warn("REQ-003: 録画中に Start");
            return false;
        }

        GraphicsCaptureItem? item = ResolveItem(target);
        if (item == null)
            return false;

        // REQ-003: 矩形が空 (0x0) は「モニタ全体」を意味する。ホットキー起動では選択矩形が無い
        CropRect crop = target.Rect.Width == 0 || target.Rect.Height == 0
            ? new CropRect(0, 0, item.Size.Width, item.Size.Height)
            : target.Rect.Normalize();

        // FFmpeg が起動できない状態で capture を始めるとフレームを捨てるだけになる
        encoder = FfmpegSession.TryCreate(config, item.Size.Width, item.Size.Height, crop);
        if (encoder == null)
            return false;

        audio = config.AudioEnabled ? CreateAudio() : null;

        source = FrameSource.TryCreate(encoder);
        if (source == null)
        {
            FailStart();
            return false;
        }

        if (!source.Start(item, config.Fps))
        {
            FailStart();
            return false;
        }

        State = RecorderState.Recording;
        LastOutputPath = null;
        Log.Info($"REQ-003: recording started (mode={target.Mode}, {item.Size.Width}x{item.Size.Height}, crop={crop})");
        return true;
    }

    public bool Stop()
    {
        if (State != RecorderState.Recording)
        {
            Log.Warn("REQ-003: 待機中に Stop");
            return false;
        }

        // 停止順: capture を止める -> pipe を閉じて moov を確定 -> WAV を閉じる -> mux
        source?.Stop();
        encoder?.Stop();

        if (audio != null)
        {
            audio.Stop();
            Log.Info($"REQ-005: audio data = {(audio.HasAudio() ? "あり" : "なし (無音区間。REQ-005 案1)")}");
        }

        string finalName = FileNameBuilder.ResolveCollision(config.OutputDir, config.Prefix, DateTimeOffset.Now);
        string finalPath = Path.Combine(config.OutputDir, finalName);

        bool ok = Muxer.Mux(config, encoder!.TempPath, audio?.TempPath, finalPath);
        LastOutputPath = ok ? finalPath : null;

        State = RecorderState.Idle;
        Finished?.Invoke(this, new RecordingFinishedEventArgs(ok, LastOutputPath));
        return ok;
    }

    // REQ-003: region モードは WGC に矩形 item が無い。元フレームはモニタ全体で、crop は FFmpeg 側
    private GraphicsCaptureItem? ResolveItem(CaptureTarget target)
    {
        switch (target.Mode)
        {
            case CaptureMode.Window:
                if (!target.HasWindow)
                {
                    Log.Warn("REQ-003: window が未選択");
                    return null;
                }
                return factory.CreateForWindow(target.Hwnd);

            case CaptureMode.Region:
                if (!target.HasRegion)
                {
                    Log.Warn("REQ-003: region 矩形が不正");
                    return null;
                }
                return factory.CreateForMonitor(target.MonitorIndex);

            default:
                return factory.CreateForMonitor(target.MonitorIndex);
        }
    }

    private AudioRecorder? CreateAudio()
    {
        string tempPath = Path.Combine(config.OutputDir, AudioRecorder.TempAudioName);
        AudioRecorder? recorder = AudioRecorder.TryCreate(config, tempPath);

        if (recorder == null)
            Log.Warn("REQ-005: 音声なしで録画継続");

        return recorder;
    }

    private void FailStart()
    {
        source?.Dispose();
        audio?.Dispose();
        encoder?.Dispose();

        source = null;
        audio = null;
        encoder = null;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        if (State == RecorderState.Recording)
            Stop();

        disposed = true;

        source?.Dispose();
        audio?.Dispose();
        encoder?.Dispose();
    }
}
