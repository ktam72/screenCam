// REQ-005: WASAPI loopback を録音して .tmp.wav へ書く。フォーマットは device ネイティブ (REQ-005 修正 2026-10-08)。AAC は Muxer 側
// REQ-005 案1 (承認 2026-11-23): 無音区間をゼロで埋めて音声タイムラインを連続化する。
//   WAV は定速 PCM で、DataAvailable が走らない無音区間が飛ぶと音声 stream 長 < 映像 stream 長
//   になり再生が映像とズレる (実測: video 63.87s / audio 42.68s = 無音 21.19s collapse)。
//   wall-clock を基準に、各 burst 直前までゼロを書き、burst を実際の再生時刻へ戻す。
// REQ-005 案1 バグ修正 (承認 2026-11-24): 埋めは必ず sample frame の整数倍で行う。
//   バイト数の差で書くと frame 境界がずれ、以降の実サンプルが NaN/Inf 化して AAC が Invalid argument (code -22)。
//   (実測 .tmp.wav data = 242,155,405 = frame(=8) で余り 5。frame = channels × bytesPerSample。sample 単位で追跡する)
namespace ScreenCam.Encode;

using System;
using System.Diagnostics;
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

    // ゼロ埋めは大確保を避けてこの単位で分割書きする
    private const int MaxZeroChunkBytes = 256 * 1024;

    private readonly MMDevice device;
    private readonly WasapiLoopbackCapture capture;
    private WaveFileWriter? writer;
    private readonly object gate = new();

    // REQ-005 案1: 実時間に対する期待 sample 数を決める基準
    private readonly Stopwatch clock = new();
    private int sampleRate;
    private int frameBytes;          // channels × bytesPerSample (1 sample frame のバイト数)
    private long samplesWritten;     // per-channel sample 数 (data + 埋めたゼロ)。常に frame 整列
    private bool hasRealAudio;       // 実際の音声 burst が一度でも走ったか (音声トラックを出すか)
    private bool warned;
    private bool disposed;

    public string TempPath { get; }

    // device 未検出は null を返す (呼び出し側は warn して映像のみ継続。REQ-005)
    public static AudioRecorder? TryCreate(Config config, string tempPath)
    {
        MMDevice? device = ResolveDevice(config);
        if (device == null)
            return null;

        AudioRecorder recorder = new(device, tempPath);
        if (!recorder.Start())
        {
            recorder.Dispose();
            return null;
        }

        return recorder;
    }

    // REQ-005 案B (承認 2026-11-24): audio_device_id が現存しない場合、既定 render へフォールバック。
    //   両方が取れない時のみ null (音声なしで録画継続)。既存は空欄時のみ既定だった (実測 0x80070490)。
    private static MMDevice? ResolveDevice(Config config)
    {
        using MMDeviceEnumerator enumerator = new();

        // ハッピーパス: 明示 ID が有効ならそのまま。失敗はガード節で既定へ
        if (!string.IsNullOrWhiteSpace(config.AudioDeviceId))
        {
            try
            {
                return enumerator.GetDevice(config.AudioDeviceId!);
            }
            catch (Exception ex)
            {
                Log.Warn($"REQ-005: audio_device_id を解析できない ({config.AudioDeviceId})。既定 render へフォールバック: {ex.Message}");
            }
        }

        try
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (Exception ex)
        {
            Log.Warn($"REQ-005: 既定 render も取得できない。音声なしで録画継続: {ex.Message}");
            return null;
        }
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

            // REQ-005 案1 バグ修正: frame 整列の基準 (frameBytes = channels × bytesPerSample) を固定
            WaveFormat format = capture.WaveFormat;
            int bytesPerSample = format.BitsPerSample / 8;
            sampleRate = format.SampleRate;
            frameBytes = format.Channels * bytesPerSample;

            lock (gate)
            {
                writer = new WaveFileWriter(TempPath, format);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"REQ-005: WAV を開けない ({TempPath}): {ex.Message}");
            return false;
        }

        capture.DataAvailable += OnDataAvailable;

        // REQ-005 案1: wall-clock の起点は録音開始。burst の実時間位置を復元する基準
        clock.Restart();
        capture.StartRecording();

        Log.Info($"REQ-005: audio capture started ({capture.WaveFormat.SampleRate}Hz / {capture.WaveFormat.Encoding} / {capture.WaveFormat.Channels}ch)");
        return true;
    }

    // REQ-005: 実音声 burst が一度でも走ったか。ゼロ埋めのみの (無音) は false = 音声トラック不要
    public bool HasAudio()
    {
        return hasRealAudio;
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

            // REQ-005 案1: 末尾の無音区間を埋めて音声 stream 長 = 録画実時間にする
            //   音声 burst が一度も無かった回は埋めない (音声トラックを作らない。REQ-005 方針)
            if (hasRealAudio && frameBytes > 0)
                PadSilence(ExpectedSamplesAtNow() - samplesWritten);

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

            // REQ-005 案1 バグ修正: 実時間期待から既に書いた sample 数を引いた差分 = 直前の無音区間。
            //   frame の整数倍で埋める (byte 差だと境界がずれ NaN/Inf になる)。連続音声では差分 ~0。
            if (frameBytes > 0)
            {
                long gapSamples = ExpectedSamplesAtNow() - samplesWritten;
                if (gapSamples > 0)
                    PadSilence(gapSamples);
            }

            try
            {
                // NAudio の buffer は sample frame の整数倍 (float)。frameBytes で割って sample 数を加算
                writer.Write(e.Buffer, 0, e.BytesRecorded);
                samplesWritten += e.BytesRecorded / frameBytes;
                hasRealAudio = true;
            }
            catch (Exception ex)
            {
                WarnOnce(ex.Message);
            }
        }
    }

    // REQ-005 案1: 録音開始からの実時間に対応する期待 sample 数 (per channel)
    private long ExpectedSamplesAtNow()
    {
        double seconds = (double)clock.ElapsedTicks / Stopwatch.Frequency;
        return (long)Math.Floor(seconds * sampleRate);
    }

    // ゼロ (無音) を data chunk へ gapSamples × frameBytes バイト追記。常に frame の整数倍。大確保回避で MaxZeroChunkBytes 単位
    private void PadSilence(long gapSamples)
    {
        if (writer == null || gapSamples <= 0)
            return;

        long totalBytes = gapSamples * frameBytes;
        byte[] zeros = new byte[Math.Min(totalBytes, MaxZeroChunkBytes)];
        long remaining = totalBytes;
        while (remaining > 0)
        {
            int n = (int)Math.Min(remaining, zeros.Length);
            writer.Write(zeros, 0, n);
            remaining -= n;
        }
        samplesWritten += gapSamples;
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
