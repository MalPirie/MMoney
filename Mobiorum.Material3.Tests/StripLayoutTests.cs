using Mobiorum.Material3;
using Xunit;

namespace Mobiorum.Material3.Tests;

public class StripLayoutTests
{
    // A layout with all-real edges by default; individual tests flip HasBackEdge/HasFwdEdge to model open ends.
    private static StripLayout Layout(
        double[] widths, double scroll = 0, double viewport = 200, double spacing = 0,
        bool back = true, bool fwd = true) =>
        new(widths, spacing, viewport, scroll, back, fwd);

    // ── geometry ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Total_SumsWidthsAndTheGapsBetween()
    {
        var layout = Layout(new[] { 50.0, 100, 150 }, spacing: 10);
        Assert.Equal(320, layout.Total, 3); // 300 widths + 2 gaps of 10
    }

    [Fact]
    public void Total_IsZero_ForAnEmptyWindow()
    {
        Assert.Equal(0, Layout(System.Array.Empty<double>()).Total, 3);
    }

    [Fact]
    public void ContentLeft_IsCumulativeWidthPlusSpacing()
    {
        var layout = Layout(new[] { 50.0, 100, 150 }, spacing: 10);
        Assert.Equal(0, layout.ContentLeft(0), 3);
        Assert.Equal(60, layout.ContentLeft(1), 3);  // 50 + 10
        Assert.Equal(170, layout.ContentLeft(2), 3); // 50 + 10 + 100 + 10
    }

    [Fact]
    public void SpanMin_CollapsesToZero_WhenContentFitsTheViewport()
    {
        var layout = Layout(new[] { 50.0, 50 }); // total 100 < viewport 200
        Assert.Equal(0, layout.SpanMin, 3);
    }

    [Fact]
    public void SpanMin_IsTheNegativeOverflow_WhenContentIsWiderThanTheViewport()
    {
        var layout = Layout(new[] { 100.0, 100, 100 }); // total 300, viewport 200
        Assert.Equal(-100, layout.SpanMin, 3);
    }

    // ── bounds / clamp ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Bounds_RealEdgesBothSides_ClampToTheSpanExtent()
    {
        var (min, max) = Layout(new[] { 100.0, 100, 100 }).Bounds();
        Assert.Equal(-100, min, 3);
        Assert.Equal(0, max, 3);
    }

    [Fact]
    public void Bounds_OpenEnds_AreUnclampedToInfinity()
    {
        var (min, max) = Layout(new[] { 100.0, 100, 100 }, back: false, fwd: false).Bounds();
        Assert.Equal(double.NegativeInfinity, min);
        Assert.Equal(double.PositiveInfinity, max);
    }

    [Fact]
    public void Clamp_RealEdges_PinScrollFlushWithNoGutter()
    {
        var layout = Layout(new[] { 100.0, 100, 100 });
        Assert.Equal(-100, layout.Clamp(-500), 3); // can't scroll past the last tab flush
        Assert.Equal(0, layout.Clamp(50), 3);      // can't scroll past tab 0 flush
    }

    [Fact]
    public void Clamp_OpenForwardEnd_LetsScrollRunPastTheSpan_ButStillPinsTheRealBackEdge()
    {
        var layout = Layout(new[] { 100.0, 100, 100 }, fwd: false); // open forward, real back edge
        Assert.Equal(-500, layout.Clamp(-500), 3); // forward is open — no clamp
        Assert.Equal(0, layout.Clamp(50), 3);      // back edge is real — still pinned
    }

    // ── hit-test ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HitTest_FindsTheTabUnderThePoint_AtRest()
    {
        var layout = Layout(new[] { 100.0, 100, 100 });
        Assert.Equal(1, layout.HitTest(120));
    }

    [Fact]
    public void HitTest_MirrorsOutTheScroll_SoTapsFollowTheScrolledRow()
    {
        var layout = Layout(new[] { 100.0, 100, 100 }, scroll: -100);
        Assert.Equal(1, layout.HitTest(20)); // contentX 120 → tab 1, proving a tap lands after scrolling
    }

    [Fact]
    public void HitTest_ReturnsMinusOne_PastTheEnd()
    {
        Assert.Equal(-1, Layout(new[] { 100.0, 100, 100 }).HitTest(500));
    }

