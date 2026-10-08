# screenCam 設計 (フェーズ3)

Status: 承認待ち (コード生成前)
Stack: C# / .NET 10 (`net10.0-windows`) + WGC (CsWinRT) + FFmpeg 9.0.2 (NVENC) + NAudio

## 確定した方針 (2026-10-08)

| Open Issue | 決定 | 理由 |
|---|---|---|
| 同名衝突 | 同名なら秒を+1して再試行 (最大60) | 命名規則の可読性を保ち上書き回避 |
| 一時ファイル recovery | 起動時に `.tmp.*` を走査し mux 試行、不可なら `.wav` だけ `recovered_` 接頭辞で保存 | クラッシュ時の損失を最小化 |
| mux 方式 | 停止後 FFmpeg 2パス (映像 stream copy + AAC) | リアルタイム 2入力 pipe は Windows の匿名パイプ制約で複雑。品質差なし |
| 自動起動 | Scheduled Task (ログオン時) + config で off | 既存の autostart 運用パターンと統一 |
| git | 初期化済み (main) | Conventional Commits 運用に必須 |
| 領域確定操作 | ドラッグで矩形、Enter/ダブルクリック確定、Esc キャンセル | 誤確定を防ぐ |
| トレイ | Hardcodet.NotifyIcon.WPF | WPF 単一 UI 枠、WinForms 混在回避 |

## コンポーネント構成

```
src/
  Program.cs                 REQ-001  entry + named mutex 単一インスタンス
  App.xaml(.cs)              REQ-001  WPF app lifecycle + tray host
  Domain/
    Config.cs                REQ-007  設定モデル
    ConfigStore.cs           REQ-007  config.yaml 読み書き (YamlDotNet)
    AutoStart.cs             REQ-001  ログイン時自動起動 (HKCU Run キー)
    FileNameBuilder.cs       REQ-006  prefix_yyyyMMdd_HHmmss.mp4 + 衝突回避
    CaptureMode.cs           REQ-003  enum Window/Monitor/Region
    CaptureTarget.cs         REQ-003  選択結果 (hwnd / monitor index / rect)
  Capture/
    CaptureItemFactory.cs    REQ-003  WGC item (CreateForWindow / CreateForMonitor)
    FrameSource.cs           REQ-004  Direct3D11CaptureFramePool -> SoftwareBitmap
  Encode/
    FfmpegSession.cs         REQ-004  rawvideo pipe -> h264_nvenc -> .tmp.mp4
    FfmpegPath.cs            REQ-004  ffmpeg パス解決 (共有)
    AudioRecorder.cs         REQ-005  WASAPI loopback -> .tmp.wav
    Muxer.cs                 REQ-005  停止後 mux (video copy + aac)
    Recovery.cs              REQ-008  起動時 .tmp 走査
  Recording/
    Recorder.cs              REQ-003  オーケストレーター (item -> ffmpeg -> audio -> frame source -> mux)。Ui 依存なし
  Ui/
    TrayIcon.cs              REQ-002  トレイメニュー (実行/停止/領域選択/ウィンドウ選択/設定/終了) + 状態表示
    RegionSelectorWindow.xaml(.cs) REQ-003  矩形選択オーバーレイ (選択即開始)
    WindowPickerWindow.xaml(.cs)   REQ-003  ウィンドウピッカー (選択即開始)
    SettingsWindow.xaml(.cs) REQ-007  View
    SettingsViewModel.cs     REQ-007  ViewModel
  Logging/
    Log.cs                   REQ-008  レベル制御 + .prev ローテート
tests/
  FileNameBuilderTests.cs    REQ-006
  ConfigStoreTests.cs        REQ-007
  CropRectTests.cs           REQ-003
  FfmpegSessionTests.cs      REQ-004 (結合)
```

## データフロー (録画中)

```
WGC frame (D3D11 texture)
  -> CopySubresourceRegion -> 再利用 staging texture (1枚のみ)
  -> Map (D2D read) -> BGRA byte[] (バッファ再利用)
  -> stdin pipe (rawvideo bgra)
  -> FFmpeg: -vf crop (region のみ) -> h264_nvenc p4 cq23 -> .tmp.mp4

NAudio WasapiLoopbackCapture -> WAV writer (並列スレッド)

停止時: stdin close -> FFmpeg 終了 (moov 確定) -> mux (.tmp.mp4 + .tmp.wav) -> 確定名リネーム
```

