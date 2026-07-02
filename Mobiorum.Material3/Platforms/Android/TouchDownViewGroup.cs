using Android.Content;
using Android.Views;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Mobiorum.Material3;

/// <summary>
/// Android handler for <see cref="TouchDownContentView"/>: swaps in a <see cref="TouchObservingViewGroup"/> so
/// the view can observe native touch-down without stealing gestures. Registered by
/// <see cref="MobiorumMaterial3.UseMobiorumMaterial3"/>.
/// </summary>
public sealed class TouchDownContentViewHandler : ContentViewHandler
{
    protected override ContentViewGroup CreatePlatformView()
    {
        _ = Context ?? throw new InvalidOperationException($"{nameof(TouchDownContentViewHandler)} has no Context.");

        // Mirrors ContentViewHandler.CreatePlatformView (CrossPlatformLayout = VirtualView), but observes the
        // down-touch on the way through.
        return new TouchObservingViewGroup(Context)
        {
            CrossPlatformLayout = VirtualView,
            OnTouchDown = () => (VirtualView as TouchDownContentView)?.RaiseTouchDown(),
        };
    }
}

/// <summary>
/// A <see cref="ContentViewGroup"/> that reports <c>ACTION_DOWN</c> as it passes through interception, always
/// returning the base result so it never actually intercepts — MAUI's own gesture handling on the children is
/// left entirely intact (the spike proved a consuming touch listener starves the pan; observing at
/// <c>onInterceptTouchEvent</c> does not).
/// </summary>
internal sealed class TouchObservingViewGroup : ContentViewGroup
{
    public Action? OnTouchDown { get; set; }

    public TouchObservingViewGroup(Context context) : base(context)
    {
    }

    public override bool OnInterceptTouchEvent(MotionEvent? e)
    {
        if (e?.ActionMasked == MotionEventActions.Down)
        {
            OnTouchDown?.Invoke();
        }

        return base.OnInterceptTouchEvent(e);
    }
}
