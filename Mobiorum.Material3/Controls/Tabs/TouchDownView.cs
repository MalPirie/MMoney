using MauiControls = Microsoft.Maui.Controls;

namespace Mobiorum.Material3;

/// <summary>
/// A single-child container whose Android platform view observes the native touch-down (<c>ACTION_DOWN</c>)
/// landing inside its own bounds and raises <see cref="TouchDown"/> — <b>without consuming</b> the event, so
/// the child's MAUI pan/tap gestures are untouched. This is the library-owned replacement for the ADR-0003
/// spike's Activity-level touch dispatch: it lets a <see cref="TabStrip{TItem}"/> cancel an in-flight fling on
/// first contact (MAUI raises no touch-down for touch, and a pan needs movement before it fires). On platforms
/// other than Android there is no observer and <see cref="TouchDown"/> simply never fires (fling still cancels
/// on pan-start / tap). The Android observer lives in <c>Platforms/Android/TouchDownViewGroup.cs</c>.
/// </summary>
public sealed class TouchDownContentView : MauiControls.ContentView
{
    /// <summary>Raised on every native touch-down within the view's bounds (Android only).</summary>
    public Action? TouchDown { get; set; }

    /// <summary>Invoked by the platform view group; kept internal so only the handler raises it.</summary>
    internal void RaiseTouchDown() => TouchDown?.Invoke();
}

/// <summary>MauiReactor wrapper for <see cref="TouchDownContentView"/> — hosts one child and forwards the
/// native touch-down to <see cref="OnTouchDown(Action)"/>.</summary>
public sealed class TouchDownView : MauiReactor.ContentView<TouchDownContentView>
{
    private Action? _onTouchDown;

    /// <summary>Callback for each native touch-down within the view (Android only).</summary>
    public TouchDownView OnTouchDown(Action action)
    {
        _onTouchDown = action;
        return this;
    }

    protected override void OnUpdate()
    {
        // Re-bind every render so the callback always targets the current (possibly re-instantiated) component;
        // the native view is reused across renders, so this keeps it pointed at the live closure.
        if (NativeControl is { } control)
        {
            control.TouchDown = _onTouchDown;
        }

        base.OnUpdate();
    }
}
