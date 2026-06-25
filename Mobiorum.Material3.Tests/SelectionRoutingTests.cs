using Mobiorum.Material3;
using Xunit;

namespace Mobiorum.Material3.Tests;

public class SelectionRoutingTests
{
    [Fact]
    public void FromOffset_CurrentCell_IsNoOp()
    {
        Assert.Equal(new SelectionRoute(SelectionKind.None, 0), SelectionRouting.FromOffset(0));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, -1)]
    public void FromOffset_Adjacent_Slides(int offset, int direction)
    {
        Assert.Equal(new SelectionRoute(SelectionKind.Slide, direction), SelectionRouting.FromOffset(offset));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(-3, -1)]
    [InlineData(6, 1)]
    public void FromOffset_Distant_Jumps(int offset, int direction)
    {
        Assert.Equal(new SelectionRoute(SelectionKind.Jump, direction), SelectionRouting.FromOffset(offset));
    }
}
