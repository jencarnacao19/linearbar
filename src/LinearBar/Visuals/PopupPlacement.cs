using System.Windows.Controls.Primitives;

namespace LinearBar.Visuals;

/// <summary>Pop-ups open away from the screen edge the bar is on.</summary>
internal static class PopupPlacement
{
    /// <summary>True while the bar is at the top of the screen.</summary>
    public static bool BarAtTop { get; set; }

    /// <summary>Placement for menus anchored to bar elements.</summary>
    public static PlacementMode Menu => BarAtTop ? PlacementMode.Bottom : PlacementMode.Top;
}
