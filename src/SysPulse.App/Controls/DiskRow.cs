using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SysPulse.App.Controls;

/// <summary>
/// ディスク 1 台ぶんのセル。左下エリアを 2 列 x 5 行(最大 10 台)で使う。
/// 各セルは横幅が狭い(ウィンドウの約 1/4)ため:
///  - 左半分: 表示名 / 空き・総量・空き率 / デバイス名の 3 行
///  - 右半分: スパークラインを背景いっぱいに描き、その前面に
///    使用率 + 実速度の 2 行を重ねる
/// </summary>
public sealed class DiskRow : Grid
{
    private static readonly Brush PanelBrush = MetricRow.FreezeBrush("#1e1e1e");
    private static readonly Brush FgBrush = MetricRow.FreezeBrush("#e0e0e0");
    private static readonly Brush DimBrush = MetricRow.FreezeBrush("#8a8a8a");
    private static readonly Brush WarnBrush = MetricRow.FreezeBrush("#e6a01e");

    private readonly TextBlock _deviceLabel;
    private readonly TextBlock _spaceLabel;
    private readonly TextBlock _pct;
    private readonly TextBlock _rate;
    private readonly Sparkline _spark;

    public DiskRow(IReadOnlyList<(string Letter, string VolLabel)> nameParts, Color color, string device = "")
    {
        Background = PanelBrush;
        Margin = new Thickness(0, 1, 0, 1);

        var inner = new Grid { Margin = new Thickness(6, 2, 6, 2), ClipToBounds = true };
        Children.Add(inner);
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 左半分: 1 行目=表示名(太字。レター右は 3px、2 つ目以降のレターは左 5px)、
        // 2 行目=空き/総量+空き率、3 行目=デバイス名
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var line1 = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < nameParts.Count; i++)
        {
            var (letter, volLabel) = nameParts[i];
            line1.Children.Add(new TextBlock
            {
                Text = letter,
                Foreground = FgBrush,
                FontSize = 10.7,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(i == 0 ? 0 : 5, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (volLabel.Length > 0)
                line1.Children.Add(new TextBlock
                {
                    Text = volLabel,
                    Foreground = FgBrush,
                    FontSize = 10.7,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(3, 0, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
        }
        left.Children.Add(line1);
        _spaceLabel = new TextBlock
        {
            Foreground = DimBrush,
            FontSize = 9.5,
            Margin = new Thickness(0, 2, 0, -1),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        left.Children.Add(_spaceLabel);
        _deviceLabel = new TextBlock
        {
            Foreground = DimBrush,
            FontSize = 9.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        left.Children.Add(_deviceLabel);
        Grid.SetColumn(left, 0);
        inner.Children.Add(left);

        // 右半分: 背景=スパークライン、前面=使用率+実速度の 2 行
        var right = new Grid { Margin = new Thickness(4, 0, 0, 0) };
        _spark = new Sparkline([color], 100.0);
        right.Children.Add(_spark);

        // グラフに紛れないよう、数値の背後には半透明の暗いプレートを敷く
        var overlay = new Border
        {
            Background = MetricRow.FreezeBrush("#B3141414"),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var overlayText = new StackPanel();
        overlay.Child = overlayText;
        _pct = new TextBlock
        {
            Text = "—",
            Foreground = new SolidColorBrush(color),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
        };
        overlayText.Children.Add(_pct);
        _rate = new TextBlock
        {
            Foreground = DimBrush,
            FontSize = 9.5,
            TextAlignment = TextAlignment.Center,
        };
        overlayText.Children.Add(_rate);
        right.Children.Add(overlay);

        Grid.SetColumn(right, 1);
        inner.Children.Add(right);

        if (device.Length > 0)
            SetDevice(device);
    }

    /// <summary>デバイス名は 14 文字で省略(セルが狭いため)。</summary>
    public void SetDevice(string device)
    {
        if (device.Length > 14)
            device = device[..13] + "…";
        _deviceLabel.Text = device;
    }

    /// <summary>容量の表示("833/930GB 90%" = 空き/総量+空き率)。取れないときは空文字。
    /// warn=true(残量僅少)のときは橙気味の色にする。</summary>
    public void SetSpace(string space, bool warn = false)
    {
        _spaceLabel.Text = space;
        _spaceLabel.Foreground = warn ? WarnBrush : DimBrush;
    }

    public void Set(string pct, string rate, double? busy)
    {
        _pct.Text = pct;
        _rate.Text = rate;
        _spark.Push(busy);
    }
}
