using System.Windows;
using System.Windows.Media;

namespace HecsMonitor.Controls;

/// <summary>0~1 값을 원형 호로 표시 (절감률).</summary>
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ValueBrushProperty = DependencyProperty.Register(nameof(ValueBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.MediumSeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public Brush ValueBrush { get => (Brush)GetValue(ValueBrushProperty); set => SetValue(ValueBrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness * 2) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double rad = (size - Thickness) / 2;

        dc.DrawEllipse(null, new Pen(TrackBrush, Thickness), c, rad, rad);

        double v = Math.Clamp(double.IsNaN(Value) ? 0 : Value, 0, 1);
        if (v <= 0.001) return;
        var pen = new Pen(ValueBrush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (v >= 0.999)
        {
            dc.DrawEllipse(null, pen, c, rad, rad);
            return;
        }
        double a = v * 2 * Math.PI;
        var start = new Point(c.X, c.Y - rad);
        var end = new Point(c.X + rad * Math.Sin(a), c.Y - rad * Math.Cos(a));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(rad, rad), 0, v > 0.5, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}
