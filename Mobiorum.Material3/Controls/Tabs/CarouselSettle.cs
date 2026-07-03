namespace Mobiorum.Material3;

/// <summary>
/// The meaning of an idle carousel, resolved by <see cref="CarouselSettle.ResolveSettle"/>: the body reached a real
/// resting page (<see cref="Commit"/>), or the release straddled two pages and must be snapped to the nearest
/// (<see cref="Snap"/>), or there was nothing laid out to act on (<see cref="Ignore"/>). Mirrors the
/// <see cref="StripTransition"/> seam's decision-then-thin-adapter shape so both native resolvers read alike.
/// </summary>
public abstract record SettleOutcome
{
    private SettleOutcome()
    {
    }

    /// <summary>A completely-visible page — the real settle. The adapter reports it up and ends the gesture.</summary>
    public sealed record Commit(int Page) : SettleOutcome;

    /// <summary>The release straddled two pages: drive <c>SmoothScrollToPosition(Page)</c> and await the clean re-idle.</summary>
    public sealed record Snap(int Page) : SettleOutcome;

    /// <summary>Nothing measurable to settle on (no first-visible child, or it has no width yet) — a no-op.</summary>
    public sealed record Ignore : SettleOutcome;
}

/// <summary>
/// Pure, Android-free geometry over a native carousel's RecyclerView at a scroll event — the arithmetic MAUI's
/// <c>CarouselView</c> hides. It translates the layout manager's raw child geometry into page positions: the
/// continuous drag position (matching Android's <c>ViewPager2.onPageScrolled</c>) and the idle settle decision (a
/// completely-visible page is a real settle; a straddle must be snapped to the nearest page). Link-compiled into
/// <c>Mobiorum.Material3.Tests</c> and unit-tested with hand-authored geometry; the Android <c>SettleListener</c>
/// in <see cref="CarouselSettleObserver"/> is a thin adapter that reads the RecyclerView and delegates here.
/// See <c>docs/adr/0003-tabbed-page-view.md</c> ("Selection sync + a settle-only commit seam").
/// </summary>
public static class CarouselSettle
{
    /// <summary>
    /// The continuous page position during a drag: the first visible page plus how far it has scrolled off the left
    /// edge as a fraction of its width (== <c>ViewPager2.onPageScrolled</c> position + offset). <paramref name="childLeft"/>
    /// is negative as the page scrolls left; <paramref name="childWidth"/> must be positive (the caller guards).
    /// </summary>
    public static double ContinuousPosition(int firstVisible, int childLeft, int childWidth) =>
        firstVisible + (double)(-childLeft) / childWidth;

    /// <summary>
    /// Resolve what an idle RecyclerView means. <paramref name="completelyVisible"/> is the first completely-visible
    /// page (<see langword="null"/> = none — the release straddled two); <paramref name="firstVisible"/> is the first
    /// partially-visible page (<see langword="null"/> = nothing laid out). A completely-visible page is the resting
    /// page (<see cref="SettleOutcome.Commit"/>). Otherwise, with a measured first child, round to the nearest page
    /// and snap toward it (half rounds up, matching <see cref="ContinuousPosition"/>). With nothing measurable, ignore.
    /// </summary>
    public static SettleOutcome ResolveSettle(int? completelyVisible, int? firstVisible, int childLeft, int childWidth)
    {
        if (completelyVisible is int page)
        {
            return new SettleOutcome.Commit(page);
        }

        if (firstVisible is int first && childWidth > 0)
        {
            var nearest = -childLeft * 2 >= childWidth ? first + 1 : first;
            return new SettleOutcome.Snap(nearest);
        }

        return new SettleOutcome.Ignore();
    }
}
