using MauiReactor;
using MauiReactor.Shapes;
using Microsoft.Maui.Graphics;

namespace Mobiorum.Material3;

/// <summary>
/// A Material 3 snackbar: a low-elevation <c>inverseSurface</c> bar with a message and an optional trailing action
/// (e.g. "Undo"). Presentational only — the host decides when it is in the tree and owns the auto-dismiss timer;
/// the control reports the action tap through <see cref="OnAction"/>. Meant to be pinned to the bottom of the page
/// by the host (it applies its own 16dp margins within that slot).
/// </summary>
public sealed partial class Snackbar : Component
{
    /// <summary>The message text. Set via <c>.Message(...)</c>.</summary>
    [Prop] string _message = string.Empty;

    /// <summary>The trailing action label; shown only when both this and <see cref="OnAction"/> are set. Set via <c>.ActionText(...)</c>.</summary>
    [Prop] string _actionText = string.Empty;

    /// <summary>Invoked when the action is tapped. Set via <c>.OnAction(...)</c>.</summary>
    [Prop] Action? _onAction;

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        var children = new List<VisualNode>
        {
            Label(_message)
                .FontSize(14)
                .TextColor(scheme.InverseOnSurface)
                .MaxLines(2) // a long message wraps to two lines rather than being clipped at a large font
                .VCenter()
                .Margin(16, 0, 0, 0)
                .GridColumn(0),
        };

        if (_onAction is not null && !string.IsNullOrEmpty(_actionText))
        {
            children.Add(Button(_actionText)
                .BackgroundColor(Colors.Transparent)
                .TextColor(scheme.InversePrimary)
                .FontFamily("OpenSansSemibold")
                .FontSize(14)
                .Padding(8, 0)
                .Margin(4, 0, 4, 0)
                .VCenter()
                .OnClicked(() => _onAction?.Invoke())
                .GridColumn(1));
        }

        // Height sizes to the content (a 48dp floor with 8dp vertical padding) rather than a fixed 48dp row, so at a
        // large accessibility font the bar grows to fit the text — or its second line — instead of clipping it.
        return Border(
            Grid("Auto", "*,Auto", [.. children])
        )
        .BackgroundColor(scheme.InverseSurface)
        .StrokeThickness(0)
        .StrokeShape(new RoundRectangle().CornerRadius(4)) // M3 snackbar container: extra-small (4dp)
        .Shadow(Elevation.Level3)
        .Padding(0, 8)
        .MinimumHeightRequest(48)
        .Margin(16)
        .MinimumWidthRequest(344 - 32) // M3 min snackbar width (344dp) less the margins
        .MaximumWidthRequest(600);
    }
}