    [Fact]
    public void HitTest_ReturnsMinusOne_InAGapBetweenTabs()
    {
        var layout = Layout(new[] { 100.0, 100 }, spacing: 20);
        Assert.Equal(-1, layout.HitTest(110)); // tab0 [0,100), gap [100,120), tab1 [120,220)
    }

    // ── centring ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CentreOffset_CentresAnInteriorTabExactly()
    {
        var layout = Layout(new[] { 100.0, 100, 100, 100, 100 }); // total 500
        // tab 2 spans content [200,300], centre 250; viewport centre 100 → offset -150 (within [-300,0]).
        Assert.Equal(-150, layout.CentreOffset(2), 3);
    }

    [Fact]
    public void CentreOffset_ClampsAtTheBackEdge_LandingFlushNotCentred()
    {
        var layout = Layout(new[] { 100.0, 100, 100, 100, 100 });
        Assert.Equal(0, layout.CentreOffset(0), 3); // ideal +50 clamps to 0
    }

    [Fact]
    public void CentreOffset_ClampsAtTheForwardEdge_LandingFlushNotCentred()
    {
        var layout = Layout(new[] { 100.0, 100, 100, 100, 100 });
        Assert.Equal(-300, layout.CentreOffset(4), 3); // ideal -350 clamps to SpanMin -300
    }

    // ── visibility ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsVisible_TrueForOnScreenTabs_FalseForScrolledOffTabs()
    {
        var atRest = Layout(new[] { 100.0, 100, 100 });
        Assert.True(atRest.IsVisible(0));
        Assert.False(atRest.IsVisible(2)); // left edge sits exactly at the viewport's right — off screen

        var scrolled = Layout(new[] { 100.0, 100, 100 }, scroll: -100);
        Assert.False(scrolled.IsVisible(0)); // scrolled fully off the left
        Assert.True(scrolled.IsVisible(2));  // pulled into view
    }

    // ── slide hysteresis ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Need_GrowsFront_WhenNearAnOpenBackEnd()
    {
        var layout = Layout(new[] { 100.0, 100, 100 }, scroll: -30, back: false); // open backward
        Assert.Equal(SlideNeed.GrowFront, layout.Need(margin: 50)); // -30 > SpanMax(0) - 50
    }

    [Fact]
    public void Need_GrowsEnd_WhenNearAnOpenForwardEnd()
    {
        var layout = Layout(new[] { 100.0, 100, 100 }, scroll: -70, fwd: false); // open forward, SpanMin -100
        Assert.Equal(SlideNeed.GrowEnd, layout.Need(margin: 50)); // -70 < SpanMin(-100) + 50
    }

    [Fact]
    public void Need_None_WhenDeepInTheBuffer()
    {
        // A deep span (600 content, viewport 200 → SpanMin -400) so there's a real None zone [-350, -50].
        var layout = Layout(new[] { 100.0, 100, 100, 100, 100, 100 }, scroll: -200, back: false, fwd: false);
        Assert.Equal(SlideNeed.None, layout.Need(margin: 50)); // -200 is >50 from both open ends
    }

    [Fact]
    public void Need_None_AtARealEdge_EvenWhenFlushAgainstIt()
    {
        // Both ends real: flush at either edge must NOT try to slide — the clamp owns those.
        Assert.Equal(SlideNeed.None, Layout(new[] { 100.0, 100, 100 }, scroll: 0).Need(50));
        Assert.Equal(SlideNeed.None, Layout(new[] { 100.0, 100, 100 }, scroll: -100).Need(50));
    }

    // ── finite range (bounded BOTH ends) ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FiniteRange_NarrowerThanViewport_PinsTheScrollAtZero()
    {
        // A whole finite sequence that fits: total 120 < viewport 200, both ends real. Nothing scrolls — the
        // bounds collapse to a single point at 0 (tab 0 flush left, no gutter either side).
        var layout = Layout(new[] { 40.0, 40, 40 }, spacing: 0);
        var (min, max) = layout.Bounds();
        Assert.Equal(0, min, 3);
        Assert.Equal(0, max, 3);
        Assert.Equal(0, layout.Clamp(-50), 3); // a drag left can't move it
        Assert.Equal(0, layout.Clamp(50), 3);  // nor right
    }

