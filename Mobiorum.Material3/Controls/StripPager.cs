using System.Diagnostics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using MauiReactor;
using MauiReactor.Shapes;

namespace Mobiorum.Material3;

/// <summary>Transient interaction state for a <see cref="StripPager{TItem}"/> (see ADR-0002).</summary>
public sealed class StripPagerState<TItem> where TItem : struct
{
    /// <summary>Measured width of one page, in device-independent units (0 until first layout).</summary>
    public double PageWidth;

    /// <summary>
    /// Body drag/settle fraction in [−1, +1]. Positive drags content right toward <c>Prev</c>; negative
    /// toward <c>Next</c>. Reconstructed to undo the gesture self-distortion (see the body pan handler).
    /// </summary>
    public double Fraction;

    /// <summary>Horizontal offset of the strip, in DIP. 0 centres the selected cell; non-zero = browsed left/right.</summary>
    public double StripScroll;

    /// <summary>True while the body pages animate a commit slide; gates their <c>WithAnimation</c>.</summary>
    public bool BodyAnimating;

    /// <summary>True while the strip eases to re-centre; gates the cells' and underscore's <c>WithAnimation</c>.</summary>
    public bool StripAnimating;

    /// <summary>True while the body crossfades a jump; gates the body opacity <c>WithAnimation</c>.</summary>
    public bool Crossfading;

    /// <summary>Body opacity, driven 1→0→1 to crossfade a jump.</summary>
    public double BodyOpacity = 1;

    /// <summary>True while a horizontal drag is locked; disables the page's vertical scroll so only one axis acts.</summary>
    public bool HorizontalLock;
}

