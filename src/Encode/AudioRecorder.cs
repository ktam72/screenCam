// REQ-005: WASAPI loopback を録音して .tmp.wav へ書く。フォーマットは device ネイティブ (REQ-005 修正 2026-10-08)。AAC は Muxer 側
namespace ScreenCam.Encode;

using System;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ScreenCam.Domain;
using ScreenCam.Logging;

public sealed class AudioRecorder : IDisposable
{
    // WAV ヘッダ分だけ = DataAvailable が一度も走っていない = 音声なし
    public const int WavHeaderBytes = 44;

    // REQ-006: 録画中の一時ファイルは固定名 (単一インスタンスガードで競合しない)
    public const string TempAudioName = ".tmp.wav";

    private readonly MMDevice device;
    private readonly WasapiLoopbackCapture capture;
    private WaveFileWriter? writer;
    private readonly object gate = new();
    private long bytesWritten;
    private bool warned;
    private bool disposed;

    public string TempPath { get; }

    // device 未検出は null を返す (呼び出し側は warn して映像のみ継続。REQ-005)
    public static AudioRecorder? TryCreate(Config config, string tempPath)
    {
        MMDevice device;
        try
        {
            using MMDeviceEnumerator enumerator = new();
            device = string.IsNullOrWhiteSpace(config.AudioDeviceId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(config.AudioDeviceId!);
        }
        catch (Exception ex)
        {
            Log.Warn($"REQ-005: render デバイスを取得できない: {ex.Message}");
            return null;
        }

        AudioRecorder recorder = new(device, tempPath);
        if (!recorder.Start())
        {
            recorder.Dispose();
            return null;
        }

        return recorder;
    }

    private AudioRecorder(MMDevice device, string tempPath)
    {
        this.device = device;
        capture = new WasapiLoopbackCapture(device);
        TempPath = tempPath;
    }

    public bool Start()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TempPath) ?? ".");

            lock (gate)
                writer = new WaveFileWriter(TempPath, capture.WaveFormat);
        }
        catch (Exception ex)
        {
            Log.Error($"REQ-005: WAV を開けない ({TempPath}): {ex.Message}");
            return false;
        }

        capture.DataAvailable += OnDataAvailable;
        capture.StartRecording();

        Log.Info($"REQ-005: audio capture started ({capture.WaveFormat.SampleRate}Hz / {capture.WaveFormat.Encoding} / {capture.WaveFormat.Channels}ch)");
        return true;
    }

    public bool HasAudio()
    {
        lock (gate)
            return bytesWritten > WavHeaderBytes;
    }

    public void Stop()
    {
        if (disposed)
            return;

        try
        {
            capture.StopRecording();
        }
        catch (Exception ex)
        {
            Log.Warn($"REQ-005: capture を止められない: {ex.Message}");
        }

        lock (gate)
        {
            if (writer == null)
                return;

            try
            {
                writer.Flush();
            }
            catch (IOException ex)
            {
                Log.Warn($"REQ-005: WAV を flush できない: {ex.Message}");
            }

            writer.Dispose();
            writer = null;
        }
    }

    // DataAvailable は NAudio のスレッドで走る (単一スレッド)。gate は Stop 側との排他用
    // NAudio 2.4.0: WasapiLoopbackCapture.DataAvailable は EventHandler<WaveInEventArgs> (WaveBuffer ではない。実測)
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (disposed || e.BytesRecorded == 0)
            return;

        lock (gate)
        {
            if (writer == null)
                return;

            try
            {
                writer.Write(e.Buffer, 0, e.BytesRecorded);
                bytesWritten += e.BytesRecorded;
            }
            catch (Exception ex)
            {
                WarnOnce(ex.Message);
            }
        }
    }

    private void WarnOnce(string reason)
    {
        if (warned)
            return;

        warned = true;
        Log.Warn($"REQ-005: WAV 書き出し失敗 (以降同種は省略): {reason}");
    }

    public void Dispose()
    {
        if (disposed)
            return;

        Stop();
        disposed = true;

        capture.Dispose();
        device.Dispose();
    }
}
