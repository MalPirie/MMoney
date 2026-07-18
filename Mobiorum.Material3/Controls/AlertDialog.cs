using MauiReactor;
using MauiReactor.Shapes;
using Microsoft.Maui.Graphics;
using MauiControls = Microsoft.Maui.Controls;

namespace Mobiorum.Material3;

/// <summary>
/// A Material 3 basic alert dialog (ADR-0007's dialog family): a title, a supporting message, and two trailing
/// text buttons — a dismiss and a confirm. Presentational and stateless — the host supplies the text, the button
/// labels, and the <see cref="OnConfirm"/>/<see cref="OnDismiss"/> callbacks; meant to be centred by a
/// <see cref="ModalHost"/>. Set <see cref="Destructive"/> to tint the confirm action <c>error</c> (e.g. Delete).
/// </summary>
public sealed partial class AlertDialog : Component
{
    /// <summary>The dialog headline. Set via <c>.Title(...)</c>.</summary>
    [Prop] string _title = string.Empty;

    /// <summary>The supporting body text. Set via <c>.Message(...)</c>.</summary>
    [Prop] string _message = string.Empty;

    /// <summary>The confirm button label (defaults to "OK"). Set via <c>.ConfirmText(...)</c>.</summary>
    [Prop] string _confirmText = "OK";

    /// <summary>The dismiss button label (defaults to "Cancel"). Set via <c>.DismissText(...)</c>.</summary>
    [Prop] string _dismissText = "Cancel";

    /// <summary>Tints the confirm action <c>error</c> for a destructive action. Set via <c>.Destructive(...)</c>.</summary>
    [Prop] bool _destructive;

    /// <summary>Invoked when the confirm button is tapped. Set via <c>.OnConfirm(...)</c>.</summary>
    [Prop] Action? _onConfirm;

    /// <summary>Invoked on dismiss (the host also calls this on scrim-tap/back). Set via <c>.OnDismiss(...)</c>.</summary>
    [Prop] Action? _onDismiss;

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        var confirmColor = _destructive ? scheme.Error : scheme.Primary;

        return Border(
            VStack(
                Component.Label(_title)
                    .FontSize(16)
                    .FontAttributes(MauiControls.FontAttributes.Bold)
                    .TextColor(scheme.OnSurface),
                Component.Label(_message)
                    .FontSize(14)
                    .TextColor(scheme.OnSurfaceVariant)
                    .Margin(0, 8, 0, 0),
                HStack(
                    TextButton(_dismissText, scheme.Primary, () => _onDismiss?.Invoke()),
                    TextButton(_confirmText, confirmColor, () => _onConfirm?.Invoke())
                ).Spacing(8).HEnd().Margin(0, 16, 0, 0)
            ).Spacing(0)
        )
        .BackgroundColor(scheme.SurfaceContainer)
        .StrokeThickness(0)
        .StrokeShape(new RoundRectangle().CornerRadius(28)) // M3 dialog container
        .Shadow(Elevation.Level2)
        .Padding(20)
        .WidthRequest(300);
    }

    private static VisualNode TextButton(string text, Color color, Action onClicked) =>
        Button(text)
            .BackgroundColor(Colors.Transparent)
            .TextColor(color)
            .FontSize(14)
            .Padding(12, 8)
            .OnClicked(onClicked);
}
