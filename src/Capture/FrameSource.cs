// REQ-004: WGC フレームを BGRA へ変換してシンクへ渡す。GPU→CPU 転送は 1 フレーム 1 回 (NFR-001)
// REQ-004: 書き出しは 1/fps のグリッドで行う (案B 承認 2026-10-08)。
//   WGC は変化時のみフレームを渡す (実測: 動きあり 56.6-57.8fps / 静的 11枚のみ)。
//   グリッド時刻には直近フレームを書く = 変化がなければ同一バッファの複製。
//   これにより FFmpeg rawvideo pipe の入力長が実時間と一致する。
//   CreateRecurring は projection に無い (CS0117 実測) ので WGC 側へ固定間隔を要求できない。
namespace ScreenCam.Capture;

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using ScreenCam.Logging;

// 書き出し先 (FFmpeg pipe)。FrameSource はシンクの中身を知らない (コンポジション)
public interface IFrameSink
{
    void Write(ReadOnlySpan<byte> bgra, int width, int height);
}

public sealed class FrameSource : IDisposable
{
    private const int PoolSize = 3;
    private const int MaxPixels = 8192 * 8192; // 入力量の上限ガード (NFR: 暴走防止)

    // REQ-004: Vortice の ID3D11Device は IID_IDirect3DDevice を実装していないため、
    // WinRT 用の device ラッパーは d3d11.dll の interop で作る (実測: hr=0)
    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    private readonly ID3D11Device device;
    private readonly IDirect3DDevice winDevice;
    private readonly IFrameSink sink;

    private Direct3D11CaptureFramePool? pool;
    private GraphicsCaptureSession? session;

    // フレームごとに再利用する (毎フレーム確保は CPU/GPU 負荷)
    private byte[] packed = Array.Empty<byte>();
    private Windows.Storage.Streams.Buffer? buffer;
    private int bufferWidth;
    private int bufferHeight;
    private bool disposed;

    // 案B: グリッド書き出し (実測 30.1fps で正確。受領時刻基準は 23.3fps へドリフト)
    private const int DefaultFps = 30;
    private int targetFps = DefaultFps;
    private readonly object bufferGate = new();
    private bool hasFrame;
    private Thread? writerThread;
    private volatile bool writing;

    // device 作成失敗は null を返す (呼び出し側が warn して処理を終える)
    public static FrameSource? TryCreate(IFrameSink sink)
    {
        ID3D11Device device;
        ID3D11DeviceContext context;
        Result rc = D3D11.D3D11CreateDevice(
            IntPtr.Zero, DriverType.Hardware, DeviceCreationFlags.BgraSupport, Array.Empty<FeatureLevel>(), out device, out context);

        if (rc.Failure)
        {
            Log.Error($"REQ-004: D3D11 device を作成できない (code=0x{rc.Code:X8})");
            return null;
        }

        IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
        if (dxgiDevice == null)
        {
            Log.Error("REQ-004: device を IDXGIDevice へ QI できない");
            context.Dispose();
            device.Dispose();
            return null;
        }

        int hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr abiPtr);
        if (hr != 0 || abiPtr == IntPtr.Zero)
        {
            Log.Error($"REQ-004: WinRT device ラッパーを作成できない (code=0x{hr:X8})");
            dxgiDevice.Dispose();
            context.Dispose();
            device.Dispose();
            return null;
        }

        IDirect3DDevice winDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(abiPtr);

        // context はピクセル取得に使わない (SoftwareBitmap 側が転送する)
        context.Dispose();

