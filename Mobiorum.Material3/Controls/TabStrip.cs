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

    /// <summary>Whether the previous render was tracking a live body drag (<c>Track</c> non-null). Lets a selection
    /// change that arrives mid/just-after a drag SNAP to the already-tracked tab instead of gliding, and lets a
    /// released-without-committing drag re-centre its resting scroll rather than jumping back to a stale offset.</summary>
    public bool WasTracking;
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

    /// <summary>Live body-drag lockstep fraction: signed page units from the selected tab toward a neighbour
    /// (±1 = a full page over), or <see langword="null"/> when no drag is tracking. While non-null the underscore
    /// and scroll are driven by this instead of rest/selection geometry. Set by <see cref="TabbedPageView{TItem}"/>
    /// via <c>.Track(...)</c>; not part of the standalone tap surface.</summary>
    [Prop] double? _track;

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
        // A live body drag now owns the strip's motion: kill any in-flight fling/stretch/selection animation so the
        // lockstep override isn't fought.
        if (_track is not null && (State.FlingActive || State.StretchSettling || State.Selecting))
        {
            SetState(s => { s.FlingActive = false; s.StretchSettling = false; s.Selecting = false; s.SelectRunning = false; });
        }

        // A body fling is tracking: keep the window materialised ahead of the tracked position so the underscore can
        // follow across page after page rather than sticking at the window edge and jumping when the commit lands.
        if (_track is double trackFraction && State.Seeded)
        {
            EnsureTrackWindow(trackFraction);
        }

        // The two prop-driven resting transitions: a selection change (glide / snap / reseed, per the resolver's
        // read of was-tracking + measurement), or a drag that ended without committing (re-pin the scroll). Both
        // resolve to a pure decision and apply it. Guarded by Seeded so the initial mount doesn't act before the seed.
        if (State.Seeded && !_selected.Equals(State.LastSelected))
        {
            ResolveAndApply(StripStimulus.SelectionChanged);
        }
        else if (State.WasTracking && _track is null)
        {
            ResolveAndApply(StripStimulus.TrackEnded);
        }

        State.WasTracking = _track is not null;
        base.OnPropsChanged();
    }

    // Resolve the resting transition for a stimulus over the current layout, then execute it. The resolver owns every
    // glide-vs-snap-vs-reseed-vs-defer decision (pure, table-tested); this component only applies the numbers.
    private void ResolveAndApply(StripStimulus stimulus) =>
        Apply(stimulus, StripTransition.Resolve(stimulus, BuildInput(), Layout(), TabHPad));

    // The resolver's resolved inputs, read off State (the same Window.IndexOf / tracking lookups the control already
    // does). LastSelected still holds the PREVIOUS selection here (advanced only after Apply), so PrevIndex is the
    // tab a glide slides from and TargetIndex the tab to rest on.
    private StripTransitionInput BuildInput() => new(
        State.Window.IndexOf(State.LastSelected),
        State.Window.IndexOf(_selected),
        State.WasTracking);

    // The sole side-effecting executor: a mechanical match on the decision. No further decisions live here — the
    // resolver made them all. SeedWindow / MaybeSlide / TryInitialCentre and the SelectController remain the genuine
    // window/animation executors the thin cases hand off to.
    private void Apply(StripStimulus stimulus, StripTransition decision)
    {
        switch (decision)
        {
            case StripTransition.Glide g:
                // Arm the slide (Selecting spans the restart gap so the underscore never flashes the rest position),
                // then force a clean false→true edge on the controller so it replays from t=0 (an interrupting tap
                // otherwise loads new endpoints against a clock already past the slide window and snaps).
                SetState(s =>
                {
                    s.Live = 0;
                    s.SelFromIX = g.FromIx; s.SelToIX = g.ToIx;
                    s.SelFromIW = g.FromIw; s.SelToIW = g.ToIw;
                    s.SelFromCM = g.FromCm; s.SelToCM = g.ToCm;
                    s.IndicatorX = g.FromIx;
                    s.IndicatorW = g.FromIw;
                    s.TapFade = 0;
                    s.Selecting = true;
                    s.SelectRunning = false;
                });
                MauiControls.Application.Current?.Dispatcher.Dispatch(() => SetState(s => s.SelectRunning = true));
                break;

            case StripTransition.Snap snap:
                SetState(s =>
                {
                    s.Live = 0;
                    s.Committed = snap.Cm;
                    if (snap.Ix is double ix)
                    {
                        s.IndicatorX = ix;
                        s.IndicatorW = snap.Iw ?? 0;
                    }

                    s.TapFade = 0;
                    s.Selecting = false;
                    s.SelectRunning = false;
                });
                if (snap.ThenSlide)
                {
                    MaybeSlide(); // grow/evict the window toward the edge as the selection advances (rebased → no jump)
                }

                break;

            case StripTransition.Reseed:
                SeedWindow(_selected);        // re-centre a fresh, small window on the selection …
                State.PendingCentre = true;   // … and let TryInitialCentre snap it once those few tabs measure
                SetState(s => { s.Live = 0; s.TapFade = 0; s.Selecting = false; s.SelectRunning = false; });
                TryInitialCentre();           // … OR centre right now if the reseeded tabs are ALREADY measured
                                              // (reseeding into a recently-visited region fires no OnSizeChanged, so
                                              // waiting on one would leave the selection stuck off-screen at a stale scroll).
                break;

            case StripTransition.Defer:
                State.PendingCentre = true;    // in-window but unmeasured: centre-on-measure without moving the window
                SetState(s => { s.Live = 0; s.TapFade = 0; s.Selecting = false; s.SelectRunning = false; });
                TryInitialCentre();            // self-gates: no-op while unmeasured, centres the moment it can
                break;

            case StripTransition.None:
                break;
        }

        // Baseline bookkeeping: a selection-carrying stimulus advances LastSelected regardless of the decision (a
        // TrackEnded snap-back does not — the selection did not change). Stimulus-driven, matching the old methods.
        if (stimulus is StripStimulus.SelectionChanged or StripStimulus.RecentreTap)
        {
            State.LastSelected = _selected;
        }
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
                .BackgroundColor(scheme.Surface) // M3 tabs sit on a Surface container
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
        .BackgroundColor(scheme.Surface) // M3 tabs sit on a Surface container
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
        // The tab shown ACTIVE (bold / primary text) follows the underscore: while a body drag is tracking it is the
        // tab the continuous position currently sits on (round(index + fraction)), not the frozen committed selection.
        // So the bold highlight walks across tabs in step with the underscore during a multi-page fling instead of
        // staying on the start tab until the commit lands. Not tracking → it is simply the selected tab.
        var selIndex = State.Window.IndexOf(_selected);
        var activeIndex = _track is double f && selIndex >= 0
            ? Math.Clamp((int)Math.Round(selIndex + f, MidpointRounding.AwayFromZero), 0, State.Window.Count - 1)
            : selIndex;

        var tabs = new List<VisualNode>(State.Window.Count);
        for (var i = 0; i < State.Window.Count; i++)
        {
            tabs.Add(Tab(scheme, State.Window[i], i == activeIndex));
        }

        // While a body drag is tracking, the scroll follows the lockstep lerp; otherwise it is the clamped rest/
        // fling/selection scroll. ONE transform moves the whole row (no per-frame layout pass); the underscore
        // rides with it and takes its own lockstep geometry from the same override.
        var track = TrackView(layout);
        return Grid(
                HStack(tabs.ToArray()).Spacing(SpacingDip).HStart().VFill(),
                Underscore(scheme, layout, track)
            )
            .HStart()
            .TranslationX(track?.Scroll ?? layout.Scroll);
    }

    // <paramref name="selected"/> is the VISUAL active state (bold/primary), which during a body track follows the
    // underscore rather than the committed selection — see Row. The measurement re-render below still keys off the
    // real _selected (a geometry concern, not styling).
    private VisualNode Tab(MaterialScheme scheme, TItem item, bool selected)
    {
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
                    // The width this tab CONTRIBUTED to the last layout: its old measured width, or — for a first
                    // measure — the estimate Layout() used for it (NOT 0). The rebase below must cancel that, so it
                    // reads the estimate before State.Widths is mutated.
                    var baseline = prev > 0 ? prev : EstimatedTabWidth();
                    State.Widths[item] = s.Width;
                    if (State.PendingCentre)
                    {
                        TryInitialCentre(); // snap the selected tab to centre once the viewport has measured
                    }
                    else if (item.Equals(_selected))
                    {
                        SetState(_ => { }); // first measure of the selected tab: re-render so the underscore appears
                    }
                    else
                    {
                        // A tab BEFORE the selection changed width (a slid-in front tab measuring up from the estimate,
                        // or a re-measure). That shifts the selected tab's ContentLeft by (width − baseline), so rebase
                        // Committed by that delta to keep the selected tab — and its underscore, which rides ContentLeft
                        // — visually put. Because the baseline is the estimate (not 0), the delta is now small and no
                        // longer shoves earlier tabs off-screen before they can measure.
                        var selIdx = State.Window.IndexOf(_selected);
                        var itemIdx = State.Window.IndexOf(item);
                        if (selIdx >= 0 && itemIdx >= 0 && itemIdx < selIdx)
                        {
                            var delta = s.Width - baseline;
                            SetState(sx => sx.Committed -= delta);
                        }
                    }
                }
            })
            .WithKey((item, State.MeasurementGeneration)); // (item, generation): reuse across scroll, rebuild across remount/invalidation
    }

    private VisualNode Underscore(MaterialScheme scheme, StripLayout layout, (double Scroll, double Ix, double Iw)? track)
    {
        var index = State.Window.IndexOf(_selected);
        if (index < 0)
        {
            return Grid().WidthRequest(0).HeightRequest(0); // selected tab not materialised — nothing to underline
        }

        // A live drag drives the underscore off its lockstep lerp; otherwise it slides (Selecting) or sits at rest.
        var (ix, iw) = track is { } tr ? (tr.Ix, tr.Iw)
            : State.Selecting ? (State.IndicatorX, State.IndicatorW)
            : layout.IndicatorGeometry(index, TabHPad);
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

    // The StripLayout view over the current window, with the scroll clamped to its edge-aware bounds. Unmeasured
    // tabs are filled with an ESTIMATE, not 0: a tab scrolled off the left edge may never fire OnSizeChanged, and a
    // zero there would short ContentLeft and strand the underscore a whole tab off the selection. The estimate keeps
    // the geometry approximately right and is replaced by the exact width the instant the tab comes on-screen.
    private StripLayout Layout()
    {
        var est = EstimatedTabWidth();
        var widths = new double[State.Window.Count];
        for (var i = 0; i < State.Window.Count; i++)
        {
            widths[i] = EffectiveWidth(State.Window[i], est);
        }

        var hasBack = State.Window.Count > 0 && _prev(State.Window[0]) is null;
        var hasFwd = State.Window.Count > 0 && _next(State.Window[^1]) is null;
        var raw = State.Committed + State.Live;
        var probe = new StripLayout(widths, SpacingDip, State.ViewportWidth, raw, hasBack, hasFwd);
        return probe with { Scroll = probe.Clamp(raw) };
    }

    // A tab's width as the geometry sees it: the measured width, or — for a not-yet-measured (off-screen) tab — the
    // estimate. This ONE definition must be used everywhere geometry depends on width (the layout AND the window
    // slide's re-anchor), or ContentLeft and the rebase disagree and a slide shoves the selection off-screen.
    private double EffectiveWidth(TItem item, double est) =>
        State.Widths.TryGetValue(item, out var w) && w > 0 ? w : est;

    // The placeholder width for a not-yet-measured tab: the mean of the widths measured so far (a sensible label
    // default before anything has measured). Only ever used for off-screen tabs — every on-screen tab measures — so
    // the small estimate error is invisible and is corrected the moment the tab scrolls into view.
    private double EstimatedTabWidth()
    {
        var sum = 0.0;
        var count = 0;
        foreach (var w in State.Widths.Values)
        {
            if (w > 0)
            {
                sum += w;
                count++;
            }
        }

        return count > 0 ? sum / count : 75;
    }

    // The lockstep override while the body is being dragged/flung: place the scroll-centre and the underscore at the
    // body's CONTINUOUS position, `selectedIndex + fraction`, by lerping between the two tabs that position falls
    // between. The fraction is unclamped, so a multi-page fling walks the underscore across every tab it crosses (not
    // just the immediate neighbour) — the two bracketing tabs are clamped to the materialised window, so if a fling
    // outruns the window the underscore rides its edge until the settle commits. Null when not tracking (or the
    // selected tab isn't materialised) — the strip then uses its normal rest/selection geometry.
    private (double Scroll, double Ix, double Iw)? TrackView(StripLayout layout)
    {
        if (_track is not { } f)
        {
            return null;
        }

        var index = State.Window.IndexOf(_selected);
        if (index < 0)
        {
            return null;
        }

        var count = State.Window.Count;
        var target = index + f;                                       // continuous position in window space
        var lo = Math.Clamp((int)Math.Floor(target), 0, count - 1);   // the tab at or before it …
        var hi = Math.Clamp(lo + 1, 0, count - 1);                    // … and the next one
        var t = Math.Clamp(target - lo, 0, 1);                        // fractional part (0 at lo, 1 at hi)

        var (loX, loW) = layout.IndicatorGeometry(lo, TabHPad);
        var (hiX, hiW) = layout.IndicatorGeometry(hi, TabHPad);
        var loCentre = layout.CentreOffset(lo);
        var scroll = loCentre + (layout.CentreOffset(hi) - loCentre) * t;
        return (layout.Clamp(scroll), loX + (hiX - loX) * t, loW + (hiW - loW) * t);
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

    // Keep the window materialised ahead of a live body fling. Grows ONLY (no eviction): an append never shifts an
    // index, and a prepend shifts every index right — including the selection's, which we advance in step so the
    // tracked position `index + fraction` keeps pointing at the same tab and the visual stays put (ContentLeft and the
    // track-scroll both shift by the prepend width). Eviction back to the cap is left to the post-commit MaybeSlide,
    // so nothing churns indices mid-follow. Bounded by the fling distance and the real sequence edges.
    private void EnsureTrackWindow(double fraction)
    {
        var index = State.Window.IndexOf(_selected);
        if (index < 0)
        {
            return;
        }

        const int margin = 4; // keep this many tabs materialised beyond the tracked position
        var grew = false;

        while (index + fraction > State.Window.Count - 1 - margin && _next(State.Window[^1]) is { } n)
        {
            State.Window.Add(n);
            grew = true;
        }

        while (index + fraction < margin && _prev(State.Window[0]) is { } p)
        {
            State.Window.Insert(0, p);
            index++; // the selection (and every tab) shifted one to the right
            grew = true;
        }

        if (grew)
        {
            SetState(_ => { });
        }
    }

    // Snap the selected tab to centre on first load, once the viewport and every tab up to (and including) the
    // selected one have measured (CentreOffset needs those widths). No animation — it is the resting position.
    private void TryInitialCentre()
    {
        if (State.ViewportWidth <= 0)
        {
            return; // the viewport hasn't measured yet — CentreOffset needs it; wait for the strip's OnSizeChanged
        }

        var idx = State.Window.IndexOf(_selected);
        if (idx < 0)
        {
            return;
        }

        // Centre off the estimate-filled layout: it no longer waits for every intervening tab to have measured (an
        // off-screen one may never), so a reseed or settle can't leave the selection stranded off-screen. On-screen
        // tabs measure immediately and their exact widths refine the centre via the rebase in OnSizeChanged.
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
            var est = EstimatedTabWidth();
            var first = State.Window[0];
            var added = 0.0;
            for (var i = 0; i < GrowChunk && _prev(first) is { } p; i++)
            {
                State.Window.Insert(0, p);
                added += EffectiveWidth(p, est) + SpacingDip; // the width (or estimate) the layout now counts for it
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
        var est = EstimatedTabWidth();
        var cap = 2 * WindowRadius + GrowChunk;
        var removed = 0.0;
        while (State.Window.Count > cap)
        {
            removed += EffectiveWidth(State.Window[0], est) + SpacingDip; // same width the layout counted for it
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
            ResolveAndApply(StripStimulus.RecentreTap); // tapping the selected tab only re-centres it (no report)
        }
        else
        {
            _onSelectedChanged(item); // fire immediately; the host's prop change drives the SelectionChanged transition
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
            ResolveAndApply(StripStimulus.RecentreTap); // already selected but scrolled away — just glide/snap back
        }
        else
        {
            _onSelectedChanged(home); // proxy tap; the prop change drives the SelectionChanged transition (re-seeds if evicted)
        }
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
