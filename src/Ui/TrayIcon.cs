// REQ-002: タスクトレイのコンテキストメニューで 実行/停止/領域選択/ウィンドウ選択/設定/終了する
// ホットキーは廃止 (2026-10-08): この環境では物理キーの down がホットキー判定に届かない実測のため
namespace ScreenCam.Ui;

using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using ScreenCam.Capture;
using ScreenCam.Domain;
using ScreenCam.Logging;
using ScreenCam.Recording;

public sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon taskbarIcon;
    private readonly Recorder recorder;
    private readonly Config config;
    private readonly ConfigStore store;
    private readonly CaptureItemFactory factory;

    private readonly MenuItem startItem;
    private readonly MenuItem stopItem;

    private readonly Icon? idleIcon;
    private readonly Icon? recordingIcon;

    private bool disposed;

    public TrayIcon(Recorder recorder, Config config, ConfigStore store, CaptureItemFactory factory)
    {
        this.recorder = recorder;
        this.config = config;
        this.store = store;
        this.factory = factory;

        taskbarIcon = new TaskbarIcon();

        // REQ-001: sCam のアイコン (tools/makeicon.ps1 で生成)。無い場合はシステム既定へ退避
        idleIcon = LoadIcon("scam.ico");
        recordingIcon = LoadIcon("scam_recording.ico");

        var menu = new ContextMenu();

        startItem = new MenuItem { Header = "実行" };
        startItem.Click += (_, _) => StartFromConfig();

        stopItem = new MenuItem { Header = "停止" };
        stopItem.Click += (_, _) => Stop();

        MenuItem regionItem = new() { Header = "領域選択" };
        regionItem.Click += (_, _) => OpenSelector();

        MenuItem windowItem = new() { Header = "ウィンドウ選択" };
        windowItem.Click += (_, _) => OpenPicker();

        MenuItem settingsItem = new() { Header = "設定" };
        settingsItem.Click += (_, _) => OpenSettings();

        MenuItem exitItem = new() { Header = "終了" };
        exitItem.Click += (_, _) => Exit();

        menu.Items.Add(startItem);
        menu.Items.Add(stopItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(regionItem);
        menu.Items.Add(windowItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        taskbarIcon.ContextMenu = menu;

        // Recorder は UI を知らない。状態変化はイベントだけで伝える
        recorder.Finished += (_, _) => UpdateState();
        UpdateState();
    }

    public ContextMenu Menu => taskbarIcon.ContextMenu;

    // 現在の設定 (capture_mode + monitor_index) で開始
    public void StartFromConfig()
    {
        CaptureTarget target = CaptureTarget.FromMonitor(config.MonitorIndex, new CropRect(0, 0, 0, 0));
        recorder.Start(target);
        UpdateState();
    }

    public void Stop()
    {
        recorder.Stop();
        UpdateState();
    }

    // 案1 (承認済み): 選択したら即開始
    public void OpenSelector()
    {
        if (recorder.State == RecorderState.Recording)
        {
            Log.Warn("REQ-003: 録画中は領域選択できない");
            return;
        }

        RegionSelectorWindow window = new(factory);
        window.Completed += (_, target) =>
        {
            if (target != null)
            {
                recorder.Start(target);
                UpdateState();
            }
        };
        window.Show();
    }

    public void OpenPicker()
    {
        if (recorder.State == RecorderState.Recording)
        {
            Log.Warn("REQ-003: 録画中はウィンドウ選択できない");
            return;
        }

        WindowPickerWindow window = new();
        window.Completed += (_, target) =>
        {
            if (target != null)
            {
                recorder.Start(target);
                UpdateState();
            }
        };
        window.Show();
    }

    public void OpenSettings()
    {
        new SettingsWindow(new SettingsViewModel(config, store)).Show();
    }

    private void Exit()
    {
        if (recorder.State == RecorderState.Recording)
            Stop();

        Dispose();
        Application.Current?.Shutdown();
    }

    // REQ-001: 状態はアイコンとツールチップで表示する
    private void UpdateState()
    {
        bool recording = recorder.State == RecorderState.Recording;

        startItem.IsEnabled = !recording;
        stopItem.IsEnabled = recording;

        taskbarIcon.Icon = (recording ? recordingIcon : idleIcon)
            ?? (recording ? SystemIcons.Warning : SystemIcons.Application);
        taskbarIcon.ToolTipText = recording ? "sCam: 録画中" : "sCam: 待機中";
    }

    private static Icon? LoadIcon(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", fileName);
        if (!File.Exists(path))
        {
            Log.Warn($"REQ-001: アイコンファイルが見つからない: {path}");
            return null;
        }

        return new Icon(path);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        taskbarIcon.Dispose();
        idleIcon?.Dispose();
        recordingIcon?.Dispose();
    }
}
