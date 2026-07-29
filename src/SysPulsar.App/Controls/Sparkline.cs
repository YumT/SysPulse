using System.Windows;
using System.Windows.Media;

namespace SysPulsar.App.Controls;

/// <summary>
/// スパークライン(履歴 120 サンプル、右詰め)。
/// PORTING.md の確定仕様:
///  - 背景 #1a1a1a、枠 #3a3a3a、中央グリッド線 #2c2c2c
///  - 系列色の 22% 輝度で塗りつぶし → その上に線(2 パス描画。
///    逆順だと後の系列の塗りつぶしで先の線が隠れる)
///  - 固定最大 or 自動スケール(最大値 x1.15)
/// WPF 標準の retained-mode 描画(OnRender)なので 1 秒周期の更新はほぼタダ。
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public const int Capacity = 120;

    private static readonly Brush BackgroundBrush = Freeze(new SolidColorBrush(Hex("#1a1a1a")));
    private static readonly Pen BorderPen = FreezePen(Hex("#3a3a3a"), 1.0);
    private static readonly Pen GridPen = FreezePen(Hex("#2c2c2c"), 1.0);

    private readonly List<double?>[] _series;
    private readonly Pen[] _linePens;
    private readonly Brush[] _fillBrushes;
    private readonly double? _fixedMax;

    public Sparkline(Color[] colors, double? fixedMax)
    {
        _series = colors.Select(_ => new List<double?>(Capacity + 1)).ToArray();
        _linePens = colors.Select(c => FreezePen(c, 1.0)).ToArray();
        _fillBrushes = colors.Select(c => Freeze(new SolidColorBrush(Scale(c, 0.22)))).ToArray();
        _fixedMax = fixedMax;
        SnapsToDevicePixels = true;
    }

    /// <summary>各系列に 1 サンプル追加(null は欠測=線を切る)。</summary>
    public void Push(params double?[] values)
    {
        values ??= [];
        for (int i = 0; i < _series.Length; i++)
        {
            double? v = i < values.Length ? values[i] : null;
            var list = _series[i];
            list.Add(v);
            if (list.Count > Capacity)
                list.RemoveAt(0);
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 8 || h < 8)
            return;

        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h));
        dc.DrawLine(GridPen, new Point(1, Math.Round(h / 2) + 0.5), new Point(w - 1, Math.Round(h / 2) + 0.5));

        int count = _series[0].Count;
        if (count > 0)
        {
            double max = _fixedMax ?? AutoMax();
            if (max <= 0)
                max = 1;

            double step = (w - 2) / (Capacity - 1);
            double x0 = w - 1 - (count - 1) * step; // 右詰め

            double Y(double v) => 1 + (h - 2) * (1.0 - Math.Clamp(v, 0, max) / max);

            // パス 1: 全系列の塗りつぶし
            for (int s = 0; s < _series.Length; s++)
            {
                var geo = BuildFill(_series[s], count, x0, step, Y, h);
                if (geo != null)
                    dc.DrawGeometry(_fillBrushes[s], null, geo);
            }
            // パス 2: 全系列の線
            for (int s = 0; s < _series.Length; s++)
            {
                var geo = BuildLine(_series[s], count, x0, step, Y);
                if (geo != null)
                    dc.DrawGeometry(null, _linePens[s], geo);
            }
        }

        dc.DrawRectangle(null, BorderPen, new Rect(0.5, 0.5, w - 1, h - 1));
    }

    private double AutoMax()
    {
        double max = 0;
        foreach (var list in _series)
            foreach (var v in list)
                if (v is double d && d > max)
                    max = d;
        return max * 1.15;
    }

    private static StreamGeometry? BuildFill(List<double?> data, int count, double x0, double step,
        Func<double, double> y, double h)
    {
        StreamGeometry? geo = null;
        StreamGeometryContext? ctx = null;
        bool open = false;

        for (int i = 0; i < count; i++)
        {
            if (data[i] is double v)
            {
                double x = x0 + i * step;
                if (!open)
                {
                    geo ??= new StreamGeometry();
                    ctx ??= geo.Open();
                    ctx.BeginFigure(new Point(x, h - 1), true /* isFilled */, true /* isClosed */);
                    ctx.LineTo(new Point(x, y(v)), true, false);
                    open = true;
                }
                else
                {
                    ctx!.LineTo(new Point(x, y(v)), true, false);
                }
            }
            else if (open)
            {
                double x = x0 + (i - 1) * step;
                ctx!.LineTo(new Point(x, h - 1), true, false);
                open = false;
            }
        }
        if (open)
            ctx!.LineTo(new Point(x0 + (count - 1) * step, h - 1), true, false);
        ctx?.Close();
        return geo;
    }

    private static StreamGeometry? BuildLine(List<double?> data, int count, double x0, double step,
        Func<double, double> y)
    {
        StreamGeometry? geo = null;
        StreamGeometryContext? ctx = null;
        bool open = false;

        for (int i = 0; i < count; i++)
        {
            if (data[i] is double v)
            {
                double x = x0 + i * step;
                if (!open)
                {
                    geo ??= new StreamGeometry();
                    ctx ??= geo.Open();
                    ctx.BeginFigure(new Point(x, y(v)), false, false);
                    open = true;
                }
                else
                {
                    ctx!.LineTo(new Point(x, y(v)), true, false);
                }
            }
            else
            {
                open = false;
            }
        }
        ctx?.Close();
        return geo;
    }

    internal static Color Hex(string s) => (Color)ColorConverter.ConvertFromString(s);

    internal static Color Scale(Color c, double f) =>
        Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
    private static Pen FreezePen(Color c, double thickness)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
