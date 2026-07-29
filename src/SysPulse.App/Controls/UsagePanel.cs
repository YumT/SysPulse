using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SysPulse.App.Usage;

namespace SysPulse.App.Controls;

/// <summary>
/// 右下パネル: AI Usage(UsageWatcher 統合)。
/// プロバイダ毎にゲージ(ラベル+% / バー / リセット時刻+カウントダウン)を並べる。
/// 描画は WidgetForm (WinForms/GDI+) の WPF 移植。UpdateSnapshot は UI スレッドで呼ぶこと。
/// </summary>
public sealed class UsagePanel : Grid
{
    private static readonly Brush PanelBrush = MetricRow.FreezeBrush("#1e1e1e");
    private static readonly Brush FgBrush = MetricRow.FreezeBrush("#e0e0e0");
    private static readonly Brush DimBrush = MetricRow.FreezeBrush("#8a8a8a");
    private static readonly Brush BarBgBrush = MetricRow.FreezeBrush("#33333c");
    private static readonly Brush OkBrush = MetricRow.FreezeBrush("#22aa44");
    private static readonly Brush WarnBrush = MetricRow.FreezeBrush("#e6a01e");
    private static readonly Brush CritBrush = MetricRow.FreezeBrush("#d23232");
    private static readonly Brush NoneBrush = MetricRow.FreezeBrush("#787878");

    private readonly Settings _settings;
    private readonly StackPanel _body;
    private readonly TextBlock _placeholder;
    private readonly Dictionary<string, ProviderSection> _sections = new();
    private readonly List<string> _order = new(); // プロバイダの表示順
    private readonly HashSet<string> _hidden = new(); // 右クリックメニューで非表示にされたプロバイダ

    public UsagePanel(Settings settings)
    {
        _settings = settings;

        // ヘッダー行は持たない(下段の高さを節約するため)。ゲージ群を直接並べる
        _body = new StackPanel();
        Children.Add(_body);

        _placeholder = new TextBlock
        {
            Text = "取得中…",
            Foreground = DimBrush,
            FontSize = 10.7,
            Margin = new Thickness(8, 4, 8, 4),
        };
        _body.Children.Add(_placeholder);
    }

    /// <summary>プロバイダの表示順を先に登録する(WidgetForm.RegisterProvider 相当)。</summary>
    public void RegisterProvider(string providerId)
    {
        if (!_order.Contains(providerId))
            _order.Add(providerId);
    }

