// REQ-003: キャプチャ対象 (window / monitor) を GraphicsCaptureItem へ変換する
// WindowId は HWND の ulong 値、DisplayId は HMONITOR の ulong 値 (2026-10-08 実測で確認)。
// projection に DisplayMonitor.GetDisplayMonitorsAsync が無いため Win32 で列挙する (CS0117 実測)。
namespace ScreenCam.Capture;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.UI;
using ScreenCam.Domain;
using ScreenCam.Logging;

public sealed record MonitorEntry(int Index, IntPtr Hmonitor, string Device, CropRect Rect);

public sealed class CaptureItemFactory
{
    private const int DeviceNameCapacity = 32;

    // REQ-003: window モード
    public GraphicsCaptureItem? CreateForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            Log.Warn("REQ-003: window handle が 0 (未選択)");
            return null;
        }

        GraphicsCaptureItem? item = GraphicsCaptureItem.TryCreateFromWindowId(new WindowId((ulong)hwnd.ToInt64()));
        if (item == null)
            Log.Warn($"REQ-003: window item を作成できない (hwnd={hwnd.ToInt64():X})");
        return item;
    }

    // REQ-003: monitor モード (領域モードも元フレームはモニタ全体。crop は FFmpeg 側)
    public GraphicsCaptureItem? CreateForMonitor(int monitorIndex)
    {
        MonitorEntry? monitor = GetMonitor(monitorIndex);
        if (monitor == null)
            return null;

        GraphicsCaptureItem? item = GraphicsCaptureItem.TryCreateFromDisplayId(new DisplayId((ulong)monitor.Hmonitor.ToInt64()));
        if (item == null)
            Log.Warn($"REQ-003: monitor item を作成できない (index={monitorIndex}, hmonitor={monitor.Hmonitor.ToInt64():X})");
        return item;
    }

    // REQ-003: 設定ウィンドウ・ウィンドウピッカー用の一覧
    public IReadOnlyList<MonitorEntry> EnumerateMonitors()
    {
        // ref 引数を持つ delegate は lambda にできないため collector 経由で収集する
        var collector = new MonitorCollector();
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, collector.Callback, IntPtr.Zero))
            Log.Warn("REQ-003: EnumDisplayMonitors 失敗");

        return collector.Entries;
    }

    private MonitorEntry? GetMonitor(int index)
    {
        IReadOnlyList<MonitorEntry> entries = EnumerateMonitors();
        if (index < 0 || index >= entries.Count)
        {
            Log.Warn($"REQ-003: monitor_index が範囲外 ({index}, 台数={entries.Count})");
            return null;
        }
        return entries[index];
    }

    // 列挙結果の収集器 (delegate の ref 引数と状態を両立させるため instance method にする)
    private sealed class MonitorCollector
    {
        public List<MonitorEntry> Entries { get; } = new();

        public bool Callback(IntPtr hmonitor, IntPtr hdc, ref WinRect rect, IntPtr data)
        {
            MonitorInfoEx info = new();
            info.CbSize = Marshal.SizeOf<MonitorInfoEx>();
            if (!GetMonitorInfo(hmonitor, ref info))
            {
                Log.Warn($"REQ-003: GetMonitorInfo 失敗 (hmonitor={hmonitor.ToInt64():X})");
                return true; // 残りのモニタを続ける
            }

            CropRect rect2 = new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            Entries.Add(new MonitorEntry(Entries.Count, hmonitor, info.SzDevice, rect2));
            return true;
        }
    }

    private delegate bool EnumMonitorsProc(IntPtr hmonitor, IntPtr hdc, ref WinRect rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int CbSize;
        public WinRect RcMonitor;
        public WinRect RcWork;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DeviceNameCapacity)]
        public string SzDevice;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, EnumMonitorsProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hmonitor, ref MonitorInfoEx info);
}
