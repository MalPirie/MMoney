using MauiReactor;

namespace Mobiorum.Material3;

/// <summary>
/// Transient state for a <see cref="TabbedPageView{TItem}"/>. As with every MauiReactor component, anything that
/// must survive a host re-render lives here (the framework migrates <c>State</c> to the rebuilt instance and
/// resets plain fields) — so the materialised page buffer and the carousel position are State, not fields.
/// </summary>
public sealed class TabbedPageViewState<TItem> where TItem : struct
{
    /// <summary>The page buffer bound to the carousel: from the back edge forward to the current horizon. Grows
    /// by <b>append at the end only</b> (never shifts an index), so the forward end is effectively unbounded.</summary>
    public List<TItem> Buffer = new();

    /// <summary>Whether <see cref="Buffer"/> has been materialised (guards the lazy first build).</summary>
    public bool Built;

    /// <summary>The carousel's current page index into <see cref="Buffer"/> (mirrors the selected item).</summary>
    public int Position;

    /// <summary>Whether the next programmatic <see cref="Position"/> change animates (adjacent tab = scroll) or
    /// snaps (a jump of more than one tab). Only affects programmatic moves, not user swipes.</summary>
    public bool ScrollAnimated = true;

    /// <summary>Stable <c>AutomationId</c> for the carousel, keying its scroll-settle callback. Generated once and
    /// held in State so it survives host re-renders (the callback is re-registered against it each render).</summary>
    public string? CarouselId;

    /// <summary>Live continuous body page position while a user drag owns the scroll (null when not dragging). Each
    /// render derives the strip's lockstep fraction from this, relative to the current selection.</summary>
    public double? BodyPos;
}

