using System;
using MauiReactor;
using MauiReactor.Shapes;
using Microsoft.Maui.Graphics;
using MauiControls = Microsoft.Maui.Controls;

namespace Mobiorum.Material3;

// Note: OnTap reports the trigger's on-screen rect so the host can anchor the dropdown under it.

/// <summary>
/// A Material 3 exposed-dropdown trigger: an outlined field with a notched label, the current value, and a
/// dropdown arrow. Presentation only — on tap it captures its own on-screen rect (via the native-control ref) and
/// raises <see cref="OnTap"/> with it, so the host can anchor a <see cref="PopoverHost"/> dropdown directly under
/// the field. Set <see cref="Open"/> while the list shows so the outline/label take the focused accent.
/// Seed-agnostic. (ADR-0007: replaces an earlier inline-expand form.)
/// </summary>
public sealed partial class ComboBox : Component
{
    /// <summary>The field label, notched into the outline. Set via <c>.Label(...)</c>.</summary>
    [Prop] string _label = string.Empty;

    /// <summary>The current value text. Set via <c>.Value(...)</c>.</summary>
    [Prop] string _value = string.Empty;

    /// <summary>Whether the list is open (drives the focused accent). Set via <c>.Open(...)</c>.</summary>
    [Prop] bool _open;

    /// <summary>Invoked when tapped, with the trigger's on-screen rect (for anchoring the dropdown). Set via <c>.OnTap(...)</c>.</summary>
    [Prop] Action<Rect>? _onTap;

    // The trigger's native control, captured via MauiReactor's native-ref, to measure its on-screen position.
    private MauiControls.Border? _triggerRef;

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        var accent = _open ? scheme.Primary : scheme.Outline;

        var box = new MauiReactor.Border(r => _triggerRef = r)
            {
                Grid("*", "*,Auto",
                    Component.Label(_value).FontSize(16).TextColor(scheme.OnSurface).VCenter().GridColumn(0),
                    Component.Label(MaterialSymbols.ArrowDropDown)
                        .FontFamily(MaterialSymbols.FontFamily).FontSize(22)
                        .TextColor(scheme.OnSurfaceVariant).VCenter().GridColumn(1)
                )
            }
            .BackgroundColor(Colors.Transparent)
            .Stroke(new MauiControls.SolidColorBrush(accent))
            .StrokeThickness(_open ? 2 : 1)
            .StrokeShape(new RoundRectangle().CornerRadius(8))
            .Padding(16, 0)
            .MinimumHeightRequest(56)
            .OnTapped(() => _onTap?.Invoke(NativeGeometry.ScreenRect(_triggerRef) ?? default));

        // The notched label straddling the top outline (same idiom as TextField).
        return Grid(
            box,
            Grid(Component.Label(_label).FontSize(12).TextColor(_open ? scheme.Primary : scheme.OnSurfaceVariant))
                .BackgroundColor(scheme.Surface)
                .Padding(4, 0)
                .HStart().VStart()
                .Margin(12, 0, 0, 0)
                .TranslationY(-8));
    }
}
