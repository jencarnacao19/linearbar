using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LinearBar.Core.Metrics;

namespace LinearBar.Visuals;

/// <summary>
/// A <see cref="Border"/> replacement with squircle corners: background, outline and
/// (optionally) clipping of the child all follow the continuous-corner shape.
/// </summary>
public sealed class SquircleBorder : Decorator
{
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(Brush), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(double), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(default(Thickness), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ClipContentProperty = DependencyProperty.Register(
        nameof(ClipContent), typeof(bool), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public Brush? BorderBrush
    {
        get => (Brush?)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>Corner radius; NaN = 30 % of the shorter side; a large value gives a pill.</summary>
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

    public bool ClipContent
    {
        get => (bool)GetValue(ClipContentProperty);
        set => SetValue(ClipContentProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        Thickness p = Padding;
        if (Child is null)
        {
            return new Size(p.Left + p.Right, p.Top + p.Bottom);
        }

        Child.Measure(new Size(Math.Max(0, constraint.Width - p.Left - p.Right), Math.Max(0, constraint.Height - p.Top - p.Bottom)));
        return new Size(Child.DesiredSize.Width + p.Left + p.Right, Child.DesiredSize.Height + p.Top + p.Bottom);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Thickness p = Padding;
        Child?.Arrange(new Rect(p.Left, p.Top, Math.Max(0, arrangeSize.Width - p.Left - p.Right), Math.Max(0, arrangeSize.Height - p.Top - p.Bottom)));
        Clip = ClipContent ? Shape(arrangeSize) : null;
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Background is null && BorderBrush is null)
        {
            return;
        }

        drawingContext.DrawGeometry(Background, null, Shape(RenderSize));
        if (BorderBrush is not null)
        {
            var inner = new Rect(0.5, 0.5, Math.Max(0, RenderSize.Width - 1), Math.Max(0, RenderSize.Height - 1));
            var pen = new Pen(BorderBrush, 1);
            pen.Freeze();
            drawingContext.DrawGeometry(null, pen, SquircleGeometry.Create(inner, Math.Max(0, ResolveRadius(RenderSize) - 0.5), Glass.Smoothing));
        }
    }

    private Geometry Shape(Size size) => SquircleGeometry.Create(new Rect(size), ResolveRadius(size), Glass.Smoothing);

    private double ResolveRadius(Size size) => double.IsNaN(Radius) ? Math.Min(size.Width, size.Height) * 0.3 : Radius;
}

/// <summary>A plugin or widget icon: a named line icon, an image, an emoji or a character.</summary>
public sealed class IconView : FrameworkElement
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(IconView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ImageProperty = DependencyProperty.Register(
        nameof(Image), typeof(ImageSource), typeof(IconView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
        nameof(Brush), typeof(Brush), typeof(IconView),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public IconView()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public ImageSource? Image
    {
        get => (ImageSource?)GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    public Brush Brush
    {
        get => (Brush)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        if (Image is { } image)
        {
            drawingContext.PushClip(SquircleGeometry.Create(bounds, bounds.Width * 0.25, Glass.Smoothing));
            drawingContext.DrawImage(image, bounds);
            drawingContext.Pop();
            return;
        }

        Icons.Draw(drawingContext, Icon, bounds, Brush);
    }
}

/// <summary>A thin gauge drawn as a squircle pill track with a squircle pill fill.</summary>
public sealed class PillGauge : FrameworkElement
{
    private static readonly Brush Track = Freeze(new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(PillGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(PillGauge),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double w = RenderSize.Width, h = RenderSize.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        drawingContext.DrawGeometry(Track, null, SquircleGeometry.Create(new Rect(0, 0, w, h), h / 2, Glass.Smoothing));

        double fill = Math.Clamp(double.IsFinite(Value) ? Value : 0, 0, 1) * w;
        if (fill > 0.5)
        {
            // Keep a visible dot even for tiny values.
            fill = Math.Max(fill, h);
            drawingContext.DrawGeometry(Fill, null, SquircleGeometry.Create(new Rect(0, 0, fill, h), h / 2, Glass.Smoothing));
        }
    }

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>The Start button glyph: four squircle tiles.</summary>
public sealed class StartGlyph : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(StartGlyph),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double size = Math.Min(RenderSize.Width, RenderSize.Height);
        double gap = Math.Round(size * 0.12);
        double tile = (size - gap) / 2;
        double x0 = (RenderSize.Width - size) / 2, y0 = (RenderSize.Height - size) / 2;

        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < 2; column++)
            {
                var bounds = new Rect(x0 + column * (tile + gap), y0 + row * (tile + gap), tile, tile);
                drawingContext.DrawGeometry(Fill, null, SquircleGeometry.Create(bounds, tile * 0.32, Glass.Smoothing));
            }
        }
    }
}
