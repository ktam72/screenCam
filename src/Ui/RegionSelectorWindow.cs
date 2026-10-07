// REQ-003: 領域選択オーバーレイ (ドラッグで矩形 / Enter 確定 / Esc キャンセル)
// 確定した矩形は物理ピクセルで返す (WGC のフレームは物理ピクセル, WPF の座標は DIP)
namespace ScreenCam.Ui;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreenCam.Capture;
using ScreenCam.Domain;
using ScreenCam.Logging;

// System.Windows.Input.CaptureMode と名前が衝突するため別名で参照する
using DomainCaptureMode = ScreenCam.Domain.CaptureMode;

public partial class RegionSelectorWindow : Window
{
    private readonly CaptureItemFactory factory;

    private Point startPoint;
    private Point currentPoint;
    private bool dragging;

    // null はキャンセルを意味する
    public event EventHandler<CaptureTarget?>? Completed;

    public RegionSelectorWindow(CaptureItemFactory factory)
    {
        InitializeComponent();
        this.factory = factory;

        // 仮想画面全体を覆う (DIP 単位)
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        KeyDown += OnKeyDown;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        startPoint = e.GetPosition(this);
        currentPoint = startPoint;
        dragging = true;
        UpdateRect();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!dragging)
            return;

        currentPoint = e.GetPosition(this);
        UpdateRect();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        dragging = false;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Confirm();
        else if (e.Key == Key.Escape)
            Close();
    }

    private void UpdateRect()
    {
        double x = Math.Min(startPoint.X, currentPoint.X);
        double y = Math.Min(startPoint.Y, currentPoint.Y);
        double w = Math.Abs(currentPoint.X - startPoint.X);
        double h = Math.Abs(currentPoint.Y - startPoint.Y);

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;

        SizeInfo.Text = w > 0 && h > 0
            ? $"選択: {(int)Math.Round(w * PixelsPerDip())}x{(int)Math.Round(h * PixelsPerDip())} px"
            : string.Empty;
    }

    // DIP -> 物理ピクセルの倍率。Window が表示された後でないと取れない
    private double PixelsPerDip() => System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip;

    private void Confirm()
    {
        double wDip = Math.Abs(currentPoint.X - startPoint.X);
        double hDip = Math.Abs(currentPoint.Y - startPoint.Y);

        if (wDip <= 0 || hDip <= 0)
        {
            Log.Warn("REQ-003: 領域が選択されていない");
            return;
        }

        double scale = PixelsPerDip();

        // DIP -> 物理ピクセル (仮想画面全体の線形変換。単一モニタまたは同一 DPI を前提)
        int physicalX = (int)Math.Round((Left + Math.Min(startPoint.X, currentPoint.X)) * scale);
        int physicalY = (int)Math.Round((Top + Math.Min(startPoint.Y, currentPoint.Y)) * scale);
        int physicalW = (int)Math.Round(wDip * scale);
        int physicalH = (int)Math.Round(hDip * scale);

        MonitorEntry? monitor = FindMonitor(physicalX + physicalW / 2, physicalY + physicalH / 2);
        if (monitor == null)
        {
            Log.Warn("REQ-003: 選択矩形を含むモニタが見つからない");
            return;
        }

        // CropRect はモニタ原点相対 (FFmpeg の crop フィルタはフレーム内座標)
        CropRect crop = new CropRect(physicalX - monitor.Rect.X, physicalY - monitor.Rect.Y, physicalW, physicalH).Normalize();

        Log.Info($"REQ-003: region selected (monitor={monitor.Index}, {crop.Width}x{crop.Height} at {crop.X},{crop.Y})");

        CaptureTarget target = new(DomainCaptureMode.Region, IntPtr.Zero, monitor.Index, crop);
        Completed?.Invoke(this, target);
        Close();
    }

    private MonitorEntry? FindMonitor(int x, int y)
    {
        foreach (MonitorEntry entry in factory.EnumerateMonitors())
        {
            if (x >= entry.Rect.X && x < entry.Rect.X + entry.Rect.Width &&
                y >= entry.Rect.Y && y < entry.Rect.Y + entry.Rect.Height)
                return entry;
        }
        return null;
    }
}
