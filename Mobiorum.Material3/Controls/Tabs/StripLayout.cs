namespace Mobiorum.Material3;

/// <summary>Whether the sliding window must materialise more tabs on a side, and which.</summary>
public enum SlideNeed
{
    /// <summary>The buffer is deep enough on both sides — no re-window.</summary>
    None,

    /// <summary>The <em>front</em> (leftmost / earliest) buffer is nearly exhausted; grow backward.</summary>
    GrowFront,

    /// <summary>The <em>end</em> (rightmost / latest) buffer is nearly exhausted; grow forward.</summary>
    GrowEnd,
}

/// <summary>
/// Pure, MauiReactor-free geometry over the <c>TabStrip</c>'s currently-materialised window — the whole of the
/// control's arithmetic in one value. It reads the ordered tab <see cref="Widths"/> (index 0 = leftmost /
/// earliest), the inter-tab <see cref="Spacing"/>, the <see cref="Viewport"/> width, the current
/// <see cref="Scroll"/> (the row's <c>TranslationX</c>; 0 = tab 0 flush left, negative = scrolled left), and
/// whether each end of the window is a <em>real</em> sequence edge (<see cref="HasBackEdge"/> / <see
/// cref="HasFwdEdge"/> — a <see langword="null"/> neighbour) or merely a window edge with more tabs beyond.
///
/// <para>Coordinate model: a tab's position is in <em>content space</em> (cumulative widths from tab 0); its
/// on-screen position is <c>contentLeft + Scroll</c>. A real edge <b>hard-clamps</b> the scroll (flush, no
/// gutter); an open end is <b>not</b> clamped — the control slides the window instead (see
/// <see cref="Need"/>). Every query acts only on materialised tabs, so no off-window width is ever needed.
/// Unit-tested in <c>Mobiorum.Material3.Tests</c>; see <c>docs/adr/0003-tabbed-page-view.md</c>
/// ("Real-control design").</para>
/// </summary>
public readonly record struct StripLayout(
    IReadOnlyList<double> Widths,
    double Spacing,
    double Viewport,
    double Scroll,
    bool HasBackEdge,
    bool HasFwdEdge)
{
    /// <summary>Total content width of the materialised span (all widths plus the gaps between them).</summary>
    public double Total
    {
        get
        {
            if (Widths.Count == 0)
            {
                return 0;
            }

            var total = Spacing * (Widths.Count - 1);
            foreach (var w in Widths)
            {
                total += w;
            }

            return total;
        }
    }

    /// <summary>Left edge of tab <paramref name="index"/> in content space (cumulative widths + spacing).</summary>
    public double ContentLeft(int index)
    {
        var x = 0.0;
        for (var i = 0; i < index && i < Widths.Count; i++)
        {
            x += Widths[i] + Spacing;
        }

        return x;
    }

    /// <summary>
    /// Furthest-left resting scroll: lands the last tab flush at the right edge (no gutter). Never positive —
    /// when the span is narrower than the viewport it collapses to 0 (everything fits, tab 0 stays flush left).
    /// </summary>
    public double SpanMin => -Math.Max(0, Total - Viewport);

    /// <summary>Furthest-right resting scroll: tab 0 flush at the left edge. Always 0.</summary>
    public double SpanMax => 0;

    /// <summary>
    /// Hard-clamp bounds for the scroll. A real sequence edge clamps to the span extent on that side; an open
    /// end returns an infinity (no clamp — the window slides there instead). Note the sign mapping: the
    /// <em>back</em> (earliest) edge is the front/left of the window, so it clamps the scroll <em>maximum</em>;
    /// the <em>forward</em> edge is the end/right, clamping the <em>minimum</em>.
    /// </summary>
    public (double Min, double Max) Bounds() =>
        (HasFwdEdge ? SpanMin : double.NegativeInfinity,
         HasBackEdge ? SpanMax : double.PositiveInfinity);

    /// <summary>Clamp a candidate scroll to <see cref="Bounds"/> (open ends pass through unclamped).</summary>
    public double Clamp(double scroll)
    {
        var (min, max) = Bounds();
        return Math.Clamp(scroll, min, max);
    }

    /// <summary>
    /// The materialised tab under a viewport-space x, or −1 if the point falls in a gap or past the ends.
    /// Mirrors the scroll out to reach content space, then walks the cumulative widths.
    /// </summary>
    public int HitTest(double viewportX)
    {
        var contentX = viewportX - Scroll;
        var acc = 0.0;
        for (var i = 0; i < Widths.Count; i++)
        {
            var w = Widths[i];
            if (w > 0 && contentX >= acc && contentX < acc + w)
            {
                return i;
            }

            acc += w + Spacing;
        }

        return -1;
    }

    /// <summary>
    /// The scroll that centres tab <paramref name="index"/> in the viewport, clamped to <see cref="Bounds"/>
    /// — so an interior tab centres exactly while one near a real edge lands flush (never leaving a gutter).
    /// </summary>
    public double CentreOffset(int index)
    {
        var centre = Viewport / 2 - (ContentLeft(index) + Width(index) / 2);
        return Clamp(centre);
    }

    /// <summary>Whether any part of tab <paramref name="index"/> is currently on screen.</summary>
    public bool IsVisible(int index)
    {
        var left = ContentLeft(index) + Scroll;
        var right = left + Width(index);
        return right > 0 && left < Viewport;
    }

    /// <summary>
    /// Whether the window must grow, and which side. Fires only toward an <em>open</em> end (a real edge is
    /// hard-clamped, never slid) when the scroll comes within <paramref name="margin"/> of that end's resting
    /// extent — giving the re-window lead time before the buffer shows through.
    /// </summary>
    public SlideNeed Need(double margin)
    {
        if (!HasBackEdge && Scroll > SpanMax - margin)
        {
            return SlideNeed.GrowFront;
        }

        if (!HasFwdEdge && Scroll < SpanMin + margin)
        {
            return SlideNeed.GrowEnd;
        }

        return SlideNeed.None;
    }

    /// <summary>
    /// Rebase the scroll after the front of the window changed, keeping every tab visually put (the atomic
    /// re-anchor). Evicting front tabs shifts content space left, so the scroll rises by their width; prepending
    /// front tabs shifts it right, so the scroll drops by theirs. On-screen position (<c>contentLeft + Scroll</c>)
    /// is invariant because both terms move by the same amount.
    /// </summary>
    public static double Rebase(double scroll, double removedFrontWidth, double addedFrontWidth) =>
        scroll + removedFrontWidth - addedFrontWidth;

    private double Width(int index) => index >= 0 && index < Widths.Count ? Widths[index] : 0;
}
