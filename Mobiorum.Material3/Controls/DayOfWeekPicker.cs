using System;
using System.Linq;
using MauiReactor;
using MauiReactor.Shapes;
using Microsoft.Maui.Graphics;
using MauiControls = Microsoft.Maui.Controls;

namespace Mobiorum.Material3;

/// <summary>
/// A Material 3 Monday-start weekday multiselect: seven circular toggles (M T W T F S S). Host-driven
/// (ADR-0001): the selection is a 7-bit mask (bit 0 = Monday … bit 6 = Sunday) passed as <see cref="Selected"/>
/// and updated from <see cref="OnChanged"/>. Enforces at-least-one by making a clear-the-last tap a no-op. Uses
/// only a plain int mask, so it stays Core-free (the app maps its <c>DaysOfWeek</c> flags on/off it — their bit
/// layout matches).
/// </summary>
public sealed partial class DayOfWeekPicker : Component
{
    private static readonly string[] Letters = ["M", "T", "W", "T", "F", "S", "S"];

    /// <summary>The selected weekdays as a 7-bit mask (bit 0 = Monday). Set via <c>.Selected(...)</c>.</summary>
    [Prop] int _selected;

    /// <summary>Invoked with the new mask when a day toggles. Set via <c>.OnChanged(...)</c>.</summary>
    [Prop] Action<int>? _onChanged;

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        var cols = string.Join(",", Enumerable.Repeat("40", 7));
        var cells = Enumerable.Range(0, 7).Select(i => DayCell(i, scheme).GridColumn(i));
        return Grid("40", cols, [.. cells]).ColumnSpacing(6).HStart();
    }

    private VisualNode DayCell(int index, MaterialScheme scheme)
    {
        var on = (_selected & (1 << index)) != 0;
        return Border(
                Component.Label(Letters[index])
                    .FontSize(14)
                    .TextColor(on ? scheme.OnPrimary : scheme.OnSurfaceVariant)
                    .HCenter().VCenter()
            )
            .BackgroundColor(on ? scheme.Primary : Colors.Transparent)
            .Stroke(new MauiControls.SolidColorBrush(on ? scheme.Primary : scheme.Outline))
            .StrokeThickness(1)
            .StrokeShape(new RoundRectangle().CornerRadius(20)) // 40dp box → circle
            .OnTapped(() => Toggle(index));
    }

    private void Toggle(int index)
    {
        var next = _selected ^ (1 << index);
        if (next == 0)
        {
            return; // at least one day must stay selected
        }

        _onChanged?.Invoke(next);
    }
}
