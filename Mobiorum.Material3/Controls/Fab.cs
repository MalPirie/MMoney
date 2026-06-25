using MauiReactor;

namespace Mobiorum.Material3;

/// <summary>
/// A Material 3 floating action button: a 56dp container with the M3 standard-FAB shape (16dp rounded square),
/// the <c>primaryContainer</c> / <c>onPrimaryContainer</c> colour pair, and elevation level 3. Placement —
/// alignment, margins, hovering over a navigation bar — is the host's responsibility; the control is
/// layout-agnostic.
/// </summary>
public sealed partial class Fab : Component
{
    /// <summary>The Material Symbols glyph shown on the button. Set via <c>.Icon(...)</c>.</summary>
    [Prop] string _icon = string.Empty;

    /// <summary>Invoked when the button is tapped. Set via <c>.OnClicked(...)</c>.</summary>
    [Prop] Action? _onClicked;

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        return Button(_icon)
            .FontFamily(MaterialSymbols.FontFamily)
            .FontSize(24)
            .BackgroundColor(scheme.PrimaryContainer)
            .TextColor(scheme.OnPrimaryContainer)
            .CornerRadius(16) // M3 standard FAB: 16dp rounded square (not a circle)
            .WidthRequest(56)
            .HeightRequest(56)
            .Padding(0)
            .Shadow(Elevation.Level3)
            .OnClicked(() => _onClicked?.Invoke());
    }
}
