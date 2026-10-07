# screenCam 要件定義 (REQ-ID)

Status: 承認待ち (フェーズ2)
Project: E:\apps\screenCam
Platform: Windows 11 / .NET 10 (SDK 10.0.401)

## Concept

- 目的: Windows 11 で常駐し、ホットキートリガでスクリーンを動画録画するツール
- 形態: タスクトレイ常駐 + 設定ウィンドウ (MVVM)
- 保存先: マイベー (C:\Users\ktam7\Videos) 配下の screenCam サブフォルダ
- 技術: C# / .NET 10 + Windows Graphics Capture (WGC) + FFmpeg (NVENC)

### 非スコープ (v1)

- 事前ローリングバッファ (pre-roll)
- 自動分割録画 (時間/サイズ分割)
- 編集・アップロード機能
- マルチモニタ同時録画 (単一モニタのみ)

## 機能要件

### REQ-001 常駐と単一インスタンス

- タスクトレイアイコンで常駐する
- named mutex により単一インスタンスを保証する (二重起動時は既存インスタンスへ通知して終了)
- 録画状態 (idle / recording / paused) をトレイアイコンとツールチップで表示する
- ログイン時自動起動: 未決 (Open Issue #4)

### REQ-002 グローバルホットキー

- `Ctrl+Alt+R`: 録画開始 / 停止のトグル
- `Ctrl+Alt+S`: 領域 (矩形) 選択オーバーレイを表示
- `Ctrl+Alt+W`: ウィンドウピッカー (クリックで対象ウィンドウ決定)
- RegisterHotKey + WM_HOTKEY で実装 (フックではない)
- ホットキー文字列は config.yaml で変更可能
- 登録衝突時は warn ログを出し、アプリ自体は起動継続 (ホットキーのみ無効)

### REQ-003 キャプチャ対象 (3種)

- `window`: WGC `GraphicsCaptureItem.CreateForWindow`。`IsBorderRequired = false` (青枠なし)
- `monitor`: WGC `CreateForMonitor` (単一モニタ)。対象モニタはオーバーレイまたは config で指定
- `region`: monitor をキャプチャし指定矩形をクロップ (FFmpeg `crop` フィルタ)
- 選択矩形・対象ウィンドウは設定ウィンドウにも表示 (再選択可能)

### REQ-004 録画品質

- 既定: 1920x1080 / 30fps / H.264 / NVENC (`h264_nvenc`)
- NVENC プリセット `p4` (balanced)、`-cq 23`
- fps / 解像度 / プリセット / cq は config.yaml で変更可能
- NVENC が使用できない環境は `libx264` にフォールバックし warn ログ (CPU 負荷上昇を明示)
- フレーム取り込みは WGC `Direct3D11CaptureFramePool` (free-threaded, 3 buffers)

### REQ-005 音声

- WASAPI loopback (既定の render デバイス) を NAudio で録音
- 48kHz / 16bit / stereo PCM を録画中 `.wav` に並列記録
- 停止後に AAC へ変換し mp4 へ mux (FFmpeg 第2パス, 映像 stream copy)
- config で `audio: off` 可
- loopback デバイス未検出時は音声なしで録画継続 (warn)

### REQ-006 出力ファイル

- 命名規則: `<prefix>_<yyyyMMdd>_<HHmmss>.mp4` (ローカルタイム)
- prefix 既定: `sc` (config で変更)
- 保存先: `C:\Users\ktam7\Videos\screenCam\`
- 半角英数字と `_` のみ (スペース禁止)
- 同名衝突規則: 未決 (Open Issue #1)
- 録画中は一時ファイル (`.tmp.mp4` / `.tmp.wav`)、停止後に確定名でリネーム

### REQ-007 設定ウィンドウ (MVVM)

- View / ViewModel / Model を分離
- 項目: prefix, output dir, fps, width, height, codec, preset, cq, audio device, hotkeys, capture mode
- 永続化は `config.yaml` (YamlDotNet)
- `config.yaml` は `.gitignore` に登録 (個人設定を含む)

### REQ-008 ロギング

- レベル: error / warn / info / debug
- 出力: `logs/screenCam.log` (起動時に `.prev` へローテート)
- システムエラーは詳細をログへ記録し、ユーザーには汎用メッセージ
- ビジネスエラー (ホットキー衝突, 保存先不存在) は対処方法を含むメッセージ

## 非機能要件

| ID | 項目 | 目標 |
|----|------|------|
| NFR-001 | CPU 負荷 | 録画中の自プロセス平均 < 15% (NVENC 使用時) |
| NFR-002 | メモリ | 常駐時 < 300 MB |
| NFR-003 | フレーム欠落 | 30fps に対し < 2% |
| NFR-004 | 対応OS | Windows 11 (WGC 必要: 1903+) |
| NFR-005 | GPU | NVIDIA NVENC 対応 (実機: RTX 4070 Ti SUPER x2, driver 617.14) |

## テスト要件

| 種別 | 対象 | 目標 |
|------|------|------|
| 単体 | 命名規則, config 読み書き, クロップ計算, ホットキーパーサ | >= 80% |
| 結合 | FFmpeg 起動/停止, WGC フレーム->pipe, WASAPI loopback | >= 50% |
| E2E | ホットキー開始 -> 3秒録画 -> 停止 -> mp4 生成 -> 再生可 | 主要パス |

## 依存 (承認待ち)

| ライブラリ | 用途 | 範囲 |
|---|---|---|
| Microsoft.Windows.SDK.NET.Ref | CsWinRT 経由で Windows.Graphics.Capture | ^1.0 |
| NAudio | WASAPI loopback 録音 | ^2.2 |
| YamlDotNet | config.yaml | ^15 |
| Hardcodet.NotifyIcon.WPF | タスクトレイ (WPF) | ^1.0 |
| xUnit | テスト (dev) | ^2.9 |

代替: トレイは `System.Windows.Forms.NotifyIcon` (依存追加なし, WPF と混在)

## Open Issues

1. 同名ファイル衝突規則 (秒加算 / 連番 / 上書き)
2. クラッシュ時の一時ファイル (.tmp.*) の recovery 方針
3. mux 方式: 停止後 FFmpeg 2パス (現案) かリアルタイム mux か
4. ログイン時自動起動の要否 (Scheduled Task / Startup)
5. `git init` の要否 (現状 git repo ではない)
6. 領域選択オーバーレイの操作詳細 (ドラッグ確定 / Enter 確定)
