using System.Windows;
using System.Windows.Media;

namespace OpenCleaner.UI;

/// <summary>Anneau de score dessiné à la main, sans dépendance externe.</summary>
public sealed class ScoreRing : FrameworkElement
{
    public static readonly DependencyProperty ScoreProperty = DependencyProperty.Register(
        nameof(Score), typeof(int), typeof(ScoreRing),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxScoreProperty = DependencyProperty.Register(
        nameof(MaxScore), typeof(int), typeof(ScoreRing),
        new FrameworkPropertyMetadata(100, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
        nameof(RingThickness), typeof(double), typeof(ScoreRing),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Score
    {
        get => (int)GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    public int MaxScore
    {
        get => (int)GetValue(MaxScoreProperty);
        set => SetValue(MaxScoreProperty, value);
    }

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public ScoreRing()
    {
        Width = 150;
        Height = 150;
    }

    public void RefreshTheme() => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        double size = Math.Min(ActualWidth, ActualHeight);
        double thickness = Math.Clamp(RingThickness, 2, Math.Max(2, size / 3));
        double radius = (size - thickness) / 2;
        if (radius <= 0) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double ratio = MaxScore <= 0 ? 0 : Math.Clamp((double)Score / MaxScore, 0, 1);
        DrawArc(drawingContext, ResourceBrush("Border", Colors.DimGray), 1, center, radius, thickness);

        var color = ratio >= 0.90 ? ResourceColor("Accent", Colors.SeaGreen)
            : ratio >= 0.75 ? ResourceColor("RiskSafe", Colors.MediumSeaGreen)
            : ratio >= 0.55 ? ResourceColor("WarnColor", Colors.Goldenrod)
            : ResourceColor("Danger", Colors.IndianRed);
        DrawArc(drawingContext, new SolidColorBrush(color), ratio, center, radius, thickness);
    }

    private static void DrawArc(DrawingContext drawingContext, Brush brush, double fraction,
        Point center, double radius, double thickness)
    {
        if (fraction <= 0) return;
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();

        if (fraction >= 0.9995)
        {
            drawingContext.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        const double startAngle = -Math.PI / 2;
        double endAngle = startAngle + fraction * Math.PI * 2;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointOnCircle(center, radius, startAngle), isFilled: false, isClosed: false);
            context.ArcTo(PointOnCircle(center, radius, endAngle), new Size(radius, radius), 0,
                isLargeArc: fraction > 0.5, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angle) =>
        new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));

    private Color ResourceColor(string key, Color fallback) =>
        TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;

    private Brush ResourceBrush(string key, Color fallback) =>
        TryFindResource(key) as SolidColorBrush ?? new SolidColorBrush(fallback);
}
