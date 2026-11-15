using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LinearBar.Core.Animation;
using LinearBar.Core.Dock;
using LinearBar.Visuals;

namespace LinearBar.Dock;

/// <summary>Describes the slot under the cursor; raised by <see cref="MagnifyPanel"/>.</summary>
internal readonly record struct DockHover(int Index, double CenterX, double Top);

/// <summary>
/// Lays dock cells out in a centred row and magnifies them around the cursor like the
/// macOS dock.
/// </summary>
/// <remarks>
/// <para>
/// Children are measured and arranged once, at their resting size and position. The
/// magnification is applied purely through render transforms (a uniform scale around the
/// bottom-centre of each cell plus a horizontal shift), so an animation frame costs no
/// measure pass and bitmaps are resampled straight from their full-size source.
/// The geometry comes from <see cref="DockLayout"/> and <see cref="Lens"/> in LinearBar.Core.
/// </para>
/// <para>
/// The panel sits in a transparent, click-through window band above the bar. Windows only
/// delivers mouse input where a layered window has non-zero alpha, so the panel paints an
/// almost invisible "hit plate" behind the cells, covering the magnified dock up to the
/// height of the tallest cell. Without it the cursor would fall through the gaps between
/// magnified icons and the dock would flicker.
/// </para>
/// </remarks>
internal sealed class MagnifyPanel : Panel
{
    /// <summary>Seconds for the lens to grow when the cursor enters the dock.</summary>
    private const double GrowTime = 0.11;

    /// <summary>Seconds for the lens to shrink after the cursor leaves.</summary>
    private const double ShrinkTime = 0.17;

    private static readonly Brush HitPlateBrush = CreateHitPlateBrush();

    public static readonly DependencyProperty SlotWidthProperty = Register(nameof(SlotWidth), 48.0, measure: true);
    public static readonly DependencyProperty CellHeightProperty = Register(nameof(CellHeight), 47.0, measure: true);
    public static readonly DependencyProperty BottomMarginProperty = Register(nameof(BottomMargin), 2.0);
    public static readonly DependencyProperty MaxScaleProperty = Register(nameof(MaxScale), 1.75);
    public static readonly DependencyProperty LensRadiusProperty = Register(nameof(LensRadius), 120.0);
    public static readonly DependencyProperty PlatterHeightProperty = Register(nameof(PlatterHeight), 54.0);
    public static readonly DependencyProperty PlatterBottomProperty = Register(nameof(PlatterBottom), 5.0);
    public static readonly DependencyProperty PlatterRadiusProperty = Register(nameof(PlatterRadius), 19.0);
    public static readonly DependencyProperty LeftInsetProperty = Register(nameof(LeftInset), 0.0);
    public static readonly DependencyProperty RightInsetProperty = Register(nameof(RightInset), 0.0);

    private SlotPlacement[] _slots = [];
    private SmoothDamp _amount = new(0);
    private double _cursor = double.NaN;
    private bool _hovering;
    private bool _ticking;
    private TimeSpan? _lastTick;
    private double _fit = 1;
    private double _origin;
    private DockHover _hover = new(-1, 0, 0);
    private DockHover _reportedHover = new(-1, 0, 0);
    private Rect _platter = Rect.Empty;

    public MagnifyPanel()
    {
        Unloaded += (_, _) => StopTicking();
        Unloaded += (_, _) => Glass.Changed -= InvalidateVisual;
        Loaded += (_, _) => Glass.Changed += InvalidateVisual;

        // Listeners reposition other elements (the name label). Telling them from inside
        // ArrangeOverride would have them change layout in the middle of a layout pass, so
        // wait until the pass has finished.
        LayoutUpdated += (_, _) =>
        {
            if (_hover != _reportedHover)
            {
                _reportedHover = _hover;
                HoverChanged?.Invoke(_hover);
            }
        };
    }

    /// <summary>Raised when the slot under the cursor, or its position, changes.</summary>
    public event Action<DockHover>? HoverChanged;

    /// <summary>Resting width of a slot (icon plus spacing).</summary>
    public double SlotWidth
    {
        get => (double)GetValue(SlotWidthProperty);
        set => SetValue(SlotWidthProperty, value);
    }

    /// <summary>Resting height of a cell (icon plus indicator strip).</summary>
    public double CellHeight
    {
        get => (double)GetValue(CellHeightProperty);
        set => SetValue(CellHeightProperty, value);
    }

