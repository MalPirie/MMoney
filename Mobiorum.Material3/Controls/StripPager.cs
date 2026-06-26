using System.Diagnostics;
using MauiReactor;
using MauiReactor.Shapes;

namespace Mobiorum.Material3;

// =====================================================================================================
// PURE-MVU StripPager (native-drive reverted 2026-06-26).
//
// The native-control-drive redesign (mutating TranslationX/ScaleX directly, off-render) cut the per-frame
// re-render cost but desynced MAUI's pan-gesture state: translating the very view that owns the pan, with no
// re-render, made the platform drop terminal events (Completed/Canceled) on fast flicks — the body froze
// mid-swipe. This version goes back to driving everything through SetState (declarative), which keeps MAUI's
// gesture state in sync and makes terminal events reliable. The cost is a re-render per drag frame; the
// stutter that originally motivated the native drive turned out to be page WEIGHT (the host's 30-row,
// non-virtualised MonthPage × 3), not the render mechanism — that's the host's to virtualise.
//
// Search "AREA OF INTEREST" for the spots that matter to the gesture/perf investigation.
// =====================================================================================================

/// <summary>Transient interaction state for a <see cref="StripPager{TItem}"/> (see ADR-0002).</summary>
public sealed class StripPagerState<TItem> where TItem : struct
{
    /// <summary>Measured width of one page, in device-independent units (0 until first layout).</summary>
    public double PageWidth;

    /// <summary>Body drag/settle fraction in [−1, +1]. Positive drags content right toward <c>Prev</c>; negative toward <c>Next</c>.</summary>
    public double Fraction;

    /// <summary>Strip offset in DIP, carried per cell. 0 centres the selected cell; non-zero = browsed/sliding.</summary>
    public double StripScroll;

    /// <summary>True while the body pages animate a commit slide; gates their <c>WithAnimation</c>.</summary>
    public bool BodyAnimating;

    /// <summary>True while the strip eases to re-centre; gates the cells' and underscore's <c>WithAnimation</c>.</summary>
    public bool StripAnimating;

    /// <summary>True while the body crossfades a jump; gates the body opacity <c>WithAnimation</c>.</summary>
    public bool Crossfading;

    /// <summary>Body opacity, driven 1→0→1 to crossfade a jump.</summary>
    public double BodyOpacity = 1;
}

