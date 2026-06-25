namespace Mobiorum.Material3;

/// <summary>The outcome of a released horizontal drag on a <see cref="StripPager{TItem}"/>.</summary>
public enum PagerCommit
{
    /// <summary>Spring the current page back to rest — too short a drag, or a finite edge (bounce).</summary>
    RubberBack,

    /// <summary>Commit to the previous (offset −1) item.</summary>
    CommitPrev,

    /// <summary>Commit to the next (offset +1) item.</summary>
    CommitNext,
}

/// <summary>
/// Pure decisions for the pan-driven pager. Sign convention: <c>fraction = pan.TotalX / pageWidth</c>,
/// so a <em>positive</em> fraction drags the content right to reveal <c>Prev</c>, a <em>negative</em>
/// fraction reveals <c>Next</c>. A <see langword="null"/> neighbour in the drag direction is a finite
/// edge and always rubber-bands (the damped bounce). See <c>docs/adr/0002-strip-pager.md</c>.
/// </summary>
public static class PagerGesture
{
    /// <summary>Fraction of a page past which a released drag commits (the "point of no return").</summary>
    public const double CommitThreshold = 0.5;

    /// <summary>Decide the outcome of a released drag.</summary>
    /// <param name="fraction">Drag fraction in [−1, +1]; positive toward <c>Prev</c>, negative toward <c>Next</c>.</param>
    /// <param name="flick">Whether release velocity exceeded the flick threshold (commits below the distance threshold).</param>
    /// <param name="hasPrev">Whether a previous neighbour exists (else the back edge bounces).</param>
    /// <param name="hasNext">Whether a next neighbour exists (else the forward edge bounces).</param>
    /// <param name="threshold">Commit distance threshold; defaults to <see cref="CommitThreshold"/>.</param>
    public static PagerCommit Decide(
        double fraction, bool flick, bool hasPrev, bool hasNext, double threshold = CommitThreshold)
    {
        if (fraction > 0)
        {
            if (!hasPrev)
            {
                return PagerCommit.RubberBack;
            }

            return fraction >= threshold || flick ? PagerCommit.CommitPrev : PagerCommit.RubberBack;
        }

        if (fraction < 0)
        {
            if (!hasNext)
            {
                return PagerCommit.RubberBack;
            }

            return -fraction >= threshold || flick ? PagerCommit.CommitNext : PagerCommit.RubberBack;
        }

        return PagerCommit.RubberBack;
    }

    /// <summary>
    /// Damp an overscroll drag past a finite edge into a resisted rubber-band offset. Sign-preserving,
    /// monotonic, and always smaller in magnitude than the raw drag; resistance rises with distance so the
    /// edge feels progressively firmer.
    /// </summary>
    /// <param name="fraction">The raw (undamped) drag fraction past the edge.</param>
    /// <param name="factor">Base resistance in (0, 1]; lower is stiffer. Defaults to 0.4.</param>
    public static double DampOverscroll(double fraction, double factor = 0.4)
    {
        var magnitude = Math.Abs(fraction);
        // Rising resistance: divide the linear pull by (1 + magnitude) so each extra unit of drag yields less.
        var damped = factor * magnitude / (1 + magnitude);
        return Math.Sign(fraction) * damped;
    }
}
