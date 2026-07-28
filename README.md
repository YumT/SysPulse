# SysPulse (C# 移植版)

タスクマネージャー風システムモニター。Python 版の C# 再実装。
仕様の正典は Python 版の PORTING.md(旧ワークスペース
`SysPulse タスクマネージャー開発/SysPulse_python3`)。**管理者権限不要**が最重要方針。

## 構成

- `src/SysPulse.Core` — 計測ライブラリ。UI 非依存。後の WPF アプリからもこれを参照する
  - `Metrics/` — CPU(GetSystemTimes + PDH)、メモリ(GlobalMemoryStatusEx)、
    ディスク(PDH `\PhysicalDisk(*)\% Disk Time`)、ネット(NIC バイトカウンタ差分)、
    プロセス(TotalProcessorTime 差分 + GetProcessIoCounters)、GPU(NVML P/Invoke)
  - `Pdh/PdhQuery.cs` — PDH ラッパー。`PdhAddEnglishCounter` を使うため
    日本語 Windows でも英語カウンタ名が使える(PerformanceCounter クラスは不可)
  - `DeviceInfo/DeviceInfoProvider.cs` — WMI 系の遅いデバイス名取得。
    **必ずバックグラウンドスレッドから呼ぶ**(Python 版で起動フリーズした経緯あり)
  - `SystemMonitor.cs` — 計測の窓口(facade)
- `src/SysPulse.Dump` — 検証用 CLI。Python 版の `python syspulse.py --dump` 相当。
  全メトリクスを 1 回 JSON で出力
- `src/SysPulse.App` — WPF GUI(2x2 レイアウト、スパークライン自前描画、
  ドラッグ/リサイズ中は描画停止)。実行時は `SysPulse.exe`
  - `Controls/Sparkline.cs` — 履歴 120 サンプル右詰め、塗りつぶし→線の 2 パス描画
  - `Controls/MetricRow.cs` / `Controls/ProcessTable.cs` — メトリクス行 / プロセス表
  - `Controls/DiskRow.cs` — ディスクセル(2 列 x 5 行。背景スパークライン+2 行表示)
  - `Controls/UsagePanel.cs` — AI Usage ゲージ(右下パネル。後述)
  - `Usage/` — UsageWatcher から移植した AI 使用量取得(Providers/Poller/Settings/Log)
  - `config.json` — PC 固有設定(下記)

## レイアウト

```
左上: CPU / メモリ / GPU / イーサネット(負荷・速度・スパークライン)
右上: CPU 負荷上位プロセス(CPU降順、メモリ・ディスク I/O 付き)
左下: ディスク(2 列 x 5 行のセル。背景スパークライン付き)
右下: AI Usage(Claude / Kimi の使用量ゲージ)
```

## 設定(config.json)・状態(window-state.json)

実行ファイルと同じフォルダに置く。終了時にウィンドウの位置・サイズを
`window-state.json` に保存し、次回起動時に復元する
(画面外に保存されていた場合は中央に戻す)。

```json
{
  "intervalMs": 1000,
  "namesRefreshSec": 30,
  "disks": [
    { "number": 3, "label": "システム" },
    { "number": 4, "label": "ゲーム" }
  ]
}
```

- `disks` が空 → Online ディスクを番号順に最大 10 台自動検出
- `disks` を指定 → その順・ラベルで先頭から固定表示。残りの枠は固定以外の
  Online ディスクを番号順に自動追加(USB メモリ等の抜き差しに追従)
- 配置は左下ブロックを 2 列 x 5 行で使用(最大 10 台。左・右・左・右の順)。
  各セルは横幅が狭いため、左半分=表示名/デバイス名の 2 行、
  右半分=スパークラインを背景いっぱいに描き使用率/実速度の 2 行を重ねる。
  行は台数に関わらず常に 5 行確保(空き行は下に余り、セル高は 1/5 で一定)。
  表示台数の決定ロジックは従来どおり
- ディスクの増減は `namesRefreshSec` 周期(既定 30 秒)で検出して行を作り直す

## AI Usage(UsageWatcher 統合)

右下パネルに Claude / Kimi の AI 使用量ゲージを表示する
(KimiCreditWatcher の UsageWatcher から移植。単体版と同等の取得仕様)。

- **表示**: プロバイダ毎にゲージ(ラベル+% / バー / リセット時刻+カウントダウン)
  - Claude: Current session / All models
  - Kimi: 5-hour usage / 7-day usage / Total usage(取れるものだけ表示)
- **色分け**: 50% 未満=緑、50% 以上=橙、80% 以上=赤(しきい値は設定で変更可)
- **ポーリング**: 120 秒周期(下限 30 秒)をプロバイダ毎にバックグラウンドで実行。
  HTTP 429 は Retry-After(最低 300 秒)でバックオフ。
  通信失敗時は直前の値に「通信失敗のため直前値を表示中」を添えて継続表示
