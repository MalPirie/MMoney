using Mobiorum.Material3;
using Xunit;

namespace Mobiorum.Material3.Tests;

/// <summary>
/// The strip's resting-transition decision, tested directly at its pure interface — the whole glide-vs-snap-vs-
/// reseed-vs-defer branch that used to be welded to <c>SetState</c> inside the component (untestable) and that
/// produced this feature's device bugs. Widths are hand-authored, exactly like <see cref="StripLayoutTests"/>.
/// </summary>
public class StripTransitionTests
{
    private const double Inset = 16;

    // Five uniform 100-wide tabs in a 200 viewport (SpanMin −300); both ends real so the clamp is exercised.
    private static readonly double[] Five = { 100, 100, 100, 100, 100 };

    private static StripLayout Layout(double[] widths, double scroll = 0) =>
        new(widths, 0, 200, scroll, true, true);

    private static StripTransition Resolve(
        StripStimulus stimulus, double[] widths, int prev, int target, bool wasTracking = false, double scroll = 0) =>
        StripTransition.Resolve(stimulus, new StripTransitionInput(prev, target, wasTracking), Layout(widths, scroll), Inset);

    // ── track-ended (snap-back) ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackEnded_InWindow_PinsTheScrollToCentre_NoUnderscoreNoSlide()
    {
        var d = Resolve(StripStimulus.TrackEnded, Five, prev: 2, target: 2);
        var snap = Assert.IsType<StripTransition.Snap>(d);
        Assert.Null(snap.Ix);           // no underscore work on a snap-back
        Assert.False(snap.ThenSlide);   // and no window slide
        Assert.Equal(-150, snap.Cm, 3); // CentreOffset(2)
    }

    [Fact]
    public void TrackEnded_TargetNotMaterialised_DoesNothing()
    {
        Assert.IsType<StripTransition.None>(Resolve(StripStimulus.TrackEnded, Five, prev: 0, target: -1));
    }

    // ── recentre-tap (tap the already-selected tab) ─────────────────────────────────────────────────────

    [Fact]
    public void RecentreTap_InWindow_GlidesHomeWithNoUnderscoreSlide()
    {
        var d = Resolve(StripStimulus.RecentreTap, Five, prev: 2, target: 2, scroll: 0);
        var g = Assert.IsType<StripTransition.Glide>(d);
        Assert.Equal(g.FromIx, g.ToIx, 3); // same tab → the underscore does not move …
        Assert.Equal(g.FromIw, g.ToIw, 3);
        Assert.Equal(216, g.ToIx, 3);      // IndicatorGeometry(2) X
        Assert.Equal(0, g.FromCm, 3);      // eases from the current scroll …
        Assert.Equal(-150, g.ToCm, 3);     // … to the centre
    }

    [Fact]
    public void RecentreTap_TargetEvicted_ReseedsAndDefers()
    {
        // Unified (Q6): tapping a selected tab scrolled off-window re-seeds + defers rather than snapping off
        // possibly-unmeasured widths (the old immediate-snap that could not self-correct).
        Assert.IsType<StripTransition.Reseed>(Resolve(StripStimulus.RecentreTap, Five, prev: -1, target: -1));
    }

    // ── selection changed, was tracking (the drag-lock commit → snap) ────────────────────────────────────

    [Fact]
    public void SelectionChanged_WasTracking_InWindowMeasured_SnapsWithSlide()
    {
        var d = Resolve(StripStimulus.SelectionChanged, Five, prev: 1, target: 2, wasTracking: true);
        var snap = Assert.IsType<StripTransition.Snap>(d);
        Assert.Equal(216, snap.Ix!.Value, 3); // adopt the already-tracked tab's underscore directly
        Assert.Equal(68, snap.Iw!.Value, 3);
        Assert.Equal(-150, snap.Cm, 3);
        Assert.True(snap.ThenSlide);           // advance the window forward as the selection commits
    }

    [Fact]
    public void SelectionChanged_WasTracking_InWindowUnmeasured_DefersWithoutReseeding()
    {
        // A fast multi-page pan lands on a rendered-but-unmeasured tab (a zero width before the target). Deferring
        // WITHOUT reseeding is the anti-flick path: reseeding here would shift the origin and flick the row.
        var widths = new[] { 100.0, 0, 100, 100, 100 };
        Assert.IsType<StripTransition.Defer>(
            Resolve(StripStimulus.SelectionChanged, widths, prev: 1, target: 2, wasTracking: true));
    }

    [Fact]
    public void SelectionChanged_WasTracking_TargetEvicted_Reseeds()
    {
        Assert.IsType<StripTransition.Reseed>(
            Resolve(StripStimulus.SelectionChanged, Five, prev: 1, target: -1, wasTracking: true));
    }

    // ── selection changed, not tracking (a tab / Home tap → glide) ───────────────────────────────────────

    [Fact]
    public void SelectionChanged_NotTracking_InWindowMeasured_GlidesFromPrevToTarget()
    {
        var d = Resolve(StripStimulus.SelectionChanged, Five, prev: 1, target: 3, scroll: -50);
        var g = Assert.IsType<StripTransition.Glide>(d);
        Assert.Equal(116, g.FromIx, 3); // IndicatorGeometry(1) X — slides from the previous tab …
        Assert.Equal(316, g.ToIx, 3);   // IndicatorGeometry(3) X — … to the target
        Assert.Equal(68, g.FromIw, 3);
        Assert.Equal(68, g.ToIw, 3);
        Assert.Equal(-50, g.FromCm, 3);  // eases from the current (clamped) scroll …
        Assert.Equal(-250, g.ToCm, 3);   // … to CentreOffset(3): 50 − 300, within [−300,0]
    }

    [Fact]
    public void SelectionChanged_NotTracking_TargetEvicted_Reseeds()
    {
        Assert.IsType<StripTransition.Reseed>(Resolve(StripStimulus.SelectionChanged, Five, prev: 1, target: -1));
    }

    [Fact]
    public void SelectionChanged_NotTracking_PrevEvicted_Reseeds_CannotGlideFromNowhere()
    {
        Assert.IsType<StripTransition.Reseed>(Resolve(StripStimulus.SelectionChanged, Five, prev: -1, target: 2));
    }

    [Fact]
    public void SelectionChanged_NotTracking_UnmeasuredAcrossTheSpan_Reseeds()
    {
        // A far tab reached across not-yet-measured tabs (the free-scroll-then-tap case): a zero width between prev
        // and target fails MeasuredThrough(max(prev,target)), so it re-seeds rather than gliding off zero widths.
        var widths = new[] { 100.0, 100, 0, 100, 100 };
        Assert.IsType<StripTransition.Reseed>(Resolve(StripStimulus.SelectionChanged, widths, prev: 1, target: 3));
    }
}
