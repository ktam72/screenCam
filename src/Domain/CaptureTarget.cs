// REQ-003: 選択結果 (ホットキーまたは設定ウィンドウから確定する)
namespace ScreenCam.Domain;

public sealed record CaptureTarget(CaptureMode Mode, IntPtr Hwnd, int MonitorIndex, CropRect Rect)
{
    // Window モードでは HWND が必須。0 は「未選択」を意味する (ガード節用)
    public bool HasWindow => Mode == CaptureMode.Window && Hwnd != IntPtr.Zero;

    public bool HasRegion => Mode == CaptureMode.Region && Rect.Width > 0 && Rect.Height > 0;

    public static CaptureTarget FromMonitor(int monitorIndex, CropRect fullFrame) =>
        new(CaptureMode.Monitor, IntPtr.Zero, monitorIndex, fullFrame);
}
