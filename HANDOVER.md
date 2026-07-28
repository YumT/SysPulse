# Kimi Code への引き継ぎ資料 (2026-07-28)

Kimi Work から Kimi Code へ作業を引き継ぐための資料。
このファイルだけ読めば、環境・手順・現状・未決事項がすべて分かるようにしてある。

---

## 1. プロジェクト概要

- **SysPulseAI**: タスクマネージャー風システムモニター **SysPulse の C# (WPF) 版**。
  Python 版からの移植。元の Python 版は別ワークスペース
  `C:\Users\yu\Documents\kimi\Workspaces\SysPulse タスクマネージャー開発`
  (そこは**温存指示あり。削除・変更しない**)。
- ワークスペース(= プロジェクトルート): `C:\Users\yu\Documents\Kimi\Workspaces\SysPulseAI`
- ソリューション構成:
  - `src/SysPulse.Core` — 計測ライブラリ(UI 非依存)
  - `src/SysPulse.App` — WPF GUI。実行時は `SysPulse.exe`
  - `src/SysPulse.Dump` — 検証用 CLI。全メトリクスを 1 回 JSON で出力
    (Python 版の `--dump` 相当)
- **管理者権限不要が最重要方針**(Python 版からの方針継承)。
- AI Usage(Claude / Kimi の使用量ゲージ)は旧 UsageWatcher から移植統合済み。
  UsageWatcher 本体は終了・自動起動解除済み(削除も済み)。

## 2. 現在のレイアウト(確定状態)

```
┌──────────────────────────┬──────────────────────────┐
│ 左上: CPU / メモリ / GPU / │ 右上(上下 1:1 分割):       │
│   イーサネット            │  上: プロセス表 CPU降順8行   │
│   (負荷・速度・スパーク    │     列=名前/CPU%/メモリ%/    │
│    ライン)               │     ディスク/GPU%           │
│                          │  下: システムログイベント    │
│                          │     (直近4件・レベル色分け)   │
├──────────────────────────┼──────────────────────────┤
│ 左下: ディスク 2列×最大5行 │ 右下: AI Usage            │
│   (8台以下は各1/4高、      │   (ヘッダーなし。           │
│    9台以上で1/5。          │    Claude/Kimi ゲージ)      │
│    セル左=名前2行、        │                          │
│    右=背景スパークライン上  │                          │
│    に半透明プレート         │                          │
│    #B3141414 付き数値2行) │                          │
└──────────────────────────┴──────────────────────────┘
```

## 3. ビルド / publish / インストール手順(重要・定型)

### ビルド

このマシンの Git Bash は ProgramFiles 系の環境変数が無く、
**素の `dotnet build` は失敗する**。必ずラッパー経由:

```bash
bash build.sh -c Release
```

### publish(単一 exe)

次の定型コマンドをそのまま使う(環境変数を全部渡すのが必須):

```bash
env PATH="$PATH:/c/Program Files/dotnet" \
  ProgramData='C:\ProgramData' ProgramFiles='C:\Program Files' \
  'ProgramFiles(x86)=C:\Program Files (x86)' \
  CommonProgramFiles='C:\Program Files\Common Files' \
  'CommonProgramFiles(x86)=C:\Program Files (x86)\Common Files' \
  ProgramW6432='C:\Program Files' \
  CommonProgramW6432='C:\Program Files\Common Files' \
  TEMP='C:\Users\yu\AppData\Local\Temp' TMP='C:\Users\yu\AppData\Local\Temp' \
  dotnet publish src/SysPulse.App -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish -nr:false --nologo -v q
```

### インストール

publish 後に:

```bash
taskkill //F //IM SysPulse.exe   # 実行中なら。無くてもよい
/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe \
  -NoProfile -ExecutionPolicy Bypass -File install.ps1
```

- インストール先: `%LOCALAPPDATA%\Programs\SysPulse`
- スタートアップ登録: Startup フォルダの `SysPulse.lnk`(install.ps1 が作る)

### 検証(スクリーンショット)

起動後 **15 秒待ってから**、ウィンドウタイトル "SysPulse" を EnumWindows で探して
PIL ImageGrab でキャプチャし `syspulse_installed_check.png` に保存、
ReadMediaFile で目視確認するのがこれまでの定型。
起動は `"$LOCALAPPDATA/Programs/SysPulse/SysPulse.exe" &` でよい。
**検証後、アプリは起動したまま残す**こと(ユーザーが常駐させて使っている)。

## 4. 環境の注意点(ハマりどころ)

- **Bash から PowerShell を直接 `powershell -Command "..."` で叩くと `$_` が
  壊れる**。PowerShell スクリプトは必ず .ps1 ファイルに書いて `-File` で実行する
  (install.ps1 方式)。Python 一時スクリプトも heredoc で /tmp に書いてから実行。
- Git Bash のパス: `/c/...` 表記が返ったら `C:\...` に変換して使う。
- コミット時に LF→CRLF の warning が出るが問題ない(そのまま進めてよい)。
- ビルド warning: `Usage/Models.cs` の `AppOptions.Debug` CS0649 は既知・無害。

