using System.Diagnostics.Eventing.Reader;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SysPulse.App.Controls;

/// <summary>
/// システムログの「重大」(Level=1)イベント監視パネル。
/// 直近 N 件を 2 行(日時+ソース / メッセージ先頭行)で表示する。
/// System ログは標準ユーザーで読めるため管理者権限は不要(Security ログは不可)。
/// 取得はブロックするので必ずバックグラウンドスレッドから QueryRecent を呼び、
/// 結果を UI スレッドで SetEvents に渡すこと。
/// </summary>
public sealed class CriticalEventPanel : Grid
{
    private static readonly Brush PanelBrush = MetricRow.FreezeBrush("#1e1e1e");
    private static readonly Brush FgBrush = MetricRow.FreezeBrush("#e0e0e0");
    private static readonly Brush DimBrush = MetricRow.FreezeBrush("#8a8a8a");
    private static readonly Brush CritBrush = MetricRow.FreezeBrush("#ef5350");
    private static readonly Brush ErrBrush = MetricRow.FreezeBrush("#e6a01e");
    private static readonly Brush WarnBrush = MetricRow.FreezeBrush("#fdd835");

    /// <summary>1 件ぶん。When=null は取得失敗などの注記行。</summary>
    public sealed record Entry(DateTime? When, string Provider, long Id, string Text, byte Level);

    private readonly StackPanel _stack;

    public CriticalEventPanel()
    {
        Background = PanelBrush;
        Margin = new Thickness(0, 1, 0, 1);

        var inner = new Grid { Margin = new Thickness(8, 4, 8, 4) };
        Children.Add(inner);
        _stack = new StackPanel();
        inner.Children.Add(_stack);
        _stack.Children.Add(new TextBlock
        {
            Text = "システムログの重大イベントを確認中…",
            Foreground = DimBrush,
            FontSize = 9.5,
        });
    }

    public void SetEvents(IReadOnlyList<Entry> events)
    {
        _stack.Children.Clear();
        if (events.Count == 0)
        {
            _stack.Children.Add(new TextBlock
            {
                Text = "重大イベントなし",
                Foreground = DimBrush,
                FontSize = 9.5,
            });
            return;
        }

        foreach (var e in events)
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

            var levelBrush = e.Level switch
            {
                1 => CritBrush,   // 重大
                2 => ErrBrush,    // エラー
                3 => WarnBrush,   // 警告
                _ => DimBrush,    // 情報
            };
            _stack.Children.Add(new TextBlock
            {
                Text = $"{when:M/d H:mm}  {e.Provider} (#{e.Id})",
                Foreground = levelBrush,
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

    /// <summary>直近 count 件を新しい順に返す。失敗時は注記行 1 件を返す。
    /// TODO(表示確認用): 現在は重大以外(Level 2〜4)も含めている。最終的は Level=1 のみ。</summary>
    public static IReadOnlyList<Entry> QueryRecent(int count)
    {
        var list = new List<Entry>();
        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[(Level=2 or Level=3 or Level=4)]]")
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
        }
        catch (Exception ex)
        {
            list.Add(new Entry(null, "", 0, "イベントログの読み取りに失敗: " + ex.Message, 0));
        }
        return list;
    }
}
