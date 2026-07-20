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

    /// <summary>The hardware-back dismiss handle. In <c>State</c> (not a field) so it survives MauiReactor's
    /// per-render instance swap — a field would be null on every instance after the one <c>OnMounted</c> ran on.</summary>
    internal OverlayDismissHandle? Handle { get; set; }

    /// <summary>Whether <see cref="Handle"/> is currently on the shared back stack.</summary>
    internal bool Armed { get; set; }
}

/// <summary>
/// A dropdown/popover overlay (ADR-0007): like <see cref="ModalHost"/> but it <b>anchors</b> its content just
/// under a target rather than centring, and uses a transparent (undimmed) catcher — an M3 exposed-dropdown menu,
/// not a modal. The host passes the target's on-screen rect (<see cref="Anchor"/>, from
/// <see cref="NativeGeometry.ScreenRect"/>); this converts it into local space using its own measured origin and
/// places the content beneath, matched to the target's width. The origin is measured with a retrying dispatched
/// call after layout (measuring on the render path returns null — the native view isn't realized yet), then cached
/// since the page's top-left is stable. Dismisses on an outside tap or hardware-back (via the shared
/// <see cref="OverlayBackStack"/>, reached through a <see cref="Native.ModalAwareContentPage"/> — the same route
/// <see cref="ModalHost"/> uses); falls back to centring until the origin is known, so the list is never lost.
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

    protected override void OnMounted()
    {
        // Seed the back handle into State (not a field): MauiReactor swaps the instance each render and migrates
        // only State, so a field set here would be null on every later instance. Mirrors ModalHost.
        State.Handle = new OverlayDismissHandle();
        ScheduleMeasure(attemptsLeft: 12);
        base.OnMounted();
    }

    protected override void OnWillUnmount()
    {
        if (State is { Armed: true, Handle: { } handle })
        {
            OverlayBackStack.Remove(handle);
            State.Armed = false;
        }

        State.Handle = null;
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
        if (State.Handle is { } handle)
        {
            // Re-point at this instance's OnDismiss each render: the closure would otherwise hold the prop of the
            // discarded instance OnMounted ran on. Push on open / pop on close so the shared stack order matches
            // nesting and hardware-back reaches the innermost overlay. Mirrors ModalHost.
            handle.OnBack = () => _onDismiss?.Invoke();

            if (_isOpen && !State.Armed)
            {
                OverlayBackStack.Push(handle);
                State.Armed = true;
            }
            else if (!_isOpen && State.Armed)
            {
                OverlayBackStack.Remove(handle);
                State.Armed = false;
            }
        }

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
}
