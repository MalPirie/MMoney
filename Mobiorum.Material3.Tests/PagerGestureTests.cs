using Mobiorum.Material3;
using Xunit;

namespace Mobiorum.Material3.Tests;

public class PagerGestureTests
{
    [Theory]
    [InlineData(0.6, PagerCommit.CommitPrev)]   // dragged right past threshold, Prev exists
    [InlineData(0.5, PagerCommit.CommitPrev)]   // exactly at threshold commits
    [InlineData(0.49, PagerCommit.RubberBack)]  // just short springs back
    public void Decide_TowardPrev_CommitsAtThreshold(double fraction, PagerCommit expected)
    {
        Assert.Equal(expected, PagerGesture.Decide(fraction, flick: false, hasPrev: true, hasNext: true));
    }

    [Theory]
    [InlineData(-0.6, PagerCommit.CommitNext)]
    [InlineData(-0.5, PagerCommit.CommitNext)]
    [InlineData(-0.49, PagerCommit.RubberBack)]
    public void Decide_TowardNext_CommitsAtThreshold(double fraction, PagerCommit expected)
    {
        Assert.Equal(expected, PagerGesture.Decide(fraction, flick: false, hasPrev: true, hasNext: true));
    }

    [Fact]
    public void Decide_Flick_CommitsBelowDistanceThreshold()
    {
        Assert.Equal(PagerCommit.CommitNext, PagerGesture.Decide(-0.2, flick: true, hasPrev: true, hasNext: true));
        Assert.Equal(PagerCommit.CommitPrev, PagerGesture.Decide(0.2, flick: true, hasPrev: true, hasNext: true));
    }

    [Fact]
    public void Decide_FiniteBackEdge_BouncesEvenPastThreshold()
    {
        Assert.Equal(PagerCommit.RubberBack, PagerGesture.Decide(0.9, flick: true, hasPrev: false, hasNext: true));
    }

    [Fact]
    public void Decide_FiniteForwardEdge_BouncesEvenPastThreshold()
    {
        // Symmetric with the back edge — the forward bound the MonthOnly demonstrator never hits.
        Assert.Equal(PagerCommit.RubberBack, PagerGesture.Decide(-0.9, flick: true, hasPrev: true, hasNext: false));
    }

    [Fact]
    public void Decide_NoMovement_RubberBacks()
    {
        Assert.Equal(PagerCommit.RubberBack, PagerGesture.Decide(0.0, flick: false, hasPrev: true, hasNext: true));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(-0.8)]
    public void DampOverscroll_IsSignPreservingAndSmallerThanRaw(double raw)
    {
        var damped = PagerGesture.DampOverscroll(raw);

        Assert.Equal(Math.Sign(raw), Math.Sign(damped));
        Assert.True(Math.Abs(damped) < Math.Abs(raw));
    }

    [Fact]
    public void DampOverscroll_RisesMonotonicallyButResistsMore()
    {
        var near = PagerGesture.DampOverscroll(0.2);
        var far = PagerGesture.DampOverscroll(0.8);

        Assert.True(far > near);                       // monotonic: more drag, more offset
        Assert.True((far - near) < (0.8 - 0.2));       // but resisted: offset grows slower than the drag
    }

    [Fact]
    public void DampOverscroll_AtRest_IsZero()
    {
        Assert.Equal(0.0, PagerGesture.DampOverscroll(0.0));
    }
}
