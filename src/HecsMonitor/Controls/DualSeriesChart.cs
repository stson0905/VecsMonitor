using System.Globalization;
using System.Windows;
using System.Windows.Media;
using HecsMonitor.Services;

namespace HecsMonitor.Controls;

/// <summary>원본(A) 대 HECS(B) 비트레이트를 겹쳐 그리는 경량 영역 차트.</summary>
public sealed class DualSeriesChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesAProperty = Reg(nameof(SeriesA), typeof(IReadOnlyList<double>), null);
    public static readonly DependencyProperty SeriesBProperty = Reg(nameof(SeriesB), typeof(IReadOnlyList<double>), null);
    public static readonly DependencyProperty VersionProperty = Reg(nameof(Version), typeof(int), 0);
    public static readonly DependencyProperty CapacityProperty = Reg(nameof(Capacity), typeof(int), 60);
    public static readonly DependencyProperty BrushAProperty = Reg(nameof(BrushA), typeof(Brush), Brushes.Orange);
    public static readonly DependencyProperty BrushBProperty = Reg(nameof(BrushB), typeof(Brush), Brushes.Cyan);
    public static readonly DependencyProperty ShowAxisProperty = Reg(nameof(ShowAxis), typeof(bool), false);

    static DependencyProperty Reg(string name, Type t, object def) =>
        DependencyProperty.Register(name, t, typeof(DualSeriesChart),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double> SeriesA { get => (IReadOnlyList<double>)GetValue(SeriesAProperty); set => SetValue(SeriesAProperty, value); }
    public IReadOnlyList<double> SeriesB { get => (IReadOnlyList<double>)GetValue(SeriesBProperty); set => SetValue(SeriesBProperty, value); }
    public int Version { get => (int)GetValue(VersionProperty); set => SetValue(VersionProperty, value); }
    public int Capacity { get => (int)GetValue(CapacityProperty); set => SetValue(CapacityProperty, value); }
    public Brush BrushA { get => (Brush)GetValue(BrushAProperty); set => SetValue(BrushAProperty, value); }
    public Brush BrushB { get => (Brush)GetValue(BrushBProperty); set => SetValue(BrushBProperty, value); }
    public bool ShowAxis { get => (bool)GetValue(ShowAxisProperty); set => SetValue(ShowAxisProperty, value); }

    static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), 1) { DashStyle = new DashStyle(new double[] { 2, 4 }, 0) });
    static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x88, 0xD0, 0xDA, 0xE6)));
    static readonly Typeface LabelFace = new("Segoe UI");

    static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 8 || h < 8) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        int cap = Math.Max(2, Capacity);
        double max = Math.Max(Max(SeriesA, cap), Max(SeriesB, cap));
        max = NiceCeil(max <= 0 ? 1e6 : max * 1.12);

        double left = 0, top = 2, plotW = w, plotH = h - 2;
        if (ShowAxis)
        {
            left = 52; top = 8; plotW = w - left; plotH = h - top - 4;
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            for (int i = 0; i <= 4; i++)
            {
                double y = top + plotH * i / 4;
                dc.DrawLine(GridPen, new Point(left, y), new Point(w, y));
                var ft = new FormattedText(Fmt.RateText(max * (4 - i) / 4).Replace("—", "0"), CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, LabelFace, 10, LabelBrush, dpi);
                dc.DrawText(ft, new Point(left - ft.Width - 8, y - ft.Height / 2));
            }
        }

        var r = new Rect(left, top, plotW, plotH);
        Draw(dc, SeriesA, BrushA, max, cap, r);
        Draw(dc, SeriesB, BrushB, max, cap, r);
    }

    static double Max(IReadOnlyList<double> s, int cap)
    {
        if (s == null) return 0;
        double m = 0;
        for (int i = Math.Max(0, s.Count - cap); i < s.Count; i++) m = Math.Max(m, s[i]);
        return m;
    }

    static double NiceCeil(double v)
    {
        double exp = Math.Pow(10, Math.Floor(Math.Log10(v)));
        double f = v / exp;
        double nice = f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10;
        return nice * exp;
    }

    static void Draw(DrawingContext dc, IReadOnlyList<double> s, Brush brush, double max, int cap, Rect r)
    {
        if (s == null || s.Count < 2) return;
        int start = Math.Max(0, s.Count - cap);
        int n = s.Count - start;
        double dx = r.Width / (cap - 1);
        double x0 = r.Right - (n - 1) * dx;

        Point P(int i) => new(x0 + i * dx, r.Bottom - Math.Clamp(s[start + i] / max, 0, 1) * (r.Height - 2) - 1);

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(P(0), false, false);
            for (int i = 1; i < n; i++) g.LineTo(P(i), true, true);
        }
        line.Freeze();

        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(x0, r.Bottom), true, true);
            for (int i = 0; i < n; i++) g.LineTo(P(i), true, false);
            g.LineTo(new Point(x0 + (n - 1) * dx, r.Bottom), true, false);
        }
        area.Freeze();

        var c = (brush as SolidColorBrush)?.Color ?? Colors.White;
        var fill = new LinearGradientBrush(Color.FromArgb(0x55, c.R, c.G, c.B), Color.FromArgb(0x00, c.R, c.G, c.B), 90);
        fill.Freeze();
        var pen = new Pen(brush, 1.6) { LineJoin = PenLineJoin.Round };
        pen.Freeze();

        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, pen, line);
        dc.DrawEllipse(brush, null, P(n - 1), 2.6, 2.6);
    }
}
