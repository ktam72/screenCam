// REQ-003: ウィンドウピッカー (EnumWindows で可視トップレベルウィンドウを列挙し、選択して確定する)
namespace ScreenCam.Ui;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using ScreenCam.Domain;
using ScreenCam.Logging;

// System.Windows.Input.CaptureMode と名前が衝突するため別名で参照する
using DomainCaptureMode = ScreenCam.Domain.CaptureMode;

public sealed record WindowEntry(IntPtr Hwnd, string Title)
{
    public override string ToString() => Title;
}

public partial class WindowPickerWindow : Window
{
    private const int TitleCapacity = 256;

    // null はキャンセルを意味する
    public event EventHandler<CaptureTarget?>? Completed;

    public WindowPickerWindow()
    {
        InitializeComponent();

        List<WindowEntry> entries = EnumerateWindows();
        foreach (WindowEntry entry in entries)
            WindowList.Items.Add(entry);

        if (entries.Count == 0)
        {
            Info.Text = "選択できるウィンドウがありません";
            return;
        }

        WindowList.SelectedIndex = 0;
        WindowList.MouseDoubleClick += (_, _) => Confirm();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Confirm();
        else if (e.Key == Key.Escape)
            Close();
    }

    private void Confirm()
    {
        if (WindowList.SelectedItem is not WindowEntry entry)
        {
            Log.Warn("REQ-003: ウィンドウが選択されていない");
            return;
        }

        // Window モードでは矩形選択は不要。0x0 は Recorder で item 全体へ解決される
        CaptureTarget target = new(DomainCaptureMode.Window, entry.Hwnd, 0, new CropRect(0, 0, 0, 0));

        Log.Info($"REQ-003: window selected (hwnd={entry.Hwnd.ToInt64():X}, title={entry.Title})");

        Completed?.Invoke(this, target);
        Close();
    }

    private static List<WindowEntry> EnumerateWindows()
    {
        var entries = new List<WindowEntry>();
        uint ownProcessId = (uint)Environment.ProcessId;

        bool Callback(IntPtr hwnd, IntPtr data)
        {
            if (!IsWindowVisible(hwnd))
                return true;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownProcessId)
                return true; // 自分 (ピッカー自身・トレイ) は対象にしない

            string title = GetTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title))
                return true;

            entries.Add(new WindowEntry(hwnd, title));
            return true;
        }

        EnumWindows(Callback, IntPtr.Zero);
        return entries;
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var buffer = new StringBuilder(TitleCapacity);
        int length = GetWindowText(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString() : string.Empty;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
}
