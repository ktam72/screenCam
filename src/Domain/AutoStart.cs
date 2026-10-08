// REQ-001: ログイン時自動起動 (HKCU の Run キー)。Open Issue #4 は Run キー方式で解決 (承認 2026-10-08)
namespace ScreenCam.Domain;

using Microsoft.Win32;
using ScreenCam.Logging;

public sealed class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "screenCam";

    // 呼び出し側 (Program / SettingsViewModel) は config.AutoStart を渡すだけ。失敗は warn して起動を止めない
    public static void Apply(bool enabled, string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            Log.Warn("REQ-001: exe パスが取得できないため自動起動を更新できません");
            return;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                Log.Warn($"REQ-001: Run キーを開けません: {RunKeyPath}");
                return;
            }

            if (!enabled)
            {
                if (key.GetValue(ValueName) == null)
                    return;

                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Info("REQ-001: ログイン時自動起動を解除");
                return;
            }

            // Run キーの値は引用符付きパス (Explorer が cwd を exe 置き場として扱う)
            string value = $"\"{exePath}\"";
            key.SetValue(ValueName, value);
            Log.Info($"REQ-001: ログイン時自動起動を更新: {value}");
        }
        catch (Exception ex)
        {
            Log.Warn($"REQ-001: Run キーの更新に失敗: {ex.Message}");
        }
    }

    public static bool IsEnabled(string exePath)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key == null)
                return false;

            string? current = key.GetValue(ValueName) as string;
            if (string.IsNullOrWhiteSpace(current))
                return false;

            return string.Equals(current, $"\"{exePath}\"", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warn($"REQ-001: Run キーの読み取りに失敗: {ex.Message}");
            return false;
        }
    }
}
