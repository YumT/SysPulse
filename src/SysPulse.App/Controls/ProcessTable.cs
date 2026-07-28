using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SysPulse.Core.Models;

namespace SysPulse.App.Controls;

/// <summary>CPU 負荷上位プロセスの表(CPU 降順 8 行、Idle は Core 側で除外済み)。</summary>
public sealed class ProcessTable : Grid
{
    private static readonly (string Text, bool Left)[] Headers =
    [
        ("プロセス", true), ("CPU %", false), ("メモリ %", false), ("ディスク", false), ("ネット", false),
    ];

    private static readonly Brush PanelBrush = MetricRow.FreezeBrush("#1e1e1e");
    private static readonly Brush FgBrush = MetricRow.FreezeBrush("#e0e0e0");
    private static readonly Brush DimBrush = MetricRow.FreezeBrush("#8a8a8a");
    private static readonly Brush CpuBrush = MetricRow.FreezeBrush("#4fc3f7");

    private readonly TextBlock[,] _cells;

    /// <summary>CPU 負荷上位プロセスの表(CPU 降順 8 行、Idle は Core 側で除外済み)。
    /// 右上をイベント監視と上下分割するため行間を詰めたコンパクト表示。</summary>
    public ProcessTable(int rows = 8)
    {
        Background = PanelBrush;
        Margin = new Thickness(0, 1, 0, 1);

        var inner = new Grid { Margin = new Thickness(8, 0, 8, 0) };
        Children.Add(inner);

        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 90 });
        for (int c = 1; c < Headers.Length; c++)
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 40 });
        // 行は内容量ぴったり(Auto)にして行間を最小に。フォントは元の大きさのまま
        for (int r = 0; r <= rows; r++)
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int c = 0; c < Headers.Length; c++)
            inner.Children.Add(Cell(Headers[c].Text, DimBrush, Headers[c].Left, 0, c));

        _cells = new TextBlock[rows, Headers.Length];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < Headers.Length; c++)
            {
                var tb = Cell("", c == 1 ? CpuBrush : FgBrush, Headers[c].Left, r + 1, c);
                _cells[r, c] = tb;
                inner.Children.Add(tb);
            }
    }

    private static TextBlock Cell(string text, Brush fg, bool left, int row, int col)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = fg,
            FontSize = 10.7,
            TextAlignment = left ? TextAlignment.Left : TextAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        return tb;
    }

    public void SetRows(IReadOnlyList<ProcSample> rows)
    {
        for (int r = 0; r < _cells.GetLength(0); r++)
        {
            if (r < rows.Count)
            {
                var p = rows[r];
                string name = p.Name;
                if (name.Length > 14)
                    name = name[..13] + "…";
                _cells[r, 0].Text = name;
                _cells[r, 1].Text = $"{p.Cpu:F1}";
                _cells[r, 2].Text = p.Mem is double m ? $"{m:F1}" : "—";
                _cells[r, 3].Text = p.Disk is double d ? $"{d:F1}" : "—";
                _cells[r, 4].Text = "—"; // プロセス毎の通信量は OS API 非対応(PORTING.md)
            }
            else
            {
                for (int c = 0; c < Headers.Length; c++)
                    _cells[r, c].Text = "";
            }
        }
    }
}
