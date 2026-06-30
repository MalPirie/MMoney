using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MauiReactor.Shapes;
using Mobiorum.Material3;

namespace MMoney.App.Components.Sandbox;

internal sealed class TabStripSpikeState
{
    /// <summary>Committed resting offset (negative = scrolled left). Visual position = Committed + Live.</summary>
    public double Committed;

    /// <summary>Live drag delta during an active gesture; folded into Committed on release.</summary>
    public double Live;

    /// <summary>Last tapped tab index — proves a tap lands on the tab visually under the finger after a scroll.</summary>
    public int Selected;

    /// <summary>Last non-Running pan status seen (terminal-event reliability readout — risk 3).</summary>
    public string LastPan = "—";

    /// <summary>Raw <c>TotalX</c> at the last release — eyeball vs finger travel to detect self-distortion.</summary>
    public double LastTotalX;

    /// <summary>Release velocity (dip/s) of the last gesture — fling readout.</summary>
    public double LastVelocity;

    /// <summary>ScaleX of the row — the M3 overscroll stretch (1 = none). Springs back to 1 on release.</summary>
    public double Stretch = 1;

    /// <summary>AnchorX for the stretch: 0 pins the left (start) edge, 1 pins the right (end) edge.</summary>
    public double StretchAnchor;

    /// <summary>Underscore left edge, in row content space (animated during a selection slide).</summary>
    public double IndicatorX;

    /// <summary>Underscore width = the selected tab's width (M3 content-width indicator).</summary>
    public double IndicatorW;

    /// <summary>Transient state-layer strength on the tapped tab: 1 on tap, fading to 0 (the M3 "set and fade").</summary>
    public double TapFade;
}

/// <summary>
/// THROWAWAY device spike for <c>docs/adr/0003-tabbed-page-view.md</c> — <b>not</b> the real <c>TabStrip</c>.
/// Settles the gesture/positioning mechanics on glass before any real building. Tabs have deliberately
/// variable-width labels (to stress content sizing) and a hard lower bound at index 0 (the edit-lock edge).
/// Everything is logged under the <c>[TabStripSpike]</c> tag; drive it with the <c>scripts/</c> loop. Delete
/// once the mechanics are settled and <c>TabStrip</c> lands.
///
/// ITERATION 5 (transform + committed/live): device findings so far — model A's stationary catcher only fired
/// from the background; a <c>Margin</c>-driven row tracked the finger 1:1 but <b>jittered</b> (per-frame layout
/// pass). So this iteration moves the row with a <c>TranslationX</c> transform (no layout pass) and splits state
/// into <b>Committed</b> (resting) + <b>Live</b> (drag delta), folding Live into Committed on release. The pan
/// lives on the <c>HStack</c> that translates — watch for self-distortion (row lagging the finger), since
/// translating the pan's own view shrinks <c>TotalX</c>. A tap is suppressed if the gesture moved past
/// <see cref="TapSlop"/>, so a drag never fires a stray selection.
/// </summary>
internal sealed partial class TabStripSpike : Component<TabStripSpikeState>
{
    private const int LowerBound = 0;
    private const int UpperBound = 40;
    private const int Count = UpperBound - LowerBound + 1;
    private const double SpacingDip = 1; // gap between tabs (must match the HStack Spacing)
    private const double TapSlop = 5;    // a gesture that moved more than this suppresses the trailing tap

    private const double FrameMs = 16;          // fling tick interval
    private const double VelocityWindowMs = 80; // window for release-velocity measurement (noise rejection)
    private const double FlickVelocity = 300;   // dip/s release speed required to start a fling
    private const double MinFlingVelocity = 20; // dip/s below which the fling stops
    private const double FlingDecay = 0.95;     // velocity multiplier per tick (friction)
    private const double MaxStretch = 0.15;     // max ScaleX delta at full overscroll (M3 stretch, ~1.15)
    private const double StretchScale = 250;    // overscroll dip controlling how fast the stretch ramps in
    private const double SpringFactor = 0.25;   // ease-out factor per tick for the stretch spring-back
    private const double StateLayerOpacity = 0.12; // M3 primary state-layer alpha at full strength
    private const double UnderscoreHeight = 3;  // M3 active-indicator bar height (dip)
    private const double TabHPad = 16;          // tab horizontal padding; underscore spans the text (tab − 2×pad)
    private const double SlideDurationMs = 250; // M3 active-indicator slide + centre (medium1, emphasized/decelerate)
    private const double FadeInMs = 100;        // M3 state-layer ramp-in (~short2)
    private const double FadeOutMs = 200;       // M3 state-layer fade-out (~short4)

