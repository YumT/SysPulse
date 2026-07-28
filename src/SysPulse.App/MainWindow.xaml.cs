using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SysPulse.App.Controls;
using SysPulse.Core;
using SysPulse.Core.Models;
using WpfColor = System.Windows.Media.Color;

namespace SysPulse.App;

/// <summary>
/// 2x2 レイアウトの常駐モニター(Python 版 App クラスの移植)。
/// 計測はバックグラウンドスレッド、描画は UI スレッド。
/// ドラッグ/リサイズ中は WM_ENTERSIZEMOVE/EXITSIZEMOVE で検出して描画を止める。
/// </summary>
public partial class MainWindow : Window
{
    private const int MaxDisks = 10;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;

    private static readonly WpfColor CCpu = Sparkline.Hex("#4fc3f7");
    private static readonly WpfColor CMem = Sparkline.Hex("#ba68c8");
    private static readonly WpfColor CGpu = Sparkline.Hex("#ef5350");
    private static readonly WpfColor CDown = Sparkline.Hex("#81c784");
    private static readonly WpfColor CUp = Sparkline.Hex("#fdd835");
    private static readonly WpfColor CDisk = Sparkline.Hex("#90a4ae");

    private readonly SystemMonitor _monitor = new();
    private readonly AppConfig _config;
    private readonly Dictionary<string, MetricRow> _rows = new();
    private readonly Dictionary<int, MetricRow> _diskRows = new();
    private ProcessTable _procTable = null!;
    private Grid _bl = null!;
    private Grid _br = null!;
    private TextBlock _diskPlaceholder = null!;

    private volatile bool _stop;
    private volatile bool _dragging;
    private Thread? _samplerThread;
    private Thread? _namesThread;

    private bool _disksBuilt;
    private List<(int num, string label)> _diskOrder = new();

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
                SysPulse.App.WindowState.Save(Left, Top, Width, Height);
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
            Text = "SysPulse",
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
        if (SysPulse.App.WindowState.Load() is { } s)
        {
            Left = s.Left;
            Top = s.Top;
            Width = Math.Max(MinWidth, s.Width);
            Height = Math.Max(MinHeight, s.Height);
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
        var tl = MakeBlock(0, 0);
        AddRow(tl, "cpu", "CPU", [CCpu], 100.0);
        AddRow(tl, "mem", "メモリ", [CMem], 100.0,
               device: $"{SysPulse.Core.Metrics.MemoryMonitor.TotalGb:F1} GB");
        AddRow(tl, "gpu", "GPU", [CGpu], 100.0);
        AddRow(tl, "net", "イーサネット", [CDown, CUp], null, subLarge: true, subColor: CUp);

        // 右上: プロセス表
        var tr = MakeBlock(0, 1);
        _procTable = new ProcessTable(rows: 8);
        tr.Children.Add(_procTable);

        // 左下・右下: ディスク(行は Online 判定の結果が来てから構築)
        _bl = MakeBlock(1, 0);
        _br = MakeBlock(1, 1);
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
        _samplerThread = new Thread(SampleLoop) { IsBackground = true, Name = "SysPulse.Sampler" };
        _samplerThread.Start();
        _namesThread = new Thread(NamesLoop) { IsBackground = true, Name = "SysPulse.Names" };
        _namesThread.Start();
    }

    private void SampleLoop()
    {
        int interval = Math.Max(200, _config.IntervalMs);
        while (!_stop)
        {
            var t0 = Environment.TickCount64;
            Snapshot snap = _monitor.Sample(processLimit: 8);
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

    // ---- 描画 ----

    private static string FmtPct(double? v) => v is double d ? $"{d:F0} %" : "—";

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
                row.Set(FmtPct(ds.Busy), ds.Mbps is double m ? $"{m:F1} MB/s" : "", ds.Busy);
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

        BuildDiskRows(info.Disks);
    }

    /// <summary>
    /// ディスク行の構築(最大 10 台)。配置は左・右・左・右の交互で、
    /// 奇数の場合は右側の最後の行が空く。
    /// config に固定割り当てがあればそれを先頭に使い、残りは Online ディスクを
    /// 番号順に自動追加。抜き差し(増減)があれば行を作り直す。
    /// 固定割り当てがなければ Online ディスクを番号順に最大 10 台自動検出。
    /// </summary>
    private void BuildDiskRows(Dictionary<int, string> models)
    {
        var desired = new List<(int num, string label)>();
        var fixedDisks = _config.Disks;
        if (fixedDisks.Count > 0)
        {
            var fixedNums = fixedDisks.Select(d => d.Number).ToHashSet();
            foreach (var e in fixedDisks.Take(MaxDisks))
                desired.Add((e.Number, e.Label));
            // 固定以外の Online ディスクを空き枠へ自動追加(切断されれば消える)
            foreach (int n in models.Keys.Where(n => !fixedNums.Contains(n)).OrderBy(n => n))
            {
                if (desired.Count >= MaxDisks)
                    break;
                desired.Add((n, $"ディスク {n}"));
            }
        }
        else
        {
            foreach (int n in models.Keys.OrderBy(n => n).Take(MaxDisks))
                desired.Add((n, $"ディスク {n}"));
        }

        // 構成が変わっていなければモデル名の更新だけ
        if (_disksBuilt && _diskOrder.Select(d => d.num).SequenceEqual(desired.Select(d => d.num)))
        {
            foreach (var (num, _) in desired)
                if (_diskRows.TryGetValue(num, out var row) && models.TryGetValue(num, out string? model) && model.Length > 0)
                    row.SetDevice(model);
            return;
        }

        _disksBuilt = true;
        _diskPlaceholder.Visibility = Visibility.Collapsed;
        _bl.Children.Clear();
        _br.Children.Clear();
        _bl.RowDefinitions.Clear();
        _br.RowDefinitions.Clear();
        _diskRows.Clear();
        _diskOrder = desired;

        // 両ブロックに同じ行数(N)を確保し、左右の行の高さを揃える
        int rowsPerBlock = Math.Max(1, (desired.Count + 1) / 2);
        for (int r = 0; r < rowsPerBlock; r++)
        {
            _bl.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _br.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }
        for (int i = 0; i < desired.Count; i++)
        {
            var block = i % 2 == 0 ? _bl : _br; // 左・右・左・右…
            var (num, label) = desired[i];
            AddDiskRow(block, i / 2, num, label, models.GetValueOrDefault(num, ""));
        }
    }

    private void AddDiskRow(Grid block, int rowIndex, int number, string label, string model)
    {
        var row = new MetricRow(label, [CDisk], 100.0, model);
        Grid.SetRow(row, rowIndex);
        block.Children.Add(row);
        _diskRows[number] = row;
    }

    // ---- ドラッグ/リサイズ検出 ----

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (HwndSource?)PresentationSource.FromVisual(this);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmEnterSizeMove)
            _dragging = true;
        else if (msg == WmExitSizeMove)
            _dragging = false;
        return IntPtr.Zero;
    }
}
