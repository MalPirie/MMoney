using System;
using System.Collections.Generic;
using MauiReactor;
using MauiReactor.Shapes;
using Microsoft.Maui.Graphics;
using MauiControls = Microsoft.Maui.Controls;

namespace Mobiorum.Material3;

/// <summary>The <see cref="ChoiceDialog"/>'s transient state: the in-flight radio selection (committed only on OK).</summary>
public sealed class ChoiceDialogState
{
    /// <summary>The highlighted option index; seeded from the prop on mount.</summary>
    public int Selected { get; set; }
}

/// <summary>
/// A Material 3 single-choice dialog surface (ADR-0007): an optional title, a vertical radio list, and Cancel/OK.
/// Presentational and seed-agnostic — the host supplies the <see cref="Options"/>, the initial
/// <see cref="SelectedIndex"/>, and the <see cref="OnConfirm"/>/<see cref="OnCancel"/> callbacks; the control owns
/// only the transient selection (ADR-0001), seeded on mount so a fresh open resets it. Meant to be centred by a
/// <see cref="ModalHost"/>. Reused by the repeat preset picker and (later) the §8 occurrence scope dialog.
/// </summary>
public sealed partial class ChoiceDialog : Component<ChoiceDialogState>
{
    /// <summary>Optional heading above the list. Set via <c>.Title(...)</c>.</summary>
    [Prop] string _title = string.Empty;

    /// <summary>The option labels, top to bottom. Set via <c>.Options(...)</c>.</summary>
    [Prop] string[] _options = [];

    /// <summary>The initially selected index. Set via <c>.SelectedIndex(...)</c>.</summary>
    [Prop] int _selectedIndex;

    /// <summary>Invoked with the chosen index on OK. Set via <c>.OnConfirm(...)</c>.</summary>
    [Prop] Action<int>? _onConfirm;

    /// <summary>Invoked on Cancel (the host also calls this on scrim-tap/back). Set via <c>.OnCancel(...)</c>.</summary>
    [Prop] Action? _onCancel;

    protected override void OnMounted()
    {
        State.Selected = _selectedIndex;
        base.OnMounted();
    }

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        var rows = new List<VisualNode>();

        if (!string.IsNullOrEmpty(_title))
        {
            rows.Add(Component.Label(_title)
                .FontSize(16)
                .FontAttributes(MauiControls.FontAttributes.Bold)
                .TextColor(scheme.OnSurface)
                .Margin(0, 0, 0, 4));
        }

        for (var i = 0; i < _options.Length; i++)
        {
            rows.Add(RadioRow(i, scheme));
        }

        rows.Add(Actions(scheme));

        return Border(VStack([.. rows]).Spacing(2))
            .BackgroundColor(scheme.SurfaceContainer)
            .StrokeThickness(0)
            .StrokeShape(new RoundRectangle().CornerRadius(28)) // M3 dialog container
            .Shadow(Elevation.Level2)
            .Padding(20)
            .WidthRequest(300);
    }

    private VisualNode RadioRow(int index, MaterialScheme scheme) =>
        Grid("48", "Auto,*",
            Radio(State.Selected == index, scheme).GridColumn(0),
            Component.Label(_options[index]).FontSize(14).TextColor(scheme.OnSurface).VCenter().GridColumn(1)
        )
        .ColumnSpacing(12)
        .OnTapped(() => SetState(s => s.Selected = index));

    // A 20dp ring (2px) with a 10dp filled dot when selected.
    private static VisualNode Radio(bool selected, MaterialScheme scheme)
    {
        var inner = new List<VisualNode>();
        if (selected)
        {
            inner.Add(Border()
                .BackgroundColor(scheme.Primary)
                .StrokeThickness(0)
                .StrokeShape(new RoundRectangle().CornerRadius(5))
                .WidthRequest(10).HeightRequest(10)
                .HCenter().VCenter());
        }

        return Border(Grid([.. inner]))
            .BackgroundColor(Colors.Transparent)
            .Stroke(new MauiControls.SolidColorBrush(selected ? scheme.Primary : scheme.OnSurfaceVariant))
            .StrokeThickness(2)
            .StrokeShape(new RoundRectangle().CornerRadius(10))
            .WidthRequest(20).HeightRequest(20)
            .VCenter();
    }

    private VisualNode Actions(MaterialScheme scheme) =>
        HStack(
            TextButton("Cancel", scheme, () => _onCancel?.Invoke()),
            TextButton("OK", scheme, () => _onConfirm?.Invoke(State.Selected))
        ).Spacing(8).HEnd().Margin(0, 8, 0, 0);

    private static VisualNode TextButton(string text, MaterialScheme scheme, Action onClicked) =>
        Button(text)
            .BackgroundColor(Colors.Transparent)
            .TextColor(scheme.Primary)
            .FontSize(14)
            .Padding(12, 8)
            .OnClicked(onClicked);
}