        return new FrameSource(device, winDevice, sink);
    }

    private FrameSource(ID3D11Device device, IDirect3DDevice winDevice, IFrameSink sink)
    {
        this.device = device;
        this.winDevice = winDevice;
        this.sink = sink;
    }

    public bool Start(GraphicsCaptureItem item, int fps)
    {
        if (disposed)
        {
            Log.Warn("REQ-004: disposed な FrameSource の Start");
            return false;
        }

        if (item == null || item.Size.Width <= 0 || item.Size.Height <= 0)
        {
            Log.Warn("REQ-004: item が null またはサイズ不正");
            return false;
        }

        // 案B: fps が 1 未満ならグリッドを作れないので既定値へ寄せる
        targetFps = fps > 0 ? fps : DefaultFps;

        pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            winDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, PoolSize, item.Size);

        session = pool.CreateCaptureSession(item);
        // 枠線・カーソルは既定 off (設計方針 2026-10-08)
        session.IsBorderRequired = false;
        session.IsCursorCaptureEnabled = false;

        if (!PrepareBuffer(item.Size.Width, item.Size.Height))
            return false;

        lock (bufferGate)
        {
            hasFrame = false;
        }

        pool.FrameArrived += OnFrameArrived;

        writing = true;
        writerThread = new Thread(WriterLoop) { IsBackground = true };
        writerThread.Start();

        session.StartCapture();
        Log.Info($"REQ-004: capture started ({item.Size.Width}x{item.Size.Height}, grid {targetFps}fps)");
        return true;
    }

    public void Stop()
    {
        if (pool == null || session == null)
            return;

        writing = false;
        writerThread?.Join();
        writerThread = null;

        pool.FrameArrived -= OnFrameArrived;
        session.Dispose();
        pool.Dispose();
        session = null;
        pool = null;
    }

    private bool PrepareBuffer(int width, int height)
    {
        if (buffer != null && bufferWidth == width && bufferHeight == height)
            return true;

        if ((long)width * height > MaxPixels)
        {
            Log.Error($"REQ-004: フレームサイズが上限超過 ({width}x{height})");
            return false;
        }

        // Buffer に Close/Dispose は無い (実測)。参照を失くせば native 側で解放される
        packed = new byte[width * height * 4];
        buffer = new Windows.Storage.Streams.Buffer((uint)packed.Length);
        bufferWidth = width;
        bufferHeight = height;
        return true;
    }

    // FreeThreaded プールなのでハンドラはワーカースレッドで走る。例外は握り潰さずログへ出す
    private async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (disposed)
            return;

        Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
        if (frame == null)
            return;

        try
        {
            // REQ-004: frame.Surface は IID_IDirect3DSurface しか QI できない (実測 E_NOINTERFACE)。
            // 管理コードからのピクセル取得はドキュメント化された SoftwareBitmap 路径を使う
            SoftwareBitmap? bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore);
            if (bitmap == null)
            {
                Log.Warn("REQ-004: SoftwareBitmap が null");
                return;
            }

            try
            {
                lock (bufferGate)
                {
                    if (!PrepareBuffer(bitmap.PixelWidth, bitmap.PixelHeight))
                        return;

                    bitmap.CopyToBuffer(buffer!);

                    using DataReader reader = DataReader.FromBuffer(buffer!);
                    reader.ReadBytes(packed);

                    // 案B: ここでは書かない。グリッド時刻に WriterLoop が直近バッファを書く
                    hasFrame = true;
                }
            }
            finally
            {
                bitmap.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"REQ-004: フレーム処理で例外: {ex.Message}");
        }
        finally
        {
            frame.Dispose();
        }
    }

    // 案B: 1/fps グリッドで直近フレームを書く。変化がなければ同一バッファの複製なので
    // 静的画面でも出力長が実時間と一致する (実測: ドロップのみでは 0.6-1.2fps へ崩壊)
    private void WriterLoop()
    {
        long start = Stopwatch.GetTimestamp();
        double budget = 1000.0 / targetFps;
        double slotMs = 0;

        while (writing)
        {
            double now = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            if (now < slotMs)
            {
                int waitMs = (int)(slotMs - now);
                if (waitMs > 0)
                    Thread.Sleep(waitMs);
            }

            try
            {
                lock (bufferGate)
                {
                    // 最初のフレーム到着までは書かない (バッファが空)
                    if (hasFrame)
                        sink.Write(packed, bufferWidth, bufferHeight);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"REQ-004: グリッド書き出しで例外: {ex.Message}");
            }

            slotMs += budget;
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Stop();
        buffer = null;
        winDevice.Dispose();
        device.Dispose();
    }
}
