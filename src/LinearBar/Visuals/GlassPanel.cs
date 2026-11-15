using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LinearBar.Visuals;

/// <summary>A decorator that draws a squircle glass surface behind its child.</summary>
public sealed class GlassPanel : Decorator
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(double), typeof(GlassPanel),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(GlassPanel),
        new FrameworkPropertyMetadata(default(Thickness), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ShadowProperty = DependencyProperty.Register(
        nameof(Shadow), typeof(bool), typeof(GlassPanel),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public GlassPanel()
    {
        Loaded += (_, _) => Glass.Changed += InvalidateVisual;
        Unloaded += (_, _) => Glass.Changed -= InvalidateVisual;
    }

    /// <summary>Corner radius; NaN picks one from the height.</summary>
    public double Radius
    {
        get => (double)GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    public bool Shadow
    {
        get => (bool)GetValue(ShadowProperty);
        set => SetValue(ShadowProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        Thickness p = Padding;
        double horizontal = p.Left + p.Right;
        double vertical = p.Top + p.Bottom;
        if (Child is null)
        {
            return new Size(horizontal, vertical);
        }

        Child.Measure(new Size(Math.Max(0, constraint.Width - horizontal), Math.Max(0, constraint.Height - vertical)));
        return new Size(Child.DesiredSize.Width + horizontal, Child.DesiredSize.Height + vertical);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Thickness p = Padding;
        Child?.Arrange(new Rect(
            p.Left,
            p.Top,
            Math.Max(0, arrangeSize.Width - p.Left - p.Right),
            Math.Max(0, arrangeSize.Height - p.Top - p.Bottom)));
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        Size size = RenderSize;
        double radius = double.IsNaN(Radius) ? Math.Round(Math.Min(size.Width, size.Height) * 0.36) : Radius;
        Glass.DrawSurface(drawingContext, new Rect(size), radius, Shadow);
    }
}
