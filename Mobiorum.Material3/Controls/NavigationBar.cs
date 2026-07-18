using MauiReactor;
using MauiReactor.Shapes;

namespace Mobiorum.Material3;

/// <summary>How a <see cref="NavigationBar"/> arranges its destinations across the bar.</summary>
public enum NavArrangement
{
    /// <summary>Distribute evenly across the full width (the M3 default).</summary>
    Fill,

    /// <summary>Pack to the start, leaving the trailing space free (e.g. for a floating FAB).</summary>
    Start,
}

/// <summary>
/// A Material 3 bottom navigation bar. Presentational only: it renders the supplied
/// <see cref="NavDestination"/>s, animates the selection pill on whichever reports
/// <see cref="NavDestination.Selected(bool)"/>, and reports taps through each destination's callback. It owns
/// no selection state — the host derives each destination's selected flag from its own source of truth. Carries
/// elevation level 2.
/// </summary>
public sealed partial class NavigationBar : Component
{
    /// <summary>The destinations to render, in order. Set via <c>.Destinations(...)</c>.</summary>
    [Prop] NavDestination[] _destinations = [];

    /// <summary>
    /// How destinations are laid out across the bar; defaults to <see cref="NavArrangement.Fill"/>. Set via
    /// <c>.Arrangement(...)</c>.
    /// </summary>
    [Prop] NavArrangement _arrangement = NavArrangement.Fill;

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        return Grid("80", "*", // M3 navigation bar container height is 80dp
            _arrangement == NavArrangement.Start ? RenderStart(scheme) : RenderFill(scheme)
        )
        .BackgroundColor(scheme.SurfaceContainer)
        .Shadow(Elevation.TopEdge); // lifts the bar's top edge off the content beneath (a bottom bar's shadow casts up)
    }

    private VisualNode RenderStart(MaterialScheme scheme) =>
        HStack(_destinations.Select(d => RenderItem(d, scheme)).ToArray())
            .Spacing(4).Padding(8, 0).HStart().VCenter();

    private VisualNode RenderFill(MaterialScheme scheme) =>
        Grid("*", string.Join(",", Enumerable.Repeat("*", _destinations.Length)),
            _destinations
                .Select((d, i) => RenderItem(d, scheme).GridColumn(i))
                .ToArray()
        );

    private VisualNode RenderItem(NavDestination destination, MaterialScheme scheme)
    {
        var selected = destination.IsSelected;
        return VStack(
            Grid("32", "64",
                Border()
                    .BackgroundColor(scheme.SecondaryContainer)
                    .StrokeThickness(0)
                    .StrokeShape(new RoundRectangle().CornerRadius(16))
                    .WidthRequest(56)
                    .HeightRequest(32)
                    .HCenter()
                    .VCenter()
                    .Scale(selected ? 1 : 0.5)
                    .Opacity(selected ? 1 : 0)
                    .WithAnimation(duration: 200),
                Label(destination.Icon)
                    .FontFamily(MaterialSymbols.FontFamily)
                    .FontSize(22)
                    .TextColor(selected ? scheme.OnSecondaryContainer : scheme.OnSurfaceVariant)
                    .HCenter()
                    .VCenter()
            ),
            Label(destination.Label)
                .FontSize(11)
                .TextColor(selected ? scheme.OnSurface : scheme.OnSurfaceVariant)
                .HCenter()
        ).Spacing(2).Padding(6, 0).HCenter().VCenter().OnTapped(destination.Invoke);
    }
}
