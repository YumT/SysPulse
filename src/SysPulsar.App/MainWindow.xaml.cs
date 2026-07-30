using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SysPulsar.App.Controls;
using SysPulsar.App.Usage;
using SysPulsar.Core;
using SysPulsar.Core.Models;
using WpfColor = System.Windows.Media.Color;

namespace SysPulsar.App;

/// <summary>
/// 2x2 レイアウトの常駐モニター。
/// 表示要素は メトリクス(CPU/メモリ/GPU/ネット) / プロセス＆イベント /
/// ディスク / AI Usage の 4 つで、「表示エリア」の領域数(1/2/2/4)ぶんの
/// スロットにこの順で流し込まれる。表示要素は右クリックメニューで切替可能。
/// 計測はバックグラウンドスレッド、描画は UI スレッド。
/// ドラッグ/リサイズ中は WM_ENTERSIZEMOVE/EXITSIZEMOVE で検出して描画を止める。
/// </summary>
public partial class MainWindow : Window
{
    private const int MaxDisks = 10;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int WmSettingChange = 0x001A;

    private static readonly WpfColor CCpu = Sparkline.Hex("#4fc3f7");
    private static readonly WpfColor CMem = Sparkline.Hex("#ba68c8");
    private static readonly WpfColor CGpu = Sparkline.Hex("#ef5350");
    private static readonly WpfColor CDown = Sparkline.Hex("#81c784");
    private static readonly WpfColor CUp = Sparkline.Hex("#fdd835");
    private static readonly WpfColor CDisk = Sparkline.Hex("#90a4ae");

    private readonly SystemMonitor _monitor = new();
    private readonly AppConfig _config;
    private readonly Dictionary<string, MetricRow> _rows = new();
    private readonly Dictionary<int, DiskRow> _diskRows = new();
    private ProcessTable _procTable = null!;
    private CriticalEventPanel _eventPanel = null!;
    private Grid _tl = null!;
    private Grid _bl = null!;
    private Grid _tr = null!;
    private Grid _br = null!;
    private UsagePanel _usagePanel = null!;
    private Usage.Settings _usageSettings = null!;
    private UsagePoller? _usagePoller;
    private DispatcherTimer? _usageTick;
    private TextBlock _diskPlaceholder = null!;

    /// <summary>表示エリア(11=1x1 / 12=1x2 / 21=2x1 / 22=2x2)。スロット数は 1/2/2/4。</summary>
    private int _layoutMode = 22;
    private readonly Dictionary<int, MenuItem> _layoutItems = new();

    /// <summary>更新適用項目をメニューに挿入済みか(二重挿入防止)。</summary>
    private bool _updateOffered;

    /// <summary>表示要素の有効/無効(メトリクス/プロセス＆イベント/ディスク/AI Usage)。
    /// 有効な要素が固定順でスロットに流し込まれる。スロット数を超えた分は出ない。</summary>
    private bool _showMetrics = true;
    private bool _showProcesses = true;
    private bool _showDisks = true;
    private bool _showUsage = true;

    private volatile bool _stop;
    private volatile bool _dragging;
    private Thread? _samplerThread;
    private Thread? _namesThread;
    private Thread? _eventsThread;

    private bool _disksBuilt;
    private List<(int num, string label, List<(string Letter, string VolLabel)> parts)> _diskOrder = new();

    private System.Windows.Forms.NotifyIcon? _tray;
    private EventWaitHandle? _showEvent;
    private bool _reallyExit;

