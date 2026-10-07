# screenCam プロジェクト固有ルール

## 技術スタック (承認済み 2026-10-08)

- C# / .NET 10 (TFM: `net10.0-windows`)
- Windows Graphics Capture (WGC) を CsWinRT 経由で使用
- エンコード: FFmpeg サブプロセス + NVENC (`h264_nvenc`)
- 音声: WASAPI loopback (NAudio)
- UI: WPF + MVVM (タスクトレイ常駐)
- 設定: `config.yaml` (YamlDotNet)

## 命名規則

- 出力ファイル: `<prefix>_<yyyyMMdd>_<HHmmss>.mp4` (半角英数字と `_` のみ)
- 保存先: `C:\Users\ktam7\Videos\screenCam\`

## ホットキー

- `Ctrl+Alt+R`: 録画開始/停止
- `Ctrl+Alt+S`: 領域選択オーバーレイ
- `Ctrl+Alt+W`: ウィンドウピッカー

## コード規則

- 全コードブロックに `docs/requirements.md` の REQ-ID コメントを付与
- 1回の生成は 300 行以内
- 継承よりコンポジション、ガード節、名前付き定数
- シークレットはハードコード禁止 (config.yaml / 環境変数)

## ビルド・検証

```
dotnet build
dotnet test
dev-toolchain format .
dev-toolchain lint .
dev-toolchain scan .
```

## コミット

- Conventional Commits + REQ-ID
- 例: `feat: REQ-006 出力ファイル命名規則を追加`

## 動作確認

- FFmpeg サブプロセスはフォアグラウンドでログを確認
- バックグラウンド実行時はプロセスとログの両方をこまめに確認
- GPU 負荷確認: `nvidia-smi --query-gpu=utilization.gpu --format=csv`