    /// <summary>プロバイダ単位の表示/非表示を切り替える(右クリックメニューから)。
    /// セクション未作成(初回スナップショット前)でも _hidden に記録しておき、
    /// 作成時に反映する。パネル全体の表示/非表示は MainWindow 側で行う。</summary>
    public void SetProviderVisible(string providerId, bool visible)
    {
        if (visible)
            _hidden.Remove(providerId);
        else
            _hidden.Add(providerId);
        if (_sections.TryGetValue(providerId, out var sec))
            sec.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public Brush ColorFor(double pct)
    {
        if (pct >= _settings.ThresholdRed) return CritBrush;
        if (pct >= _settings.ThresholdOrange) return WarnBrush;
        return OkBrush;
    }

    public void UpdateSnapshot(UsageSnapshot snap)
    {
        _placeholder.Visibility = Visibility.Collapsed;

        if (!_sections.TryGetValue(snap.ProviderId, out var sec))
        {
            sec = new ProviderSection(snap.DisplayName);
            _sections[snap.ProviderId] = sec;
            // 登録順に並ぶよう挿入位置を決める
            int index = _body.Children.Count;
            int myOrder = _order.IndexOf(snap.ProviderId);
            if (myOrder >= 0)
            {
                for (int i = 0; i < _body.Children.Count; i++)
                {
                    if (_body.Children[i] is not FrameworkElement fe ||
                        fe.Tag is not string pid) continue;
                    int other = _order.IndexOf(pid);
                    if (other > myOrder) { index = i; break; }
                }
            }
            sec.Tag = snap.ProviderId;
            sec.Visibility = _hidden.Contains(snap.ProviderId) ? Visibility.Collapsed : Visibility.Visible;
            _body.Children.Insert(index, sec);
        }

        // 警告(認証/通信)表示
        string? warn = null;
        Brush warnBrush = WarnBrush;
        if (snap.AuthMessage != null)
            warn = "⚠ " + snap.AuthMessage;
        else if (snap.Gauges.Count == 0 && snap.Error != null)
        {
            warn = "⚠ 通信失敗: " + snap.Error;
            warnBrush = CritBrush;
        }
        else if (snap.Error != null)
            warn = "⚠ 通信失敗のため直前値を表示中";
        sec.SetWarning(warn, warnBrush);

        // ゲージ(本数は変わり得るので差分管理)
        foreach (var g in snap.Gauges)
            sec.SetGauge(g.Label, g.Percent, g.ResetAtUtc, ColorFor(g.Percent));
    }

    /// <summary>カウントダウン表示だけ更新する(定期呼び出し用)。</summary>
    public void Tick()
    {
        foreach (var sec in _sections.Values)
            sec.Tick();
    }

    // ---- プロバイダ 1 社ぶん ----

    private sealed class ProviderSection : Border
    {
        private readonly TextBlock _warn;
        private readonly StackPanel _gauges;
        private readonly Dictionary<string, GaugeRow> _rows = new();

        public ProviderSection(string displayName)
        {
            Background = PanelBrush;
            Padding = new Thickness(8, 4, 8, 4);
            Margin = new Thickness(0, 1, 0, 1);

            var stack = new StackPanel();
            Child = stack;

            stack.Children.Add(new TextBlock
            {
                Text = displayName,
                Foreground = DimBrush,
                FontSize = 10.7,
                FontWeight = FontWeights.Bold,
            });

            _warn = new TextBlock
            {
                Foreground = WarnBrush,
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };
            stack.Children.Add(_warn);

            _gauges = new StackPanel();
            stack.Children.Add(_gauges);
        }

        public void SetWarning(string? text, Brush brush)
        {
            if (text == null)
            {
                _warn.Visibility = Visibility.Collapsed;
                return;
            }
            _warn.Text = text;
            _warn.Foreground = brush;
            _warn.Visibility = Visibility.Visible;
        }

        public void SetGauge(string label, double pct, DateTimeOffset? resetUtc, Brush color)
        {
            if (!_rows.TryGetValue(label, out var row))
            {
                row = new GaugeRow(label);
                _rows[label] = row;
                _gauges.Children.Add(row);
            }
            row.Set(pct, resetUtc, color);
        }

        public void Tick()
        {
            foreach (var row in _rows.Values)
                row.Tick();
        }
    }

    // ---- ゲージ 1 本 ----

    private sealed class GaugeRow : Grid
    {
        private readonly TextBlock _pctLabel;
        private readonly Border _fill;
        private readonly Border _bar;
        private readonly TextBlock _resetLabel;
        private double _pct;
        private DateTimeOffset? _resetUtc;

        public GaugeRow(string label)
        {
            Margin = new Thickness(0, 2, 0, 0);
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ラベル + %
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = FgBrush,
                FontSize = 10.7,
            });
            _pctLabel = new TextBlock
            {
                Text = "—",
                Foreground = NoneBrush,
                FontSize = 10.7,
                FontWeight = FontWeights.Bold,
            };
            Grid.SetColumn(_pctLabel, 1);
            top.Children.Add(_pctLabel);
            Grid.SetRow(top, 0);
            Children.Add(top);

            // バー(背景の上に左詰めで使用量を重ねる)
            _bar = new Border
            {
                Background = BarBgBrush,
                CornerRadius = new CornerRadius(3.5),
                Height = 7,
                Margin = new Thickness(0, 1, 0, 0),
            };
            _fill = new Border
            {
                Background = NoneBrush,
                CornerRadius = new CornerRadius(3.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0,
            };
            _bar.Child = _fill;
            _bar.SizeChanged += (_, _) => UpdateFill();
            Grid.SetRow(_bar, 1);
            Children.Add(_bar);

            // リセット時刻 + カウントダウン
            _resetLabel = new TextBlock
            {
                Foreground = DimBrush,
                FontSize = 9.5,
                Margin = new Thickness(2, 0, 0, 0),
            };
            Grid.SetRow(_resetLabel, 2);
            Children.Add(_resetLabel);
        }

        public void Set(double pct, DateTimeOffset? resetUtc, Brush color)
        {
            _pct = Math.Clamp(pct, 0, 100);
            _pctLabel.Text = $"{pct:0}%";
            _pctLabel.Foreground = color;
            _fill.Background = color;
            _resetUtc = resetUtc;
            Tick();
            UpdateFill();
        }

        public void Tick()
        {
            _resetLabel.Text = _resetUtc is { } r
                ? $"⟳ {r.LocalDateTime:M/d H:mm}（{FormatCountdown(r)}）"
                : "";
        }

        private void UpdateFill()
        {
            double w = _bar.ActualWidth * _pct / 100.0;
            _fill.Width = w > 0 ? Math.Max(w, 7) : 0;
        }

        private static string FormatCountdown(DateTimeOffset resetUtc)
        {
            var span = resetUtc - DateTimeOffset.Now;
            if (span <= TimeSpan.Zero) return "まもなくリセット";
            if (span.TotalDays >= 1) return $"あと {(int)span.TotalDays}日{span.Hours}時間";
            if (span.TotalHours >= 1) return $"あと {(int)span.TotalHours}時間{span.Minutes}分";
            return $"あと {Math.Max(1, span.Minutes)}分";
        }
    }
}
