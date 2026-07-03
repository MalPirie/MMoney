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
    // AutomationId → its owner's paired settle + live-drag handlers. One entry per carousel, so a caller cannot
    // half-register (settle without scroll, or vice versa). Plain managed state, safe on every platform.
    private static readonly Dictionary<string, CarouselCallbacks> Callbacks = new();

    // The settle commit ("the carousel settled on this index") and the live-drag report ("the body is at this
    // continuous page position", fired only during a user drag), held together so registration is atomic.
    private sealed record CarouselCallbacks(Action<int> OnSettled, Action<double> OnScrolled);

    /// <summary>Point an <c>AutomationId</c> at its owner's settle + live-drag handlers (idempotent — re-register
    /// each render so the callbacks always target the live, possibly-rebuilt component instance).</summary>
    public static void Register(string automationId, Action<int> onSettled, Action<double> onScrolled) =>
        Callbacks[automationId] = new CarouselCallbacks(onSettled, onScrolled);

    /// <summary>Drop the callbacks when the owner truly unmounts.</summary>
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
            // rest straddling two pages (no completely-visible child). The pure CarouselSettle kernel decides: a
            // completely-visible page is a real settle (Loop=false → adapter index == item index) and commits; a
            // straddle drives a snap to the nearest page and the resulting clean idle re-enters here to commit;
            // nothing laid out is ignored. Whatever page the body lands on is committed as-is — a firm drag crossing
            // several pages moves several pages (the strip snaps onto the landed tab).
            var completely = layout.FindFirstCompletelyVisibleItemPosition();
            var first = layout.FindFirstVisibleItemPosition();
            var child = first == RecyclerView.NoPosition ? null : layout.FindViewByPosition(first);

            var outcome = CarouselSettle.ResolveSettle(
                completely == RecyclerView.NoPosition ? null : completely,
                first == RecyclerView.NoPosition ? null : first,
                child?.Left ?? 0,
                child?.Width ?? 0);

            switch (outcome)
            {
                case SettleOutcome.Commit commit:
                    _userDrag = false; // reached a real resting page — the gesture (and any snap it triggered) is complete
                    if (TryCarouselId(out var id) && Callbacks.TryGetValue(id, out var callbacks))
                    {
                        callbacks.OnSettled(commit.Page);
                    }

                    break;

                case SettleOutcome.Snap snap:
                    recyclerView.SmoothScrollToPosition(snap.Page); // straddled two — drive to nearest, await the re-idle
                    break;

                case SettleOutcome.Ignore:
                    break; // nothing laid out yet — stay latched and wait for the next idle
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

            // Continuous page position (the CarouselSettle kernel owns the formula == ViewPager2.onPageScrolled).
            var position = CarouselSettle.ContinuousPosition(first, child.Left, child.Width);
            if (TryCarouselId(out var id) && Callbacks.TryGetValue(id, out var callbacks))
            {
                callbacks.OnScrolled(position);
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