/// <summary>
/// A Material 3 "synced strip + pager": a horizontal, independently-scrollable label <b>strip</b> with a
/// sliding <b>underscore</b> above a swipeable, vertically-scrolling <b>pager</b> body, kept in lockstep.
/// Generic over a value-type item navigated by <c>Next</c>/<c>Prev</c> (no index/count). See
/// <c>docs/adr/0002-strip-pager.md</c>. The host owns <see cref="Selected(TItem)"/>; the control owns all
/// transient interaction state.
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

    /// <summary>
    /// The body <em>content</em> for one item. The control wraps it in its own vertical scroller (so the pan
    /// recogniser can arbitrate horizontal-page vs vertical-scroll) — return the content, not a scroller.
    /// </summary>
    [Prop] Func<TItem, VisualNode> _page = _ => null!;

    /// <summary>Invoked when a swipe commits or a cell is tapped, with the newly selected item. Set via <c>.OnSelectedChanged(...)</c>.</summary>
    [Prop] Action<TItem> _onSelectedChanged = _ => { };

    private const double SelectionDurationMs = 220;
    private const double AxisSlopDip = 10;
    private const double StripHeightDip = 48;
    private const double StripCellWidthDip = 84;
    private const double UnderscoreWidthDip = 32;
    private const double UnderscoreHeightDip = 3;
    private const double FlickVelocityDipPerSec = 450;
    private const int StripRadius = 14; // cells materialised each side — generous so the strip can be browsed

    // Per-gesture bookkeeping kept off State (no re-render on change).
    private int _axisLock;             // 0 = undecided, 1 = horizontal (page), 2 = vertical (inner scroller)
    private double _appliedBodyOffset; // page translation we last applied — used to undo gesture self-distortion
    private double _lastTrue;          // last reconstructed true offset (for velocity)
    private long _lastTicks;           // timestamp of last sample (for velocity)
    private double _lastVelocity;      // DIP/s, signed (+ toward Prev)
    private double _stripStartScroll;  // StripScroll at the start of a strip-browse drag
    private double _appliedStripDelta; // strip translation applied this gesture — undoes self-distortion
    private int _generation;           // bumps on every settle/jump to cancel stale async continuations

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        var width = State.PageWidth > 0 ? State.PageWidth : FallbackWidth;

        return Grid("Auto,*", "*",
            RenderStrip(scheme, width).GridRow(0),
            RenderPager(scheme, width).GridRow(1)
        );
    }

    // ---- Pager body --------------------------------------------------------------------------------------

    private VisualNode RenderPager(MaterialScheme scheme, double width)
    {
        var (prev, current, next) = PagerWindow.Pages(_selected, _next, _prev);
        var offset = State.Fraction * width; // live peek during a drag, and the slide during a commit

        var pages = new List<VisualNode> { PageHost(current, 0, width, offset) };
        if (prev is { } p)
        {
            pages.Insert(0, PageHost(p, -1, width, offset));
        }

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

        return State.Crossfading ? body.WithAnimation(duration: SelectionDurationMs) : body;
    }

    private VisualNode PageHost(TItem item, int slot, double width, double offset)
    {
        // The control owns the vertical scroller and puts the pan on its content: on Android a vertical
        // ScrollView lets horizontal moves reach its children, so the pan arbitrates correctly. While a
        // horizontal drag is locked, the scroller's orientation drops to Neither so only one axis acts.
        var host = ScrollView(
                Grid(_page(item)).OnPanUpdated(OnBodyPan)
            )
            .Orientation(State.HorizontalLock ? ScrollOrientation.Neither : ScrollOrientation.Vertical)
            .WidthRequest(width)
            .HStart()
            .TranslationX(slot * width + offset);

        return State.BodyAnimating ? host.WithAnimation(duration: SelectionDurationMs) : host;
    }

    // ---- Strip + underscore ------------------------------------------------------------------------------

    private VisualNode RenderStrip(MaterialScheme scheme, double width)
    {
        var cells = PagerWindow.Strip(_selected, _next, _prev, StripRadius);
        var displayOffset = DisplaySelectedOffset();
        var centre = width / 2;

        var nodes = cells
            .Select(cell => StripCell(scheme, cell, centre, cell.Offset == displayOffset))
            .ToList();
        nodes.Add(Underscore(scheme, centre));

        return Grid(nodes.ToArray())
            .HeightRequest(StripHeightDip)
            .BackgroundColor(scheme.Surface)
            .IsClippedToBounds(true);
    }

    private VisualNode StripCell(MaterialScheme scheme, PagerCell<TItem> cell, double centre, bool displaySelected)
    {
        // The pan must sit on the cells (where the touch lands) — a pan on the parent strip container never
        // fires because the tappable cells sit on top of it. Each cell carries both tap (select) and pan
        // (browse the strip sideways).
        var node = Grid(
            Label(_label(cell.Item))
                .FontSize(13)
                .FontAttributes(displaySelected ? FontAttributes.Bold : FontAttributes.None)
                .TextColor(displaySelected ? scheme.OnSurface : scheme.OnSurfaceVariant)
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
        // Seated under the selected cell at rest; slides toward the drag candidate ("in step" with the finger).
        var drift = Math.Clamp(-State.Fraction, -1, 1) * StripCellWidthDip;

        var bar = Border()
            .BackgroundColor(scheme.Primary)
            .StrokeThickness(0)
            .StrokeShape(new RoundRectangle().CornerRadius(UnderscoreHeightDip / 2))
            .WidthRequest(UnderscoreWidthDip)
            .HeightRequest(UnderscoreHeightDip)
            .HStart()
            .VEnd()
            .TranslationX(centre - (UnderscoreWidthDip / 2) + State.StripScroll + drift);

        return State.StripAnimating ? bar.WithAnimation(duration: SelectionDurationMs) : bar;
    }

    /// <summary>The offset rendered in the selected state: the drag candidate while dragging, else the committed cell.</summary>
    private int DisplaySelectedOffset()
    {
        if (State.Fraction > 0 && _prev(_selected) is not null)
        {
            return -1;
        }

        if (State.Fraction < 0 && _next(_selected) is not null)
        {
            return 1;
        }

        return 0;
    }

    // ---- Body gesture ------------------------------------------------------------------------------------

    private void OnBodyPan(PanUpdatedEventArgs e)
    {
        var width = State.PageWidth > 0 ? State.PageWidth : FallbackWidth;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _axisLock = 0;
                _appliedBodyOffset = 0;
                _lastTrue = 0;
                _lastVelocity = 0;
                _lastTicks = Stopwatch.GetTimestamp();
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

                    _axisLock = Math.Abs(e.TotalX) >= Math.Abs(e.TotalY) ? 1 : 2;
                    if (_axisLock == 1)
                    {
                        SetState(s => s.HorizontalLock = true); // strict lock: stop the inner scroller
                    }
                }

                if (_axisLock != 1)
                {
                    return; // vertical drag — the inner scroller owns it
                }

                // Undo the self-distortion: translating the pan's own view shrinks the reported TotalX, so the
                // true finger offset is the report plus what we have already applied. (See ADR-0002.)
                var trueOffset = e.TotalX + _appliedBodyOffset;

                var nowTicks = Stopwatch.GetTimestamp();
                var dt = (nowTicks - _lastTicks) / (double)Stopwatch.Frequency;
                if (dt > 0)
                {
                    _lastVelocity = (trueOffset - _lastTrue) / dt;
                }

                _lastTrue = trueOffset;
                _lastTicks = nowTicks;

                var fraction = DragFraction(trueOffset / width);
                SetState(s => s.Fraction = fraction);
                _appliedBodyOffset = fraction * width;
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                var wasHorizontal = _axisLock == 1;
                _axisLock = 0;
                if (State.HorizontalLock)
                {
                    SetState(s => s.HorizontalLock = false);
                }

                if (wasHorizontal)
                {
                    var flick = Math.Abs(_lastVelocity) > FlickVelocityDipPerSec;
                    var decision = PagerGesture.Decide(
                        State.Fraction, flick, hasPrev: _prev(_selected) is not null, hasNext: _next(_selected) is not null);
                    Settle(decision);
                }

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
                // The pan sits on the cells, which translate with StripScroll — so undo the self-distortion
                // the same way as the body (true delta = report + what we have already applied).
                var trueDelta = e.TotalX + _appliedStripDelta;
                var limit = StripRadius * StripCellWidthDip;
                var scroll = Math.Clamp(_stripStartScroll + trueDelta, -limit, limit);
                SetState(s => { s.StripScroll = scroll; s.StripAnimating = false; });
                _appliedStripDelta = scroll - _stripStartScroll;
                break;
        }
    }

    // ---- Selection changes -------------------------------------------------------------------------------

    private void OnCellTapped(int offset)
    {
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

    private async void Settle(PagerCommit decision)
    {
        var generation = ++_generation;
        var target = decision switch
        {
            PagerCommit.CommitPrev => 1.0,
            PagerCommit.CommitNext => -1.0,
            _ => 0.0,
        };

        // Phase A: slide the body toward the committed neighbour (or rubber-band back to 0 if not committing).
        SetState(s => { s.BodyAnimating = true; s.Fraction = target; });
        await Task.Delay((int)SelectionDurationMs);
        if (_generation != generation)
        {
            return;
        }

        var committed = decision switch
        {
            PagerCommit.CommitPrev => _prev(_selected),
            PagerCommit.CommitNext => _next(_selected),
            _ => null,
        };

        if (committed is not { } item)
        {
            SetState(s => s.BodyAnimating = false); // rubber-band complete
            return;
        }

        // Phase B: swap selection (the body snaps seamlessly — the neighbour is already centred) and compensate
        // the strip so it does not jump, then ease it back to centre. A 1-cell step here; |offset|>1 via Jump.
        var stripStep = decision == PagerCommit.CommitNext ? 1 : -1;
        SetState(s => { s.BodyAnimating = false; s.Fraction = 0; s.StripScroll += stripStep * StripCellWidthDip; });
        _onSelectedChanged(item);

        await Task.Delay(1);
        if (_generation != generation)
        {
            return;
        }

        SetState(s => { s.StripAnimating = true; s.StripScroll = 0; });
        await Task.Delay((int)SelectionDurationMs);
        if (_generation == generation)
        {
            SetState(s => s.StripAnimating = false);
        }
    }

    private async void Jump(TItem target, int offset)
    {
        var generation = ++_generation;

        // The jump applies to the body (crossfade — no slide through the gap); the strip always scrolls to centre.
        SetState(s => { s.Crossfading = true; s.BodyOpacity = 0; });
        await Task.Delay((int)(SelectionDurationMs / 2));
        if (_generation != generation)
        {
            return;
        }

        SetState(s => { s.Fraction = 0; s.StripScroll += offset * StripCellWidthDip; });
        _onSelectedChanged(target);

        await Task.Delay(1);
        if (_generation != generation)
        {
            return;
        }

        SetState(s => { s.BodyOpacity = 1; s.StripAnimating = true; s.StripScroll = 0; });
        await Task.Delay((int)SelectionDurationMs);
        if (_generation == generation)
        {
            SetState(s => { s.Crossfading = false; s.StripAnimating = false; });
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
        SetState(s => { s.StripAnimating = true; s.StripScroll = 0; });
        await Task.Delay((int)SelectionDurationMs);
        if (_generation == generation)
        {
            SetState(s => s.StripAnimating = false);
        }
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
