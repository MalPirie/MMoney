using System.Diagnostics;
using MauiReactor;
using MauiReactor.Animations;
using MauiReactor.Shapes;
using MauiControls = Microsoft.Maui.Controls;

namespace Mobiorum.Material3;

/// <summary>
/// Transient interaction state for a <see cref="TabStrip{TItem}"/> — everything the host does not own. This
/// <b>must</b> hold all data that has to survive a host re-render: MauiReactor migrates the <c>State</c> object
/// to the new component instance but resets plain instance fields, so the materialised window, width map, and
/// last-selected marker live here (not in fields) or they vanish the moment the host changes selection.
/// </summary>
public sealed class TabStripState<TItem> where TItem : struct
{
    /// <summary>Ordered materialised items (index 0 = leftmost/earliest). Survives host re-renders via State.</summary>
    public List<TItem> Window = new();

    /// <summary>Measured rendered tab widths (device-independent). Survives host re-renders via State.</summary>
    public Dictionary<TItem, double> Widths = new();

    /// <summary>Previous selection, to slide the underscore from and to gate OnPropsChanged re-animation.</summary>
    public TItem LastSelected;

    /// <summary>Whether <see cref="Window"/> has been seeded — guards a lazy first-seed.</summary>
    public bool Seeded;

    /// <summary>Whether the selected tab still needs its initial snap-to-centre (once widths/viewport measure).</summary>
    public bool PendingCentre;

    /// <summary>Per-mount measurement generation (keys tab views so a real remount forces a clean re-measure).</summary>
    public int MeasurementGeneration;

    /// <summary>Measured viewport width (for the scroll clamp). Survives host re-renders via State.</summary>
    public double ViewportWidth;

    /// <summary>Measured full strip width; minus <see cref="ViewportWidth"/> = the Home button's leading column
    /// (a tap position comes in page-relative, so the hit-test subtracts this to reach viewport-local space).</summary>
    public double StripWidth;

    /// <summary>Committed resting scroll (the row's <c>TranslationX</c>; negative = scrolled left).</summary>
    public double Committed;

    /// <summary>Live drag delta during an active gesture; folded into <see cref="Committed"/> on release.</summary>
    public double Live;

    /// <summary>ScaleX of the viewport — the M3 overscroll stretch (1 = none). Springs back to 1 on release.</summary>
    public double Stretch = 1;

    /// <summary>AnchorX for the stretch: 0 pins the start (left) edge, 1 pins the end (right) edge.</summary>
    public double StretchAnchor;

    /// <summary>Underscore left edge in row content space (animated during a selection slide).</summary>
    public double IndicatorX;

    /// <summary>Underscore width — the selected tab's text width (M3 content-width indicator).</summary>
    public double IndicatorW;

    /// <summary>Transient state-layer strength on the selected tab: pulses 0→1→0 on selection (the M3 "set and fade").</summary>
    public double TapFade;

    /// <summary>Drives the fling controller; false stops it cleanly, leaving <see cref="Committed"/> at the last tick.</summary>
    public bool FlingActive;

    /// <summary>Drives the overscroll stretch spring-back controller.</summary>
    public bool StretchSettling;

    /// <summary>A slide is armed/active: the underscore reads <see cref="IndicatorX"/>/<see cref="IndicatorW"/>
    /// (not rest geometry) from arm until completion. Spans the one-frame controller-restart gap so an
    /// interrupting selection never flashes the new tab's resting underscore.</summary>
    public bool Selecting;

    /// <summary>Drives the selection controller's <c>IsEnabled</c>. Kept distinct from <see cref="Selecting"/> so
    /// an in-flight slide can be cleanly restarted: the controller only replays on a false→true edge, so an
    /// interrupting tap cycles this false (this frame) then true (next frame) while <see cref="Selecting"/>
    /// stays true throughout.</summary>
    public bool SelectRunning;

    /// <summary>Underscore slide endpoints (row content space) + the paired scroll ease. In State so they
    /// survive both the restart dispatcher hop and any host re-render mid-slide.</summary>
    public double SelFromIX, SelToIX, SelFromIW, SelToIW, SelFromCM, SelToCM;
}