/// <summary>
/// A Material 3 "synced strip + pager": a horizontal, independently-scrollable label <b>strip</b> with a
/// selected-cell <b>underscore</b> above a swipeable, vertically-scrolling <b>pager</b> body, kept in lockstep.
/// Generic over a value-type item navigated by <c>Next</c>/<c>Prev</c> (no index/count). See
/// <c>docs/adr/0002-strip-pager.md</c>. The host owns <see cref="Selected(TItem)"/>; the control owns all
/// transient interaction state. Pure-MVU drive (see the file header).
/// </summary>
public sealed partial class StripPager<TItem> : Component<StripPagerState<TItem>>
    where TItem : struct
{
    /// <summary>The committed, centred item — the host's single source of truth. Set via <c>.Selected(...)</c>.</summary>
    [Prop] TItem _selected;

    /// <summary>Step forward; returns <see langword="null"/> at a finite forward edge. Set via <c>.Next(...)</c>.</summary>
    [Prop] Func<TItem, TItem?> _next = _ => null;

    /// <summary>Step back; returns <see langword="null"/> at the back edge (e.g. the edit lock). Set via <c>.Prev(...)</c>.</summary>
    [Prop] Func<TItem, TItem?> _prev = _ => null;

    /// <summary>The strip cell's text for an item. Set via <c>.Label(...)</c>.</summary>
    [Prop] Func<TItem, string> _label = x => x.ToString() ?? string.Empty;

    /// <summary>The body content for one item (the control wraps it in its own vertical scroller). Set via <c>.Page(...)</c>.</summary>
    [Prop] Func<TItem, VisualNode> _page = _ => null!;

    /// <summary>Invoked when a swipe commits or a cell is tapped. Set via <c>.OnSelectedChanged(...)</c>.</summary>
    [Prop] Action<TItem> _onSelectedChanged = _ => { };

    private const double SelectionDurationMs = 220;
    private const int AnimSetupFrameMs = 16;     // one frame to establish WithAnimation before tweening a value
    private const double FrameBudgetMs = 16;     // coalesce drag re-renders to ~one per frame
    private const double AxisSlopDip = 10;
    private const double StripHeightDip = 48;
    private const double StripCellWidthDip = 84;
    private const double UnderscoreWidthDip = 32;
    private const double UnderscoreHeightDip = 3;
    private const double FlickVelocityDipPerSec = 450;
    private const double VelocityWindowMs = 80;  // window over which release velocity is measured (noise rejection)
    private const int StripRadius = 8;           // cells materialised each side; grows as the strip is browsed

    // Per-gesture bookkeeping kept off State (no re-render on change).
    private int _axisLock;             // 0 = undecided, 1 = horizontal (page), 2 = vertical (inner scroller)
    private double _appliedBodyOffset; // page translation we last applied — used to undo gesture self-distortion
    private double _lastVelocity;      // DIP/s, signed (+ toward Prev) — measured over a short time window
    private readonly List<(long Ticks, double Offset)> _velSamples = new();
    private double _pendingFraction;   // latest drag fraction (may be un-applied due to coalescing)
    private long _lastApplyTicks;      // timestamp of the last applied (rendered) drag frame
    private double _stripStartScroll;  // StripScroll at the start of a strip-browse drag
    private double _appliedStripDelta; // strip translation applied this gesture — undoes self-distortion
    private int _generation;           // bumps on every settle/jump to cancel stale async continuations
    private int _windowLo = -StripRadius; // materialise-window lower offset; grows as the strip is browsed back
    private int _windowHi = StripRadius;  // materialise-window upper offset; grows as the strip is browsed forward
    private int _matLo;                // actual most-negative offset materialised last render (a null edge truncates)
    private int _matHi;                // actual most-positive offset materialised last render

    // AREA OF INTEREST — recreation-on-commit. The render counter resets to #1 when the control is rebuilt; if a
    // commit (OnSelectedChanged → host SetState) tears the control down and remounts it, instance fields below
    // reset mid-flight. Watch OnMounted/OnWillUnmount around a commit during the investigation.
    private int _renderCount;

    public override VisualNode Render()
    {
        Debug.WriteLine($"[StripPager] RENDER #{++_renderCount} frac={State.Fraction:F2} scroll={State.StripScroll:F0}");
        var scheme = MaterialTheme.Current;
        var width = State.PageWidth > 0 ? State.PageWidth : FallbackWidth;

        return Grid("Auto,*", "*",
            RenderStrip(scheme, width).GridRow(0),
            RenderPager(scheme, width).GridRow(1)
        );
    }

    protected override void OnMounted()
    {
        Debug.WriteLine("[StripPager] *** OnMounted (new instance) ***");
        base.OnMounted();
    }

    protected override void OnPropsChanged()
    {
        Debug.WriteLine("[StripPager] *** OnPropsChanged ***");
        base.OnPropsChanged();
    }

    protected override void OnWillUnmount()
    {
        Debug.WriteLine("[StripPager] *** OnWillUnmount (instance destroyed) ***");
        base.OnWillUnmount();
    }

    // ---- Pager body --------------------------------------------------------------------------------------

    private VisualNode RenderPager(MaterialScheme scheme, double width)
    {
        var (prev, current, next) = PagerWindow.Pages(_selected, _next, _prev);
        var offset = State.Fraction * width; // live peek during a drag, and the slide during a commit

        // AREA OF INTEREST — page materialisation / per-frame cost. All present neighbours are rendered and
        // positioned by TranslationX in one cell. A per-frame SetState(Fraction) re-renders ALL of them — with a
        // heavy page that is the stutter. Perf levers: render only the dragged-toward neighbour (sign of
        // Fraction), and/or coalesce harder. Kept simple here for a clean investigation baseline.
        var pages = new List<VisualNode>();
        if (prev is { } p)
        {
            pages.Add(PageHost(p, -1, width, offset));
        }

        pages.Add(PageHost(current, 0, width, offset)); // current on top (added last)

        if (next is { } n)
        {
            pages.Add(PageHost(n, 1, width, offset));
        }

        var body = Grid(pages.ToArray())
            .IsClippedToBounds(true)
            .Opacity(State.BodyOpacity)
            .OnSizeChanged((Size size) =>
            {
                if (Math.Abs(size.Width - State.PageWidth) > 0.5)
                {
                    SetState(s => s.PageWidth = size.Width);
                }
            });

        // Crossfade only during a jump (BodyOpacity 1→0→1). A drag/commit drives translation, not opacity.
        return State.Crossfading ? body.WithAnimation(duration: SelectionDurationMs) : body;
    }

    private VisualNode PageHost(TItem item, int slot, double width, double offset)
    {
        //Debug.WriteLine($"[StripPager] *** RENDER PagerHost ({item.ToString() ?? "null"}) ***");

        // AREA OF INTEREST — the gesture seam (why MVU is reliable). The pan sits on the page CONTENT inside the
        // control's own vertical ScrollView: on Android the vertical scroller passes horizontal moves to its
        // children, so the pan arbitrates vertical-scroll vs horizontal-page. The page translates (slot*width +
        // offset), so the pan's own view moves with the drag — the "self-distortion" that OnBodyPan reconstructs.
        // Because each drag frame is a real SetState re-render, MAUI's gesture state stays in sync and the
        // terminal event (Completed/Canceled) fires reliably — the thing the native (no-re-render) drive broke.
        var host = ScrollView(
                Grid(_page(item)).OnPanUpdated(OnBodyPan)
            )
            .Orientation(ScrollOrientation.Vertical)
            .WidthRequest(width)
            .HStart()
            .TranslationX(slot * width + offset)
            .WithKey(item); // reuse a surviving page's view across a window shift instead of rebuilding it

        // AREA OF INTEREST — WithAnimation priming. MauiReactor only tweens a property whose node carried
        // WithAnimation on the PREVIOUS render; Settle turns these flags on one frame before changing the value
        // (see Settle/Jump). Leaving it on every frame would set up an animation per node per frame and stutter
        // the drag, so it is off during a drag and on only for the settle.
        return State.BodyAnimating ? host.WithAnimation(duration: SelectionDurationMs) : host;
    }

    // ---- Strip + underscore ------------------------------------------------------------------------------

    private VisualNode RenderStrip(MaterialScheme scheme, double width)
    {
        var cells = PagerWindow.StripRange(_selected, _next, _prev, _windowLo, _windowHi);

        // Remember the actual span materialised (a null edge — e.g. the edit lock — truncates it) so a browse
        // clamps to it and only grows further where the sequence actually continues.
        _matLo = cells.Count > 0 ? cells[0].Offset : 0;
        _matHi = cells.Count > 0 ? cells[^1].Offset : 0;

        var centre = width / 2;
        var nodes = cells
            .Select(cell => StripCell(scheme, cell, centre, cell.Offset == 0))
            .ToList();
        nodes.Add(Underscore(scheme, centre));

        // The container is NEVER translated (cells carry StripScroll per-cell). MAUI/Android only delivers a
        // cell's tap when its parent isn't translated — translating the container broke taps after a browse.
        return Grid(nodes.ToArray())
            .HeightRequest(StripHeightDip)
            .BackgroundColor(scheme.Surface)
            .IsClippedToBounds(true);
    }

    private VisualNode StripCell(MaterialScheme scheme, PagerCell<TItem> cell, double centre, bool selected)
    {
        // The pan must sit on the cells (where the touch lands) — a pan on the parent never fires because the
        // tappable cells sit on top of it. Each cell carries tap (select), pan (browse), and its resting StripScroll.
        var node = Grid(
            Label(_label(cell.Item))
                .FontSize(13)
                .FontAttributes(selected ? FontAttributes.Bold : FontAttributes.None)
                .TextColor(selected ? scheme.OnSurface : scheme.OnSurfaceVariant)
                .HCenter()
                .VCenter()
        )
        .WidthRequest(StripCellWidthDip)
        .HStart()
        .TranslationX(centre - (StripCellWidthDip / 2) + cell.Offset * StripCellWidthDip + State.StripScroll)
        .OnTapped(() => OnCellTapped(cell.Offset))
        .OnPanUpdated(OnStripPan);

        return State.StripAnimating ? node.WithAnimation(duration: SelectionDurationMs) : node;
    }

    private VisualNode Underscore(MaterialScheme scheme, double centre)
    {
        // Part of the selected cell's styling: seated under offset 0, carrying StripScroll so it rides with the
        // cells. No independent motion; it hops one cell to the new selection on commit (ADR-0002).
        var bar = Border()
            .BackgroundColor(scheme.Primary)
            .StrokeThickness(0)
            .StrokeShape(new RoundRectangle().CornerRadius(UnderscoreHeightDip / 2))
            .WidthRequest(UnderscoreWidthDip)
            .HeightRequest(UnderscoreHeightDip)
            .HStart()
            .VEnd()
            .TranslationX(centre - (UnderscoreWidthDip / 2) + State.StripScroll);

        return State.StripAnimating ? bar.WithAnimation(duration: SelectionDurationMs) : bar;
    }

    // ---- Body gesture ------------------------------------------------------------------------------------

    private void OnBodyPan(PanUpdatedEventArgs e)
    {
        var width = State.PageWidth > 0 ? State.PageWidth : FallbackWidth;

        // AREA OF INTEREST — terminal-event reliability. Log every non-Running status; the investigation is
        // whether Completed/Canceled fire on release (they did NOT under the native drive on fast flicks).
        if (e.StatusType is not GestureStatus.Running)
        {
            Debug.WriteLine($"[StripPager] PAN {e.StatusType} axis={_axisLock} vel={_lastVelocity:F0} frac={_pendingFraction:F2}");
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _axisLock = 0;
                _appliedBodyOffset = 0;
                _lastVelocity = 0;
                _pendingFraction = 0;
                _lastApplyTicks = 0;
                _velSamples.Clear();
                break;

            case GestureStatus.Running:
                if (State.BodyAnimating || State.Crossfading)
                {
                    return; // ignore input mid-animation
                }

                if (_axisLock == 0)
                {
                    if (Math.Max(Math.Abs(e.TotalX), Math.Abs(e.TotalY)) < AxisSlopDip)
                    {
                        return; // within touch slop — axis not yet decided
                    }

                    // AREA OF INTEREST — axis lock with NO SetState. The native build flipped the inner ScrollView
                    // to Orientation=Neither here via SetState; that mid-gesture re-render killed the pan on fast
                    // motion. A vertical ScrollView already ignores a dominantly-horizontal drag, so the flag alone
                    // suffices. (If diagonal drags scroll vertically too much, revisit.)
                    _axisLock = Math.Abs(e.TotalX) >= Math.Abs(e.TotalY) ? 1 : 2;
                }

                if (_axisLock != 1)
                {
                    return; // vertical drag — the inner scroller owns it
                }

                // AREA OF INTEREST — self-distortion reconstruction. Translating the pan's own view shrinks MAUI's
                // reported TotalX, so the true finger offset is the report plus what we have already applied.
                var trueOffset = e.TotalX + _appliedBodyOffset;

                // AREA OF INTEREST — windowed release velocity. A single 11–16ms sample is far too noisy
                // (4–9dp ⇒ 500–800dip/s on a slow drag) and tripped the flick detector. Measure across ~80ms.
                var nowTicks = Stopwatch.GetTimestamp();
                _velSamples.Add((nowTicks, trueOffset));
                var windowStart = nowTicks - (long)(VelocityWindowMs / 1000.0 * Stopwatch.Frequency);
                while (_velSamples.Count > 2 && _velSamples[0].Ticks < windowStart)
                {
                    _velSamples.RemoveAt(0);
                }

                var span = (_velSamples[^1].Ticks - _velSamples[0].Ticks) / (double)Stopwatch.Frequency;
                if (span > 0.001)
                {
                    _lastVelocity = (_velSamples[^1].Offset - _velSamples[0].Offset) / span;
                }

                _pendingFraction = DragFraction(trueOffset / width);

                // AREA OF INTEREST — per-frame re-render / coalescing. Each applied frame is a SetState (whole
                // control re-renders). Coalesce to ~one per frame budget; skipped events still accumulate via
                // TotalX, and the reconstruction baseline only moves when a frame is actually applied.
                if ((nowTicks - _lastApplyTicks) / (double)Stopwatch.Frequency * 1000 >= FrameBudgetMs)
                {
                    var frac = _pendingFraction;
                    SetState(s => { s.Fraction = frac; s.StripScroll = frac * StripCellWidthDip; });
                    _appliedBodyOffset = frac * width;
                    _lastApplyTicks = nowTicks;
                }

                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                var wasHorizontal = _axisLock == 1;
                _axisLock = 0;
                if (!wasHorizontal)
                {
                    break;
                }

                // Apply the latest (possibly un-applied/coalesced) frame, then decide.
                var pending = _pendingFraction;
                SetState(s => { s.Fraction = pending; s.StripScroll = pending * StripCellWidthDip; });
                var flick = Math.Abs(_lastVelocity) > FlickVelocityDipPerSec;
                var decision = PagerGesture.Decide(
                    pending, flick, hasPrev: _prev(_selected) is not null, hasNext: _next(_selected) is not null);
                Debug.WriteLine($"[StripPager] RELEASE vel={_lastVelocity:F0} flick={flick} frac={pending:F2} decision={decision}");
                Settle(decision);
                break;
        }
    }

    /// <summary>Clamp an interior drag to one page; damp it into a rubber-band past a finite edge.</summary>
    private double DragFraction(double raw)
    {
        if (raw > 0 && _prev(_selected) is null)
        {
            return PagerGesture.DampOverscroll(raw);
        }

        if (raw < 0 && _next(_selected) is null)
        {
            return PagerGesture.DampOverscroll(raw);
        }

        return Math.Clamp(raw, -1, 1);
    }

    // ---- Strip gesture (independent horizontal browse) ---------------------------------------------------

    private void OnStripPan(PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _stripStartScroll = State.StripScroll;
                _appliedStripDelta = 0;
                break;

            case GestureStatus.Running:
                // The pan sits on the cells, which translate with StripScroll — undo the self-distortion the same
                // way as the body (true delta = report + what we have already applied).
                var trueDelta = e.TotalX + _appliedStripDelta;

                // Clamp to the materialised span: most-forward cell centres at -_matHi*cell, most-back at -_matLo*cell.
                var total = Math.Clamp(_stripStartScroll + trueDelta, -_matHi * StripCellWidthDip, -_matLo * StripCellWidthDip);

                // Load more when nearing a window edge the sequence continues past (back edge = the real edit lock).
                var centreOffset = (int)Math.Round(-total / StripCellWidthDip);
                if (centreOffset >= _matHi - 2 && _matHi == _windowHi)
                {
                    _windowHi += StripRadius;
                }
                else if (centreOffset <= _matLo + 2 && _matLo == _windowLo)
                {
                    _windowLo -= StripRadius;
                }

                SetState(s => { s.StripScroll = total; s.StripAnimating = false; });
                _appliedStripDelta = total - _stripStartScroll;
                break;
        }
    }

    // ---- Selection changes -------------------------------------------------------------------------------

    private void OnCellTapped(int offset)
    {
        Debug.WriteLine($"[StripPager] TAP offset={offset} scroll={State.StripScroll:F0}");
        var route = SelectionRouting.FromOffset(offset);
        switch (route.Kind)
        {
            case SelectionKind.None:
                Recentre(); // already selected — just glide back to centre if the strip was browsed
                break;

            case SelectionKind.Slide:
                Settle(route.Direction > 0 ? PagerCommit.CommitNext : PagerCommit.CommitPrev);
                break;

            case SelectionKind.Jump:
                if (Step(offset) is { } target)
                {
                    Jump(target, offset);
                }

                break;
        }
    }

    // AREA OF INTEREST — commit/settle is a chain of SetState + await, and the final step calls
    // _onSelectedChanged which re-renders the HOST. If the host remounts this control (see OnMounted), this async
    // method keeps running against the old instance while a new one mounts — watch for that during a commit.
    private async void Settle(PagerCommit decision)
    {
        var generation = ++_generation;

        var committed = decision switch
        {
            PagerCommit.CommitPrev => _prev(_selected),
            PagerCommit.CommitNext => _next(_selected),
            _ => null,
        };

        // Prime WithAnimation for one frame at the current values so the upcoming change tweens (MauiReactor only
        // animates a property whose node carried WithAnimation on the previous render — else it snaps).
        SetState(s => { s.BodyAnimating = true; s.StripAnimating = true; });
        await Task.Delay(AnimSetupFrameMs);
        if (_generation != generation)
        {
            return;
        }

        if (committed is not { } item)
        {
            // Nothing to commit (rubber-band, finite edge, or tap on current): ease the body back to rest.
            SetState(s => { s.Fraction = 0; s.StripScroll = 0; });
            await Task.Delay((int)SelectionDurationMs);
            if (_generation == generation)
            {
                SetState(s => { s.BodyAnimating = false; s.StripAnimating = false; });
            }

            return;
        }

        // One coordinated motion: body slides the neighbour in while the strip scrolls that same cell to centre.
        var toNext = decision == PagerCommit.CommitNext;
        SetState(s =>
        {
            s.Fraction = toNext ? -1.0 : 1.0;
            s.StripScroll = (toNext ? -1.0 : 1.0) * StripCellWidthDip;
        });
        await Task.Delay((int)SelectionDurationMs);
        if (_generation != generation)
        {
            return;
        }

        // Seamless swap: the neighbour is already centred in both, so resetting to rest with it selected produces
        // no visible change (the underscore hops one cell to the new selection here).
        ResetWindow();
        SetState(s =>
        {
            s.BodyAnimating = false;
            s.StripAnimating = false;
            s.Fraction = 0;
            s.StripScroll = 0;
        });
        _onSelectedChanged(item);
    }

    private async void Jump(TItem target, int offset)
    {
        var generation = ++_generation;

        SetState(s => { s.Crossfading = true; s.StripAnimating = true; });
        await Task.Delay(AnimSetupFrameMs);
        if (_generation != generation)
        {
            return;
        }

        // The jump crossfades the body (no slide through the gap); the strip scrolls the target to centre.
        SetState(s => { s.BodyOpacity = 0; s.StripScroll = -offset * StripCellWidthDip; });
        await Task.Delay((int)SelectionDurationMs);
        if (_generation != generation)
        {
            return;
        }

        ResetWindow();
        SetState(s => { s.StripAnimating = false; s.StripScroll = 0; s.Fraction = 0; });
        _onSelectedChanged(target);
        SetState(s => s.BodyOpacity = 1);
        await Task.Delay((int)SelectionDurationMs);
        if (_generation == generation)
        {
            SetState(s => s.Crossfading = false);
        }
    }

    /// <summary>Glide the browsed strip back so the selected cell is centred again (no selection change).</summary>
    private async void Recentre()
    {
        if (Math.Abs(State.StripScroll) < 0.5)
        {
            return;
        }

        var generation = ++_generation;
        SetState(s => s.StripAnimating = true);
        await Task.Delay(AnimSetupFrameMs);
        if (_generation != generation)
        {
            return;
        }

        SetState(s => s.StripScroll = 0);
        await Task.Delay((int)SelectionDurationMs);
        if (_generation == generation)
        {
            ResetWindow();
            SetState(s => s.StripAnimating = false);
        }
    }

    /// <summary>Shrink the (possibly browse-grown) strip window back to ±<see cref="StripRadius"/> around selected.</summary>
    private void ResetWindow()
    {
        _windowLo = -StripRadius;
        _windowHi = StripRadius;
    }

    /// <summary>Walk <paramref name="offset"/> steps (signed) from the current selection; null if a bound is hit.</summary>
    private TItem? Step(int offset)
    {
        TItem? cursor = _selected;
        var step = offset > 0 ? _next : _prev;
        for (var i = 0; i < Math.Abs(offset) && cursor is { } value; i++)
        {
            cursor = step(value);
        }

        return cursor;
    }

    private static double FallbackWidth
    {
        get
        {
            var info = DeviceDisplay.Current.MainDisplayInfo;
            return info.Density > 0 ? info.Width / info.Density : 360;
        }
    }
}
