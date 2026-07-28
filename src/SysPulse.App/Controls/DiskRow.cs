using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SysPulse.App.Controls;

/// <summary>
/// ディスク 1 行: 表示名 / デバイス名 / 使用率 / 実速度。グラフなし。
/// 左下パネルに全台数を縦に並べるコンパクト行。
/// </summary>
public sealed class DiskRow : Grid
{
    private static readonly Brush PanelBrush = MetricRow.FreezeBrush("#1e1e1e");
    private static readonly Brush FgBrush = MetricRow.FreezeBrush("#e0e0e0");
    private static readonly Brush DimBrush = MetricRow.FreezeBrush("#8a8a8a");
    private static readonly Brush DiskBrush = MetricRow.FreezeBrush("#90a4ae");

    private readonly TextBlock _deviceLabel;
    private readonly TextBlock _pct;
    private readonly TextBlock _rate;

    public DiskRow(string name, string device = "")
    {
        Background = PanelBrush;
        Margin = new Thickness(0, 1, 0, 1);

        var inner = new Grid { Margin = new Thickness(8, 2, 8, 2) };
        Children.Add(inner);
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 表示名(太字)
        var label = new TextBlock
        {
            Text = name,
            Foreground = FgBrush,
            FontSize = 10.7,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        inner.Children.Add(label);

        // デバイス名(小・グレー)
        _deviceLabel = new TextBlock
        {
            Foreground = DimBrush,
            FontSize = 9.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
        };
        Grid.SetColumn(_deviceLabel, 1);
        inner.Children.Add(_deviceLabel);

        // 使用率(色付き・太字)
        _pct = new TextBlock
        {
            Text = "—",
            Foreground = DiskBrush,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Right,
            MinWidth = 46,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_pct, 2);
        inner.Children.Add(_pct);

        // 実速度(小・グレー)
        _rate = new TextBlock
        {
            Foreground = DimBrush,
            FontSize = 10.7,
            TextAlignment = TextAlignment.Right,
            MinWidth = 66,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_rate, 3);
        inner.Children.Add(_rate);

        if (device.Length > 0)
            SetDevice(device);
    }

    /// <summary>デバイス名は 20 文字で省略(MetricRow と同じ)。</summary>
    public void SetDevice(string device)
    {
        if (device.Length > 20)
            device = device[..19] + "…";
        _deviceLabel.Text = device;
    }

    public void Set(string pct, string rate)
    {
        _pct.Text = pct;
        _rate.Text = rate;
    }
}
