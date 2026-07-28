using System.Diagnostics.Eventing.Reader;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SysPulse.App.Controls;

/// <summary>
/// システムログのイベント監視パネル。
/// 重大 / エラー / 警告 / 情報の 4 タブで出し分け、選択中レベルの直近 N 件を
/// 2 行(日時+ソース / メッセージ先頭行)で表示する。
/// 重大・エラー・警告のタブには 24 時間以内の件数を併記する。
/// System ログは標準ユーザーで読めるため管理者権限は不要(Security ログは不可)。
/// 取得はブロックするので必ずバックグラウンドスレッドから QueryAll を呼び、
/// 結果を UI スレッドで SetData に渡すこと。
/// </summary>
public sealed class CriticalEventPanel : Grid
{
    private static readonly Brush PanelBrush = MetricRow.FreezeBrush("#1e1e1e");
    private static readonly Brush FgBrush = MetricRow.FreezeBrush("#e0e0e0");
    private static readonly Brush DimBrush = MetricRow.FreezeBrush("#8a8a8a");
    private static readonly Brush CritBrush = MetricRow.FreezeBrush("#ef5350");
    private static readonly Brush ErrBrush = MetricRow.FreezeBrush("#e6a01e");
    private static readonly Brush WarnBrush = MetricRow.FreezeBrush("#fdd835");
    private static readonly Brush InfoBrush = MetricRow.FreezeBrush("#8a8a8a");

    /// <summary>タブの定義順(レベル, 名前, ブラシ)。</summary>
    private static readonly (byte Level, string Name, Brush Brush)[] Tabs =
    [
        (1, "重大", CritBrush),
        (2, "エラー", ErrBrush),
        (3, "警告", WarnBrush),
        (4, "情報", InfoBrush),
    ];

    private const int CountWindowMs = 24 * 60 * 60 * 1000; // 24 時間

    /// <summary>1 件ぶん。When=null は取得失敗などの注記行。</summary>
    public sealed record Entry(DateTime? When, string Provider, long Id, string Text, byte Level);

    /// <summary>1 レベルぶんの取得結果。Count24h は 24 時間以内の件数(情報は集計しないので -1)。</summary>
    public sealed record LevelData(int Count24h, IReadOnlyList<Entry> Recent);

    private readonly Dictionary<byte, TextBlock> _tabLabels = new();
    private readonly Dictionary<byte, Border> _tabUnderlines = new();
    private readonly StackPanel _stack;
    private byte _selected = 1;
    private IReadOnlyDictionary<byte, LevelData>? _data;

