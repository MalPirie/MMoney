using System;
using MauiReactor;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using MauiControls = Microsoft.Maui.Controls;

namespace Mobiorum.Material3;

/// <summary>The <see cref="PopoverHost"/>'s cached on-screen origin (measured post-layout, off the render path).</summary>
public sealed class PopoverHostState
{
    /// <summary>This host's top-left on screen, in DIPs — for converting the anchor into local coordinates.</summary>
    public Rect? Origin { get; set; }
}

/// <summary>
/// A dropdown/popover overlay (ADR-0007): like <see cref="ModalHost"/> but it <b>anchors</b> its content just
/// under a target rather than centring, and uses a transparent (undimmed) catcher — an M3 exposed-dropdown menu,
/// not a modal. The host passes the target's on-screen rect (<see cref="Anchor"/>, from
/// <see cref="NativeGeometry.ScreenRect"/>); this converts it into local space using its own measured origin and
/// places the content beneath, matched to the target's width. The origin is measured with a retrying dispatched
/// call after layout (measuring on the render path returns null — the native view isn't realized yet), then cached
/// since the page's top-left is stable. Dismisses on an outside tap or Android back; falls back to centring until
/// the origin is known, so the list is never lost.
/// </summary>
public sealed partial class PopoverHost : Component<PopoverHostState>
{
    /// <summary>Whether the popover is showing. Set via <c>.IsOpen(...)</c>.</summary>
    [Prop] bool _isOpen;

    /// <summary>The content (the dropdown list). Set via <c>.Content(...)</c>.</summary>
    [Prop] VisualNode? _content;

    /// <summary>Invoked on outside tap / back. Set via <c>.OnDismiss(...)</c>.</summary>
    [Prop] Action? _onDismiss;

    /// <summary>The trigger's on-screen rect in DIPs (from <see cref="NativeGeometry.ScreenRect"/>). Set via <c>.Anchor(...)</c>.</summary>
    [Prop] Rect _anchor;

    // The full-bleed catcher's native view, measured (post-layout) for this host's on-screen origin.
    private MauiControls.Border? _catcherRef;

#if ANDROID
    private DismissBackCallback? _backCallback;
#endif

    protected override void OnMounted()
    {
#if ANDROID
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is AndroidX.Activity.ComponentActivity activity)
        {
            _backCallback = new DismissBackCallback(() => _onDismiss?.Invoke());
            activity.OnBackPressedDispatcher.AddCallback(_backCallback);
        }
#endif
        ScheduleMeasure(attemptsLeft: 12);
        base.OnMounted();
    }

    protected override void OnWillUnmount()
    {
#if ANDROID
        _backCallback?.Remove();
        _backCallback = null;
#endif
        base.OnWillUnmount();
    }

    // Measure the (stable) host origin after layout settles, retrying until the catcher's native view is realized.
    private void ScheduleMeasure(int attemptsLeft)
    {
        var dispatcher = MauiControls.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(60), () =>
        {
            if (NativeGeometry.ScreenRect(_catcherRef) is { } rect)
            {
                if (State.Origin != rect)
                {
                    SetState(s => s.Origin = rect);
                }
            }
            else if (attemptsLeft > 0)
            {
                ScheduleMeasure(attemptsLeft - 1);
            }
        });
    }

    public override VisualNode Render()
    {
#if ANDROID
        if (_backCallback is not null)
        {
            _backCallback.Enabled = _isOpen;
        }
#endif
        VisualNode positioned;
        if (_anchor.Width > 0 && State.Origin is { } o)
        {
            // Convert the screen-space anchor into this host's local space and drop the list under it.
            var localX = _anchor.X - o.X;
            var localY = _anchor.Y + _anchor.Height - o.Y;
            positioned = Grid(_content ?? Grid())
                .HStart().VStart()
                .Margin(localX, localY, 0, 0)
                .WidthRequest(_anchor.Width);
        }
        else
        {
            positioned = Grid(_content ?? Grid()).HCenter().VCenter(); // origin not yet known → centre, never lost
        }

        return new MauiReactor.Grid
        {
            new MauiReactor.Border(r => _catcherRef = r)
                .BackgroundColor(Colors.Transparent) // undimmed catcher (a dropdown, not a modal)
                .StrokeThickness(0)
                .InputTransparent(!_isOpen)
                .OnTapped(() => { if (_isOpen) { _onDismiss?.Invoke(); } }),
            _isOpen ? positioned : Grid().InputTransparent(true),
        }
        .InputTransparent(!_isOpen);
    }

#if ANDROID
    private sealed class DismissBackCallback(Action onBack) : AndroidX.Activity.OnBackPressedCallback(enabled: false)
    {
        public override void HandleOnBackPressed() => onBack();
    }
#endif
}