    private bool _panMoved;                       // set when a gesture exceeds TapSlop, so the end-of-pan tap is ignored
    private readonly double[] _tabWidths = new double[Count]; // each tab's measured width, captured post-layout
    private double _viewportWidth;                // measured width of the clipped viewport, for the scroll clamp
    private readonly List<(long Ticks, double Live)> _velSamples = new(); // recent drag samples for release velocity
    private int _flingGen;                        // bumped to cancel an in-flight fling (a new grab or tap interrupts)
    private int _stretchGen;                      // bumped on a new grab to cancel an in-flight stretch spring-back
    private int _selectGen;                       // bumped to cancel an in-flight selection slide (new tap or grab)
    private bool _selecting;                      // true while a selection slide animates the underscore/centring
    private int _renderCount;

    // Native touch-down bridge: MainActivity.DispatchTouchEvent raises this on every ACTION_DOWN (the only
    // reliable touch-down signal on Android — pan needs movement, and pointer-pressed doesn't fire for touch).
    internal static event Action? TouchDown;

    internal static void NotifyTouchDown() => TouchDown?.Invoke();

    // AutomationId marker on the viewport Grid; a handler mapping (MauiProgram) uses it to grab the platform
    // view so the Activity can scope touch-down cancellation to the strip's bounds.
    internal const string ViewportId = "tabstrip-viewport";

    // The strip viewport's platform view (typed object to keep this shared file platform-neutral).
    internal static object? StripPlatformView;

    // Deliberately non-uniform label widths so the content-sizing path is genuinely exercised.
    private static readonly string[] Shapes =
        { "Jan", "September", "May 2026", "Q3", "Wednesday", "7", "Mid-month", "Oct" };

    private static string TabText(int i) => $"{Shapes[i % Shapes.Length]} · {i}";

    protected override void OnMounted()
    {
        TouchDown += OnGlobalTouchDown; // a touch anywhere cancels an in-flight glide (native touch-down)
        base.OnMounted();
    }

    protected override void OnWillUnmount()
    {
        TouchDown -= OnGlobalTouchDown;
        base.OnWillUnmount();
    }

    private void OnGlobalTouchDown() => _flingGen++;

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;
        Debug.WriteLine($"[TabStripSpike] RENDER #{++_renderCount} committed={State.Committed:F0} live={State.Live:F0} sel={State.Selected}");