    public CriticalEventPanel()
    {
        Background = PanelBrush;
        Margin = new Thickness(0, 1, 0, 1);

        var inner = new Grid { Margin = new Thickness(8, 4, 8, 4) };
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Children.Add(inner);

        // タブバー
        var tabBar = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (level, name, brush) in Tabs)
        {
            var label = new TextBlock
            {
                Text = name,
                Foreground = brush,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 1),
            };
            var underline = new Border
            {
                Height = 1.5,
                Background = brush,
                Visibility = Visibility.Collapsed,
            };
            var tab = new StackPanel();
            tab.Children.Add(label);
            tab.Children.Add(underline);
            var hit = new Border
            {
                Child = tab,
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand,
            };
            byte lv = level;
            hit.MouseLeftButtonDown += (_, _) => SelectTab(lv);
            tabBar.Children.Add(hit);
            _tabLabels[level] = label;
            _tabUnderlines[level] = underline;
        }
        // タブバー右端に件数の意味を注記(件数は 24 時間以内の集計であることが分かりにくいため)
        var tabBarArea = new DockPanel();
        var note = new TextBlock
        {
            Text = "※ 件数は24時間以内",
            Foreground = DimBrush,
            FontSize = 8.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(note, Dock.Right);
        tabBarArea.Children.Add(note);
        tabBarArea.Children.Add(tabBar); // 最後の子が残り幅いっぱいに広がる
        inner.Children.Add(tabBarArea);

        _stack = new StackPanel { Margin = new Thickness(0, 3, 0, 0) };
        Grid.SetRow(_stack, 1);
        inner.Children.Add(_stack);
        _stack.Children.Add(new TextBlock
        {
            Text = "システムログのイベントを確認中…",
            Foreground = DimBrush,
            FontSize = 9.5,
        });

        UpdateTabVisuals();
    }

    private void SelectTab(byte level)
    {
        _selected = level;
        UpdateTabVisuals();
        Render();
    }

    private void UpdateTabVisuals()
    {
        foreach (var (level, _, _) in Tabs)
        {
            bool sel = level == _selected;
            _tabUnderlines[level].Visibility = sel ? Visibility.Visible : Visibility.Collapsed;
            _tabLabels[level].Opacity = sel ? 1.0 : 0.55;
        }
    }

    public void SetData(IReadOnlyDictionary<byte, LevelData> data)
    {
        _data = data;
        // タブの件数表示を更新(重大・エラー・警告のみ。情報は集計しない)
        foreach (var (level, name, _) in Tabs)
        {
            if (data.TryGetValue(level, out var d) && d.Count24h >= 0)
                _tabLabels[level].Text = $"{name} {d.Count24h}";
            else
                _tabLabels[level].Text = name;
        }
        Render();
    }

    private void Render()
    {
        _stack.Children.Clear();
        if (_data == null || !_data.TryGetValue(_selected, out var d))
            return;

        var name = Tabs.First(t => t.Level == _selected).Name;
        if (d.Recent.Count == 0)
        {
            _stack.Children.Add(new TextBlock
            {
                Text = $"{name}イベントなし",
                Foreground = DimBrush,
                FontSize = 9.5,
            });
            return;
        }

        foreach (var e in d.Recent)
        {
            if (e.When is not { } when)
            {
                // 取得失敗の注記
                _stack.Children.Add(new TextBlock
                {
                    Text = "⚠ " + e.Text,
                    Foreground = CritBrush,
                    FontSize = 9.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                continue;
            }

            _stack.Children.Add(new TextBlock
            {
                Text = $"{when:M/d H:mm}  {e.Provider} (#{e.Id})",
                Foreground = Tabs.First(t => t.Level == _selected).Brush,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            _stack.Children.Add(new TextBlock
            {
                Text = e.Text,
                Foreground = FgBrush,
                FontSize = 9.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 3),
            });
        }
    }

    /// <summary>全レベルの直近 count 件と、重大・エラー・警告の 24 時間以内件数を返す。
    /// 失敗時は各レベルに注記行 1 件を返す。</summary>
    public static IReadOnlyDictionary<byte, LevelData> QueryAll(int count)
    {
        var result = new Dictionary<byte, LevelData>();
        try
        {
            foreach (var (level, _, _) in Tabs)
            {
                var recent = QueryRecent(level, count);
                // 件数集計は重大・エラー・警告のみ(情報は量が多く意味も薄い)
                int count24h = level <= 3 ? CountWithin24h(level) : -1;
                result[level] = new LevelData(count24h, recent);
            }
        }
        catch (Exception ex)
        {
            result.Clear();
            foreach (var (level, _, _) in Tabs)
                result[level] = new LevelData(-1,
                    [new Entry(null, "", 0, "イベントログの読み取りに失敗: " + ex.Message, 0)]);
        }
        return result;
    }

    /// <summary>指定レベルの直近 count 件を新しい順に返す。</summary>
    private static List<Entry> QueryRecent(byte level, int count)
    {
        var list = new List<Entry>();
        var query = new EventLogQuery("System", PathType.LogName,
            $"*[System[(Level={level})]]")
        {
            ReverseDirection = true, // 新しい順
        };
        using var reader = new EventLogReader(query);
        for (int i = 0; i < count; i++)
        {
            using var rec = reader.ReadEvent();
            if (rec == null)
                break;
            string text = "";
            try { text = rec.FormatDescription() ?? ""; }
            catch { /* メッセージ DLL 欠落など */ }
            // 先頭行だけ使う(複数行・長文をそのまま出さない)
            int nl = text.IndexOfAny(['\r', '\n']);
            if (nl >= 0)
                text = text[..nl];
            list.Add(new Entry(rec.TimeCreated, rec.ProviderName ?? "", rec.Id,
                text, rec.Level ?? 0));
        }
        return list;
    }

    /// <summary>指定レベルの 24 時間以内の件数を数える。</summary>
    private static int CountWithin24h(byte level)
    {
        var query = new EventLogQuery("System", PathType.LogName,
            $"*[System[(Level={level}) and TimeCreated[timediff(@SystemTime) <= {CountWindowMs}]]]");
        using var reader = new EventLogReader(query);
        int n = 0;
        while (reader.ReadEvent() is { } rec)
        {
            rec.Dispose();
            n++;
        }
        return n;
    }
}
