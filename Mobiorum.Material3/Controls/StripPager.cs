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
    /// Horizontal drag/settle fraction in [−1, +1]. Positive drags content right toward <c>Prev</c>;
    /// negative toward <c>Next</c>. 0 at rest.
    /// </summary>
    public double Fraction;

    /// <summary>True during phase A of a commit/slide (the page slide); gates the pager's animation.</summary>
    public bool Settling;

    /// <summary>True during phase B (the strip re-centre after a swap) and during a jump; gates the strip/body animation.</summary>
    public bool Recentering;

    /// <summary>Pager body opacity, driven 1→0→1 to crossfade a jump.</summary>
    public double BodyOpacity = 1;
}

/// <summary>
/// A Material 3 "synced strip + pager": a horizontal label <b>strip</b> with a sliding <b>underscore</b>
/// above a swipeable <b>pager</b> body, kept in lockstep. Generic over a value-type item navigated by
/// <c>Next</c>/<c>Prev</c> delegates (no index, no count); see <c>docs/adr/0002-strip-pager.md</c> for the
/// full model. The host owns <see cref="Selected(TItem)"/> as its single source of truth and is notified of
/// commits via <see cref="OnSelectedChanged(Action{TItem})"/>; all transient interaction state lives here.
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
    /// recogniser can arbitrate horizontal-page vs vertical-scroll) — return the content, not a scroller. Set
    /// via <c>.Page(...)</c>.
    /// </summary>
    [Prop] Func<TItem, VisualNode> _page = _ => null!;

    /// <summary>Invoked when a swipe commits or a cell is tapped, with the newly selected item. Set via <c>.OnSelectedChanged(...)</c>.</summary>
    [Prop] Action<TItem> _onSelectedChanged = _ => { };

    private const double SelectionDurationMs = 200;
    private const double AxisSlopDip = 12;
    private const double StripHeightDip = 44;
    private const double StripCellWidthDip = 84;
    private const double UnderscoreWidthDip = 32;
    private const double UnderscoreHeightDip = 3;

    // Per-gesture bookkeeping that does not affect layout, so it is kept off State (no re-render on change).
    // 0 = axis undecided, 1 = locked horizontal (we page), -1 = locked vertical (the inner scroller owns it).
    private int _axisLock;

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

        // The page body must NOT translate while the finger is down: the pan recogniser lives on the page
        // content, and moving that view feeds back into MAUI's TotalX (halving the reported drag). So the
        // body stays put during a raw drag — the stationary strip's underscore tracks the finger — and only
        // slides during the commit settle. See docs/adr/0002-strip-pager.md.
        var offset = State.Settling ? State.Fraction * width : 0;

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

        // Crossfade only on a jump (Recentering); a drag/settle must not fade the body.
        return State.Recentering ? body.WithAnimation(duration: SelectionDurationMs) : body;
    }

    private VisualNode PageHost(TItem item, int slot, double width, double offset)
    {
        // The control owns the vertical scroller and puts the pan recogniser on its *content*. On Android a
        // vertical ScrollView intercepts vertical moves but lets horizontal ones reach its children — so a
        // pan inside the scroller arbitrates correctly (vertical scrolls, horizontal pages). A pan placed
        // outside the scroller never sees the horizontal drag (the scroller swallows the touch stream).
        var host = ScrollView(
                Grid(_page(item)).OnPanUpdated(OnPan)
            )
            .WidthRequest(width)
            .HStart()
            .TranslationX(slot * width + offset);

        // Animate only the settle slide; during a drag the translate must track the finger 1:1 (no tween).
        return State.Settling ? host.WithAnimation(duration: SelectionDurationMs) : host;
    }

    // ---- Strip + underscore ------------------------------------------------------------------------------

    private VisualNode RenderStrip(MaterialScheme scheme, double width)
    {
        var radius = Math.Max(1, (int)Math.Ceiling((width / 2) / StripCellWidthDip) + 1);
        var cells = PagerWindow.Strip(_selected, _next, _prev, radius);
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
        var cellNode = Grid(
            Label(_label(cell.Item))
                .FontSize(13)
                .FontAttributes(displaySelected ? FontAttributes.Bold : FontAttributes.None)
                .TextColor(displaySelected ? scheme.OnSurface : scheme.OnSurfaceVariant)
                .HCenter()
                .VCenter()
        )
        .WidthRequest(StripCellWidthDip)
        .HStart()
        .TranslationX(centre - (StripCellWidthDip / 2) + cell.Offset * StripCellWidthDip)
        .OnTapped(() => OnCellTapped(cell.Offset));

        return State.Recentering ? cellNode.WithAnimation(duration: SelectionDurationMs) : cellNode;
    }

    private VisualNode Underscore(MaterialScheme scheme, double centre)
    {
        // Slides from under Selected (centre) toward Current, "in step" with the drag; clamped to one cell.
        var drift = Math.Clamp(-State.Fraction, -1, 1) * StripCellWidthDip;

        var bar = Border()
            .BackgroundColor(scheme.Primary)
            .StrokeThickness(0)
            .StrokeShape(new RoundRectangle().CornerRadius(UnderscoreHeightDip / 2))
            .WidthRequest(UnderscoreWidthDip)
            .HeightRequest(UnderscoreHeightDip)
            .HStart()
            .VEnd()
            .TranslationX(centre - (UnderscoreWidthDip / 2) + drift);

        // Animate the underscore on settle/recentre; track the finger directly during a raw drag.
        return State.Settling || State.Recentering ? bar.WithAnimation(duration: SelectionDurationMs) : bar;
    }

    /// <summary>
    /// Which offset renders in the selected visual state: the drag candidate (<c>Current</c>) when a drag is
    /// underway and that neighbour exists, otherwise the committed <c>Selected</c> (offset 0).
    /// </summary>
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

    // ---- Gesture -----------------------------------------------------------------------------------------

    private void OnPan(PanUpdatedEventArgs e)
    {
        var width = State.PageWidth > 0 ? State.PageWidth : FallbackWidth;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _axisLock = 0;
                break;

            case GestureStatus.Running:
                if (State.Settling || State.Recentering)
                {
                    return; // ignore input mid-animation
                }

                if (_axisLock == 0)
                {
                    var moved = Math.Max(Math.Abs(e.TotalX), Math.Abs(e.TotalY));
                    if (moved < AxisSlopDip)
                    {
                        return; // within touch slop — axis not yet decided
                    }

                    // Horizontal-dominant → we page. Vertical-dominant → bow out; the inner scroller owns it.
                    _axisLock = Math.Abs(e.TotalX) >= Math.Abs(e.TotalY) ? 1 : -1;
                }

                if (_axisLock == 1)
                {
                    SetState(s => s.Fraction = DragFraction(e.TotalX / width));
                }

                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (_axisLock == 1)
                {
                    var decision = PagerGesture.Decide(
                        State.Fraction, flick: false, hasPrev: _prev(_selected) is not null, hasNext: _next(_selected) is not null);
                    Settle(decision);
                }

                _axisLock = 0;
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

    // ---- Commit / settle ---------------------------------------------------------------------------------

    private async void Settle(PagerCommit decision)
    {
        var target = decision switch
        {
            PagerCommit.CommitPrev => 1.0,
            PagerCommit.CommitNext => -1.0,
            _ => 0.0,
        };

        // Phase A: slide the page (and ease the underscore) toward the committed neighbour.
        SetState(s => { s.Settling = true; s.Fraction = target; });
        await Task.Delay((int)SelectionDurationMs);

        var committed = decision switch
        {
            PagerCommit.CommitPrev => _prev(_selected),
            PagerCommit.CommitNext => _next(_selected),
            _ => null,
        };

        if (committed is { } item)
        {
            // Phase B: swap selection (pager snaps seamlessly — the neighbour is already centred), then ease
            // the strip to re-centre the new Selected.
            SetState(s => { s.Settling = false; s.Recentering = true; s.Fraction = 0; });
            _onSelectedChanged(item);
            await Task.Delay((int)SelectionDurationMs);
            SetState(s => s.Recentering = false);
        }
        else
        {
            SetState(s => { s.Settling = false; s.Fraction = 0; }); // rubber-band back
        }
    }

    private void OnCellTapped(int offset)
    {
        var route = SelectionRouting.FromOffset(offset);
        switch (route.Kind)
        {
            case SelectionKind.None:
                return;

            case SelectionKind.Slide:
                Settle(route.Direction > 0 ? PagerCommit.CommitNext : PagerCommit.CommitPrev);
                break;

            case SelectionKind.Jump:
                if (Step(offset) is { } target)
                {
                    Jump(target);
                }

                break;
        }
    }

    private async void Jump(TItem target)
    {
        // Distant tap: crossfade the body while the strip eases to re-centre — no slide through the gap.
        SetState(s => { s.Recentering = true; s.BodyOpacity = 0; });
        await Task.Delay((int)(SelectionDurationMs / 2));
        _onSelectedChanged(target);
        SetState(s => s.BodyOpacity = 1);
        await Task.Delay((int)SelectionDurationMs);
        SetState(s => s.Recentering = false);
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