## FFmpeg コマンドライン (REQ-004)

```
ffmpeg -hide_banner -loglevel error -y \
  -f rawvideo -pix_fmt bgra -s 1920x1080 -r 30 -i pipe:0 \
  [-vf crop=W:H:X:Y] \
  -c:v h264_nvenc -preset p4 -rc cq -cq 23 \
  -f mp4 recordings/.tmp.mp4
```

- NVENC probe 失敗時: `-c:v libx264 -preset veryfast -crf 23` にフォールバック (warn)
- 環境確認済み: FFmpeg 9.0.2 / driver 617.14 / RTX 4070 Ti SUPER x2

## 依存 (承認済み 2026-10-08)

| ライブラリ | 範囲 | 用途 |
|---|---|---|
| Microsoft.Windows.CsWinRT | 2.2.0 | WGC 投影。SDK 10.0.401 では自動参照されないため明示 (実測) |
| Vortice.DirectX | 3.8.3 | `D3D11.D3D11CreateDevice` (静的 holder は `Vortice.Direct3D11.D3D11`。`Direct3D11` ではない) |
| Vortice.Direct3D11 | 3.8.3 | `ID3D11Device` / `IDXGIDevice` への QI |
| Vortice.DXGI | 3.8.3 | `IDXGIDevice` (WinRT device ラッパーへ渡す native ポインタ) |
| NAudio | 2.4.0 | WASAPI loopback |
| YamlDotNet | 18.1.0 | config.yaml |
| Hardcodet.NotifyIcon.WPF | 2.0.1 | タスクトレイ |
| xUnit | ^2.9 | テスト (dev) |

### 検証済み API (実測 2026-10-08)

- `GraphicsCaptureItem.TryCreateFromWindowId(WindowId)` / `TryCreateFromDisplayId(DisplayId)`。`WindowId == HWND` (undocumented interop 不要)
- `Direct3D11CaptureFramePool.CreateFreeThreaded(IDirect3DDevice, DirectXPixelFormat, Int32, SizeInt32)` / `TryGetNextFrame()` / `IsBorderRequired` / `IsCursorCaptureEnabled`
- `IDirect3DDevice` は `d3d11.dll` の `CreateDirect3D11DeviceFromDXGIDevice` + `WinRT.MarshalInterface<IDirect3DDevice>.FromAbi` で作る。Vortice の `ID3D11Device` は `IID_IDirect3DDevice` を実装しない (キャストは通るがランタイムで InvalidCastException)
- `frame.Surface` は `IID_IDirect3DSurface` (`0BF4A146-13C1-4694-BEE3-7ABF15EAF586`) しか QI できない。`ID3D11Texture2D` / `IDXGISurface` / `ID3D11Resource` への QI は `E_NOINTERFACE` (実測)。`41D3D4D8-...` は `IDirect3DDevice` の IID
- 管理コードでのピクセル取得は `SoftwareBitmap.CreateCopyFromSurfaceAsync` → `CopyToBuffer` → `DataReader.ReadBytes` (実測 7ms + 3ms / フレーム @3840x2160。30fps 予算 33ms 内)
- `SharpGen.Runtime.ComObject` の raw pointer に対する QI は効かない。`op_Explicit` で作った型に対する QI は効く
- `Windows.Storage.Streams.Buffer` に `Dispose`/`Close` は無い (実測)。参照を失くせば native 側で解放

## 前提 (明示)

- WGC の `CreateForMonitor` / `CreateForWindow` は Windows 10 1903+。実機 Windows 11 で利用可
- `IsBorderRequired = false` は UAC 昇格なしで動作。ただし一部ウィンドウはシステム制約で枠が残り得る
- rawvideo pipe のスループット: 1080p30 = 約249 MB/s (PCIe + pipe write)。CPU 写込のみ、符号化は GPU
- FFmpeg は PATH 変更のため新シェルで有効 (現シェルではフルパスが必要)

## 未決 (実装前に確認)

- 自動起動の既定値: on か off か
- ~~録画中の一時ファイル名~~ 決定: 固定 `.tmp.mp4` / `.tmp.wav` (承認 2026-10-08。単一インスタンスガードで競合せず recovery 走査が単純)
- トレイアイコンの状態色 (idle/recording/paused) の具体値