    [Fact]
    public void FiniteRange_NarrowerThanViewport_CentresEveryTabToFlushLeft()
    {
        // With no room to scroll, "centre" clamps to 0 for every tab — the strip just sits flush.
        var layout = Layout(new[] { 40.0, 40, 40 });
        Assert.Equal(0, layout.CentreOffset(0), 3);
        Assert.Equal(0, layout.CentreOffset(1), 3);
        Assert.Equal(0, layout.CentreOffset(2), 3);
    }

    [Fact]
    public void SingleItemRange_IsFullyPinnedAndHitTestable()
    {
        // A one-tab finite range: no spacing contribution, no scroll, the sole tab centres to flush and hits.
        var layout = Layout(new[] { 80.0 });
        Assert.Equal(80, layout.Total, 3);
        var (min, max) = layout.Bounds();
        Assert.Equal(0, min, 3);
        Assert.Equal(0, max, 3);
        Assert.Equal(0, layout.CentreOffset(0), 3);
        Assert.Equal(0, layout.HitTest(40)); // a tap on it lands
        Assert.True(layout.IsVisible(0));
    }

    [Fact]
    public void FiniteRange_WiderThanViewport_NeverSlides_TheClampOwnsBothEnds()
    {
        // Both ends real, content wider than the viewport: the window must NEVER try to grow, wherever the
        // scroll sits — a finite range materialises in full, and the hard clamp (not a slide) owns both edges.
        var widths = new[] { 100.0, 100, 100, 100, 100 }; // total 500, viewport 200, SpanMin -300
        Assert.Equal(SlideNeed.None, Layout(widths, scroll: 0).Need(50));     // flush at the back edge
        Assert.Equal(SlideNeed.None, Layout(widths, scroll: -150).Need(50));  // mid-span
        Assert.Equal(SlideNeed.None, Layout(widths, scroll: -300).Need(50));  // flush at the forward edge
    }

    [Fact]
    public void EmptyWindow_HitTestReturnsMinusOne()
    {
        Assert.Equal(-1, Layout(System.Array.Empty<double>()).HitTest(10));
    }

    // ── underscore geometry ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IndicatorGeometry_SpansTheTabText_InsetOnBothSides()
    {
        var layout = Layout(new[] { 100.0, 100, 100 });
        var (x, w) = layout.IndicatorGeometry(1, inset: 16);
        Assert.Equal(116, x, 3); // content-left 100 + inset 16
        Assert.Equal(68, w, 3);  // tab 100 − 2×16
    }

    [Fact]
    public void IndicatorGeometry_ClampsWidthToZero_ForATabNarrowerThanTwiceTheInset()
    {
        var layout = Layout(new[] { 20.0 });
        var (_, w) = layout.IndicatorGeometry(0, inset: 16); // 20 − 32 would be negative
        Assert.Equal(0, w, 3);
    }

    // ── measured-through ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MeasuredThrough_TrueWhenEveryWidthUpToHiIsPositive()
    {
        Assert.True(Layout(new[] { 100.0, 100, 100 }).MeasuredThrough(2));
    }

    [Fact]
    public void MeasuredThrough_FalseWhenAZeroWidthSitsAtOrBeforeHi()
    {
        var layout = Layout(new[] { 100.0, 0, 100 }); // tab 1 unmeasured
        Assert.False(layout.MeasuredThrough(2)); // the gap at index 1 is within [0, hi]
        Assert.True(layout.MeasuredThrough(0));  // but everything through index 0 is measured
    }

    // ── re-anchor ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Rebase_OnEviction_KeepsEveryTabVisuallyPut()
    {
        // A tab at content 100 sits at visual 100 + (-250) = -150. After evicting a 100-wide front tab its
        // content becomes 0; the rebased scroll must reproduce the same -150 visual.
        var rebased = StripLayout.Rebase(scroll: -250, removedFrontWidth: 100, addedFrontWidth: 0);
        Assert.Equal(-150, rebased, 3);
        Assert.Equal(-150, 0 + rebased, 3); // that tab's new visual position, unchanged
    }

    [Fact]
    public void Rebase_OnPrepend_KeepsEveryTabVisuallyPut()
    {
        var rebased = StripLayout.Rebase(scroll: -250, removedFrontWidth: 0, addedFrontWidth: 100);
        Assert.Equal(-350, rebased, 3);
    }
}
