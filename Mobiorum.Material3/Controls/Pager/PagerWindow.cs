namespace Mobiorum.Material3;

/// <summary>
/// One materialised cell of a <see cref="StripPager{TItem}"/> window: the item together with its
/// signed offset from the current item (<c>Current</c> = 0, its forward neighbour = +1, two back = −2…).
/// The offset is tagged at materialisation so a tapped cell already knows its distance and direction —
/// the control never needs equality, an index, or a count on <typeparamref name="TItem"/>.
/// </summary>
public readonly record struct PagerCell<TItem>(TItem Item, int Offset) where TItem : struct;

/// <summary>
/// Pure, MauiReactor-free windowing over a navigable sequence (<c>Selected</c> + <c>Next</c>/<c>Prev</c>).
/// Steps the delegates out from the selected item, stopping at a <see langword="null"/> neighbour
/// (a finite edge — symmetric: a bound may exist forward, backward, both, or neither). Unit-tested in
/// <c>Mobiorum.Material3.Tests</c>; see <c>docs/adr/0002-strip-pager.md</c>.
/// </summary>
public static class PagerWindow
{
    /// <summary>
    /// Materialise strip cells centred on <paramref name="selected"/>, up to <paramref name="radius"/>
    /// each side, stopping early where <paramref name="next"/>/<paramref name="prev"/> returns
    /// <see langword="null"/>. Cells are returned in display order (most-negative offset first); the
    /// current cell (offset 0) is always present.
    /// </summary>
    public static IReadOnlyList<PagerCell<TItem>> Strip<TItem>(
        TItem selected, Func<TItem, TItem?> next, Func<TItem, TItem?> prev, int radius)
        where TItem : struct
        => StripRange(selected, next, prev, -radius, radius);

    /// <summary>
    /// Materialise strip cells with signed offsets in the inclusive range <c>[<paramref name="lo"/>,
    /// <paramref name="hi"/>]</c> (each tagged with its absolute offset from <paramref name="selected"/>),
    /// stopping early where <paramref name="next"/>/<paramref name="prev"/> returns <see langword="null"/>.
    /// Cells are returned in display order (most-negative offset first). Used to <em>grow</em> the strip window
    /// as it is browsed past its current edge, so a browse loads more cells without a selection change.
    /// </summary>
    public static IReadOnlyList<PagerCell<TItem>> StripRange<TItem>(
        TItem selected, Func<TItem, TItem?> next, Func<TItem, TItem?> prev, int lo, int hi)
        where TItem : struct
    {
        var cells = new List<PagerCell<TItem>>();
        if (lo <= 0 && 0 <= hi)
        {
            cells.Add(new PagerCell<TItem>(selected, 0));
        }

        var cursor = (TItem?)selected;
        for (var offset = 1; offset <= hi && cursor is not null; offset++)
        {
            cursor = next(cursor.Value);
            if (cursor is not { } forward)
            {
                break;
            }

            if (offset >= lo)
            {
                cells.Add(new PagerCell<TItem>(forward, offset));
            }
        }

        cursor = selected;
        for (var offset = -1; offset >= lo && cursor is not null; offset--)
        {
            cursor = prev(cursor.Value);
            if (cursor is not { } backward)
            {
                break;
            }

            if (offset <= hi)
            {
                cells.Insert(0, new PagerCell<TItem>(backward, offset));
            }
        }

        return cells;
    }

    /// <summary>
    /// The three pager bodies to render. <c>Prev</c>/<c>Next</c> are <see langword="null"/> at a finite
    /// edge (the drag in that direction rubber-bands rather than commits).
    /// </summary>
    public static (TItem? Prev, TItem Current, TItem? Next) Pages<TItem>(
        TItem selected, Func<TItem, TItem?> next, Func<TItem, TItem?> prev)
        where TItem : struct
        => (prev(selected), selected, next(selected));
}
