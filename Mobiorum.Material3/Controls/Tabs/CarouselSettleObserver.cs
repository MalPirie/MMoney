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
/// <para>It also reports the <b>live page position</b> during a user drag (the drag-lock hook): while a finger owns
/// the scroll, <c>OnScrolled</c> routes a continuous position (<c>firstVisible + −child.Left/width</c>, the same
/// value Android's <c>ViewPager2.onPageScrolled</c> exposes) so the <see cref="TabStrip{TItem}"/> can slide its
/// indicator and scroll in lockstep with the body. This fires only for user-driven scrolls — a programmatic move
/// (a tab tap) is left to the strip's own selection animation.</para>
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

    // AutomationId → "the body is at this continuous page position" (fired only during a user drag).
    private static readonly Dictionary<string, Action<double>> ScrollCallbacks = new();

    /// <summary>Point an <c>AutomationId</c> at its owner's settle handler (idempotent — re-register each render
    /// so the callback always targets the live, possibly-rebuilt component instance).</summary>
    public static void Register(string automationId, Action<int> onSettled) => Callbacks[automationId] = onSettled;

    /// <summary>Point an <c>AutomationId</c> at its owner's live-drag handler (continuous page position; idempotent,
    /// re-registered each render like <see cref="Register"/>).</summary>
    public static void RegisterScroll(string automationId, Action<double> onScrolled) => ScrollCallbacks[automationId] = onScrolled;

    /// <summary>Drop both callbacks when the owner truly unmounts.</summary>
    public static void Unregister(string automationId)
    {
        Callbacks.Remove(automationId);
        ScrollCallbacks.Remove(automationId);
    }

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

        // Latched true from the moment a finger grabs the list (ScrollStateDragging) and held through the settling
        // animation — including the seam's own snap — until a real resting page is reached. Gates the live-drag
        // reports so a programmatic scroll (a tab tap moving the body) never drives the strip's lockstep.
        private bool _userDrag;

        public SettleListener(IViewHandler handler) => _handler = handler;

        public override void OnScrollStateChanged(RecyclerView recyclerView, int newState)
        {
            base.OnScrollStateChanged(recyclerView, newState);

            if (recyclerView.GetLayoutManager() is not LinearLayoutManager layout)
            {
                return;
            }

            if (newState == RecyclerView.ScrollStateDragging)
            {
                _userDrag = true; // a finger now owns the scroll → lockstep is live until it comes to rest
                return;
            }

            if (newState != RecyclerView.ScrollStateIdle)
            {
                return; // settling — not the point of no return yet (OnScrolled keeps the strip tracking)
            }

            // MAUI's CarouselView (old Items RecyclerView) does not reliably snap on a slow release — it can come to
            // rest straddling two pages (no completely-visible child). If so, drive a snap to the nearest page and
            // bail; the resulting clean idle re-enters here and commits. Only a completely-visible page is a real
            // settle (Loop=false → adapter index == item index). Whatever page the body lands on is committed as-is
            // — a firm drag that crosses several pages moves several pages (the strip snaps onto the landed tab).
            var settled = layout.FindFirstCompletelyVisibleItemPosition();
            if (settled == RecyclerView.NoPosition)
            {
                var first = layout.FindFirstVisibleItemPosition();
                if (first == RecyclerView.NoPosition || layout.FindViewByPosition(first) is not { Width: > 0 } child)
                {
                    return;
                }

                var nearest = -child.Left * 2 >= child.Width ? first + 1 : first;
                recyclerView.SmoothScrollToPosition(nearest);
                return;
            }

            _userDrag = false; // reached a real resting page — the gesture (and any snap it triggered) is complete

            if (!TryCarouselId(out var id))
            {
                return;
            }

            if (Callbacks.TryGetValue(id, out var onSettled))
            {
                onSettled(settled);
            }
        }

        public override void OnScrolled(RecyclerView recyclerView, int dx, int dy)
        {
            base.OnScrolled(recyclerView, dx, dy);
            if (!_userDrag)
            {
                return; // only a finger-driven scroll drives the strip lockstep; programmatic moves do not
            }

            if (recyclerView.GetLayoutManager() is not LinearLayoutManager layout)
            {
                return;
            }

            var first = layout.FindFirstVisibleItemPosition();
            if (first == RecyclerView.NoPosition || layout.FindViewByPosition(first) is not { Width: > 0 } child)
            {
                return;
            }

            // Continuous page position: the first visible page plus how far it has scrolled off the left edge as a
            // fraction of its width (== Android's ViewPager2.onPageScrolled position + offset).
            var position = first + (double)(-child.Left) / child.Width;
            if (TryCarouselId(out var id) && ScrollCallbacks.TryGetValue(id, out var onScrolled))
            {
                onScrolled(position);
            }
        }

        private bool TryCarouselId(out string id)
        {
            if (_handler.VirtualView is MauiControls.CarouselView carousel && carousel.AutomationId is { Length: > 0 } found)
            {
                id = found;
                return true;
            }

            id = string.Empty;
            return false;
        }
    }
#else
    /// <summary>No-op on non-Android platforms (no optimistic-drag problem to correct).</summary>
    public static void Install()
    {
    }
#endif
}