/// <summary>
/// A Material 3 freely-scrollable <b>tab strip</b>: a horizontal, hand-scrollable row of variable-width label
/// <b>tabs</b> over a navigable sequence, with an M3 selected-tab underscore, fling momentum, an overscroll
/// stretch, and a fixed-left <b>Home</b> button. Generic over a value-type item navigated by <c>Next</c>/
/// <c>Prev</c> (no index/count). The host owns <see cref="Selected(TItem)"/>; the control owns all transient
/// interaction state and reports taps via <see cref="OnSelectedChanged(Action{TItem})"/>.
///
/// <para>All geometry is delegated to the pure <see cref="StripLayout"/> seam; the sequence is materialised as
/// a sliding window (<see cref="StripWindow.StripRange"/>) that tracks the scroll with buffered hysteresis.
/// See <c>docs/adr/0003-tabbed-page-view.md</c> ("Real-control design"). A native touch-down on the strip
/// cancels an in-flight fling via the library-owned <see cref="TouchDownContentView"/> (Android only).</para>
/// </summary>
public sealed partial class TabStrip<TItem> : Component<TabStripState<TItem>>
    where TItem : struct
{
    /// <summary>The selected item — the host's single source of truth. Set via <c>.Selected(...)</c>.</summary>
    [Prop] TItem _selected;

    /// <summary>Step forward; <see langword="null"/> at a finite forward edge. Set via <c>.Next(...)</c>.</summary>
    [Prop] Func<TItem, TItem?> _next = _ => null;

    /// <summary>Step back; <see langword="null"/> at the back edge (e.g. the edit lock). Set via <c>.Prev(...)</c>.</summary>
    [Prop] Func<TItem, TItem?> _prev = _ => null;

    /// <summary>A tab's label text. Set via <c>.Label(...)</c>.</summary>
    [Prop] Func<TItem, string> _label = x => x.ToString() ?? string.Empty;

    /// <summary>Fired immediately when a tab (or the Home button) is tapped. Set via <c>.OnSelectedChanged(...)</c>.</summary>
    [Prop] Action<TItem> _onSelectedChanged = _ => { };

    /// <summary>The home anchor; <see langword="null"/> ⇒ no Home button. Set via <c>.Home(...)</c>.</summary>
    [Prop] TItem? _home;

    /// <summary>The Home button glyph (Material Symbols). Defaults to a house. Set via <c>.HomeIcon(...)</c>.</summary>
    [Prop] string _homeIcon = MaterialSymbols.Home;

    // --- tuning (internal; not on the public surface, per ADR-0003) ----------------------------------------
    private const double SpacingDip = 1;         // gap between tabs (must match the HStack Spacing)
    private const double TapSlop = 5;            // a gesture that moved more than this suppresses the trailing tap
    private const double VelocityWindowMs = 80;  // window for release-velocity measurement (noise rejection)
    private const double FlickVelocity = 300;    // dip/s release speed required to start a fling
    private const double FlingDistance = 1.0;    // fling target distance = velocity × this (dip)
    private const double FlingDurFactor = 0.5;   // fling duration (ms) = |velocity| × this, clamped
    private const double FlingDurMin = 250;
    private const double FlingDurMax = 1100;
    private const double MaxStretch = 0.15;      // max ScaleX delta at full overscroll (M3 stretch, ~1.15)
    private const double StretchScale = 250;     // overscroll dip controlling how fast the stretch ramps in
    private const double StretchSettleMs = 250;  // overscroll stretch spring-back duration
    private const double StateLayerOpacity = 0.12; // M3 primary state-layer alpha at full strength
    private const double UnderscoreHeight = 3;   // M3 active-indicator bar height (dip)
    private const double TabHPad = 16;           // tab horizontal padding; underscore spans the text (tab − 2×pad)
    private const double SlideDurationMs = 250;  // M3 active-indicator slide + centre (medium1, emphasized/decelerate)
    private const double FadeInMs = 200;         // state-layer ramp-in
    private const double FadeOutMs = 400;        // state-layer fade-out
    private const double SelectTotalMs = 600;    // selection animation length = max(slide, fade-in+out)
    private const double StripHeight = 64;
    private const int WindowRadius = 8;          // tabs materialised each side of the scroll cursor
    private const int GrowChunk = 4;             // tabs added/evicted per slide top-up
    private const double SlideMarginDip = 48;    // re-window this far before the buffer edge shows through

    // Monotonic source for the per-mount measurement generation (stored in State so it survives host re-renders;
    // a real remount runs OnMounted again and takes a fresh token). Not a shared cache — see ADR-0003.
    private static int _globalGeneration;

    // Transient, single-interaction fields only. Anything that must survive a host re-render lives in State
    // (MauiReactor resets instance fields but migrates State to the new instance) — see TabStripState.
    private bool _panMoved;                                 // set when a gesture exceeds TapSlop, so the end tap is ignored
    private readonly List<(long Ticks, double Live)> _velSamples = new();
    private double _flingFrom, _flingTo, _flingDurMs;      // fling DoubleAnimation params, set at release
    private double _lastFlingV;                             // last controller value, so ticks apply as deltas (re-anchor-safe)
    private double _stretchFrom;                            // stretch spring-back start (current ScaleX) at release

    // Seed the window/selection into State on first render (not OnMounted): State survives host re-renders, so a
    // new instance created by a host SetState inherits the seeded window; OnMounted does not re-run for it.
    private void EnsureSeeded()
    {
        if (State.Seeded)
        {
            return;
        }

        State.MeasurementGeneration = ++_globalGeneration; // fresh token per real mount → clean re-measure
        SeedWindow(_selected);
        State.LastSelected = _selected;
        State.Seeded = true;
        State.PendingCentre = true; // centre the selected tab once its widths + the viewport have measured
    }

    protected override void OnPropsChanged()
    {
        // Host changed the selection: animate the underscore/centre toward it (re-seeding + snapping if it was
        // scrolled out of the window — the Home-evicted case). Guarded by Seeded so the initial mount doesn't
        // animate before the first-render seed.
        if (State.Seeded && !_selected.Equals(State.LastSelected))
        {
            AnimateToSelected();
        }

        base.OnPropsChanged();
    }

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        EnsureSeeded();

        // With a Home anchor, the button is a persistent pinned LEADING column and the tabs scroll in the
        // remaining width (never under the button); without one, the viewport takes the whole strip.
        if (_home is { } home)
        {
            return Grid("*", "Auto,*",
                    HomeButton(scheme, home).GridColumn(0),
                    Viewport(scheme).GridColumn(1)
                )
                .BackgroundColor(scheme.SurfaceVariant)
                .HeightRequest(StripHeight)
                .OnSizeChanged((Size s) => State.StripWidth = s.Width);
        }

        return Grid(Viewport(scheme)).HeightRequest(StripHeight);
    }

    // ---- viewport + row ----------------------------------------------------------------------------------

    private VisualNode Viewport(MaterialScheme scheme)
    {
        var layout = Layout();

        // The clipped, stretched, pannable strip. Wrapped in a TouchDownView so a native touch-down anywhere on
        // it cancels an in-flight fling immediately (before the pan gesture recognises movement) — the child
        // keeps all its own gestures; the wrapper only observes. See TouchDownContentView.
        var strip = Grid(
            Row(scheme, layout),
            new MauiReactor.TapGestureRecognizer().OnTapped(OnGridTapped),
            FlingController(),
            StretchController(),
            SelectController()
        )
        .BackgroundColor(scheme.SurfaceVariant)
        .IsClippedToBounds(true)
        .ScaleX(State.Stretch)
        .AnchorX(State.StretchAnchor)
        .OnSizeChanged((Size s) => State.ViewportWidth = s.Width)
        .OnPanUpdated(OnPan);

        // Clip at the column level: the overscroll ScaleX renders the viewport WIDER than its layout slot, and
        // the viewport's own IsClippedToBounds only clips its children (the row) — not its own scaled overflow.
        // Without this outer clip the stretch spills over the pinned Home column and paints over the button.
        return Grid(new TouchDownView { strip }.OnTouchDown(OnGlobalTouchDown))
            .IsClippedToBounds(true);
    }

    private VisualNode Row(MaterialScheme scheme, StripLayout layout)
    {
        var tabs = new List<VisualNode>(State.Window.Count);
        for (var i = 0; i < State.Window.Count; i++)
        {
            tabs.Add(Tab(scheme, State.Window[i]));
        }

        // ONE transform moves the whole row (no per-frame layout pass). The underscore rides with it.
        return Grid(
                HStack(tabs.ToArray()).Spacing(SpacingDip).HStart().VFill(),
                Underscore(scheme, layout)
            )
            .HStart()
            .TranslationX(layout.Scroll);
    }

    private VisualNode Tab(MaterialScheme scheme, TItem item)
    {
        var selected = item.Equals(_selected);
        var stateLayer = selected
            ? scheme.Primary.WithAlpha((float)(StateLayerOpacity * State.TapFade))
            : Colors.Transparent;

        return Border(
                Label(_label(item))
                    .FontSize(14)
                    .FontAttributes(selected ? MauiControls.FontAttributes.Bold : MauiControls.FontAttributes.None)
                    .TextColor(selected ? scheme.Primary : scheme.OnSurfaceVariant)
                    .VCenter()
            )
            .Padding(TabHPad, 14)
            .VFill()
            .BackgroundColor(stateLayer)
            .StrokeThickness(0)
            .OnSizeChanged((Size s) =>
            {
                var known = State.Widths.TryGetValue(item, out var prev);
                if (!known || Math.Abs(prev - s.Width) > 0.5)
                {
                    State.Widths[item] = s.Width;
                    if (State.PendingCentre)
                    {
                        TryInitialCentre(); // snap the selected tab to centre once its geometry has measured
                    }
                    else if (item.Equals(_selected))
                    {
                        SetState(_ => { }); // first measure of the selected tab: re-render so the underscore appears
                    }
                }
            })
            .WithKey((item, State.MeasurementGeneration)); // (item, generation): reuse across scroll, rebuild across remount/invalidation
    }

    private VisualNode Underscore(MaterialScheme scheme, StripLayout layout)
    {
        var index = State.Window.IndexOf(_selected);
        if (index < 0)
        {
            return Grid().WidthRequest(0).HeightRequest(0); // selected tab not materialised — nothing to underline
        }

        var (ix, iw) = State.Selecting ? (State.IndicatorX, State.IndicatorW) : IndicatorGeometry(layout, index);
        return Border()
            .BackgroundColor(scheme.Primary)
            .StrokeThickness(0)
            .StrokeShape(new RoundRectangle().CornerRadius(UnderscoreHeight / 2))
            .HeightRequest(UnderscoreHeight)
            .WidthRequest(iw)
            .HStart()
            .VEnd()
            .TranslationX(ix);
    }

    // A persistent, fixed-left, non-directional Home affordance in its own leading column. Tapping is a proxy
    // tap on the home tab (select + centre). Always shown when a Home anchor is set (never overlaps the tabs).
    private VisualNode HomeButton(MaterialScheme scheme, TItem home) =>
        Border(
                Label(_homeIcon)
                    .FontFamily(MaterialSymbols.FontFamily)
                    .FontSize(22)
                    .TextColor(scheme.OnSurface)
                    .HCenter()
                    .VCenter()
            )
            .Padding(10)
            .BackgroundColor(scheme.SurfaceContainer)
            .StrokeThickness(0)
            .StrokeShape(new RoundRectangle().CornerRadius(StripHeight / 2))
            .Shadow(Elevation.Level2)
            .VCenter()
            .Margin(6, 0, 6, 0)
            .OnTapped(() => SelectHome(home));

    // ---- animation controllers (unchanged mechanics from the device-validated spike) ---------------------

    private VisualNode FlingController() =>
        new AnimationController
        {
            new SequenceAnimation
            {
                new DoubleAnimation()
                    .StartValue(_flingFrom)
                    .TargetValue(_flingTo)
                    .Duration(TimeSpan.FromMilliseconds(_flingDurMs))
                    .Easing(Easing.CubicOut)
                    .OnTick(OnFlingTick)
            }
        }
        .IsEnabled(State.FlingActive);

    private VisualNode StretchController() =>
        new AnimationController
        {
            new SequenceAnimation
            {
                new DoubleAnimation()
                    .StartValue(_stretchFrom)
                    .TargetValue(1)
                    .Duration(TimeSpan.FromMilliseconds(StretchSettleMs))
                    .Easing(Easing.CubicOut)
                    .OnTick(OnStretchTick)
            }
        }
        .IsEnabled(State.StretchSettling);

    private VisualNode SelectController() =>
        new AnimationController
        {
            new SequenceAnimation
            {
                new DoubleAnimation()
                    .StartValue(0)
                    .TargetValue(1)
                    .Duration(TimeSpan.FromMilliseconds(SelectTotalMs))
                    .Easing(Easing.Linear) // linear progress; per-channel easing in OnSelectTick
                    .OnTick(OnSelectTick)
            }
        }
        .IsEnabled(State.SelectRunning);

    // ---- pure-seam access --------------------------------------------------------------------------------

    // The StripLayout view over the current window, with the scroll clamped to its edge-aware bounds.
    private StripLayout Layout()
    {
        var widths = new double[State.Window.Count];
        for (var i = 0; i < State.Window.Count; i++)
        {
            widths[i] = State.Widths.TryGetValue(State.Window[i], out var w) ? w : 0;
        }

        var hasBack = State.Window.Count > 0 && _prev(State.Window[0]) is null;
        var hasFwd = State.Window.Count > 0 && _next(State.Window[^1]) is null;
        var raw = State.Committed + State.Live;
        var probe = new StripLayout(widths, SpacingDip, State.ViewportWidth, raw, hasBack, hasFwd);
        return probe with { Scroll = probe.Clamp(raw) };
    }

    // The underscore spans the tab's TEXT (M3): its content-left plus padding, width minus 2×padding.
    private (double X, double W) IndicatorGeometry(StripLayout layout, int index)
    {
        var x = layout.ContentLeft(index);
        var w = index < State.Window.Count && State.Widths.TryGetValue(State.Window[index], out var tw) ? tw : 0;
        return (x + TabHPad, Math.Max(0, w - 2 * TabHPad));
    }

    private static double StretchDelta(double excess) => MaxStretch * excess / (excess + StretchScale);

    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

    // ---- windowing (sliding, buffered hysteresis) --------------------------------------------------------

    private void SeedWindow(TItem centre)
    {
        State.Window.Clear();
        foreach (var cell in StripWindow.StripRange(centre, _next, _prev, -WindowRadius, WindowRadius))
        {
            State.Window.Add(cell.Item);
        }
    }

    // Snap the selected tab to centre on first load, once the viewport and every tab up to (and including) the
    // selected one have measured (CentreOffset needs those widths). No animation — it is the resting position.
    private void TryInitialCentre()
    {
        if (State.ViewportWidth <= 0)
        {
            return;
        }

        var idx = State.Window.IndexOf(_selected);
        if (idx < 0)
        {
            return;
        }

        for (var i = 0; i <= idx; i++)
        {
            if (!State.Widths.TryGetValue(State.Window[i], out var w) || w <= 0)
            {
                return; // a width the centre depends on has not measured yet — wait for the next OnSizeChanged
            }
        }

        var centre = Layout().CentreOffset(idx);
        State.PendingCentre = false;
        SetState(s => s.Committed = centre);
    }

    // After a settle, if the scroll is within the slide margin of an OPEN buffer edge, top up that side and
    // evict the far side, rebasing the committed scroll so nothing visually jumps (the atomic re-anchor).
    private void MaybeSlide()
    {
        var need = Layout().Need(SlideMarginDip);

        if (need == SlideNeed.GrowEnd)
        {
            var last = State.Window[^1];
            for (var i = 0; i < GrowChunk && _next(last) is { } n; i++)
            {
                State.Window.Add(n);
                last = n;
            }

            EvictFront();
        }
        else if (need == SlideNeed.GrowFront)
        {
            var first = State.Window[0];
            var added = 0.0;
            for (var i = 0; i < GrowChunk && _prev(first) is { } p; i++)
            {
                State.Window.Insert(0, p);
                added += State.Widths.TryGetValue(p, out var w) ? w : 0; // usually 0 (unmeasured) — settles off-screen
                first = p;
            }

            if (added > 0)
            {
                SetState(s => s.Committed = StripLayout.Rebase(s.Committed, 0, added));
            }

            EvictEnd();
        }

        if (need != SlideNeed.None)
        {
            SetState(_ => { }); // ensure the freshly-materialised tabs render even when no rebase SetState fired
        }
    }

    private void EvictFront()
    {
        var cap = 2 * WindowRadius + GrowChunk;
        var removed = 0.0;
        while (State.Window.Count > cap)
        {
            removed += State.Widths.TryGetValue(State.Window[0], out var w) ? w + SpacingDip : 0;
            State.Window.RemoveAt(0);
        }

        if (removed > 0)
        {
            SetState(s => s.Committed = StripLayout.Rebase(s.Committed, removed, 0));
        }
    }

    private void EvictEnd()
    {
        var cap = 2 * WindowRadius + GrowChunk;
        while (State.Window.Count > cap)
        {
            State.Window.RemoveAt(State.Window.Count - 1); // end eviction doesn't move the origin — no rebase
        }
    }

    // ---- tap / selection ---------------------------------------------------------------------------------

    private Task OnGridTapped(object? sender, MauiControls.TappedEventArgs e)
    {
        if (State.FlingActive || State.StretchSettling)
        {
            SetState(s => { s.FlingActive = false; s.StretchSettling = false; });
        }

        if (_panMoved)
        {
            return Task.CompletedTask;
        }

        var view = (sender as MauiControls.GestureRecognizer)?.Parent as MauiControls.View;
        var pos = e.GetPosition(view);
        if (pos is null)
        {
            return Task.CompletedTask;
        }

        // GetPosition reports page-relative coords (the recogniser's Parent is null on Android, so `view` is
        // too), but HitTest works in viewport-local space. With a Home anchor the viewport is shifted right by
        // the pinned leading column; subtract that width to bring the tap back into viewport-local space.
        var homeOffset = _home is not null ? Math.Max(0, State.StripWidth - State.ViewportWidth) : 0;
        var hit = Layout().HitTest(pos.Value.X - homeOffset);
        if (hit < 0 || hit >= State.Window.Count)
        {
            return Task.CompletedTask;
        }

        var item = State.Window[hit];
        if (item.Equals(_selected))
        {
            Recentre(item); // tapping the selected tab only re-centres it (no report)
        }
        else
        {
            _onSelectedChanged(item); // fire immediately; the host's prop change drives AnimateToSelected
        }

        return Task.CompletedTask;
    }

    private void SelectHome(TItem home)
    {
        if (State.FlingActive || State.StretchSettling)
        {
            SetState(s => { s.FlingActive = false; s.StretchSettling = false; });
        }

        if (home.Equals(_selected))
        {
            Recentre(home); // already selected but scrolled away — just glide/snap back
        }
        else
        {
            _onSelectedChanged(home); // proxy tap; the prop change drives AnimateToSelected (re-seeds if evicted)
        }
    }

    // Underscore slide + scroll-centre + state-layer pulse toward the (host-set) selected tab. If the newly-
    // selected tab is not in the window (Home-evicted), re-seed around it and snap; otherwise glide.
    private void AnimateToSelected()
    {
        var prevIndex = State.Window.IndexOf(State.LastSelected);
        var reseeded = !State.Window.Contains(_selected);
        if (reseeded)
        {
            SeedWindow(_selected); // evicted → re-materialise around it; the coordinate origin changes → SNAP below
        }

        var layout = Layout();
        var index = State.Window.IndexOf(_selected);
        if (index < 0)
        {
            State.LastSelected = _selected;
            return; // still not materialised (no width yet) — the next render's rest geometry will show it
        }

        var (toX, toW) = IndicatorGeometry(layout, index);
        var centre = layout.CentreOffset(index);
        State.LastSelected = _selected;

        if (reseeded)
        {
            // The window's index-0 origin moved, so State.Committed is in stale coordinates and there is no
            // continuous scroll path across the (possibly infinite) gap we jumped — snap into the new frame.
            SetState(s =>
            {
                s.Live = 0;
                s.Committed = centre;
                s.IndicatorX = toX;
                s.IndicatorW = toW;
                s.TapFade = 0;
                s.Selecting = false;
                s.SelectRunning = false;
            });
            return;
        }

        // In-window: glide the underscore from the previous tab and ease the scroll to centre.
        var (fromX, fromW) = prevIndex >= 0 ? IndicatorGeometry(layout, prevIndex) : (toX, toW);

        // Arm the slide (Selecting stays true across the restart gap so the underscore never flashes the new
        // tab's rest position), then force a clean false→true edge on the controller: without cycling it, an
        // interrupting tap loads new endpoints but the still-running controller's clock is already past the
        // slide window, so it snaps to the target instead of gliding.
        SetState(s =>
        {
            s.Live = 0;
            s.SelFromIX = fromX; s.SelToIX = toX;
            s.SelFromIW = fromW; s.SelToIW = toW;
            s.SelFromCM = s.Committed;
            s.SelToCM = centre;
            s.IndicatorX = fromX;
            s.IndicatorW = fromW;
            s.TapFade = 0;
            s.Selecting = true;
            s.SelectRunning = false; // drop the edge this frame …
        });

        // … then re-raise it next frame so the controller replays from t=0. State-held endpoints make the hop
        // safe against a mid-gap re-instantiation; no host re-render is expected in the gap regardless.
        MauiControls.Application.Current?.Dispatcher.Dispatch(() => SetState(s => s.SelectRunning = true));
    }

    private void OnSelectTick(double t)
    {
        if (!State.Selecting)
        {
            return;
        }

        var slide = EaseOutCubic(Math.Min(1, t * SelectTotalMs / SlideDurationMs));
        var ix = State.SelFromIX + (State.SelToIX - State.SelFromIX) * slide;
        var iw = State.SelFromIW + (State.SelToIW - State.SelFromIW) * slide;
        var cm = State.SelFromCM + (State.SelToCM - State.SelFromCM) * slide;
        var ms = t * SelectTotalMs;
        var fade = ms <= FadeInMs ? ms / FadeInMs : Math.Max(0, 1 - (ms - FadeInMs) / FadeOutMs);

        SetState(s => { s.IndicatorX = ix; s.IndicatorW = iw; s.Committed = cm; s.TapFade = fade; });

        if (t >= 0.999)
        {
            SetState(s => { s.IndicatorX = s.SelToIX; s.IndicatorW = s.SelToIW; s.Committed = s.SelToCM; s.TapFade = 0; s.Selecting = false; s.SelectRunning = false; });
            MaybeSlide();
        }
    }

    // Glide (or snap, if evicted) the selected tab back to centre with no selection change.
    private void Recentre(TItem item)
    {
        var reseeded = !State.Window.Contains(item);
        if (reseeded)
        {
            SeedWindow(item);
        }

        var layout = Layout();
        var index = State.Window.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        var (ix, iw) = IndicatorGeometry(layout, index);
        var centre = layout.CentreOffset(index);
        State.LastSelected = _selected;

        if (reseeded)
        {
            // Reseed moved the coordinate origin — snap into the new frame (see AnimateToSelected).
            SetState(s => { s.Live = 0; s.Committed = centre; s.IndicatorX = ix; s.IndicatorW = iw; s.TapFade = 0; s.Selecting = false; s.SelectRunning = false; });
            return;
        }

        SetState(s =>
        {
            s.Live = 0;
            s.SelFromIX = ix; s.SelToIX = ix;   // no underscore slide — same tab
            s.SelFromIW = iw; s.SelToIW = iw;
            s.SelFromCM = s.Committed;
            s.SelToCM = centre;
            s.IndicatorX = ix;
            s.IndicatorW = iw;
            s.TapFade = 0;
            s.Selecting = true;
            s.SelectRunning = false;
        });
        MauiControls.Application.Current?.Dispatcher.Dispatch(() => SetState(s => s.SelectRunning = true));
    }

    // ---- pan / fling / stretch (ported from the device-validated spike, bounds now from StripLayout) ------

    private void OnGlobalTouchDown()
    {
        if (State.FlingActive)
        {
            SetState(s => s.FlingActive = false);
        }
    }

    private double ReleaseVelocity()
    {
        if (_velSamples.Count < 2)
        {
            return 0;
        }

        var first = _velSamples[0];
        var last = _velSamples[^1];
        var dt = (last.Ticks - first.Ticks) / (double)Stopwatch.Frequency;
        return dt > 0.001 ? (last.Live - first.Live) / dt : 0;
    }

    private void StartFling(double velocity)
    {
        var (min, max) = Layout().Bounds();
        _flingFrom = State.Committed;
        _flingTo = Math.Clamp(State.Committed + velocity * FlingDistance, min, max);
        _flingDurMs = Math.Clamp(Math.Abs(velocity) * FlingDurFactor, FlingDurMin, FlingDurMax);
        _lastFlingV = _flingFrom; // ticks are applied as deltas from here
        SetState(s => s.FlingActive = true);
    }

    // Apply the controller's motion as a per-tick DELTA (not an absolute set) so a mid-fling re-anchor
    // (MaybeSlide rebasing Committed) composes instead of being overwritten; _flingTo/_flingFrom stay in the
    // controller's original frame and are only used for progress/termination, so they need no rebasing.
    private void OnFlingTick(double v)
    {
        if (!State.FlingActive)
        {
            return;
        }

        var delta = v - _lastFlingV;
        _lastFlingV = v;

        var (min, max) = Layout().Bounds();
        var target = State.Committed + delta;
        var clamped = Math.Clamp(target, min, max);
        if (Math.Abs(clamped - State.Committed) > 0.01)
        {
            SetState(s => s.Committed = clamped);
        }

        MaybeSlide(); // materialise/slide the window as the glide crosses buffer edges

        if (Math.Abs(v - _flingTo) < 0.5 || Math.Abs(clamped - target) > 0.001) // reached target, or hit a real edge
        {
            SetState(s => s.FlingActive = false);
        }
    }

    private void StartStretchSettle()
    {
        _stretchFrom = State.Stretch;
        SetState(s => s.StretchSettling = true);
    }

    private void OnStretchTick(double v)
    {
        if (!State.StretchSettling)
        {
            return;
        }

        SetState(s => s.Stretch = v);
        if (Math.Abs(v - 1) < 0.001)
        {
            SetState(s => s.StretchSettling = false);
        }
    }

    private void OnPan(MauiControls.PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                if (State.FlingActive || State.StretchSettling || State.Selecting)
                {
                    SetState(s => { s.FlingActive = false; s.StretchSettling = false; s.Selecting = false; s.SelectRunning = false; });
                }

                _velSamples.Clear();
                _panMoved = false;
                break;

            case GestureStatus.Running:
            {
                if (Math.Abs(e.TotalX) > TapSlop)
                {
                    _panMoved = true;
                }

                var live = e.TotalX;
                var nowTicks = Stopwatch.GetTimestamp();
                _velSamples.Add((nowTicks, live));
                var windowStart = nowTicks - (long)(VelocityWindowMs / 1000.0 * Stopwatch.Frequency);
                while (_velSamples.Count > 2 && _velSamples[0].Ticks < windowStart)
                {
                    _velSamples.RemoveAt(0);
                }

                var layout = Layout();
                var (min, max) = layout.Bounds();
                var desired = State.Committed + live;
                double stretch = 1, anchor = State.StretchAnchor;

                // Only rubber-band when there is something to scroll. If the whole strip fits the viewport the
                // scroll bounds collapse to [0,0], so every drag would read as overscroll — but with nothing to
                // scroll there is nothing to stretch against, so the strip stays put.
                if (layout.Total > layout.Viewport)
                {
                    if (desired > max)
                    {
                        stretch = 1 + StretchDelta(desired - max);
                        anchor = 0;
                    }
                    else if (desired < min)
                    {
                        stretch = 1 + StretchDelta(min - desired);
                        anchor = 1;
                    }
                }

                SetState(s => { s.Live = live; s.Stretch = stretch; s.StretchAnchor = anchor; });
                MaybeSlide(); // grow/slide the window mid-drag so panning past the buffer edge stays materialised
                break;
            }

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
            {
                var velocity = ReleaseVelocity();
                var layout = Layout();
                var (min, max) = layout.Bounds();
                var desired = State.Committed + State.Live;
                var canScroll = layout.Total > layout.Viewport; // nothing to fling or spring back if it all fits
                var overscrolled = canScroll && (desired < min || desired > max);
                _panMoved = false;

                SetState(s =>
                {
                    s.Committed = Math.Clamp(desired, min, max);
                    s.Live = 0;
                });

                if (overscrolled)
                {
                    StartStretchSettle();
                }
                else if (canScroll && e.StatusType == GestureStatus.Completed && Math.Abs(velocity) > FlickVelocity)
                {
                    StartFling(velocity);
                }
                else
                {
                    MaybeSlide();
                }

                break;
            }
        }
    }
}
