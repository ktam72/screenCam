// REQ-001: entry point + named mutex による単一インスタンス
// REQ-007: 設定ウィンドウ (MVVM) を表示
using System;
using System.Threading;
using System.Windows;
using ScreenCam.Domain;
using ScreenCam.Logging;
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

        Log.Init("logs", LogLevel.Info);

        ConfigStore store = new("config.yaml");
        Config config = store.Load();
        Log.Info($"config loaded: prefix={config.Prefix} mode={config.CaptureMode} out={config.OutputDir}");

        SettingsViewModel viewModel = new(config, store);

        Application app = new();
        app.Run(new SettingsWindow(viewModel));
        return 0;
    }
}