/// <summary>
/// A Material 3 <b>tabbed page view</b>: a <see cref="TabStrip{TItem}"/> above a horizontally-swipeable page body,
/// kept in <b>selection sync only</b> (no shared drag fraction — that coupling is what sank ADR-0002). The body is
/// a native <c>CarouselView</c> (<c>Loop=false</c>) whose pages are the host's content in a vertical scroller, so
/// the swipe-vs-scroll axis arbitration is the native control's job. Generic over a value-type item navigated by
/// <c>Next</c>/<c>Prev</c>; the host owns <see cref="Selected(TItem)"/>. See <c>docs/adr/0003-tabbed-page-view.md</c>
/// ("TabbedPageView body composition").
///
/// <para>The carousel is fed an <b>append-on-demand buffer</b>: materialised from the back edge forward to a modest
/// horizon, extended by appending the next chunk as the swipe nears the end. Because the sequence is back-bounded
/// and forward-open, growth is append-at-end only — unbounded forward with no re-anchor and no visible jump.</para>
/// </summary>
public sealed partial class TabbedPageView<TItem> : Component<TabbedPageViewState<TItem>>
    where TItem : struct
{
    /// <summary>The selected item — the host's single source of truth. Set via <c>.Selected(...)</c>.</summary>
    [Prop] TItem _selected;

    /// <summary>Step forward; <see langword="null"/> at a finite forward edge. Set via <c>.Next(...)</c>.</summary>
    [Prop] Func<TItem, TItem?> _next = _ => null;

    /// <summary>Step back; <see langword="null"/> at the back edge (e.g. the edit lock). Set via <c>.Prev(...)</c>.</summary>
    [Prop] Func<TItem, TItem?> _prev = _ => null;

    /// <summary>The strip tab's text for an item. Set via <c>.Label(...)</c>.</summary>
    [Prop] Func<TItem, string> _label = x => x.ToString() ?? string.Empty;

    /// <summary>The page body for one item (wrapped here in a vertical scroller). Set via <c>.Page(...)</c>.</summary>
    [Prop] Func<TItem, VisualNode> _page = _ => null!;

    /// <summary>Invoked when a swipe or a tab tap changes the selection. Set via <c>.OnSelectedChanged(...)</c>.</summary>
    [Prop] Action<TItem> _onSelectedChanged = _ => { };

    /// <summary>Optional Home anchor passed through to the strip (null = no Home button). Set via <c>.Home(...)</c>.</summary>
    [Prop] TItem? _home;

    /// <summary>Home button glyph passed through to the strip. Set via <c>.HomeIcon(...)</c>.</summary>
    [Prop] string _homeIcon = MaterialSymbols.Home;

    private const int InitialForward = 24;  // pages materialised ahead of the selection on first build
    private const int AppendChunk = 24;     // pages appended per forward top-up
    private const int AppendThreshold = 6;  // append once the swiped position is within this many of the end
    private const int BackCap = 2400;       // safety bound on the backward walk (a null Prev edge normally stops it first)

    private static int _idSeq; // seeds distinct carousel AutomationIds across instances

    // Lazily materialise the buffer on first render (not OnMounted — State survives a host re-render, a plain
    // field would not, and OnMounted does not re-run for the migrated instance; mirrors TabStrip.EnsureSeeded).
    private void EnsureBuilt()
    {
        if (State.Built)
        {
            return;
        }

        State.CarouselId = $"mobiorum-tabbedpageview-{++_idSeq}"; // stable key for the scroll-settle callback
        BuildBuffer(_selected);
        State.Built = true;
    }

    protected override void OnWillUnmount()
    {
        if (State.CarouselId is { } id)
        {
            CarouselSettleObserver.Unregister(id);
        }

        base.OnWillUnmount();
    }

    // Materialise the buffer around an item: back to the real edge (or the safety cap) and forward to the horizon.
    private void BuildBuffer(TItem around)
    {
        var back = new List<TItem>();
        var cursor = (TItem?)around;
        for (var i = 0; i < BackCap && cursor is { } c && _prev(c) is { } p; i++)
        {
            back.Add(p);
            cursor = p;
        }

        back.Reverse(); // walked back-to-front; the buffer is in display order (earliest first)

        var buffer = new List<TItem>(back) { around };

        cursor = around;
        for (var i = 0; i < InitialForward && cursor is { } c && _next(c) is { } n; i++)
        {
            buffer.Add(n);
            cursor = n;
        }

        State.Buffer = buffer;
        State.Position = buffer.IndexOf(around);
    }

    protected override void OnPropsChanged()
    {
        // Keep the carousel on the selected item: a tab tap or an external change moves the body here. If the
        // selection jumped outside the buffer, rebuild around it; otherwise just realign the position.
        if (State.Built)
        {
            var idx = State.Buffer.IndexOf(_selected);
            if (idx < 0)
            {
                BuildBuffer(_selected);
                SetState(_ => { });
            }
            else if (idx != State.Position)
            {
                // Adjacent tab → animate the page across; a jump of more than one → snap (best-effort; a reliable
                // jump for a FAR tab reached after free-scrolling the strip a long way is a known gap — see the
                // "revisit" note in memory/ADR-0003: the far tabs are unmeasured, which also skews hit-testing).
                var animate = Math.Abs(idx - State.Position) <= 1;
                SetState(s => { s.Position = idx; s.ScrollAnimated = animate; });
            }
        }

        base.OnPropsChanged();
    }

    public override VisualNode Render()
    {
        EnsureBuilt();

        // Re-register every render so the callbacks always target this (possibly rebuilt) instance.
        CarouselSettleObserver.Register(State.CarouselId!, OnBodySettled);
        CarouselSettleObserver.RegisterScroll(State.CarouselId!, OnBodyScrolled);

        // Lockstep fraction for the strip: how far the body has dragged from the selected page toward a neighbour,
        // in page units (clamped ±1 — the body only ever moves one page per gesture). Null when no drag is active,
        // and naturally ~0 the instant the selection commits (BodyPos ≈ the settled index == index of the new
        // selection), so the strip adopts the tracked position without a second glide.
        double? track = State.BodyPos is double pos
            ? Math.Clamp(pos - State.Buffer.IndexOf(_selected), -1, 1)
            : null;

        return Grid("Auto,*", "*",
            new TabStrip<TItem>()
                .Selected(_selected)
                .Next(_next)
                .Prev(_prev)
                .Label(_label)
                .Home(_home)
                .HomeIcon(_homeIcon)
                .Track(track)
                .OnSelectedChanged(OnSelectionChanged)
                .GridRow(0),

            new MauiReactor.CarouselView()
                .AutomationId(State.CarouselId!)         // keys the scroll-settle observer to this instance
                .ItemsSource(State.Buffer, PageTemplate)
                .IsScrollAnimated(State.ScrollAnimated)  // set the animate flag BEFORE Position so a jump doesn't scroll
                .Position(State.Position)
                .Loop(false)
                .IsSwipeEnabled(true)
                .GridRow(1)
        );
    }

    // Each page is the host's content in its own vertical scroller — the carousel owns the horizontal axis.
    private VisualNode PageTemplate(TItem item) =>
        ScrollView(_page(item))
            .Orientation(ScrollOrientation.Vertical);

    // A tab tap reports straight up; OnPropsChanged then moves the carousel to the new selection.
    private void OnSelectionChanged(TItem item) => _onSelectedChanged(item);

    // The body is being dragged by a finger (seam-gated to user scrolls only): record the live position so the next
    // render can feed the strip its lockstep fraction. Small deltas are ignored to avoid pointless re-renders.
    private void OnBodyScrolled(double pos)
    {
        if (!State.Built)
        {
            return;
        }

        if (State.BodyPos is double cur && Math.Abs(cur - pos) < 0.003)
        {
            return;
        }

        SetState(s => s.BodyPos = pos);
    }

    // The body has SETTLED on a page (RecyclerView idle — the point of no return, not the optimistic mid-drag
    // crossover). Adopt it as the selection, appending more pages first if we are nearing the end.
    private void OnBodySettled(int pos)
    {
        if (pos < 0 || pos >= State.Buffer.Count)
        {
            return;
        }

        if (pos >= State.Buffer.Count - AppendThreshold)
        {
            AppendForward();
        }

        var item = State.Buffer[pos];
        if (!item.Equals(_selected))
        {
            // End the lockstep by DIRECT mutation *before* reporting up, so the single host re-render carries the
            // new selection and the ended track together and the strip snaps onto the committed tab atomically. A
            // separate/later SetState would render the strip with the ended track but a stale (still-old) selection,
            // freezing the underscore on the previous tab.
            State.Position = pos;        // keep our record aligned with where the carousel already settled
            State.BodyPos = null;
            _onSelectedChanged(item);    // report up; the host's Selected change lands with track already cleared
        }
        else if (State.BodyPos is not null)
        {
            // Snap-back (no commit): our own render ends the track and re-centres the unchanged tab.
            SetState(s => s.BodyPos = null);
        }
    }

    // Append the next chunk to the END of the buffer. Appending never shifts an existing index, so the current
    // position and the visible page are undisturbed — the forward end just gains room to swipe into.
    private void AppendForward()
    {
        var last = State.Buffer[^1];
        var added = false;
        for (var i = 0; i < AppendChunk && _next(last) is { } n; i++)
        {
            State.Buffer.Add(n);
            last = n;
            added = true;
        }

        if (added)
        {
            SetState(_ => { }); // re-render so the carousel picks up the longer source
        }
    }
}
