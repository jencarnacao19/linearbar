using System.Windows;
using System.Windows.Media;
using LinearBar.Core.Metrics;

namespace LinearBar.Widgets;

/// <summary>
/// Draws a <see cref="SampleHistory"/> as a small line graph with a filled area underneath,
/// optionally with a second series drawn as a plain line.
/// </summary>
/// <remarks>
/// Rendering is done directly in <see cref="OnRender"/> with frozen geometry: no child
/// elements, no layout, and a redraw only when <see cref="Version"/> changes.
/// </remarks>
internal sealed class SparklineControl : FrameworkElement
{
    private const double StrokeThickness = 1.25;

    public static readonly DependencyProperty HistoryProperty = Register<SampleHistory?>(nameof(History), null);
    public static readonly DependencyProperty SecondaryHistoryProperty = Register<SampleHistory?>(nameof(SecondaryHistory), null);
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), 100.0);
    public static readonly DependencyProperty VersionProperty = Register(nameof(Version), 0);
    public static readonly DependencyProperty StrokeProperty = Register<Brush?>(nameof(Stroke), null);
    public static readonly DependencyProperty FillProperty = Register<Brush?>(nameof(Fill), null);
    public static readonly DependencyProperty SecondaryStrokeProperty = Register<Brush?>(nameof(SecondaryStroke), null);

    private (double X, double Y)[] _points = [];

    /// <summary>Samples of the main series.</summary>
    public SampleHistory? History
    {
        get => (SampleHistory?)GetValue(HistoryProperty);
        set => SetValue(HistoryProperty, value);
    }

    /// <summary>Samples of the optional second series.</summary>
    public SampleHistory? SecondaryHistory
    {
        get => (SampleHistory?)GetValue(SecondaryHistoryProperty);
        set => SetValue(SecondaryHistoryProperty, value);
    }

    /// <summary>Value drawn at the top edge.</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Bind to a counter that changes with every new sample to trigger a redraw.</summary>
    public int Version
    {
        get => (int)GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush? SecondaryStroke
    {
        get => (Brush?)GetValue(SecondaryStrokeProperty);
        set => SetValue(SecondaryStrokeProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= StrokeThickness * 2)
        {
            return;
        }

        // The secondary line goes underneath so the main series stays the most readable.
        DrawSeries(drawingContext, SecondaryHistory, SecondaryStroke, null, width, height);
        DrawSeries(drawingContext, History, Stroke, Fill, width, height);
    }

    private static DependencyProperty Register<T>(string name, T defaultValue)
    {
        return DependencyProperty.Register(
            name,
            typeof(T),
            typeof(SparklineControl),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
    }

    private void DrawSeries(DrawingContext drawingContext, SampleHistory? history, Brush? stroke, Brush? fill, double width, double height)
    {
        if (history is null || stroke is null || history.Count < 2)
        {
            return;
        }

        if (_points.Length < history.Count)
        {
            _points = new (double X, double Y)[history.Capacity];
        }

        // Keep the line fully inside the control: inset by its thickness at top and bottom.
        double plotHeight = height - StrokeThickness * 2;
        int count = Sparkline.BuildPoints(history, Maximum, width, plotHeight, _points);
        if (count < 2)
        {
            return;
        }

        var line = new StreamGeometry();
        using (StreamGeometryContext context = line.Open())
        {
            context.BeginFigure(ToPoint(_points[0]), isFilled: false, isClosed: false);
            for (int i = 1; i < count; i++)
            {
                context.LineTo(ToPoint(_points[i]), isStroked: true, isSmoothJoin: true);
            }
        }

        line.Freeze();

        if (fill is not null)
        {
            var area = new StreamGeometry();
            using (StreamGeometryContext context = area.Open())
            {
                context.BeginFigure(new Point(_points[0].X, height), isFilled: true, isClosed: true);
                for (int i = 0; i < count; i++)
                {
                    context.LineTo(ToPoint(_points[i]), isStroked: false, isSmoothJoin: false);
                }

                context.LineTo(new Point(_points[count - 1].X, height), isStroked: false, isSmoothJoin: false);
            }

            area.Freeze();
            drawingContext.DrawGeometry(fill, null, area);
        }

        var pen = new Pen(stroke, StrokeThickness) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        drawingContext.DrawGeometry(null, pen, line);

        static Point ToPoint((double X, double Y) point) => new(point.X, point.Y + StrokeThickness);
    }
}
