using Mobiorum.Material3;
using Xunit;

namespace Mobiorum.Material3.Tests;

public class PagerWindowTests
{
    // An int sequence. Forward is open-ended; backward is bounded at `floor` (Prev returns null there),
    // mirroring MonthOnly's open-ended-forward / edit-lock-bounded-back shape — but the helpers make no
    // such assumption, so other tests bound the forward edge too.
    private static Func<int, int?> Next(int? ceiling = null) => x => ceiling is { } c && x >= c ? null : x + 1;
    private static Func<int, int?> Prev(int? floor = null) => x => floor is { } f && x <= f ? null : x - 1;

    [Fact]
    public void Strip_Unbounded_FillsBothSidesToRadius()
    {
        var cells = PagerWindow.Strip(0, Next(), Prev(), radius: 2);

        Assert.Equal(
            new[] { -2, -1, 0, 1, 2 },
            cells.Select(c => c.Offset));
        Assert.Equal(
            new[] { -2, -1, 0, 1, 2 },
            cells.Select(c => c.Item));
    }

    [Fact]
    public void Strip_AlwaysIncludesCurrentAtOffsetZero()
    {
        var cells = PagerWindow.Strip(7, Next(ceiling: 7), Prev(floor: 7), radius: 3);

        var current = Assert.Single(cells);
        Assert.Equal(new PagerCell<int>(7, 0), current);
    }

    [Fact]
    public void Strip_StopsAtBackEdge_PinningTheCurrentToTheStart()
    {
        // Selected sits one step above the floor: only a single backward cell exists.
        var cells = PagerWindow.Strip(1, Next(), Prev(floor: 0), radius: 3);

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, cells.Select(c => c.Item));
        Assert.Equal(new[] { -1, 0, 1, 2, 3 }, cells.Select(c => c.Offset));
    }

    [Fact]
    public void Strip_StopsAtForwardEdge_SymmetricWithTheBack()
    {
        // The control assumes no infinity: a bounded forward edge truncates just like the back.
        var cells = PagerWindow.Strip(9, Next(ceiling: 10), Prev(), radius: 3);

        Assert.Equal(new[] { 6, 7, 8, 9, 10 }, cells.Select(c => c.Item));
        Assert.Equal(new[] { -3, -2, -1, 0, 1 }, cells.Select(c => c.Offset));
    }

    [Fact]
    public void Strip_BoundedBothSides_ReturnsOnlyWhatExists()
    {
        var cells = PagerWindow.Strip(5, Next(ceiling: 6), Prev(floor: 4), radius: 5);

        Assert.Equal(new[] { 4, 5, 6 }, cells.Select(c => c.Item));
        Assert.Equal(new[] { -1, 0, 1 }, cells.Select(c => c.Offset));
    }

    [Fact]
    public void StripRange_OffsetWindowAllForward_OmitsCurrent()
    {
        // A window browsed entirely forward of the selection (lo > 0) does not include offset 0.
        var cells = PagerWindow.StripRange(0, Next(), Prev(), lo: 3, hi: 5);

        Assert.Equal(new[] { 3, 4, 5 }, cells.Select(c => c.Offset));
        Assert.Equal(new[] { 3, 4, 5 }, cells.Select(c => c.Item));
    }

    [Fact]
    public void StripRange_AsymmetricWindow_GrowsForwardKeepsBack()
    {
        // The browse grows the forward edge while the back stays put: [-2 .. 6].
        var cells = PagerWindow.StripRange(0, Next(), Prev(), lo: -2, hi: 6);

        Assert.Equal(new[] { -2, -1, 0, 1, 2, 3, 4, 5, 6 }, cells.Select(c => c.Offset));
    }

    [Fact]
    public void StripRange_TruncatesAtNullEdgeWithinTheWindow()
    {
        // Back edge (edit lock) at floor 0: a wide back window truncates to what exists.
        var cells = PagerWindow.StripRange(1, Next(), Prev(floor: 0), lo: -5, hi: 2);

        Assert.Equal(new[] { 0, 1, 2, 3 }, cells.Select(c => c.Item));
        Assert.Equal(new[] { -1, 0, 1, 2 }, cells.Select(c => c.Offset));
    }

    [Fact]
    public void StripRange_MatchesStrip_ForSymmetricRange()
    {
        var range = PagerWindow.StripRange(4, Next(ceiling: 6), Prev(floor: 2), lo: -3, hi: 3);
        var strip = PagerWindow.Strip(4, Next(ceiling: 6), Prev(floor: 2), radius: 3);

        Assert.Equal(strip.Select(c => c.Offset), range.Select(c => c.Offset));
        Assert.Equal(strip.Select(c => c.Item), range.Select(c => c.Item));
    }

    [Fact]
    public void Pages_ExposesNullableNeighbours()
    {
        var (prev, current, next) = PagerWindow.Pages(0, Next(), Prev());
        Assert.Equal(-1, prev);
        Assert.Equal(0, current);
        Assert.Equal(1, next);
    }

    [Fact]
    public void Pages_NullAtEachFiniteEdge()
    {
        var atBack = PagerWindow.Pages(0, Next(), Prev(floor: 0));
        Assert.Null(atBack.Prev);
        Assert.Equal(1, atBack.Next);

        var atFront = PagerWindow.Pages(10, Next(ceiling: 10), Prev());
        Assert.Equal(9, atFront.Prev);
        Assert.Null(atFront.Next);
    }
}
