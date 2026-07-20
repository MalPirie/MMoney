using MauiReactor;

namespace Mobiorum.Material3;

/// <summary>
/// A Material 3 floating action button: by default a 56dp standard FAB (16dp rounded square, the
/// <c>primaryContainer</c> / <c>onPrimaryContainer</c> pair, elevation level 3). <see cref="Small"/> renders the
/// 40dp M3 small FAB and <see cref="Secondary"/> swaps to the <c>secondaryContainer</c> pair — together the shape
/// a secondary action takes when it sits above the primary FAB. Placement — alignment, margins, hovering over a
/// navigation bar — is the host's responsibility; the control is layout-agnostic.
/// </summary>
public sealed partial class Fab : Component
{
    /// <summary>The Material Symbols glyph shown on the button. Set via <c>.Icon(...)</c>.</summary>
    [Prop] string _icon = string.Empty;

    /// <summary>Renders the 40dp M3 small FAB instead of the 56dp standard one. Set via <c>.Small(...)</c>.</summary>
    [Prop] bool _small;

    /// <summary>Uses the <c>secondaryContainer</c> colour pair (for a secondary action). Set via <c>.Secondary(...)</c>.</summary>
    [Prop] bool _secondary;

    /// <summary>Invoked when the button is tapped. Set via <c>.OnClicked(...)</c>.</summary>
    [Prop] Action? _onClicked;

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        return Button(_icon)
            .FontFamily(MaterialSymbols.FontFamily)
            .FontSize(_small ? 20 : 24)
            .BackgroundColor(_secondary ? scheme.SecondaryContainer : scheme.PrimaryContainer)
            .TextColor(_secondary ? scheme.OnSecondaryContainer : scheme.OnPrimaryContainer)
            .CornerRadius(_small ? 12 : 16) // M3: small FAB 12dp, standard FAB 16dp rounded square
            .WidthRequest(_small ? 40 : 56)
            .HeightRequest(_small ? 40 : 56)
            .Padding(0)
            .Shadow(Elevation.Level3)
            .OnClicked(() => _onClicked?.Invoke());
    }
}
