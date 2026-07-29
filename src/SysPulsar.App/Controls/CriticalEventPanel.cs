using System.Diagnostics.Eventing.Reader;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SysPulsar.App.Controls;

/// <summary>
/// システムログのイベント件数パネル。
/// 重大 / エラー / 警告 / 情報の 24 時間以内の件数だけを 2 行
/// (見出し「イベント」+ レベル別件数)で表示する。詳細(直近イベント)は出さない。
/// System ログは標準ユーザーで読めるため管理者権限は不要(Security ログは不可)。
/// 取得はブロックするので必ずバックグラウンドスレッドから QueryCounts を呼び、
/// 結果を UI スレッドで SetData に渡すこと。
/// </summary>
public sealed class CriticalEventPanel : Grid
{
    private static readonly Brush PanelBrush = MetricRow.FreezeBrush("#1e1e1e");
    private static readonly Brush DimBrush = MetricRow.FreezeBrush("#8a8a8a");
    private static readonly Brush CritBrush = MetricRow.FreezeBrush("#ef5350");
    private static readonly Brush ErrBrush = MetricRow.FreezeBrush("#e6a01e");
    private static readonly Brush WarnBrush = MetricRow.FreezeBrush("#fdd835");
    private static readonly Brush InfoBrush = MetricRow.FreezeBrush("#8a8a8a");

    /// <summary>件数の表示順(レベル, 名前, ブラシ)。</summary>
    private static readonly (byte Level, string Name, Brush Brush)[] Levels =
    [
        (1, "重大", CritBrush),
        (2, "エラー", ErrBrush),
        (3, "警告", WarnBrush),
        (4, "情報", InfoBrush),
    ];

    private const int CountWindowMs = 24 * 60 * 60 * 1000; // 24 時間

    private readonly Dictionary<byte, TextBlock> _countLabels = new();
    private readonly TextBlock _note;

    public CriticalEventPanel()
    {
        Background = PanelBrush;
        Margin = new Thickness(0, 1, 0, 1);

        var inner = new Grid { Margin = new Thickness(8, 3, 8, 3) };
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(inner);

        // 1 行目: 見出し(件数が 24 時間以内の集計であることもここで示す)
        inner.Children.Add(new TextBlock
        {
            Text = "イベント (24時間以内の件数)",
            Foreground = DimBrush,
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
        });

        // 2 行目: レベル別件数(色分け)
        var counts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        foreach (var (level, name, brush) in Levels)
        {
            var label = new TextBlock
            {
                Text = $"{name} —",
                Foreground = brush,
                FontSize = 9.5,
                Margin = new Thickness(0, 0, 10, 0),
            };
            counts.Children.Add(label);
            _countLabels[level] = label;
        }
        Grid.SetRow(counts, 1);
        inner.Children.Add(counts);

        // 取得失敗時の注記(通常は非表示)
        _note = new TextBlock
        {
            Foreground = CritBrush,
            FontSize = 9.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = Visibility.Collapsed,
        };
        Grid.SetRow(_note, 1);
        inner.Children.Add(_note);
    }

    /// <summary>件数を反映する。error != null のときは注記に置き換える。</summary>
    public void SetData(IReadOnlyDictionary<byte, int>? counts, string? error = null)
    {
        if (error != null)
        {
            _note.Text = "⚠ " + error;
            _note.Visibility = Visibility.Visible;
            return;
        }
        _note.Visibility = Visibility.Collapsed;
        if (counts == null)
            return;
        foreach (var (level, name, _) in Levels)
            if (counts.TryGetValue(level, out int n) && _countLabels.TryGetValue(level, out var label))
                label.Text = $"{name} {n}";
    }

    /// <summary>全レベルの 24 時間以内の件数を返す。失敗時は例外を投げる
    /// (呼び出し側で注記表示に変換する)。</summary>
    public static IReadOnlyDictionary<byte, int> QueryCounts()
    {
        var result = new Dictionary<byte, int>();
        foreach (var (level, _, _) in Levels)
            result[level] = CountWithin24h(level);
        return result;
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