## 5. 変更時の流儀(これまでのやり方)

1. コード変更
2. `bash build.sh -c Release` でビルド確認
3. publish(定型コマンド)
4. taskkill → install.ps1
5. 起動してキャプチャ検証(目視)
6. **README.md も同期して更新**(レイアウト・取得方法の記述の正典)
7. git コミット(コミットメッセージは日本語で変更内容を具体的に)

最新コミット: `e9d9639 プロセス表のネット列を GPU % 列に置換(...)`

## 6. 主要ファイルと役割

| ファイル | 役割 |
|---|---|
| `src/SysPulse.Core/SystemMonitor.cs` | 計測の窓口(facade)。`Sample()` で全メトリクス取得 |
| `src/SysPulse.Core/Metrics/ProcessMonitor.cs` | プロセス別 CPU/メモリ/ディスク/GPU。GPU は PDH `\GPU Engine(*)\Utilization Percentage` をインスタンス名の `pid_N_...` から PID 毎に合算(100% クランプ) |
| `src/SysPulse.Core/Metrics/DiskMonitor.cs` | ディスク。PDH `\PhysicalDisk(*)` ワイルドカード使用例の参考実装 |
| `src/SysPulse.Core/Metrics/GpuMonitor.cs` | GPU 全体の負荷・温度。NVML P/Invoke(NVIDIA のみ) |
| `src/SysPulse.Core/Pdh/PdhQuery.cs` | PDH ラッパー。`PdhAddEnglishCounter` で日本語 Windows でも英語カウンタ名が使える。`GetWildcardValues` がワイルドカード展開 |
| `src/SysPulse.Core/Models/Snapshot.cs` | 全メトリクスのモデル。`ProcSample` = name/pid/cpu/mem/disk/gpu |
| `src/SysPulse.App/MainWindow.xaml.cs` | レイアウト構築・タイマー駆動の描画更新 |
| `src/SysPulse.App/Controls/ProcessTable.cs` | プロセス表(右上 上半分) |
| `src/SysPulse.App/Controls/CriticalEventPanel.cs` | イベントログ監視パネル(右上 下半分) |
| `src/SysPulse.App/Controls/DiskRow.cs` | ディスクセル(左下) |
| `src/SysPulse.App/Controls/UsagePanel.cs` | AI Usage ゲージ(右下) |
| `src/SysPulse.App/Usage/` | Claude/Kimi 使用量取得(旧 UsageWatcher 移植) |
| `config.json`(実行 exe と同じフォルダ) | PC 固有設定(API キー等) |
| `PORTING.md` | Python 版からの移植仕様の正典 |
| `README.md` | 構成・レイアウト・設定の記述の正典 |

## 7. 未決事項・残タスク

### 【要ユーザー確認】イベントエリアの XPath

`CriticalEventPanel.cs` の `QueryRecent` は**見た目確認用の一時仕様**のまま:

```csharp
// TODO(表示確認用): 現在は重大以外(Level 2〜4)も含めている。最終的は Level=1 のみ。
"*[System[(Level=2 or Level=3 or Level=4)]]"
```

- 最終形は `*[System[(Level=1)]]`(重大のみ)に戻す想定だったが、
  ユーザーに「戻すか / 全イベント表示のままにするか」を質問したところ
  **未回答のまま引き継ぎとなった**。
- 対応: 次回セッションでユーザーに再確認し、戻す場合は XPath を
  `*[System[(Level=1)]]` に変更 + TODO コメント削除 + README.md の
  「※ 表示確認のため現在は…」の注記削除 → ビルド → publish → インストール
  → キャプチャ → コミット。
- なお重大イベントは滅多に発生しないため、Level=1 のみに戻すと
  通常時は「重大イベントなし」表示になる点はユーザーに伝えること。

### その他の既知事項

- プロセス表の GPU% は PDH GPU Engine カウンタが取れない環境では
  全行「—」になる(GPU 全体の NVML と違い AMD iGPU でも取れることを実機確認済み)。
- 左上の GPU 行は NVML(NVIDIA)ベースのため、このマシン(AMD iGPU)では「—」。
  これは仕様。もし今後「AMD/Intel でも GPU 全体を表示したい」と言われたら、
  PDH GPU Engine の全インスタンス合算か luid 単位合算で代替可能。

## 8. 会話の経緯(このセッションでやったこと)

1. プロセス表の「ネット」列(OS API 非対応で常に「—」)を廃止し、
   「GPU %」列に置換(PDH GPU Engine を PID 毎に合算)
2. ビルド → publish → インストール → キャプチャで実値確認
   (Kimi プロセスで 6.3% 等を確認)
3. README.md 更新、コミット `e9d9639`
4. イベントエリア Level=1 復帰の確認をユーザーに投げたところで引き継ぎ指示

## 9. ユーザーとのやり取りの前提

- ユーザーは日本語。返答も日本語で。
- 作業後にアプリの見た目を必ずキャプチャで確認する文化。
- 変更のたびに README 更新 + コミットまでやるのが期待値。
- 「OK」「これでOK」等の短い確認で次に進むスタイル。