        return VStack(
            // Live readout + instructions sit OUTSIDE the viewport so they never intercept the gesture.
            Label("TabStrip spike — transform-driven row, committed + live state")
                .FontSize(12)
                .TextColor(scheme.OnSurface),
            Label($"committed={State.Committed:F0}  live={State.Live:F0}  selected={State.Selected}  lastPan={State.LastPan}  vel={State.LastVelocity:F0}")
                .FontSize(12)
                .TextColor(scheme.OnSurfaceVariant),
            Label("Drag ON a tab to scroll · tap a tab to select · does the row keep up with your finger?")
                .FontSize(11)
                .TextColor(scheme.OnSurfaceVariant),

            // The viewport is the STATIONARY, clipped container; the row inside it translates.
            Grid(
                Row(scheme),
                new TapGestureRecognizer().OnTapped(OnGridTapped) // position-aware tap; tab found by hit-testing
            )
            .AutomationId(ViewportId) // marker so a handler mapping can grab this Grid's platform view (touch scoping)
            .HeightRequest(64)
            .BackgroundColor(scheme.SurfaceVariant)
            .IsClippedToBounds(true)
            .ScaleX(State.Stretch)        // M3 overscroll stretch applied to the whole viewport, anchored at the
            .AnchorX(State.StretchAnchor) // pulled edge — stretches the VISIBLE content uniformly, no gutter
            .OnSizeChanged((Size s) => _viewportWidth = s.Width) // capture viewport width for the scroll clamp
            .OnPanUpdated(OnPan) // pan on the STATIONARY Grid; the row child translates; Started cancels a glide
        )
        .Spacing(8)
        .Padding(16);
    }

    private VisualNode Row(MaterialScheme scheme)
    {
        var tabs = Enumerable.Range(LowerBound, UpperBound - LowerBound + 1)
            .Select(i => Tab(scheme, i))
            .ToArray();

        var (min, max) = ScrollBounds();
        var offset = Math.Clamp(State.Committed + State.Live, min, max); // hard clamp — no gutter ever appears

        // ONE transform moves the whole row — no per-frame layout pass. The underscore is a child of the row,
        // so it rides with the scroll for free; it only animates on selection (SelectTab). Overscroll shows as
        // a ScaleX stretch on the viewport (no gutter).
        return Grid(
                HStack(tabs).Spacing(SpacingDip).HStart().VFill(),
                Underscore(scheme)
            )
            .HStart()
            .TranslationX(offset);
    }

    private VisualNode Underscore(MaterialScheme scheme)
    {
        // While a selection slides, use the animated geometry; otherwise sit under the selected tab (rides scroll).
        var (ix, iw) = _selecting ? (State.IndicatorX, State.IndicatorW) : IndicatorGeometry(State.Selected);
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

    // Emphasized/decelerate ease (fast-in, settle-out) — the M3 feel for an arriving element.
    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

    // A tab's geometry in row content space: left edge (cumulative widths + spacing) and its own width.
    private (double X, double W) TabGeometry(int index)
    {
        var x = 0.0;
        for (var i = 0; i < index - LowerBound; i++)
        {
            x += _tabWidths[i] + SpacingDip;
        }

        return (x, _tabWidths[index - LowerBound]);
    }

    // The underscore spans the tab's TEXT (M3), i.e. the tab minus its horizontal padding.
    private (double X, double W) IndicatorGeometry(int index)
    {
        var (x, w) = TabGeometry(index);
        return (x + TabHPad, Math.Max(0, w - 2 * TabHPad));
    }

    // No-gutter scroll bounds derived from content: the furthest-left offset lands the last tab flush at the
    // right edge (you can reach it, but no empty gutter beyond it); the furthest-right offset is 0, landing
    // tab 0 flush at the left edge. Reduces to [0, 0] until widths/viewport are measured.
    private (double Min, double Max) ScrollBounds()
    {
        var total = SpacingDip * (Count - 1);
        foreach (var w in _tabWidths)
        {
            total += w;
        }

        return (-Math.Max(0, total - _viewportWidth), 0);
    }

    // Map an overscroll excess (dip past a bound) to a capped ScaleX delta (asymptotes to MaxStretch).
    private static double StretchDelta(double excess) => MaxStretch * excess / (excess + StretchScale);

    private VisualNode Tab(MaterialScheme scheme, int i)
    {
        var selected = i == State.Selected;

        // M3: no persistent fill. Selection = underscore + text colour. The tapped tab shows a transient
        // primary state layer that pulses in then out (TapFade 0→1→0). The Border fills the full tab height
        // (VFill) so the state layer covers the whole tab, down to the underscore strip.
        var stateLayer = selected
            ? scheme.Primary.WithAlpha((float)(StateLayerOpacity * State.TapFade))
            : Colors.Transparent;

        return Border(
                Label(TabText(i))
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
                _tabWidths[i - LowerBound] = s.Width; // record width for hit-testing + underscore geometry
                if (i == State.Selected)
                {
                    SetState(_ => { }); // first measure of the selected tab: re-render so the underscore appears
                }
            })
            .WithKey(i); // stable identity so a drag re-render REUSES each tab's native view, not rebuilds it
    }

    private Task OnGridTapped(object? sender, MauiControls.TappedEventArgs e)
    {
        _flingGen++; // a tap interrupts an in-flight glide

        if (_panMoved)
        {
            Debug.WriteLine("[TabStripSpike] GRID TAP suppressed (pan moved)");
            return Task.CompletedTask;
        }

        // The tap's sender is the TapGestureRecognizer; its Parent is the viewport view, so GetPosition(view)
        // gives viewport-local coordinates. Subtract the row's translation to reach content space.
        var view = (sender as MauiControls.GestureRecognizer)?.Parent as MauiControls.View;
        var pos = e.GetPosition(view);
        if (pos is null)
        {
            Debug.WriteLine("[TabStripSpike] GRID TAP — no position");
            return Task.CompletedTask;
        }

        var contentX = pos.Value.X - State.Committed;

        // Walk cumulative tab widths (+ spacing) to find which tab contains contentX.
        var acc = 0.0;
        var hit = -1;
        for (var i = 0; i < _tabWidths.Length; i++)
        {
            var w = _tabWidths[i];
            if (w > 0 && contentX >= acc && contentX < acc + w)
            {
                hit = i;
                break;
            }

            acc += w + SpacingDip;
        }

        Debug.WriteLine($"[TabStripSpike] GRID TAP x={pos.Value.X:F0} contentX={contentX:F0} hit={hit}");
        if (hit >= 0)
        {
            SelectTab(hit + LowerBound);
        }

        return Task.CompletedTask;
    }

    // Full M3 selection: slide+resize the underscore from the old tab to the new one, scroll the new tab toward
    // centre, and fade the tapped tab's state layer — all over one coordinated, generation-guarded animation.
    private async void SelectTab(int index)
    {
        var gen = ++_selectGen;
        _selecting = true;

        var (toX, toW) = IndicatorGeometry(index);                   // text-width indicator target
        var (fromX, fromW) = IndicatorGeometry(State.Selected);      // begin the slide from the previously-selected tab
        var (tabX, tabW) = TabGeometry(index);
        var (min, max) = ScrollBounds();
        var targetCommitted = Math.Clamp(_viewportWidth / 2 - (tabX + tabW / 2), min, max); // centre, clamped (no gutter)

        var startIX = fromX;
        var startIW = fromW;
        var startCM = State.Committed;
        SetState(s =>
        {
            s.Selected = index;
            s.IndicatorX = fromX;
            s.IndicatorW = fromW;
            s.TapFade = 0; // pulse: ramp in, then out
        });

        // Time-based so the durations match M3 tokens; emphasized/decelerate ease on the slide, eased pulse.
        var sw = Stopwatch.StartNew();
        while (true)
        {
            if (_selectGen != gen)
            {
                return; // a new tap or a grab took over
            }

            var ms = sw.Elapsed.TotalMilliseconds;
            var slide = EaseOutCubic(Math.Min(1, ms / SlideDurationMs));
            var ix = startIX + (toX - startIX) * slide;
            var iw = startIW + (toW - startIW) * slide;
            var cm = startCM + (targetCommitted - startCM) * slide;
            var fade = ms <= FadeInMs ? ms / FadeInMs : Math.Max(0, 1 - (ms - FadeInMs) / FadeOutMs);

            SetState(s => { s.IndicatorX = ix; s.IndicatorW = iw; s.Committed = cm; s.TapFade = fade; });

            if (ms >= SlideDurationMs && ms >= FadeInMs + FadeOutMs)
            {
                break;
            }

            await Task.Delay((int)FrameMs);
        }

        if (_selectGen == gen)
        {
            SetState(s => { s.IndicatorX = toX; s.IndicatorW = toW; s.Committed = targetCommitted; s.TapFade = 0; });
            _selecting = false; // back to deriving the underscore from the selected tab (rides scroll)
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

    // Hand-rolled momentum: glide Committed in the release direction, shedding velocity each tick, until it
    // slows below MinFlingVelocity or hits a no-gutter bound. Interrupted by a new grab/tap via _flingGen.
    private async void Fling(double velocity)
    {
        var gen = ++_flingGen;
        var v = velocity;
        while (Math.Abs(v) > MinFlingVelocity)
        {
            if (_flingGen != gen)
            {
                return; // a new grab or tap cancelled this glide
            }

            var (min, max) = ScrollBounds();
            var prev = State.Committed;
            var next = Math.Clamp(prev + v * (FrameMs / 1000.0), min, max);
            SetState(s => s.Committed = next);
            if (next == prev)
            {
                return; // hit a bound
            }

            v *= FlingDecay;
            await Task.Delay((int)FrameMs);
        }
    }

    // Ease the overscroll ScaleX stretch back to 1 after release. Uses its own generation so a tap doesn't
    // abort it (only a new grab does, via OnPan Started bumping _stretchGen).
    private async void SettleStretch()
    {
        var gen = ++_stretchGen;
        while (Math.Abs(State.Stretch - 1) > 0.001)
        {
            if (_stretchGen != gen)
            {
                return;
            }

            var next = State.Stretch + (1 - State.Stretch) * SpringFactor;
            SetState(s => s.Stretch = next);
            await Task.Delay((int)FrameMs);
        }

        if (_stretchGen == gen)
        {
            SetState(s => s.Stretch = 1);
        }
    }

    private void OnPan(MauiControls.PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _flingGen++;        // grabbing the row cancels any in-flight fling
                _stretchGen++;      // ...and any in-flight stretch spring-back
                _selectGen++;       // ...and any in-flight selection slide
                _selecting = false; // underscore snaps to the selected tab and rides the drag
                _velSamples.Clear();
                Debug.WriteLine($"[TabStripSpike] PAN Started committed={State.Committed:F0}, id={e.GestureId}, total={e.TotalX}/{e.TotalY}");
                break;

            case GestureStatus.Running:
            {
                if (Math.Abs(e.TotalX) > TapSlop)
                {
                    _panMoved = true;
                }

                var live = e.TotalX;

                // Sample (time, live) over a short window so release velocity isn't a single noisy delta.
                var nowTicks = Stopwatch.GetTimestamp();
                _velSamples.Add((nowTicks, live));
                var windowStart = nowTicks - (long)(VelocityWindowMs / 1000.0 * Stopwatch.Frequency);
                while (_velSamples.Count > 2 && _velSamples[0].Ticks < windowStart)
                {
                    _velSamples.RemoveAt(0);
                }

                // Overscroll past a bound shows as a damped ScaleX stretch anchored at the flush edge —
                // the offset itself stays hard-clamped, so no gutter appears.
                var (min, max) = ScrollBounds();
                var desired = State.Committed + live;
                double stretch = 1, anchor = State.StretchAnchor;
                if (desired > max)
                {
                    stretch = 1 + StretchDelta(desired - max);
                    anchor = 0; // pin the start (left) edge
                }
                else if (desired < min)
                {
                    stretch = 1 + StretchDelta(min - desired);
                    anchor = 1; // pin the end (right) edge
                }

                SetState(s => { s.Live = live; s.Stretch = stretch; s.StretchAnchor = anchor; });
                break;
            }

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
            {
                var raw = e.TotalX;
                var velocity = ReleaseVelocity();
                var (min, max) = ScrollBounds();
                var desired = State.Committed + State.Live;
                var overscrolled = desired < min || desired > max;
                Debug.WriteLine($"[TabStripSpike] PAN {e.StatusType} committed={State.Committed:F0} live={State.Live:F0} desired={desired:F0} vel={velocity:F0} stretch={State.Stretch:F2}");
                _panMoved = false;

                // Settle flush at the edge (hard clamp — no gutter); the stretch springs back separately.
                SetState(s =>
                {
                    s.Committed = Math.Clamp(desired, min, max);
                    s.Live = 0;
                    s.LastPan = e.StatusType.ToString();
                    s.LastTotalX = raw;
                    s.LastVelocity = velocity;
                });

                if (overscrolled)
                {
                    SettleStretch(); // spring the ScaleX stretch back to 1; no fling
                }
                else if (e.StatusType == GestureStatus.Completed && Math.Abs(velocity) > FlickVelocity)
                {
                    Fling(velocity); // a real flick launches a momentum glide
                }

                break;
            }
        }
    }
}
