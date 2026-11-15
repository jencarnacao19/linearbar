using System.Windows;
using System.Windows.Media;

namespace LinearBar.Visuals;

/// <summary>
/// An app icon. With <see cref="ShowTile"/> the icon sits on a frosted squircle tile, so
/// icons of every shape and era look like one family (as on macOS and iOS).
/// </summary>
public sealed class IconTile : FrameworkElement
{
    /// <summary>Size of the icon relative to its tile.</summary>
    private const double IconScale = 0.66;

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(ImageSource), typeof(IconTile),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(
        nameof(Tint), typeof(Color?), typeof(IconTile),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowTileProperty = DependencyProperty.Register(
        nameof(ShowTile), typeof(bool), typeof(IconTile),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public IconTile()
    {
        // Icons are 256 px bitmaps drawn small and then magnified: resample them well.
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Loaded += (_, _) => Glass.Changed += InvalidateVisual;
        Unloaded += (_, _) => Glass.Changed -= InvalidateVisual;
    }

    public ImageSource? Icon
    {
        get => (ImageSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Colour the tile is tinted with, usually the icon's dominant colour.</summary>
    public Color? Tint
    {
        get => (Color?)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    public bool ShowTile
    {
        get => (bool)GetValue(ShowTileProperty);
        set => SetValue(ShowTileProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double size = Math.Min(RenderSize.Width, RenderSize.Height);
        if (size <= 0)
        {
            return;
        }

        var bounds = new Rect((RenderSize.Width - size) / 2, (RenderSize.Height - size) / 2, size, size);

        if (ShowTile || Icon is null)
        {
            Glass.DrawTile(drawingContext, bounds, Tint);
        }

        if (Icon is { } icon)
        {
            double iconSize = ShowTile ? size * IconScale : size;
            var iconBounds = new Rect(
                bounds.X + (size - iconSize) / 2,
                bounds.Y + (size - iconSize) / 2,
                iconSize,
                iconSize);
            drawingContext.DrawImage(icon, iconBounds);
        }
    }
}
