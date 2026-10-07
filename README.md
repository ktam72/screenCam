# sCam (screenCam)

Windows 11 常駐スクリーンレコーダー。タスクトレイのアイコンから録画を開始/停止します。

- キャプチャ: 特定ウィンドウ / 指定矩形（領域）/ 単一モニタ（全画面）
- エンコード: FFmpeg + NVENC (`h264_nvenc`)
- 音声: WASAPI loopback
- 既定: 1920x1080 / 30fps / H.264 / mp4

## 前提

- Windows 11（実機: build 26300）
- .NET 10 SDK（TFM: `net10.0-windows10.0.26100.0`）
- FFmpeg（NVENC が使えること）
  - `winget install Gyan.FFmpeg`
  - 導入済みか確認: `ffmpeg -hide_banner -f lavfi -i color=c=black:s=256x256:d=1 -c:v h264_nvenc -f null -`
- NVIDIA GPU（実機: RTX 4070 Ti SUPER）

## インストール

```
git clone <repo>
cd screenCam
dotnet build
```

実行ファイル: `bin/Debug/net10.0-windows10.0.26100.0/screenCam.exe`

## クイックスタート

1. `screenCam.exe` を実行する（コンソール出力はない。トレイにアイコンが出る）
2. トレイのアイコンを右クリック → メニュー
   - `実行` … 現在の設定（`capture_mode` + `monitor_index`）で録画を開始
   - `停止` … 停止して確定名で保存
   - `領域選択` … 矩形をドラッグして `Enter`（選択したら即開始）
   - `ウィンドウ選択` … 一覧から選んで `Enter`（選択したら即開始）
   - `設定` … 設定ウィンドウ（保存すると `config.yaml` に書き込む）
   - `終了` … 録画中なら先に停止して終了
3. 保存先: `config.yaml` の `output_dir`（既定 `C:\Users\ktam7\Videos\screenCam\`）
4. 名前: `<prefix>_<yyyyMMdd>_<HHmmss>.mp4`（既定 prefix `sc`）

初回実行時、`config.yaml` が無い場合は既定値で自動生成される。

## 設定（config.yaml）

| キー | 既定 | 内容 |
|---|---|---|
| `prefix` | `sc` | 出力ファイルの接頭辞（半角英数字と `_`、1〜16文字） |
| `output_dir` | `%USERPROFILE%\Videos\screenCam` | 保存先 |
| `width` / `height` | 1920 / 1080 | 出力解像度（領域選択時は crop サイズで出す） |
| `fps` | 30 | 1〜60 |
| `video_encoder` | `h264_nvenc` | `h264_nvenc` / `hevc_nvenc` / `libx264` |
| `nvenc_preset` | `p4` | `p1`〜`p7` |
| `cq` | 23 | 1〜51 |
| `audio_enabled` | `True` | `False` にすると映像のみ |
| `audio_device_id` | （空 = 既定デバイス） | WASAPI loopback のデバイス ID |
| `ffmpeg_path` | （空 = PATH 検索） | フルパスで書くと確実 |
| `capture_mode` | `Monitor` | `Window` / `Region` / `Monitor` |
| `monitor_index` | 0 | 対象モニタ |
| `autostart` | `True` | ログイン時自動起動（未実装） |
| `log_level` | `Info` | `Trace` / `Debug` / `Info` / `Warn` / `Error` |

## 補足

- 単一インスタンス: named mutex `Global\screenCam`。二重起動は既存インスタンスを通知して終了する
- 異常終了した回は、起動時に `.tmp.mp4` / `.tmp.wav` を `recovered_<作成時刻>` へ確定する
- 音声は再生があった区間だけストリームに入る（無音区間は音声なし。承認済みの方針）
- トレイアイコン: `assets/scam.ico`（待機）/ `assets/scam_recording.ico`（録画中）。再生成は `powershell -NoProfile -File tools/makeicon.ps1`

## トラブル

- **ffmpeg が見つからない**: `winget` で入れた PATH は既に開いているシェルに反映されないことがある。`config.yaml` の `ffmpeg_path` にフルパスを書く
- **NVENC が probe で失敗**: `h264_nvenc` の `-rc` に `cq`/`cqp` は不正（`-cq` 単独を使う）。probe は 64x64 で失敗し 256x256 で成功する
- **30fps が出ない**: 静的画面ではフレームが来ないため、グリッド基準で直近フレームを複製して 30fps を保つ（実測 30.0fps）

## ビルド・検証

```
dotnet build
dotnet test
dev-toolchain format .
dev-toolchain lint .
dev-toolchain scan .
```

コミットは Conventional Commits + REQ-ID（例: `feat: REQ-006 出力ファイル命名規則を追加`）。