- **認証(読むだけ原則)**: 認証ファイルは読み取り専用で開き、絶対に書き込まない
  (書き込むと本家 CLI のログインが壊れる)
  - Claude: `~/.claude/.credentials.json` の accessToken → `api.anthropic.com/api/oauth/usage`
  - Kimi: `~/.kimi-code/config.toml`(無ければ Kimi Work 同梱の config.toml)の
    api_key / base_url → `{base_url}/usages` と `api.kimi.com/coding/v1/usages`
- **設定共有**: `%LOCALAPPDATA%\UsageWatcher\settings.json` を単体版 Watcher と共有
  (しきい値・ポーリング間隔・プロバイダ有効/無効)。ログも同じフォルダに出る
- ※ 両プロバイダとも非公式 API。スキーマ変更時は UsageWatcher 側と合わせて直す

## 常駐動作(タスクトレイ)

- **多重起動禁止**: 2 個目を起動しても既存ウィンドウが復帰するだけ(単一インスタンス)
- **最小化 / ×ボタン** → ウィンドウは閉じずタスクトレイへ退避(常駐継続)
- **トレイアイコン ダブルクリック / メニュー「開く」** → ウィンドウ復帰
- **トレイメニュー「終了」** → 完全終了
- 初回起動後、トレイアイコンは Windows のオーバーフロー(^)に隠れることがある。
  常時表示したい場合はトレイ設定でピン留めする

## ビルド

```sh
./build.sh              # Debug
./build.sh -c Release   # Release
```

Visual Studio から開く場合は通常の環境変数が揃っているためそのままビルド可能。

## 配布・インストール

```sh
# 自己完結 single-file(.NET ランタイム不要、約 65MB)を publish/ に生成
dotnet publish src/SysPulse.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish

# %LOCALAPPDATA%\Programs\SysPulse にコピーし、スタートアップに SysPulse.lnk を作成
# (Windows ログオン時に自動起動。削除はショートカットを消すだけ)
powershell -ExecutionPolicy Bypass -File install.ps1
```

## 動作確認

```sh
./src/SysPulse.Dump/bin/Debug/net10.0-windows/SysPulse.Dump.exe   # 計測のみ
./src/SysPulse.App/bin/Release/net10.0-windows/SysPulse.exe       # GUI
```

初回サンプルはレート系(CPU/ネット/ディスク)が計算できないため内部で捨て、
1 秒後の 2 回目を出力する。

## この PC での検証結果(2026-07-28)

- CPU 名 / 定格クロック / メモリ構成(DDR4-2667 8GB+16GB)/ NIC 名 / ディスクモデルを正しく取得
- GPU は Radeon Vega 内蔵のため NVML 非対応 → `null`(「—」表示)に正しくフォールバック
- MSFT_Disk の Online 判定で切断済みディスクを除外
- GUI 常駐計測(Release): **CPU 約 0.3〜0.6%**(typeperf 全コア合計の 2.3〜4.7% ÷ 8 スレッド)、
  ワーキングセット 約 113〜128MB(Python 版 5% から大幅改善。メモリは今後の削減余地あり)

## 温度表示の検証結果(2026-07-28、非管理者で実測)

「権限不要」方針での温度取得は**不可**と確定。試した全経路と結果:

- CPU 温度: WMI `MSAcpi_ThermalZoneTemperature` → この PC は Not supported
- ディスク温度(WMI): `MSFT_StorageReliabilityCounter` / `MSStorageDriver_FailurePredictData`
  → どちらも Access denied(要管理者)
- ディスク温度(NVMe PoC): `IOCTL_STORAGE_QUERY_PROPERTY` の全バリエーション
  (素の StorageDeviceTemperatureProperty(21)、NVMe プロトコル固有(18/19)、
  物理ディスク/ボリューム(C:/D:)両経路)を実装して検証
  → `GENERIC_READ` でのデバイスオープンは標準ユーザーに拒否(err=5)、
  アクセス権 0 のハンドルでは全 IOCTL が err=1(INVALID_FUNCTION)
  → **Windows のセキュリティモデル上、非管理者での SMART/温度取得は不可能**
- メモリ温度: DIMM センサー読み出し自体にドライバ+管理者権限が必要

温度を表示する唯一の現実的経路は LibreHardwareMonitor + 管理者権限
(Python 版で撤去した判断と同じ結論)。GPU 温度のみ NVML 経由で NVIDIA 搭載時は表示可能。

## 今後の候補

- メモリ使用量の削減検討(自己完結版は WS 約 240MB)
