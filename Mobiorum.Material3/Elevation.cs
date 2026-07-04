using MauiReactor;
using Microsoft.Maui.Graphics;
using MauiControls = Microsoft.Maui.Controls;

namespace Mobiorum.Material3;

/// <summary>
/// Material 3 elevation tokens, each mapped to a MauiReactor <see cref="MauiReactor.Shadow"/> node. M3 conveys elevation
/// with a shadow plus a tonal surface overlay; the controls here already carry tonal containers
/// (<c>surfaceContainer</c>, <c>primaryContainer</c>), so these tokens supply only the shadow. M3's
/// <c>shadow</c> colour role is pure black in both light and dark themes, so the tokens are not
/// scheme-dependent — refining dark-mode elevation is deferred to the on-device colour review.
/// </summary>
/// <remarks>
/// Each access returns a <em>fresh</em> node: a <see cref="MauiReactor.Shadow"/> is a bindable object and must not be
/// shared across elements.
/// </remarks>
public static class Elevation
{
    /// <summary>No elevation — a fully transparent shadow, to toggle elevation off without passing null.</summary>
    public static MauiReactor.Shadow None => new MauiReactor.Shadow()
        .Brush(new MauiControls.SolidColorBrush(Colors.Black))
        .Offset(0, 0)
        .Radius(0)
        .Opacity(0f);

    /// <summary>
    /// M3 elevation level 2 (≈3dp) — e.g. the navigation bar and dropdown menus. M3 conveys 3dp mostly through
    /// tonal surface colour (the container roles already carry it), so the shadow is deliberately soft; a heavier
    /// drop shadow reads as far higher than 3dp.
    /// </summary>
    public static MauiReactor.Shadow Level2 => new MauiReactor.Shadow()
        .Brush(new MauiControls.SolidColorBrush(Colors.Black))
        .Offset(0, 1)
        .Radius(2)
        .Opacity(0.10f);

    /// <summary>M3 elevation level 3 (≈6dp) — e.g. the FAB.</summary>
    public static MauiReactor.Shadow Level3 => new MauiReactor.Shadow()
        .Brush(new MauiControls.SolidColorBrush(Colors.Black))
        .Offset(0, 4)
        .Radius(8)
        .Opacity(0.20f);
}