    /// <summary>Distance from the bottom of the panel to the bottom of the cells.</summary>
    public double BottomMargin
    {
        get => (double)GetValue(BottomMarginProperty);
        set => SetValue(BottomMarginProperty, value);
    }

    /// <summary>Scale under the cursor; 1 disables magnification.</summary>
    public double MaxScale
    {
        get => (double)GetValue(MaxScaleProperty);
        set => SetValue(MaxScaleProperty, value);
    }

    /// <summary>Reach of the magnification on each side of the cursor.</summary>
    public double LensRadius
    {
        get => (double)GetValue(LensRadiusProperty);
        set => SetValue(LensRadiusProperty, value);
    }

    /// <summary>Height of the glass platter the icons rest on.</summary>
    public double PlatterHeight
    {
        get => (double)GetValue(PlatterHeightProperty);
        set => SetValue(PlatterHeightProperty, value);
    }

    /// <summary>Distance from the bottom of the panel to the bottom of the platter.</summary>
    public double PlatterBottom
    {
        get => (double)GetValue(PlatterBottomProperty);
        set => SetValue(PlatterBottomProperty, value);
    }

    public double PlatterRadius
    {
        get => (double)GetValue(PlatterRadiusProperty);
        set => SetValue(PlatterRadiusProperty, value);
    }

    /// <summary>Current platter bounds in panel coordinates (empty when the dock is empty).</summary>
    public Rect PlatterBounds => _platter;

    /// <summary>Raised when the platter moves or resizes (every frame while it animates).</summary>
    public event Action<Rect>? PlatterChanged;

    /// <summary>Width at the left end of the panel the dock must keep clear (the Start button).</summary>
    public double LeftInset
    {
        get => (double)GetValue(LeftInsetProperty);
        set => SetValue(LeftInsetProperty, value);
    }

    /// <summary>Width at the right end of the panel the dock must keep clear (graphs, tray, clock).</summary>
    public double RightInset
    {
        get => (double)GetValue(RightInsetProperty);
        set => SetValue(RightInsetProperty, value);
    }

    /// <summary>Current strength of the lens, from 0 (resting) to 1 (fully magnified).</summary>
    public double Amount => _amount.Value;

    /// <summary>Slot currently under the cursor.</summary>
    public DockHover Hover => _hover;

    /// <summary>Current scale of every slot, for diagnostics.</summary>
    public IReadOnlyList<SlotPlacement> Slots => _slots;

    /// <summary>
    /// Drives the panel as if the cursor were at <paramref name="x"/> (panel coordinates), or
    /// had left when null. Used by the smoke test when no real pointer input is available.
    /// </summary>
    public void SimulateHover(double? x)
    {
        if (x is { } position)
        {
            SetCursor(position);
        }
        else
        {
            ClearCursor();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var cell = new Size(SlotWidth, CellHeight);
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(cell);
        }

        // Normally the panel is stretched by its parent; these are just sensible minimums.
        return new Size(
            double.IsInfinity(availableSize.Width) ? InternalChildren.Count * SlotWidth : 0,
            double.IsInfinity(availableSize.Height) ? CellHeight + BottomMargin : 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = InternalChildren.Count;
        if (_slots.Length != count)
        {
            _slots = new SlotPlacement[count];
        }

        double slotWidth = SlotWidth;
        double cellHeight = CellHeight;
        double fullMagnification = Math.Max(0, MaxScale - 1);

        // Shrink the whole dock when it would not fit between the widgets at either end,
        // then centre it on the screen as far as those widgets allow.
        double lensMargin = new Lens(fullMagnification, LensRadius).MaxDisplacement;
        double available = Math.Max(0, finalSize.Width - LeftInset - RightInset);
        _fit = DockLayout.FitScale(count, slotWidth, lensMargin, available);

        double slot = slotWidth * _fit;
        _origin = DockLayout.PlaceOrigin(finalSize.Width, LeftInset, RightInset, count * slot, lensMargin * _fit);

        var lens = new Lens(fullMagnification * _amount.Value, LensRadius * _fit);
        DockLayout.Arrange(_slots, _origin, slot, _cursor, lens);

        double top = finalSize.Height - BottomMargin - cellHeight;
        for (int i = 0; i < count; i++)
        {
            UIElement child = InternalChildren[i];
            double restCenter = _origin + (i + 0.5) * slot;

            child.Arrange(new Rect(restCenter - slotWidth / 2, top, slotWidth, cellHeight));

            // Scale around the bottom-centre of the cell, then slide it to its lens position.
            double scale = _fit * _slots[i].Scale;
            Matrix matrix = Matrix.Identity;
            matrix.ScaleAt(scale, scale, slotWidth / 2, cellHeight);
            matrix.Translate(_slots[i].Center - restCenter, 0);

            if (child.RenderTransform is MatrixTransform { IsFrozen: false } transform)
            {
                transform.Matrix = matrix;
            }
            else
            {
                child.RenderTransform = new MatrixTransform(matrix);
            }
        }

        UpdateHover(finalSize, count, slot, cellHeight);
        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        int count = InternalChildren.Count;
        if (count == 0)
        {
            SetPlatter(Rect.Empty);
            return;
        }

        // The platter follows the magnified extent of the dock, like the macOS dock.
        double slot = SlotWidth * _fit;
        double magnification = Math.Max(0, MaxScale - 1) * _amount.Value;
        var lens = new Lens(magnification, LensRadius * _fit);
        (double left, double right) = DockLayout.Extent(count, _origin, slot, _cursor, lens);

        double pad = BarGeometry.PlatterSidePadding;
        double platterTop = RenderSize.Height - PlatterBottom - PlatterHeight;
        var platter = new Rect(Math.Round(left - pad), Math.Round(platterTop), Math.Round(right - left + 2 * pad), PlatterHeight);
        Glass.DrawSurface(drawingContext, platter, PlatterRadius);
        SetPlatter(platter);

        // The hit plate above the platter: see the class remarks. As tall as the tallest
        // cell can currently be.
        double height = Math.Min(RenderSize.Height, BottomMargin + CellHeight * _fit * (1 + magnification));
        double above = RenderSize.Height - height;
        if (right > left && above < platterTop)
        {
            drawingContext.DrawRectangle(HitPlateBrush, null, new Rect(left, above, right - left, platterTop - above));
        }
    }

    private void SetPlatter(Rect platter)
    {
        if (platter != _platter)
        {
            _platter = platter;
            PlatterChanged?.Invoke(platter);
        }
    }

    protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);

