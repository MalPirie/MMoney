namespace Mobiorum.Material3;

/// <summary>The stimulus that prompts a resting transition — which of the strip's entry points fired.</summary>
public enum StripStimulus
{
    /// <summary>The host changed the selection (a tap on a different tab, a body-swipe commit, or an external change).</summary>
    SelectionChanged,

    /// <summary>A body drag ended without changing the selection (the drag-lock snap-back) — re-pin the resting scroll.</summary>
    TrackEnded,

    /// <summary>The already-selected tab (or Home when Home is the selection) was tapped — re-centre it, no selection change.</summary>
    RecentreTap,
}

/// <summary>
/// The resolved, MauiReactor-free inputs a <see cref="StripTransition"/> is a pure function of, alongside the
/// <see cref="StripLayout"/> and the underscore inset. The component reads these off its <c>State</c> (the same
/// <c>Window.IndexOf</c> / tracking lookups it already does) and hands them across so the decision itself is pure.
/// </summary>
/// <param name="PrevIndex">Window index of the previously-selected tab (<c>−1</c> if evicted). Only consulted for a
/// <see cref="StripStimulus.SelectionChanged"/> glide (the underscore slides <em>from</em> here).</param>
/// <param name="TargetIndex">Window index of the tab to come to rest on (<c>−1</c> if not materialised).</param>
/// <param name="WasTracking">Whether the previous render was tracking a live body drag — picks snap (adopt the
/// already-tracked tab) over glide for a selection change.</param>
public readonly record struct StripTransitionInput(int PrevIndex, int TargetIndex, bool WasTracking);

/// <summary>
/// A pure, MauiReactor-free decision describing HOW the <see cref="TabStrip{TItem}"/> should come to rest after a
/// stimulus: the whole glide-vs-snap-vs-reseed-vs-defer branch that used to be smeared across the component's
/// <c>AnimateToSelected</c>/<c>SnapToSelected</c>/<c>RecentreCommitted</c>/<c>Recentre</c> methods, each welded to a
/// <c>SetState</c>. <see cref="Resolve"/> composes the pure <see cref="StripLayout"/> geometry and returns one of
/// these; the component becomes a thin executor that applies the numbers and restarts a controller. The interface
/// is the test surface — every branch is a table-testable function of <c>(stimulus, input, layout)</c>.
///
/// <para>Because a reseed <em>mutates the window</em> (the freshly-seeded tabs are unmeasured — that is why those
/// paths defer), the resolver cannot carry post-reseed geometry: <see cref="Reseed"/>/<see cref="Defer"/> are thin
/// signals the component completes via <c>SeedWindow</c> + <c>TryInitialCentre</c>, while <see cref="Glide"/>/
/// <see cref="Snap"/> carry concrete endpoints computed off the current layout.</para>
///
/// <para>See <c>docs/adr/0003-tabbed-page-view.md</c> ("Real-control design"); unit-tested in
/// <c>Mobiorum.Material3.Tests</c>.</para>
/// </summary>
public abstract record StripTransition
{
    private StripTransition()
    {
    }

    /// <summary>Animate the underscore + scroll-centre from the previous rest to the target (via the SelectController).
    /// A recentre-tap on the same tab is a glide with <c>FromIx == ToIx</c> (no underscore slide, scroll eases home).</summary>
    public sealed record Glide(double FromIx, double ToIx, double FromIw, double ToIw, double FromCm, double ToCm) : StripTransition;

    /// <summary>Set the resting scroll (and, when <paramref name="Ix"/> is non-null, the underscore) directly with no
    /// animation. <paramref name="Ix"/> null = a scroll-only pin (track-ended snap-back); <paramref name="ThenSlide"/>
    /// runs the window's <c>MaybeSlide</c> afterward (the committed selection advancing forward).</summary>
    public sealed record Snap(double? Ix, double? Iw, double Cm, bool ThenSlide) : StripTransition;

    /// <summary>Re-seed a fresh window around the target and defer the centre to <c>TryInitialCentre</c> (the target
    /// was evicted, or reached across unmeasured tabs). Carries no geometry — the seed's widths are not yet known.</summary>
    public sealed record Reseed : StripTransition;

    /// <summary>Defer the centre to <c>TryInitialCentre</c> <em>without</em> re-seeding — the target is in the window
    /// but not yet measured. Distinct from <see cref="Reseed"/> so the snap path never re-centres the window on an
    /// in-window tab, which would shift the origin and flick the row.</summary>
    public sealed record Defer : StripTransition;

    /// <summary>Do nothing (the target is not materialised and there is nothing to place).</summary>
    public sealed record None : StripTransition;

    /// <summary>
    /// Resolve the transition for <paramref name="stimulus"/> over the current <paramref name="layout"/>, using
    /// <paramref name="inset"/> for the underscore geometry. Pure: no state is mutated, no framework is touched.
    /// </summary>
    public static StripTransition Resolve(StripStimulus stimulus, in StripTransitionInput input, StripLayout layout, double inset)
    {
        switch (stimulus)
        {
            case StripStimulus.TrackEnded:
                // A drag released without a commit: pin the scroll to the selected tab's centre, no underscore work.
                return input.TargetIndex < 0
                    ? new None()
                    : new Snap(null, null, layout.CentreOffset(input.TargetIndex), ThenSlide: false);

            case StripStimulus.RecentreTap:
            {
                var index = input.TargetIndex;
                if (index < 0)
                {
                    return new Reseed(); // the selected tab was scrolled off-window → re-seed around it and defer
                }

                var (ix, iw) = layout.IndicatorGeometry(index, inset);
                return new Glide(ix, ix, iw, iw, layout.Scroll, layout.CentreOffset(index)); // same tab: no slide, ease home
            }

            case StripStimulus.SelectionChanged:
            {
                var target = input.TargetIndex;

                if (input.WasTracking)
                {
                    // The drag lockstep already walked the underscore onto the new tab — SNAP, don't glide back-then-forth.
                    if (target < 0)
                    {
                        return new Reseed(); // committed onto a tab outside the window → seed + defer
                    }

                    if (!layout.MeasuredThrough(target))
                    {
                        return new Defer(); // a fast multi-page pan landed on a rendered-but-unmeasured tab → defer, no reseed
                    }

                    var (six, siw) = layout.IndicatorGeometry(target, inset);
                    return new Snap(six, siw, layout.CentreOffset(target), ThenSlide: true);
                }

                // Not tracking (a tab/Home tap): glide the underscore from the previous tab when the geometry from the
                // window start through the target is fully measured; otherwise re-seed a small window and defer.
                var prev = input.PrevIndex;
                var canGlide = prev >= 0 && target >= 0 && layout.MeasuredThrough(Math.Max(prev, target));
                if (!canGlide)
                {
                    return new Reseed();
                }

                var (fromX, fromW) = layout.IndicatorGeometry(prev, inset);
                var (toX, toW) = layout.IndicatorGeometry(target, inset);
                return new Glide(fromX, toX, fromW, toW, layout.Scroll, layout.CentreOffset(target));
            }

            default:
                return new None();
        }
    }
}
