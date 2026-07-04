using MauiReactor;
using Microsoft.Maui.Graphics;

namespace Mobiorum.Material3;

/// <summary>
/// A Material 3 small top app bar: an optional leading back button and a title. Presentational only — it renders
/// the chrome and reports the back tap through <see cref="OnBack"/>. Which page hosts it, and its branding, are
/// the host's concern (ADR-0001). Per M3 the bar defaults to <c>surface</c> colours; MMoney overrides the
/// container to <c>primary</c> via <see cref="Container"/>/<see cref="OnContainer"/> so its app bars match the
/// brand banner. Sized to the M3 small-top-app-bar height of 64dp.
/// </summary>
public sealed partial class TopAppBar : Component
{
    /// <summary>The bar's title text. Set via <c>.Title(...)</c>.</summary>
    [Prop] string _title = string.Empty;

    /// <summary>Invoked when the leading back button is tapped; no back button is shown when null. Set via <c>.OnBack(...)</c>.</summary>
    [Prop] Action? _onBack;

    /// <summary>The bar's background colour; defaults to <c>surface</c> per M3. Set via <c>.Container(...)</c>.</summary>
    [Prop] Color? _container;

    /// <summary>The colour of the title and back icon; defaults to <c>onSurface</c>. Set via <c>.OnContainer(...)</c>.</summary>
    [Prop] Color? _onContainer;

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        var container = _container ?? scheme.Surface;
        var onContainer = _onContainer ?? scheme.OnSurface;

        var children = new List<VisualNode>();
        if (_onBack is not null)
        {
            // 48dp touch target; margin(4) centres the 24dp glyph ≈16dp from the edge (M3 nav-icon inset).
            children.Add(Button(MaterialSymbols.ArrowBack)
                .FontFamily(MaterialSymbols.FontFamily)
                .FontSize(24)
                .BackgroundColor(Colors.Transparent)
                .TextColor(onContainer)
                .Padding(0)
                .CornerRadius(24)
                .WidthRequest(48)
                .HeightRequest(48)
                .HStart().VCenter()
                .Margin(4, 0, 0, 0)
                .OnClicked(() => _onBack?.Invoke())
                .GridColumn(0));
        }

        // With a back button the title starts ≈56dp in (M3); without one it takes the standard 16dp inset.
        children.Add(Label(_title)
            .FontSize(22) // M3 title-large
            .TextColor(onContainer)
            .VCenter()
            .Margin(_onBack is null ? 16 : 4, 0, 0, 0)
            .GridColumn(1));

        return Grid("64", "Auto,*", [.. children])
            .BackgroundColor(container);
    }
}
