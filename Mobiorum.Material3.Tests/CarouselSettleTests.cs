using Mobiorum.Material3;
using Xunit;

namespace Mobiorum.Material3.Tests;

/// <summary>
/// The native carousel's settle arithmetic, tested at its pure interface — the continuous drag position and the
/// idle straddle-vs-commit decision that were previously trapped behind <c>#if ANDROID</c> in the RecyclerView
/// listener, untested and device-tuned. Geometry is hand-authored (a page width and how far it has scrolled off
/// the left), exactly like the strip seams.
/// </summary>
public class CarouselSettleTests
{
    // ── continuous drag position ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ContinuousPosition_IsTheFirstPagePlusItsFractionOffTheLeftEdge()
    {
        Assert.Equal(3.0, CarouselSettle.ContinuousPosition(firstVisible: 3, childLeft: 0, childWidth: 100), 3);
        Assert.Equal(3.5, CarouselSettle.ContinuousPosition(3, childLeft: -50, childWidth: 100), 3);
        Assert.Equal(3.99, CarouselSettle.ContinuousPosition(3, childLeft: -99, childWidth: 100), 3);
    }

    // ── idle settle decision ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveSettle_CompletelyVisiblePage_IsTheRealSettle_Commit()
    {
        var outcome = CarouselSettle.ResolveSettle(completelyVisible: 4, firstVisible: 4, childLeft: 0, childWidth: 100);
        Assert.Equal(4, Assert.IsType<SettleOutcome.Commit>(outcome).Page);
    }

    [Fact]
    public void ResolveSettle_Straddle_PastHalfway_SnapsToTheNextPage()
    {
        // No completely-visible page; the first page is 60% scrolled off → nearest is the next one.
        var outcome = CarouselSettle.ResolveSettle(completelyVisible: null, firstVisible: 3, childLeft: -60, childWidth: 100);
        Assert.Equal(4, Assert.IsType<SettleOutcome.Snap>(outcome).Page);
    }

    [Fact]
    public void ResolveSettle_Straddle_BeforeHalfway_SnapsBackToTheFirstPage()
    {
        var outcome = CarouselSettle.ResolveSettle(completelyVisible: null, firstVisible: 3, childLeft: -40, childWidth: 100);
        Assert.Equal(3, Assert.IsType<SettleOutcome.Snap>(outcome).Page);
    }

    [Fact]
    public void ResolveSettle_Straddle_ExactlyHalfway_RoundsUp_MatchingContinuousPosition()
    {
        // Half rounds up (−left·2 ≥ width), consistent with ContinuousPosition(3, −50, 100) == 3.5.
        var outcome = CarouselSettle.ResolveSettle(completelyVisible: null, firstVisible: 3, childLeft: -50, childWidth: 100);
        Assert.Equal(4, Assert.IsType<SettleOutcome.Snap>(outcome).Page);
    }

    [Fact]
    public void ResolveSettle_NothingLaidOut_Ignores()
    {
        Assert.IsType<SettleOutcome.Ignore>(
            CarouselSettle.ResolveSettle(completelyVisible: null, firstVisible: null, childLeft: 0, childWidth: 0));
    }

    [Fact]
    public void ResolveSettle_FirstChildNotYetMeasured_Ignores()
    {
        // A first-visible page exists but has no width yet (mid-layout) — nothing to round against, so ignore.
        Assert.IsType<SettleOutcome.Ignore>(
            CarouselSettle.ResolveSettle(completelyVisible: null, firstVisible: 3, childLeft: 0, childWidth: 0));
    }
}
