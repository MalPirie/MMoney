namespace Mobiorum.Material3;

/// <summary>How a tapped strip cell reaches its target (see <see cref="SelectionRouting"/>).</summary>
public enum SelectionKind
{
    /// <summary>The current cell was tapped — nothing to do.</summary>
    None,

    /// <summary>An adjacent cell (offset ±1): slide one page, reusing the swipe-commit animation.</summary>
    Slide,

    /// <summary>A distant cell (|offset| &gt; 1): crossfade the body without sliding through the gap.</summary>
    Jump,
}

/// <summary>The route from the current item to a tapped one: how to animate, and which way.</summary>
/// <param name="Kind">Slide, jump, or no-op.</param>
/// <param name="Direction">+1 toward <c>Next</c>, −1 toward <c>Prev</c>, 0 for a no-op.</param>
public readonly record struct SelectionRoute(SelectionKind Kind, int Direction);

/// <summary>
/// Pure tap routing: a tapped strip cell's signed offset decides slide-vs-jump and direction. Distance 1
/// slides (the same animation as a committed swipe); distance &gt; 1 jumps (crossfade + underscore glide).
/// </summary>
public static class SelectionRouting
{
    /// <summary>Route a tap by the cell's signed offset from the current item.</summary>
    public static SelectionRoute FromOffset(int offset)
    {
        if (offset == 0)
        {
            return new SelectionRoute(SelectionKind.None, 0);
        }

        var direction = Math.Sign(offset);
        var kind = Math.Abs(offset) == 1 ? SelectionKind.Slide : SelectionKind.Jump;
        return new SelectionRoute(kind, direction);
    }
}
