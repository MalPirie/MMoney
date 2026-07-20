using MauiReactor;
using MauiReactor.Shapes;
using Microsoft.Maui.Graphics;

namespace Mobiorum.Material3;

/// <summary>
/// One item in a <see cref="Menu"/>. Its <see cref="Icon"/> (an optional leading glyph) and <see cref="Label"/>
/// are stable identity, set at construction; its tap callback is set fluently. A plain config object — the menu
/// does the rendering and owns no state, mirroring <see cref="NavDestination"/>.
/// </summary>
public sealed class MenuItem
{
    private Action? _onSelected;

    /// <summary>Creates an item with a leading icon.</summary>
    public MenuItem(string icon, string label)
    {
        Icon = icon;
        Label = label;
    }

    /// <summary>Creates a label-only item (no leading icon).</summary>
    public MenuItem(string label) : this(string.Empty, label) { }

    /// <summary>The Material Symbols glyph shown before the label; empty for a label-only item.</summary>
    public string Icon { get; }

    /// <summary>The item's text label.</summary>
    public string Label { get; }

    /// <summary>Sets the action invoked when this item is tapped.</summary>
    public MenuItem OnSelected(Action onSelected)
    {
        _onSelected = onSelected;
        return this;
    }

    internal void Invoke() => _onSelected?.Invoke();
}

/// <summary>
/// A Material 3 dropdown menu surface: a rounded <c>surfaceContainer</c> card at elevation level 2 holding a
/// vertical list of <see cref="MenuItem"/>s. Presentational only — it renders the items and reports taps through
/// each item's callback; it owns no open/closed state and no placement. The host decides when to show it
/// (typically inside an overlay layer — see ADR-0004) and where to anchor it. The surface sizes to its content
/// within the M3 112–280dp width band.
/// </summary>
public sealed partial class Menu : Component
{
    /// <summary>The items to render, top to bottom. Set via <c>.Items(...)</c>.</summary>
    [Prop] MenuItem[] _items = [];

    /// <summary>
    /// Whether the menu is open. Drives the M3 open/close motion — a fade + scale-from-anchor — internally, the
    /// way <see cref="NavigationBar"/> animates its selection pill. Placement (and whether the surface is in the
    /// tree at all) remains the host's concern. Set via <c>.IsOpen(...)</c>.
    /// </summary>
    [Prop] bool _isOpen;

    // M3 level-2 surface tint: how much (0..1) the container is tinted toward primary to express elevation.
    private const float SurfaceTintLevel2 = 0.08f;

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        // The open motion — anchor + scale — must live on a NESTED node, not this component's root: MauiReactor
        // drops render-transform properties set on a Component's root node (the same reason TabStrip anchors its
        // inner strip, not its outer root). Set on the root, AnchorX(1) is lost and the scale falls back to a
        // centre pivot, so the menu appears to grow from the wrong place instead of out of the top-right corner.
        return Grid(
            Border(
                VStack(_items.Select(item => RenderItem(item, scheme)).ToArray()).Spacing(0)
            )
            // M3 conveys the menu's elevation mainly through a tonal surface tint — surfaceContainer nudged toward
            // primary — rather than a heavy shadow; the theme-appropriate primary keeps it right in light and dark.
            .BackgroundColor(Tint(scheme.SurfaceContainer, scheme.Primary, SurfaceTintLevel2))
            .StrokeThickness(0)
            .StrokeShape(new RoundRectangle().CornerRadius(4)) // M3 menu container: extra-small (4dp) corners
            // Gate the shadow on open: MAUI paints a Border's platform shadow even at Opacity 0, so an always-in-tree
            // closed menu would leave a resting shadow rectangle where it will appear.
            .Shadow(_isOpen ? Elevation.Level2 : Elevation.None)
            .Padding(0, 8) // 8dp top/bottom list padding; items own their horizontal padding
            .MinimumWidthRequest(112) // M3 dropdown menu min width
            .MaximumWidthRequest(280) // …and max width
            .AnchorX(1) // grow from the top-right corner — the usual anchor for a right-aligned overflow menu
            .AnchorY(0)
            .Scale(_isOpen ? 1 : 0.9)
            .Opacity(_isOpen ? 1 : 0)
            .WithAnimation(duration: 150)
        );
        // The wrapper is a single-cell Grid that sizes to the Border, so the host's Auto placement track still
        // measures to the menu's width and the 8dp inset tracks keep it pinned top-right.
    }

    // One 48dp-tall row: an optional leading icon (24dp, onSurfaceVariant) then the label (onSurface). 12dp
    // horizontal padding and a 12dp gap after the icon, per M3 menu-item metrics. The full row is the tap target.
    private static VisualNode RenderItem(MenuItem item, MaterialScheme scheme)
    {
        var children = new List<VisualNode>();
        if (!string.IsNullOrEmpty(item.Icon))
        {
            children.Add(Label(item.Icon)
                .FontFamily(MaterialSymbols.FontFamily)
                .FontSize(24)
                .TextColor(scheme.OnSurfaceVariant)
                .VCenter()
                .Margin(0, 0, 12, 0)
                .GridColumn(0));
        }

        children.Add(Label(item.Label)
            .FontSize(14)
            .TextColor(scheme.OnSurface)
            .VCenter()
            .GridColumn(1));

        return Grid("48", "Auto,*", [.. children])
            .Padding(12, 0)
            .OnTapped(item.Invoke);
    }

    // Linearly mixes <paramref name="amount"/> of <paramref name="tint"/> into <paramref name="surface"/> — the M3
    // surface-tint overlay used to express elevation as a colour shift rather than a shadow.
    private static Color Tint(Color surface, Color tint, float amount) => new(
        surface.Red + (tint.Red - surface.Red) * amount,
        surface.Green + (tint.Green - surface.Green) * amount,
        surface.Blue + (tint.Blue - surface.Blue) * amount);
}
