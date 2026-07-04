using MauiReactor;
using MauiReactor.Shapes;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using MauiControls = Microsoft.Maui.Controls;

namespace Mobiorum.Material3;

/// <summary>The <see cref="LabeledToggle"/>'s internal state: the measured track width and the live drag position.</summary>
public sealed class LabeledToggleState
{
    /// <summary>The control's measured width, from which the thumb size and its travel are derived.</summary>
    public double TrackWidth { get; set; }

    /// <summary>Whether a pan drag is in progress (the thumb follows the finger; no slide animation).</summary>
    public bool Dragging { get; set; }

    /// <summary>The thumb's left offset (dp within the padded track) while dragging.</summary>
    public double DragX { get; set; }
}

/// <summary>
/// A Material 3 style two-state slide switch: a pill track with an inset, elevated thumb that slides between an
/// "off" and an "on" side. <b>Tap</b> it to flip, or <b>drag</b> the thumb across (it snaps to the nearer side on
/// release). Both labels and both colours are configurable, so it is seed-agnostic; MMoney uses it for the income
/// (on) / expense (off) choice. Host-driven for the committed value (ADR-0001): the host passes <see cref="IsOn"/>
/// and updates it from <see cref="OnToggled"/>; the control owns only its transient measurement and drag state.
/// </summary>
public sealed partial class LabeledToggle : Component<LabeledToggleState>
{
    private const double TrackHeight = 44;
    private const double Pad = 4;        // inset around the thumb, so it floats in the track like a switch knob
    private const double Radius = 22;

    /// <summary>The label for the off (false) side. Set via <c>.OffLabel(...)</c>.</summary>
    [Prop] string _offLabel = string.Empty;

    /// <summary>The label for the on (true) side. Set via <c>.OnLabel(...)</c>.</summary>
    [Prop] string _onLabel = string.Empty;

    /// <summary>The thumb colour on the off side; defaults to <c>error</c>. Set via <c>.OffColor(...)</c>.</summary>
    [Prop] Color? _offColor;

    /// <summary>The thumb colour on the on side; defaults to <c>primary</c>. Set via <c>.OnColor(...)</c>.</summary>
    [Prop] Color? _onColor;

    /// <summary>Whether the toggle is on (true). Set via <c>.IsOn(...)</c>.</summary>
    [Prop] bool _isOn;

    /// <summary>Invoked with the newly selected side when it changes (tap or drag). Set via <c>.OnToggled(...)</c>.</summary>
    [Prop] Action<bool>? _onToggled;

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        var offColor = _offColor ?? scheme.Error;
        var onColor = _onColor ?? scheme.Primary;

        var inner = Math.Max(0, State.TrackWidth - 2 * Pad); // travel area inside the padding
        var half = inner / 2;
        var resting = _isOn ? half : 0;
        var thumbX = State.Dragging ? State.DragX : resting;
        var onSide = inner > 0 ? thumbX >= half / 2 : _isOn; // which side the thumb currently covers
        var thumbColor = onSide ? onColor : offColor;

        return Grid($"{TrackHeight}", "*",
            // track
            Border()
                .BackgroundColor(scheme.SurfaceVariant)
                .StrokeThickness(0)
                .StrokeShape(new RoundRectangle().CornerRadius(Radius)),

            // the inset, elevated, sliding thumb (a rounded knob covering one side)
            Grid("*", "*",
                Border()
                    .BackgroundColor(thumbColor)
                    .StrokeThickness(0)
                    .StrokeShape(new RoundRectangle().CornerRadius((TrackHeight - 2 * Pad) / 2))
                    .WidthRequest(half)
                    .HStart()
                    .TranslationX(thumbX)
                    .Shadow(Elevation.Level2)
                    .WithAnimation(duration: State.Dragging ? 0 : 150)
            ).Padding(Pad),

            // labels (above the thumb so the active one reads white on its colour)
            Grid("*", "*,*",
                Label(_offLabel).FontSize(14).TextColor(onSide ? scheme.OnSurfaceVariant : Colors.White).HCenter().VCenter().GridColumn(0),
                Label(_onLabel).FontSize(14).TextColor(onSide ? Colors.White : scheme.OnSurfaceVariant).HCenter().VCenter().GridColumn(1)
            ),

            // one transparent, hit-testable layer carrying BOTH gestures (two competing children swallowed the pan):
            // tap flips, pan drags the thumb.
            Grid("*", "*")
                .BackgroundColor(Colors.Transparent)
                .OnTapped(Toggle)
                .OnPanUpdated(OnPan)
        )
        // Re-render when the width first becomes known (or changes) so the thumb sizes/positions; guard to avoid a loop.
        .OnSizeChanged((Size s) =>
        {
            if (Math.Abs(State.TrackWidth - s.Width) > 0.5)
            {
                SetState(st => st.TrackWidth = s.Width);
            }
        });
    }

    private void Toggle() => _onToggled?.Invoke(!_isOn);

    private void OnPan(MauiControls.PanUpdatedEventArgs e)
    {
        var half = Math.Max(0, State.TrackWidth - 2 * Pad) / 2;
        if (half <= 0)
        {
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
            case GestureStatus.Running:
                var start = _isOn ? half : 0;
                var x = Math.Clamp(start + e.TotalX, 0, half);
                SetState(s =>
                {
                    s.Dragging = true;
                    s.DragX = x;
                });
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                var target = State.DragX >= half / 2; // snap to the nearer side
                SetState(s => s.Dragging = false);
                if (target != _isOn)
                {
                    _onToggled?.Invoke(target);
                }
                break;
        }
    }
}