        // The plate depends on the number of children but adding one does not re-render us.
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetCursor(e.GetPosition(this).X);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ClearCursor();
    }

    private static DependencyProperty Register(string name, double defaultValue, bool measure = false)
    {
        FrameworkPropertyMetadataOptions options = FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender;
        if (measure)
        {
            options |= FrameworkPropertyMetadataOptions.AffectsMeasure;
        }

        return DependencyProperty.Register(name, typeof(double), typeof(MagnifyPanel), new FrameworkPropertyMetadata(defaultValue, options));
    }

    private static Brush CreateHitPlateBrush()
    {
        // Alpha 1/255: invisible, but enough for the layered window to receive mouse input.
        var brush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        brush.Freeze();
        return brush;
    }

    private void SetCursor(double x)
    {
        _cursor = x;
        _hovering = true;
        _amount.Target = 1;
        Invalidate();
    }

    private void ClearCursor()
    {
        // Keep the last cursor position so the lens shrinks in place instead of jumping.
        _hovering = false;
        _amount.Target = 0;
        Invalidate();
    }

    /// <summary>Re-runs arrange (which re-applies the lens) and re-renders the hit plate.</summary>
    private void Invalidate()
    {
        InvalidateVisual();
        if (!_amount.IsSettled)
        {
            StartTicking();
        }
    }

    private void StartTicking()
    {
        if (_ticking)
        {
            return;
        }

        _ticking = true;
        _lastTick = null;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopTicking()
    {
        if (!_ticking)
        {
            return;
        }

        _ticking = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        TimeSpan now = ((RenderingEventArgs)e).RenderingTime;
        if (_lastTick is not { } last)
        {
            _lastTick = now;
            return;
        }

        // WPF can raise Rendering more than once for the same frame.
        if (now == last)
        {
            return;
        }

        _lastTick = now;
        bool moving = _amount.Step((now - last).TotalSeconds, _hovering ? GrowTime : ShrinkTime);
        InvalidateVisual();

        if (!moving)
        {
            StopTicking();
        }
    }

    private void UpdateHover(Size finalSize, int count, double slot, double cellHeight)
    {
        int index = _hovering ? DockLayout.HitTest(count, _origin, slot, _cursor) : -1;

        // Reported to listeners once the layout pass is over; see the constructor.
        _hover = index < 0
            ? new DockHover(-1, 0, 0)
            : new DockHover(
                index,
                _slots[index].Center,
                finalSize.Height - BottomMargin - cellHeight * _fit * _slots[index].Scale);
    }
}
