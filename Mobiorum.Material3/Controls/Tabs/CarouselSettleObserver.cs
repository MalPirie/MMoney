#if ANDROID
using AndroidX.RecyclerView.Widget;
using Microsoft.Maui.Controls.Handlers.Items;
using MauiControls = Microsoft.Maui.Controls;
#endif

namespace Mobiorum.Material3;

/// <summary>
/// A library-owned Android seam that reports when a <c>CarouselView</c> has <b>settled</b> (its underlying
/// <c>RecyclerView</c> scroll state returns to idle). MAUI raises <c>PositionChanged</c>/<c>CurrentItemChanged</c>
/// <em>optimistically</em> the instant a page crosses centre mid-drag — so a slow drag that snaps back flickers
/// the reported position. There is no settle-only signal in the managed API, so <see cref="TabbedPageView{TItem}"/>
/// commits its selection off this instead: the tab changes only at the point of no return.
///
/// <para>The observer keys callbacks by the CarouselView's <c>AutomationId</c> (each <see cref="TabbedPageView{TItem}"/>
/// stamps a stable one). On non-Android platforms the whole thing is a no-op — there is no optimistic-drag problem
/// to correct (and the managed <c>PositionChanged</c> can be used directly if a body is ever needed there).</para>
/// </summary>
internal static class CarouselSettleObserver
{
    // AutomationId → "the carousel settled on this index". Plain managed state, safe on every platform.
    private static readonly Dictionary<string, Action<int>> Callbacks = new();

    /// <summary>Point an <c>AutomationId</c> at its owner's settle handler (idempotent — re-register each render
    /// so the callback always targets the live, possibly-rebuilt component instance).</summary>
    public static void Register(string automationId, Action<int> onSettled) => Callbacks[automationId] = onSettled;

    /// <summary>Drop a callback when its owner truly unmounts.</summary>
    public static void Unregister(string automationId) => Callbacks.Remove(automationId);

#if ANDROID
    // Track which RecyclerViews already carry our listener (the handler mapping runs on every update).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RecyclerView, object> Hooked = new();

    /// <summary>Install the handler-mapping hook. Called once from <see cref="MobiorumMaterial3.UseMobiorumMaterial3"/>.</summary>
    public static void Install()
    {
        CarouselViewHandler.Mapper.AppendToMapping("MobiorumCarouselSettle", (handler, view) =>
        {
            if (handler.PlatformView is not RecyclerView recyclerView || Hooked.TryGetValue(recyclerView, out _))
            {
                return;
            }

            Hooked.Add(recyclerView, new object());
            recyclerView.AddOnScrollListener(new SettleListener(handler)); // additive — MAUI's own listener is untouched
        });
    }

    private sealed class SettleListener : RecyclerView.OnScrollListener
    {
        private readonly IViewHandler _handler;

        public SettleListener(IViewHandler handler) => _handler = handler;

        public override void OnScrollStateChanged(RecyclerView recyclerView, int newState)
        {
            base.OnScrollStateChanged(recyclerView, newState);
            if (newState != RecyclerView.ScrollStateIdle)
            {
                return; // still dragging or settling — not the point of no return yet
            }

            if (_handler.VirtualView is not MauiControls.CarouselView carousel || carousel.AutomationId is not { Length: > 0 } id)
            {
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[Settle] IDLE id={id} pos={carousel.Position}");
            if (Callbacks.TryGetValue(id, out var onSettled))
            {
                onSettled(carousel.Position); // MAUI's own scroll listener has already resolved Position by idle
            }
        }
    }
#else
    /// <summary>No-op on non-Android platforms (no optimistic-drag problem to correct).</summary>
    public static void Install()
    {
    }
#endif
}
