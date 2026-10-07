// REQ-003: 領域選択の矩形。Domain 層は WPF 型に依存しない (テスト可能にするため自前 struct)
namespace ScreenCam.Domain;

public sealed record CropRect(int X, int Y, int Width, int Height)
{
    // FFmpeg の crop フィルタは偶数幅/高さを要求する。奇数だと符号化側で切り詰められ
    // rawvideo 入力サイズとズレて警告が出るため、ここで偶数へ正規化する。
    public CropRect Normalize() => new(X, Y, Width & ~1, Height & ~1);

    public bool IsWithin(int frameWidth, int frameHeight) =>
        Width > 0 && Height > 0 && X >= 0 && Y >= 0 && X + Width <= frameWidth && Y + Height <= frameHeight;

    // 領域モードで矩形がフレーム全体を覆う場合は crop フィルタを省略できる (CPU 負荷削減)
    public bool IsFullFrame(int frameWidth, int frameHeight) =>
        X == 0 && Y == 0 && Width == frameWidth && Height == frameHeight;
}
