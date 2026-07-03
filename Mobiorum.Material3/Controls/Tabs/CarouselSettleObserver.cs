#if ANDROID
using AndroidX.RecyclerView.Widget;
using Microsoft.Maui.Controls.Handlers.Items;
using MauiControls = Microsoft.Maui.Controls;
#endif

namespace Mobiorum.Material3;

/// <summary>
/// A library-owned Android seam that reports when a <c>CarouselView</c> has genuinely <b>settled</b> on a page, and
/// enforces the snap that MAUI's own <c>CarouselView</c> (the old Items <c>RecyclerView</c>) does unreliably. Two
/// managed-API problems make this necessary: (1) MAUI raises <c>PositionChanged</c>/<c>CurrentItemChanged</c>
/// <em>optimistically</em> the instant a page crosses centre mid-drag, so a slow drag that reverses reports a page
/// the user never landed on; and (2) on a slow release the RecyclerView can come to rest <em>straddling two pages</em>
/// (no completely-visible child) — MAUI never corrects it. So on idle this seam checks the geometry: if a page is
/// completely visible it is the real settle and gets reported; if none is, it drives a snap to the nearest page and
/// waits for the resulting clean idle. <see cref="TabbedPageView{TItem}"/> commits its selection only off a reported
/// settle, so the tab changes exactly once, at the point of no return.
///
/// <para>The observer keys callbacks by the CarouselView's <c>AutomationId</c> (each <see cref="TabbedPageView{TItem}"/>
/// stamps a stable one). On non-Android platforms the whole thing is a no-op — there is no optimistic-drag or
/// missed-snap problem to correct (and the managed <c>PositionChanged</c> can be used directly if a body is ever
/// needed there).</para>
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

            if (recyclerView.GetLayoutManager() is not LinearLayoutManager layout)
            {
                return;
            }

            // MAUI's CarouselView (the old Items RecyclerView) does NOT reliably snap on a slow release — it can
            // come to rest straddling two pages (no completely-visible child). Committing that guesses a page the
            // user never landed on. So the seam OWNS the snap: if we're not cleanly on a page, drive a snap to the
            // nearest one and bail — the resulting clean idle re-enters here and commits. Only a page that is
            // actually completely visible is a real settle (Loop=false → adapter index == item index).
            var settled = layout.FindFirstCompletelyVisibleItemPosition();
            if (settled == RecyclerView.NoPosition)
            {
                var first = layout.FindFirstVisibleItemPosition();
                if (first == RecyclerView.NoPosition || layout.FindViewByPosition(first) is not { Width: > 0 } child)
                {
                    return;
                }

                var scrolledOff = -child.Left;                              // px of `first` dragged past the left edge
                var nearest = scrolledOff * 2 >= child.Width ? first + 1 : first;
                recyclerView.SmoothScrollToPosition(nearest);              // re-enters this handler on the clean idle
                return;
            }

            if (_handler.VirtualView is not MauiControls.CarouselView carousel || carousel.AutomationId is not { Length: > 0 } id)
            {
                return;
            }

            if (Callbacks.TryGetValue(id, out var onSettled))
            {
                onSettled(settled);
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