    public MainWindow()
    {
        InitializeComponent();
        _config = AppConfig.Load();
        BuildLayout();
        SetupTrayIcon();
        Loaded += (_, _) =>
        {
            RestoreWindowState();
            StartThreads();
        };
        // ×ボタンは終了ではなくトレイへ退避(常駐)。完全終了はトレイメニューから。
        Closing += (sender, e) =>
        {
            if (WindowState == System.Windows.WindowState.Normal)
                SaveWindowState();
            if (!_reallyExit)
            {
                e.Cancel = true;
                HideToTray();
            }
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == System.Windows.WindowState.Minimized)
                HideToTray();
        };
        Closed += (_, _) =>
        {
            _stop = true;
            _usageTick?.Stop();
            _usagePoller?.Dispose();
            _tray?.Dispose();
            _showEvent?.Dispose();
            _monitor.Dispose();
        };
    }

    // ---- タスクトレイ ----

    private void SetupTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("開く", null, (_, _) => RestoreFromTray());
        menu.Items.Add("終了", null, (_, _) =>
        {
            _reallyExit = true;
            Close();
        });

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "SysPulsar",
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();

        // 2 個目のインスタンスからの復帰信号を受け取る(多重起動禁止の受け側)
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, App.ShowEventName);
        ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(RestoreFromTray), null, -1, false);
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = System.Windows.WindowState.Normal;
        Activate();
    }

    /// <summary>前回の位置・サイズを復元。初回や画面外の場合は中央表示。</summary>
    private void RestoreWindowState()
    {
        if (SysPulsar.App.WindowState.Load() is { } s)
        {
            Left = s.Left;
            Top = s.Top;
            Width = Math.Max(MinWidth, s.Width);
            Height = Math.Max(MinHeight, s.Height);
            Topmost = s.Topmost;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
        }
    }

    // ---- レイアウト構築 ----

    private void BuildLayout()
    {
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 左上: CPU / メモリ / GPU / イーサネット
        _tl = MakeBlock(0, 0);
        AddRow(_tl, "cpu", "CPU", [CCpu], 100.0);
        AddRow(_tl, "mem", "メモリ", [CMem], 100.0,
               device: $"{SysPulsar.Core.Metrics.MemoryMonitor.TotalGb:F1} GB");
        AddRow(_tl, "gpu", "GPU", [CGpu], 100.0);
        AddRow(_tl, "net", "イーサネット", [CDown, CUp], null, subLarge: true, subColor: CUp);

        // 右上: プロセス表(残り全部) + イベント件数(下・2行ぶんの固定高)
        _tr = MakeBlock(0, 1);
        _tr.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _tr.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _procTable = new ProcessTable(rows: 12);
        Grid.SetRow(_procTable, 0);
        _tr.Children.Add(_procTable);
        _eventPanel = new CriticalEventPanel();
        Grid.SetRow(_eventPanel, 1);
        _tr.Children.Add(_eventPanel);

        // 左下: ディスク(2 列 x 5 行。行は Online 判定の結果が来てから構築)
        _bl = MakeBlock(1, 0);
        _diskPlaceholder = new TextBlock
        {
            Text = "ディスク情報を取得中…",
            Foreground = MetricRow.FreezeBrush("#8a8a8a"),
            FontSize = 10.7,
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _bl.Children.Add(_diskPlaceholder);

        // 右下: AI Usage(UsageWatcher 統合)
        _br = MakeBlock(1, 1);
        _usageSettings = Usage.Settings.Load();
        _usagePanel = new UsagePanel(_usageSettings);
        _br.Children.Add(_usagePanel);

        // 右クリックメニュー(どのパネル上でも表示。子要素から親へ辿って出る)
        Root.ContextMenu = ExternalTools.BuildContextMenu();
        Root.ContextMenu.Items.Add(new Separator());

        // 表示エリア(レイアウト)の切替。領域数は 1/2/2/4(1x1/1x2/2x1/2x2)。
        // 状態は window-state.json に保存し次回起動時に復元
        var saved = SysPulsar.App.WindowState.LoadRaw();
        _layoutMode = saved?.Layout is 11 or 12 or 21 or 22 ? saved.Layout : 22;
        if (saved != null)
        {
            _showMetrics = saved.ShowMetrics;
            _showProcesses = saved.ShowProcesses;
            _showDisks = saved.ShowDisks;
            _showUsage = saved.ShowUsage;
        }
        var layoutMenu = new MenuItem { Header = "表示エリア" };
        foreach (var (mode, label) in new[]
        {
            (11, "1×1 (領域 1)"), (12, "1×2 (領域 2)"),
            (21, "2×1 (領域 2)"), (22, "2×2 (領域 4)"),
        })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = _layoutMode == mode };
            int m = mode;
            item.Click += (_, _) => SetLayoutMode(m);
            layoutMenu.Items.Add(item);
            _layoutItems[mode] = item;
        }
        Root.ContextMenu.Items.Add(layoutMenu);

        // 表示要素の切替(有効な要素が メトリクス→プロセス＆イベント→ディスク→AI Usage の
        // 順にスロットへ流し込まれる。表示エリアの領域数を超えた分は表示されない)
        var elementMenu = new MenuItem { Header = "表示要素" };
        elementMenu.Items.Add(MakeElementToggle("CPU など (メトリクス)", _showMetrics, v => _showMetrics = v));
        elementMenu.Items.Add(MakeElementToggle("プロセス＆イベント", _showProcesses, v => _showProcesses = v));
        elementMenu.Items.Add(MakeElementToggle("ドライブ (ディスク)", _showDisks, v => _showDisks = v));
        elementMenu.Items.Add(MakeElementToggle("AI Usage (Claude/Kimi)", _showUsage, v => _showUsage = v));
        Root.ContextMenu.Items.Add(elementMenu);

        // 常に手前に表示(状態は window-state.json に保存し次回起動時に復元)
        bool topmost = saved?.Topmost ?? false;
        Topmost = topmost;
        var topmostToggle = new MenuItem { Header = "常に手前に表示", IsCheckable = true, IsChecked = topmost };
        topmostToggle.Click += (_, _) =>
        {
            Topmost = topmostToggle.IsChecked;
            SaveWindowState();
        };
        Root.ContextMenu.Items.Add(topmostToggle);
        Root.ContextMenu.Items.Add(new Separator());

        // AI Usage の Claude / Kimi 個別の表示切替(デフォルト両方表示。
        // 状態は settings.json に保存し次回起動時に復元。
        // 両方非表示なら AI Usage 要素は出ず、そのぶん他の要素が詰めて表示される)
        Root.ContextMenu.Items.Add(MakeUsageToggle("Claude", "claude", _usageSettings.ShowClaude, v => _usageSettings.ShowClaude = v));
        Root.ContextMenu.Items.Add(MakeUsageToggle("Kimi", "kimi", _usageSettings.ShowKimi, v => _usageSettings.ShowKimi = v));
        Root.ContextMenu.Items.Add(new Separator());

        // 手動の更新確認(起動時の自動チェックと同じ流れ。更新があれば DL まで済ませて
        // メニュー先頭に適用項目を挿入、最新版/失敗はメッセージで通知)
        var checkItem = new MenuItem { Header = "更新を確認" };
        checkItem.Click += async (_, _) =>
        {
            checkItem.IsEnabled = false; // 連打防止(完了/失敗後に戻す)
            try { await CheckForUpdateAsync(manual: true); }
            finally { checkItem.IsEnabled = true; }
        };
        Root.ContextMenu.Items.Add(checkItem);

        // 前回の表示エリア・表示要素の状態を復元
        ApplyLayout();
    }

    /// <summary>現在のウィンドウ状態(位置・サイズ・Topmost・表示エリア・表示要素)を
    /// window-state.json に保存する。</summary>
    private void SaveWindowState() =>
        SysPulsar.App.WindowState.Save(new SysPulsar.App.WindowState
        {
            Left = Left, Top = Top, Width = Width, Height = Height,
            Topmost = Topmost, Layout = _layoutMode,
            ShowMetrics = _showMetrics, ShowProcesses = _showProcesses,
            ShowDisks = _showDisks, ShowUsage = _showUsage,
        });

    /// <summary>表示エリアを切り替える(右クリックメニュー「表示エリア」から)。</summary>
    private void SetLayoutMode(int mode)
    {
        _layoutMode = mode;
        foreach (var (m, item) in _layoutItems)
            item.IsChecked = m == mode;
        ApplyLayout();
        SaveWindowState();
    }

    /// <summary>表示要素の切替メニュー項目を作る。</summary>
    private MenuItem MakeElementToggle(string name, bool initial, Action<bool> setter)
    {
        var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = initial };
        item.Click += (_, _) =>
        {
            setter(item.IsChecked);
            ApplyLayout();
            SaveWindowState();
        };
        return item;
    }

    /// <summary>AI Usage のプロバイダ表示切替メニュー項目を作る。</summary>
    private MenuItem MakeUsageToggle(string name, string providerId, bool initial, Action<bool> setter)
    {
        var item = new MenuItem { Header = $"{name} を表示", IsCheckable = true, IsChecked = initial };
        item.Click += (_, _) =>
        {
            setter(item.IsChecked);
            _usageSettings.Save();
            ApplyLayout();
        };
        return item;
    }

    /// <summary>表示エリア(レイアウト)と表示要素の設定をグリッドに適用する。
    /// モードの領域数(1/2/2/4)ぶんのスロットに、有効な表示要素を固定順
    /// (メトリクス→プロセス＆イベント→ディスク→AI Usage)で流し込む。
    /// スロット数を超えた要素は出ない。空いた下段スロットは真上の要素を
    /// 縦に伸ばして埋める(2x2 で AI Usage 非表示時にプロセス領域が縦いっぱいに
    /// なる従来挙動もこれで実現される)。</summary>
    private void ApplyLayout()
    {
        _usagePanel.SetProviderVisible("claude", _usageSettings.ShowClaude);
        _usagePanel.SetProviderVisible("kimi", _usageSettings.ShowKimi);

        bool hasBottom = _layoutMode is 21 or 22;
        bool hasRight = _layoutMode is 12 or 22;

        Root.RowDefinitions[1].Height = new GridLength(hasBottom ? 1 : 0, GridUnitType.Star);
        Root.ColumnDefinitions[1].Width = new GridLength(hasRight ? 1 : 0, GridUnitType.Star);

        // モードのスロット(配置順)
        (int Row, int Col)[] slots = _layoutMode switch
        {
            11 => [(0, 0)],
            12 => [(0, 0), (0, 1)],
            21 => [(0, 0), (1, 0)],
            _ => [(0, 0), (0, 1), (1, 0), (1, 1)],
        };

        // 有効な表示要素(固定順)。AI Usage は Claude/Kimi 両方非表示なら要素自体なし
        bool usageVisible = _showUsage && (_usageSettings.ShowClaude || _usageSettings.ShowKimi);
        var elements = new List<Grid>(4);
        if (_showMetrics) elements.Add(_tl);
        if (_showProcesses) elements.Add(_tr);
        if (_showDisks) elements.Add(_bl);
        if (usageVisible) elements.Add(_br);

        // いったん全要素を隠してスパンを戻してから、スロットに流し込む
        var occupied = new Dictionary<(int Row, int Col), Grid>();
        foreach (var g in new[] { _tl, _tr, _bl, _br })
        {
            g.Visibility = Visibility.Collapsed;
            Grid.SetRowSpan(g, 1);
        }
        for (int i = 0; i < elements.Count && i < slots.Length; i++)
        {
            var (row, col) = slots[i];
            var g = elements[i];
            Grid.SetRow(g, row);
            Grid.SetColumn(g, col);
            g.Margin = col == 0 ? new Thickness(6, 3, 3, 3) : new Thickness(3, 3, 6, 3);
            g.Visibility = Visibility.Visible;
            occupied[(row, col)] = g;
        }
        // 空いた下段スロットは真上の要素を縦に伸ばして埋める
        if (hasBottom)
            for (int c = 0; c <= (hasRight ? 1 : 0); c++)
                if (occupied.TryGetValue((0, c), out var above) && !occupied.ContainsKey((1, c)))
                    Grid.SetRowSpan(above, 2);

        // モードに応じて最小サイズを緩める(小さいモードでは小さく畳めるように)。
        // 2x2 に戻したとき現在サイズが最小を下回っていれば WPF が自動で広げる
        (MinWidth, MinHeight) = _layoutMode switch
        {
            11 => (300.0, 280.0), // 領域 1
            12 => (560.0, 280.0), // 領域 2(横並び)
            21 => (300.0, 500.0), // 領域 2(縦並び)
            _ => (560.0, 500.0),  // 領域 4
        };
    }

    private Grid MakeBlock(int row, int col)
    {
        var block = new Grid
        {
            Margin = col == 0 ? new Thickness(6, 3, 3, 3) : new Thickness(3, 3, 6, 3),
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, col);
        Root.Children.Add(block);
        return block;
    }

    private void AddRow(Grid block, string key, string name, WpfColor[] colors,
        double? fixedMax, string device = "", bool subLarge = false, WpfColor? subColor = null)
    {
        int i = block.RowDefinitions.Count;
        block.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var row = new MetricRow(name, colors, fixedMax, device, subLarge, subColor);
        Grid.SetRow(row, i);
        block.Children.Add(row);
        _rows[key] = row;
    }

    // ---- スレッド ----

    private void StartThreads()
    {
        _samplerThread = new Thread(SampleLoop) { IsBackground = true, Name = "SysPulsar.Sampler" };
        _samplerThread.Start();
        _namesThread = new Thread(NamesLoop) { IsBackground = true, Name = "SysPulsar.Names" };
        _namesThread.Start();
        _eventsThread = new Thread(EventsLoop) { IsBackground = true, Name = "SysPulsar.Events" };
        _eventsThread.Start();

        // AI Usage ポーリング(120秒周期 + 429 バックオフ。通信失敗時は直前値を保持)
        var providers = new List<IUsageProvider>();
        if (_usageSettings.EnableClaude) providers.Add(new ClaudeProvider());
        if (_usageSettings.EnableKimi) providers.Add(new KimiProvider(_config.KimiApiKey));
        foreach (var p in providers) _usagePanel.RegisterProvider(p.Id);
        _usagePoller = new UsagePoller(providers, _usageSettings);
        _usagePoller.SnapshotUpdated += snap =>
        {
            try { Dispatcher.BeginInvoke(() => _usagePanel.UpdateSnapshot(snap)); }
            catch (InvalidOperationException) { /* 終了中 */ }
        };
        _usagePoller.Start();

        // リセットまでのカウントダウン表示の定期更新
        _usageTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _usageTick.Tick += (_, _) => _usagePanel.Tick();
        _usageTick.Start();

        // 自動更新チェック(起動時のみ。あれば DL まで済ませてメニューに出す)
        _ = CheckForUpdateAsync();
    }

    /// <summary>更新チェック。新バージョンがあれば DL・展開まで済ませ、
    /// 右クリックメニューの先頭に適用項目を挿入する(自動・手動で共通)。
    /// manual == true(右クリックメニュー「更新を確認」)のときは、
    /// 最新版 / 確認失敗もメッセージで通知する(自動チェックは黙殺)。</summary>
    private async Task CheckForUpdateAsync(bool manual = false)
    {
        var result = await UpdateChecker.CheckAndDownloadAsync(CancellationToken.None);
        if (_stop)
            return;
        if (result.Info == null)
        {
            if (manual)
            {
                try
                {
                    _ = Dispatcher.BeginInvoke(() => MessageBox.Show(this,
                        result.UpToDate
                            ? $"最新バージョンです (v{UpdateChecker.CurrentVersion})"
                            : "更新を確認できませんでした。ネットワーク接続を確認してください。",
                        "SysPulsar 更新確認", MessageBoxButton.OK,
                        result.UpToDate ? MessageBoxImage.Information : MessageBoxImage.Warning));
                }
                catch (InvalidOperationException) { /* 終了中 */ }
            }
            return;
        }
        var info = result.Info;
        if (_updateOffered)
            return; // 既にメニューに出ている(手動で再確認した場合など)
        _updateOffered = true;
        try
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                var menu = Root.ContextMenu;
                var item = new MenuItem
                {
                    Header = $"v{info.Version} に更新 (ダウンロード済み・クリックで適用)",
                    FontWeight = FontWeights.Bold,
                };
                item.Click += (_, _) =>
                {
                    UpdateChecker.LaunchUpdater(info);
                    _reallyExit = true; // ×ボタンと違いトレイに退避させず完全終了させる
                    Close();
                };
                menu.Items.Insert(0, item);
                menu.Items.Insert(1, new Separator());
            });
        }
        catch (InvalidOperationException) { /* 終了中 */ }
    }

    private void SampleLoop()
    {
        int interval = Math.Max(200, _config.IntervalMs);
        while (!_stop)
        {
            var t0 = Environment.TickCount64;
            Snapshot snap = _monitor.Sample(processLimit: 12);
            if (!_dragging)
            {
                try { Dispatcher.BeginInvoke(() => ApplySnapshot(snap)); }
                catch (InvalidOperationException) { /* 終了中 */ }
            }
            long elapsed = Environment.TickCount64 - t0;
            int wait = (int)Math.Max(0, interval - elapsed);
            if (wait > 0)
                Thread.Sleep(wait);
        }
    }

    private void NamesLoop()
    {
        int refreshMs = Math.Max(5, _config.NamesRefreshSec) * 1000;
        while (!_stop)
        {
            DeviceInfo info = _monitor.GetDeviceInfo(); // 遅い(WMI)。専用スレッドなので OK
            if (!_dragging)
            {
                try { Dispatcher.BeginInvoke(() => ApplyDeviceInfo(info)); }
                catch (InvalidOperationException) { }
            }
            for (int waited = 0; waited < refreshMs && !_stop; waited += 200)
                Thread.Sleep(200);
        }
    }

    /// <summary>システムログのイベント件数(全レベル)を 60 秒周期で確認する。</summary>
    private void EventsLoop()
    {
        while (!_stop)
        {
            IReadOnlyDictionary<byte, int>? counts = null;
            string? error = null;
            try
            {
                counts = CriticalEventPanel.QueryCounts(); // ブロックする。専用スレッドなので OK
            }
            catch (Exception ex)
            {
                error = "イベントログの読み取りに失敗: " + ex.Message;
            }
            if (!_dragging)
            {
                try { Dispatcher.BeginInvoke(() => _eventPanel.SetData(counts, error)); }
                catch (InvalidOperationException) { }
            }
            for (int waited = 0; waited < 60_000 && !_stop; waited += 200)
                Thread.Sleep(200);
        }
    }

    // ---- 描画 ----

    private static string FmtPct(double? v) => v is double d ? $"{d:F0} %" : "—";

    /// <summary>ディスク実速度。500MB/s 以上は "0.9GB/s" のように GB/s 表記。</summary>
    private static string FmtRate(double mbps) =>
        mbps >= 500 ? $"{mbps / 1024.0:F1}GB/s" : $"{mbps:F1} MB/s";

    private void ApplySnapshot(Snapshot snap)
    {
        if (_rows.TryGetValue("cpu", out var cpu))
            cpu.Set(FmtPct(snap.Cpu.Load),
                    snap.Cpu.Ghz is double g ? $"{g:F2} GHz" : "",
                    snap.Cpu.Load);

        if (_rows.TryGetValue("mem", out var mem))
            mem.Set($"{snap.Mem.Percent:F0} %", $"{snap.Mem.UsedGb:F1} GB", snap.Mem.Percent);

        if (_rows.TryGetValue("gpu", out var gpu))
        {
            if (snap.Gpu is GpuSample g)
                gpu.Set(FmtPct(g.Load), g.Temp is double t ? $"{t:F0} °C" : "", g.Load);
            else
                gpu.Set("—", "", (double?)null);
        }

        if (_rows.TryGetValue("net", out var net))
        {
            double? d = snap.Net.DownMbps, u = snap.Net.UpMbps;
            net.Set(d is double dd ? $"↓ {dd:F1} Mbps" : "—",
                    u is double uu ? $"↑ {uu:F1} Mbps" : "",
                    d, u);
        }

        foreach (var (num, row) in _diskRows)
        {
            if (snap.Disks.TryGetValue(num, out var ds))
                row.Set(FmtPct(ds.Busy), ds.Mbps is double m ? FmtRate(m) : "", ds.Busy);
            else
                row.Set("—", "", (double?)null);
        }

        _procTable.SetRows(snap.Processes);
    }

    private void ApplyDeviceInfo(DeviceInfo info)
    {
        if (_rows.TryGetValue("cpu", out var cpu))
            cpu.SetDevice(info.Cpu);
        if (_rows.TryGetValue("mem", out var mem))
        {
            // WMI でモジュール情報が取れたときだけ上書き(取れなければ総容量表示のまま)
            if (info.Mem.Length > 0)
                mem.SetDevice(info.Mem);
        }
        if (_rows.TryGetValue("gpu", out var gpu))
            gpu.SetDevice(info.Gpu);
        if (_rows.TryGetValue("net", out var net))
            net.SetDevice(info.Net);

        BuildDiskRows(info.Disks, info.DiskLetters, info.DiskSpaces);
    }

    /// <summary>
    /// ディスク行の構築(最大 10 台)。左下ブロックを 2 列 x 5 行で使い、
    /// 左・右・左・右の順に詰める(セルは背景スパークライン付きの 2 行表示)。
    /// config に固定割り当てがあればそれを先頭に使い、残りは Online ディスクを
    /// ドライブレター順に自動追加。抜き差し(増減)があれば行を作り直す。
    /// 固定割り当てがなければ Online ディスクをドライブレター順に最大 10 台自動検出。
    /// 自動追加分の表示名はドライブレター+ボリュームラベル("C:システム"、
    /// 複数パーティションは "C:システム F:データ")。2 行目に空き/総量+空き率
    /// ("833/930GB 90%")を併記し、セルから溢れた分はクリップして隠す。
    /// レターが取れないディスクだけ従来の「ディスク N」表記にフォールバック。
    /// </summary>
    private void BuildDiskRows(Dictionary<int, string> models, IReadOnlyDictionary<int, string> letters,
        IReadOnlyDictionary<int, DiskSpaceInfo> spaces)
    {
        // 表示名はレター+ボリュームラベルの組のリスト("C:"+システム")。
        // 複数パーティションはレター毎に並べる。ラベルが無ければ "C:" のみ。
        // レター自体が取れなければ「ディスク N」。間隔制御のため文字列連結せず
        // パーツのまま DiskRow に渡す(label は変更検知用の文字列表現)。
        (string Label, List<(string Letter, string VolLabel)> Parts) AutoName(int n)
        {
            if (!letters.TryGetValue(n, out string? l) || l.Length == 0)
                return ($"ディスク {n}", [($"ディスク {n}", "")]);
            spaces.TryGetValue(n, out var sp);
            var parts = l.Split(':', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => ($"{c}:", sp != null && sp.VolumeLabels.TryGetValue(c, out string? v) ? v : ""))
                .ToList();
            return (string.Join(" ", parts.Select(p => p.Item1 + p.Item2)), parts);
        }

        // 並びはドライブレター順(複合 "C:F:" は先頭レター C を基準)。
        // レターが取れないディスクは後ろに回し、従来どおり番号順。
        int LetterKey(int n) =>
            letters.TryGetValue(n, out string? l) && l.Length > 0 ? l[0] : 0x100 + n;

        var desired = new List<(int num, string label, List<(string Letter, string VolLabel)> parts)>();
        var fixedDisks = _config.Disks;
        if (fixedDisks.Count > 0)
        {
            var fixedNums = fixedDisks.Select(d => d.Number).ToHashSet();
            foreach (var e in fixedDisks.Take(MaxDisks))
                desired.Add((e.Number, e.Label, [(e.Label, "")]));
            // 固定以外の Online ディスクを空き枠へ自動追加(切断されれば消える)
            foreach (int n in models.Keys.Where(n => !fixedNums.Contains(n)).OrderBy(LetterKey))
            {
                if (desired.Count >= MaxDisks)
                    break;
                var a = AutoName(n);
                desired.Add((n, a.Label, a.Parts));
            }
        }
        else
        {
            foreach (int n in models.Keys.OrderBy(LetterKey).Take(MaxDisks))
            {
                var a = AutoName(n);
                desired.Add((n, a.Label, a.Parts));
            }
        }

        // 構成(台数・表示名)が変わっていなければモデル名と空き容量の更新だけ
        if (_disksBuilt && _diskOrder.Select(d => (d.num, d.label)).SequenceEqual(desired.Select(d => (d.num, d.label))))
        {
            foreach (var (num, _, _) in desired)
                if (_diskRows.TryGetValue(num, out var row))
                {
                    if (models.TryGetValue(num, out string? model) && model.Length > 0)
                        row.SetDevice(model);
                    var (text, warn) = FmtSpace(spaces, num);
                    row.SetSpace(text, warn);
                }
            return;
        }

        _disksBuilt = true;
        _diskPlaceholder.Visibility = Visibility.Collapsed;
        _bl.Children.Clear();
        _bl.RowDefinitions.Clear();
        _bl.ColumnDefinitions.Clear();
        _diskRows.Clear();
        _diskOrder = desired;

        // 2 列 x 最大 5 行(最大 10 台)。配置は左・右・左・右の順。
        // 行は常に 4 行以上確保してセル高を保つ(8 台以下なら 1/4、
        // 9 台以上で 5 行=1/5 に縮む。空き行は下に余る)
        _bl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _bl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int rowCount = Math.Max(4, (desired.Count + 1) / 2);
        for (int r = 0; r < rowCount; r++)
            _bl.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < desired.Count; i++)
        {
            var (num, _, parts) = desired[i];
            var row = AddDiskRow(_bl, i / 2, i % 2, num, parts, models.GetValueOrDefault(num, ""));
            var (text, warn) = FmtSpace(spaces, num);
            row.SetSpace(text, warn);
        }
    }

    /// <summary>容量の表示("833GB/930GB 89%" / "46GB/1.82TB 2%" = 空き/総量+空き率)と
    /// 残量僅少フラグ。取れないときは空文字。1TB 以上の値は小数 2 桁の TB 表記。
    /// 空き率 10% 未満で warn=true(橙表示)にする。</summary>
    private static (string Text, bool Warn) FmtSpace(IReadOnlyDictionary<int, DiskSpaceInfo> spaces, int num)
    {
        if (!spaces.TryGetValue(num, out var s) || s.TotalGb <= 0)
            return ("", false);
        double pct = s.FreeGb / s.TotalGb * 100.0;
        return ($"{FmtCap(s.FreeGb)}/{FmtCap(s.TotalGb)} {pct:F0}%", pct < 10.0);
    }

    /// <summary>1TB 以上は TB 表記(10TB 以上は小数 1 桁、それ未満は小数 2 桁)、1TB 未満は整数 GB。</summary>
    private static string FmtCap(double gb) =>
        gb >= 10240.0 ? $"{gb / 1024.0:F1}TB" :
        gb >= 1024.0 ? $"{gb / 1024.0:F2}TB" : $"{gb:F0}GB";

    private DiskRow AddDiskRow(Grid block, int rowIndex, int column, int number,
        List<(string Letter, string VolLabel)> nameParts, string model)
    {
        var row = new DiskRow(nameParts, CDisk, model);
        Grid.SetRow(row, rowIndex);
        Grid.SetColumn(row, column);
        block.Children.Add(row);
        _diskRows[number] = row;
        return row;
    }

    // ---- ドラッグ/リサイズ検出 ----

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (HwndSource?)PresentationSource.FromVisual(this);
        source?.AddHook(WndProc);
        ApplyWindowFrameTheme();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmEnterSizeMove)
            _dragging = true;
        else if (msg == WmExitSizeMove)
            _dragging = false;
        else if (msg == WmSettingChange)
            ApplyWindowFrameTheme(); // OS のテーマ変更に追従
        return IntPtr.Zero;
    }

    // ---- ウィンドウ枠のテーマ ----

    /// <summary>タイトルバーなどのウィンドウ枠を OS のアプリテーマ(ライト/ダーク)に
    /// 合わせる。Personalize\AppsUseLightTheme を読み、DWM の
    /// DWMWA_USE_IMMERSIVE_DARK_MODE に反映する。本体 UI は常にダークのまま。
    /// 属性 20 は 20H1 以降、19 は 1903/1909 用(失敗した側は無視)。</summary>
    private void ApplyWindowFrameTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        bool dark;
        try
        {
            dark = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is int v && v == 0;
        }
        catch
        {
            dark = true; // 読めなければ本体 UI に合わせてダーク
        }
        int useDark = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int)) == 0 ||
            DwmSetWindowAttribute(hwnd, 19, ref useDark, sizeof(int)) == 0;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
