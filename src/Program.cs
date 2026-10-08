// REQ-001: entry point + named mutex による単一インスタンス + ログイン時自動起動 (Run キー)
// REQ-002: タスクトレイのコンテキストメニューで操作する (ホットキーは廃止 2026-10-08)
// REQ-008: 起動時に .tmp.* を recovery する
using System;
using System.IO;
using System.Threading;
using System.Windows;
using ScreenCam.Capture;
using ScreenCam.Domain;
using ScreenCam.Encode;
using ScreenCam.Logging;
using ScreenCam.Recording;
using ScreenCam.Ui;

internal static class Program
{
    private const string MutexName = "Global\\screenCam";

    [STAThread]
    private static int Main(string[] args)
    {
        bool createdNew;
        using var mutex = new Mutex(true, MutexName, out createdNew);
        if (!createdNew)
        {
            Console.Error.WriteLine("既存インスタンスが起動しています");
            return 1;
        }

        // REQ-001: ログイン時起動では cwd が保証されないため、設定とログは exe と同じ場所へ (承認 2026-10-08)
        string baseDir = AppContext.BaseDirectory;
        Log.Init(Path.Combine(baseDir, "logs"), LogLevel.Info);

        ConfigStore store = new(Path.Combine(baseDir, "config.yaml"));
        Config config = store.Load();

        var errors = config.Validate();
        if (errors.Count > 0)
        {
            foreach (string error in errors)
                Log.Warn($"REQ-007: {error}");
            return 2;
        }

        // REQ-001: config.AutoStart に応じて Run キーを登録/解除 (失敗しても起動を止めない)
        AutoStart.Apply(config.AutoStart, Environment.ProcessPath ?? baseDir);

        Recovery.Run(config);

        Recorder? recorder = Recorder.TryCreate(config);
        if (recorder == null)
            return 3;

        Application app = new();

        // 常駐アプリはウィンドウを持たない。OnLastWindowClosed だと Run() が即戻る (実測)
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        TrayIcon tray = new(recorder, config, store, new CaptureItemFactory());

        // 設定ウィンドウはトレイメニューから開く。明示的に引数を与えた時だけ起動時に表示する
        if (args.Length > 0 && args[0] == "--settings")
            tray.OpenSettings();

        Log.Info("REQ-002: 常駐起動。トレイメニューから 実行/停止/領域選択/ウィンドウ選択/設定/終了");

        app.Run();

        tray.Dispose();
        recorder.Dispose();
        return 0;
    }
}
