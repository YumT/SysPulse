using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SysPulse.App.Controls;

/// <summary>
/// メトリクス 1 行: 左=デバイス種別+デバイス名 / 中央=現在値+副値(2行) / 右=スパークライン。
/// 3 分割はちょうど 1/3 ずつ(Python 版と同じ)。
/// </summary>
public sealed class MetricRow : Grid
{
    private static readonly Brush PanelBrush = FreezeBrush("#1e1e1e");
    private static readonly Brush FgBrush = FreezeBrush("#e0e0e0");
    private static readonly Brush DimBrush = FreezeBrush("#8a8a8a");

    private readonly TextBlock _deviceLabel;
    private readonly TextBlock _value;
    private readonly TextBlock _sub;
    private readonly Sparkline _spark;

    public MetricRow(string name, Color[] colors, double? fixedMax,
        string device = "", bool subLarge = false, Color? subColor = null)
    {
        Background = PanelBrush;
        Margin = new Thickness(0, 1, 0, 1);

        var inner = new Grid { Margin = new Thickness(8, 4, 8, 4) };
        Children.Add(inner);
        for (int c = 0; c < 3; c++)
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 左: デバイス種別(太字) + デバイス名(小・グレー)
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = FgBrush,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
        });
        _deviceLabel = new TextBlock
        {
            Foreground = DimBrush,
            FontSize = 9.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        left.Children.Add(_deviceLabel);
        Grid.SetColumn(left, 0);
        inner.Children.Add(left);

        // 中央: 現在値(大・色付き) + 副値(小)
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _value = new TextBlock
        {
            Text = "—",
            Foreground = new SolidColorBrush(colors[0]),
            FontSize = 14.7,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
        };
        center.Children.Add(_value);
        _sub = new TextBlock
        {
            Foreground = subColor is Color sc ? new SolidColorBrush(sc) : DimBrush,
            FontSize = subLarge ? 14.7 : 10.7,
            FontWeight = subLarge ? FontWeights.Bold : FontWeights.Normal,
            TextAlignment = TextAlignment.Center,
        };
        center.Children.Add(_sub);
        Grid.SetColumn(center, 1);
        inner.Children.Add(center);

        // 右: スパークライン
        _spark = new Sparkline(colors, fixedMax) { Margin = new Thickness(6, 0, 0, 0) };
        Grid.SetColumn(_spark, 2);
        inner.Children.Add(_spark);

        if (device.Length > 0)
            SetDevice(device);
    }

    /// <summary>デバイス名は 20 文字で省略(Python 版と同じ)。</summary>
    public void SetDevice(string device)
    {
        if (device.Length > 20)
            device = device[..19] + "…";
        _deviceLabel.Text = device;
    }

    public void Set(string value, string sub, params double?[] sparkValues)
    {
        _value.Text = value;
        _sub.Text = sub;
        _spark.Push(sparkValues);
    }

    internal static Brush FreezeBrush(string hex)
    {
        var b = new SolidColorBrush(Sparkline.Hex(hex));
        b.Freeze();
        return b;
    }
}
