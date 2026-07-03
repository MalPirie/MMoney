# TabbedPageView: a decoupled, freely-scrollable tab strip (supersedes the StripPager direction)

The `StripPager` (ADR-0002) is being removed. Its central thesis — a label **strip** and a swipeable
**pager** body kept in lockstep off one shared drag fraction — proved too complex and unreliable on device
(see ADR-0002's revert note). We are replacing it with `TabbedPageView`, a generic `Mobiorum.Material3`
control built in smaller, independently-testable pieces. This ADR captures the new control's decisions as
they are made.

> **Status:** **implemented and device-validated on Android (2026-07-03).** Both controls are built on
> `Mobiorum.Material3` and shipping: `TabStrip<TItem>` (freely-scrollable bar, Home button, native touch seam)
> and `TabbedPageView<TItem>` (strip + `CarouselView` body), the latter now driving MMoney's live **Transactions**
> tab. The **coupled drag-lock** (strip tracks the body swipe 1:1) is done — see "TabbedPageView body composition"
> below. `StripPager` and the dead `Pager*` seam are deleted; the surviving windowing was renamed `StripWindow`/
> `StripCell` under `Tabs/`. Scope is **Android-only** (the agreed bar); cross-platform, a11y, and a configurable
> style surface remain out of scope. One known limitation is recorded under the drag-lock bullet. **Read "Spike
> outcomes" first — it is the authoritative interaction architecture; the "Decisions" section is kept as design
> rationale/history, and bullets it supersedes are tagged.**

## Spike outcomes — device-validated architecture (2026-06-30, device `cb90e980`)

This supersedes the tagged decisions below. All of it is working in the spike.

- **Scroll: one `TranslationX` on the row, not `Margin`.** Tabs live in a `HorizontalStackLayout` (content-
  width, overflowing a clipped viewport `Grid`); the whole row is moved by a single `TranslationX`. `Margin`
  tracked the finger 1:1 but **jittered** (it forces a per-frame layout pass); `TranslationX` (a post-layout
  transform) is smooth. The offset is **hard-clamped** (no gutter). *(Reverses "positioned by `Margin`".)*

- **Gesture model: pan on the stationary viewport `Grid` + position-based tap.** The pan lives on the
  stationary `Grid` (so there is **no self-distortion** — the pan view never moves). Tap is a
  `TapGestureRecognizer` on the same `Grid`; the tapped tab is found by `GetPosition` → subtract the scroll →
  hit-test the measured width map. **A per-tab tap recogniser starves the parent pan** (confirmed), so tap
  *cannot* be per-tab — hence position hit-testing. *(Reverses the "per-tab pan + reconstruction" outcome.)*
  - MauiReactor wrinkle: a gesture recogniser's `.Parent` is null, so the viewport's **platform view is
    captured via a handler mapping keyed on `AutomationId`** (`MauiProgram`), used for tap scoping + native touch.

- **Bounds: content-derived, no gutter.** `min = −max(0, Σtabwidths − viewportWidth)`, `max = 0`. Every tab is
  reachable and lands flush at the edge; no empty gutter ever shows.

- **Momentum (fling) is in.** A velocity-projected target tweened with `CubicOut`. Interruptible by
  grab/tap/touch. *(Reverses "no momentum for v1".)*
  - **Native touch-down stops the fling.** Android raises **no touch-down to MAUI for touch** (PointerPressed
    and PointerEntered don't fire; only PointerReleased does, and pan needs movement). So
    `MainActivity.DispatchTouchEvent` observes `ACTION_DOWN` and cancels the glide, **scoped to the strip's
    bounds** via the captured platform view.

- **Overscroll: M3 `ScaleX` stretch (no gutter).** The offset stays hard-clamped; pulling past an edge applies
  a damped `ScaleX` stretch **on the viewport**, anchored at the pulled edge, springing back to 1 on release.
  *(Augments the no-gutter clamp.)*

- **Selection: M3 underscore + scroll-centre + state-layer pulse.** A **text-width** underscore (spans the
  tab's label, i.e. tab − 2×padding) **slides and resizes** to the tapped tab; the strip **scroll-centres** the
  tab; a **transient state-layer pulse** (fade in then out) plays on the tapped tab. No persistent fill;
  selected text is `primary`/bold, unselected `OnSurfaceVariant`. M3 token durations, emphasized/decelerate
  easing. *(Refines the selection-animation decision below; eases per M3, not "default easing ~220ms".)*

- **Animation mechanism: `AnimationController`, not `WithAnimation`, for interruptible motion.** Every motion
  here (fling, stretch spring-back, selection slide/centre/pulse) is grab/touch-interruptible, and
  `WithAnimation` jumps `State` straight to the target (the in-between lives only in the native layer), so a
  mid-flight interrupt can't resume from the on-screen position. `AnimationController` ticks
  `DoubleAnimation.OnTick → SetState`, keeping `State` current → clean interrupt (`IsEnabled=false` stops it
  where it is). Cost: a re-render per tick (acceptable; framework-driven, not a hand-rolled loop). **Gotcha:**
  a `DoubleAnimation` must be wrapped in a `SequenceAnimation`/`ParallelAnimation` **container** to tick — a
  bare tween directly under the controller silently no-ops.

- **Known issues / deferred.** (1) **Remount zeroes the width map** — navigating away and back to the Sandbox
  resets `_tabWidths`, and `OnSizeChanged` doesn't re-fire for `WithKey`-reused tabs, collapsing the bounds;
  the real control must not depend solely on `OnSizeChanged` across remounts. (2) The **recall button** was
  not spiked. (3) The spike is **finite** (41 measured tabs); the **semi-infinite windowing vs. measured-
  widths** gap (you can't measure off-window tabs) is unresolved and must be designed in the real control.

## Real-control design (decided post-spike, 2026-07-02)

These decisions govern the real `TabStrip<TItem>`; where they touch a spike outcome they say so. The control
is generic: it must serve a **fixed finite range**, a **singly-bounded semi-infinite** range (MMoney — bounded
back at the edit lock, open forward), and an **infinite-in-both-directions** range with **one code path**.

> **Completion status (2026-07-02).** `TabStrip<TItem>` is **built, on `Mobiorum.Material3`, and
> device-validated on Android** (`cb90e980`): tab-select + underscore slide (incl. the fast-re-tap restart fix),
> scroll-centre, initial centre-on-load, pan/fling/overscroll-stretch, the Home button, the sliding window
> during pan/fling, the hit-test with the Home leading column, and the **library-owned native touch-down seam**.
> **Finite-range** geometry is covered headlessly (`StripLayoutTests` — both-ends clamp, narrower-than-viewport
> no-scroll, single-item, "never slides") and drivable on glass via the `TabStripSandbox` mode toggle
> (Infinite / Finite 0–15 / Fits 0–2). **Scope is Android-only for now** (the agreed bar). **Done since (2026-07-03):**
> `TabbedPageView` replaced `StripPager` on the live Transactions tab, `StripPager.cs` + the dead `Pager*` seam
> (`PagerGesture`, `SelectionRoute`) were deleted, and the surviving windowing was renamed `PagerWindow`/`PagerCell`
> → `StripWindow`/`StripCell` under `Tabs/`. The dev `TabStripSandbox`/`TabbedPageViewSandbox` scaffolding was
> removed. Cross-platform, accessibility, and a configurable style surface remain explicitly **out of scope**.

- **One sliding window, tracking the scroll (not the selection).** Resolves the "semi-infinite windowing vs.
  measured widths" gap. The control materialises a bounded window via `PagerWindow.StripRange` (radius *R* each
  side of the **viewport/scroll cursor**, not the selected tab — you scroll far from the selection, that is the
  recall button's whole reason to exist). Tabs enter one edge and are evicted from the other. When the whole
  range fits the window it degenerates to materialise-all, so fixed-range and semi-infinite are the *same* path
  — no special-casing. *(Supersedes the spike's fixed `double[Count]` + global `Σwidths − viewport` bound, which
  was a finite-range convenience that assumed every tab is materialised.)*
  - **Slide trigger: buffered hysteresis, not recompute-per-frame.** The window materialises a **buffer**
    beyond the viewport and re-windows only when the scroll comes within a margin of the materialised span's
    edge — then it tops up by a chunk on that side and evicts the far side. So the hot path (drag/fling at
    60fps) almost never re-windows or re-anchors; the sliding cost is paid occasionally at the buffer boundary,
    which matters because a fling can cross many tabs fast. `StripLayout` owns the "within margin of the edge?"
    test (it already holds the span edges vs. scroll). *(Recompute-viewport-centre-each-frame rejected: it
    re-anchors on every tab-boundary crossing, including mid-fling, for no benefit on the smoothest-must-be path.)*
    - **The hysteresis must run *during* motion, not only at rest** (device-found 2026-07-02): the slide check
      fires every pan `Running` frame and every fling tick, or the window doesn't top up until the finger lifts
      (blank past the buffer edge). Corollary — the **fling applies its controller value as a per-tick delta**,
      not an absolute `Committed` set, so a mid-fling re-anchor composes instead of being overwritten each tick
      (`_flingTo` stays in the controller's original frame, used only for progress/termination).

- **Uniform floating origin; re-anchor atomically (no jitter, no branch).** One materialised item is the anchor
  at content-space X = 0; the single `TranslationX` positions it. When a slide evicts the anchor, the control
  re-anchors to a still-materialised item and adjusts `TranslationX` in the **same `SetState`**. Visual position
  is `contentX + TranslationX`; both shift by ∓*w* in one render, so they cancel exactly — the rebase is
  provably jitter-free. There is deliberately **no** "pin to the finite edge" special case: a fixed/singly-
  bounded range simply never triggers a re-anchor because its window doesn't slide off a materialised end.

- **The only real branch is *sequence edge* vs. *window edge*.** A `null` from `Prev`/`Next` means the
  materialised span reached a **real end** → clamp hard (flush, no gutter, the M3 `ScaleX` overscroll stretch).
  A materialised edge with a non-null neighbour is merely the **window edge** → do not clamp; slide and
  materialise more. This fork is inherent to any windowed control (it answers "is there more?"), not an
  origin/coordinate branch; it is data-driven off the null-neighbour signal `PagerWindow` already emits. Bounds
  are therefore computed over the **materialised span only** (`Σ materialised widths`), which is always
  well-defined because every materialised tab is measured.

- **Home button: a fixed-left, non-directional "return to home" affordance (replaces return-to-selected).** The
  control takes a nullable, host-supplied **`Home` item** (`.Home(TItem?)`) — the anchor to jump back to. The
  generic control knows nothing of dates; **MMoney binds `Home` = the current-date tab** ("today"), pushing the
  date knowledge to where it lives. A pinned button sits **always on the left** and tapping it returns to
  `Home`. It is deliberately **not** side-aware: the earlier ADR rejected a
  fixed-side button because a *chevron is directional* and lies when the target is off the other edge. Changing
  the affordance from directional to **semantic** ("home") dissolves that — a **house icon does not point**, so
  a fixed-left button is honest whether `Home` fell off the left (forward browse) or the right (browsing closed
  months into the past). The two decisions are load-bearing on each other: fixed-left is only valid *because*
  the icon is non-directional.
  - **Always present, in its own pinned leading column — not a conditional overlay** (revised 2026-07-02 on
    device). The button is a persistent fixed-left affordance whenever `Home` is set; the tabs scroll in the
    remaining width beside it and never pass under it. This reverses the earlier "appears only while `Home` is
    off-screen" call: as an overlay it covered the first tab, and popping in/out as `Home` scrolled past the
    edge was jarring. A persistent leading action (like a browser home button) is calmer and removes the
    "`Home` not visible?" test from the button-visibility path entirely.
  - **Icon is a configurable M3 symbol**, default a house (`MaterialSymbols.Home`, codepoint `0xE88A`, to be
    added).
  - **Tapping Home is a proxy tap on the home tab — `SelectTab(Home)`.** It carries no behaviour of its own: it
    *selects* today (fires `OnSelectedChanged(Home)`), moves/resizes the underscore, scroll-centres, and pulses
    the state layer — the entire tap path reused, with the re-seed-and-snap fallback when Home is evicted. No
    navigation-only path and no new callback (reuses `OnSelectedChanged`); "go to today" makes today current in
    one gesture. The dropped case — peek at today without disturbing selection — is the return-to-selected use
    we deliberately cut.
  - **Return-to-selected is dropped.** One anchor only (`Home`); a home button *and* a return-to-selected
    fighting for edge space is clutter, and in a ledger "today" is the meaningful anchor, not a transient tap.
  - **Motion reuses the glide-when-close / snap-when-far rule:** if `Home` is within the materialised window
    (off-screen but rendered), smooth scroll-centre to it; if `Home` has been evicted, re-seed the window
    around `Home` (`StripRange(Home, …)`) and snap-centre — there is no continuous scroll path across an
    unmeasured (possibly infinite) gap. "`Home` not visible" (button-shown test) is likewise cheap: not in the
    window ⇒ not visible; in the window ⇒ the spike's measured hit-test.

- **Width measurement: rendered `OnSizeChanged` widths, re-measured via a generation token (not persisted).**
  Widths are the exact rendered pixels the spike proved (hit-tests and the underscore land perfectly) — no
  analytic/`Microsoft.Maui.Graphics` prediction (rejected: it drifts from real pixels, misses the bold-selected
  width the spike relies on, and — because our windowing never needs off-window widths — buys nothing). Fixes
  the remount known-issue (#1) **without** a persisted cache: the width map stays plain instance state, and tab
  views are keyed on **`(item, measurementGeneration)`**. The generation is fresh per component instance and
  bumps on **font/theme/label** change. During scroll it is stable so `WithKey` reuses native views (smooth hot
  path); on remount or an invalidating change the generation differs, keys miss the reused views, fresh views
  are built, `OnSizeChanged` re-fires, and the map repopulates. One trigger covers both the remount zeroing and
  the font/theme/label re-measure the ADR already requires; no static/host-threaded cache, no stale widths.
  Cost: a one-off rebuild of the *visible* tabs on nav-in (a row of light labels — negligible). *(Analytic
  seed-then-refine hybrid considered and rejected as unnecessary given exact rendered widths and no off-window
  need.)*

- **Native touch-down: a library-owned, per-instance platform seam (no app statics).** *(IMPLEMENTED +
  device-validated 2026-07-02.)* The spike's glide-cancel-on-touch climbed to `MainActivity.DispatchTouchEvent`
  + a static `StripPlatformView` captured via a `MauiProgram` `AutomationId` handler mapping — because Android
  surfaces no touch-down for touch and the viewport `Grid`'s single `OnTouchListener` is owned by MAUI's pan.
  The real control moves this **into `Mobiorum.Material3`**: the viewport is wrapped in a `TouchDownContentView`
  (a `ContentView` subclass) whose Android handler (`TouchDownContentViewHandler`) swaps the platform view for a
  `TouchObservingViewGroup` that peeks `ACTION_DOWN` in **`OnInterceptTouchEvent`** — returning `base` so it
  observes **without consuming**, leaving the child's pan/tap untouched (the spike proved a consuming
  `OnTouchListener` starves the pan; intercept-observe does not). It raises a **per-instance** callback
  (`OnTouchDown` → the component's `OnGlobalTouchDown`), so no statics and no bounds-capture (a touch reaching
  the wrapper's own interception is inherently inside its bounds). The static `TouchDown` event, `NotifyTouchDown`,
  `StripPlatformView`, the `MainActivity.DispatchTouchEvent` override, and the `MauiProgram` `AppendToMapping`
  capture are all deleted. The one remaining host touch-point is a single idiomatic **`builder.UseMobiorumMaterial3()`**
  line (MAUI requires custom-control handlers to be registered on the app builder) — a clean library opt-in, not
  spike glue. Cost: the library gains a `Platforms/Android` custom `ViewGroup` + handler + the `UseMobiorumMaterial3`
  extension. **Scope: Android only for v1** — the cancel-on-touch exists *because* Android hides touch-down;
  iOS/Windows surface pointer-pressed, so their seam is a later, simpler (or no-op) impl (non-Android registers
  the stock `ContentViewHandler`, so the wrapper is a transparent pass-through there). *(Host-wired
  `ITouchDownSource` abstraction rejected: boilerplate per consumer, and it re-introduces the
  single-instance/statics smell.)*

- **Pure seam: a cohesive `StripLayout` value type; the `Component` stays a single, thin shell.** All the
  control's arithmetic — hit-test, content-X, scroll bounds, centre-offset, visibility, floating-origin
  re-anchor — operates on one shared bundle (ordered materialised items, width map, scroll, viewport width,
  and whether each end is a real edge). Unlike `PagerGesture`'s independent decisions, that is a cohesive view
  over one state, so it is a **`readonly record struct StripLayout`** built from that bundle and exposing pure
  queries (`HitTest`, `ContentX`, `Bounds`, `CentreOffset`, `IsVisible`, `ReAnchor`) — MauiReactor-free, unit-
  tested in `Mobiorum.Material3.Tests` with hand-authored widths, exactly like the existing seam. `PagerWindow.
  StripRange` stays alongside as the windowing primitive that *produces* the item list feeding `StripLayout`.
  Free static functions (option b) were rejected: they would re-thread the same five-part bundle through every
  query. The `TabStrip` `Component` is therefore **thin and single** (no sub-component tree): it holds `State`,
  the three `AnimationController`s (fling / stretch / select), the pan/tap/touch-down wiring, and `WithKey`
  rendering, and delegates **every number** to `StripLayout`. The underscore and home button are presentational,
  driven by parent state — splitting them into their own stateful components would only re-thread state as props.
  - **Second pure seam: `StripTransition`, the resting-transition resolver (added 2026-07-03).** `StripLayout`
    deepened the *geometry*, but the layer above it — "a stimulus arrived; glide, snap, reseed, defer, or nothing?"
    — was smeared across `AnimateToSelected`/`SnapToSelected`/`RecentreCommitted`/`Recentre`, each welded to a
    `SetState` and therefore untestable. That layer is exactly where the drag-lock device bugs lived (underscore
    one-tab-behind, backward-flick, far-tap). It is now a pure `abstract record StripTransition` with cases
    `Glide`/`Snap`/`Reseed`/`Defer`/`None` and a static `Resolve(stimulus, input, layout, inset)` that composes
    `StripLayout` and returns a decision; the `Component` is a thin executor (`Apply`) that maps the decision to
    `SetState` + a controller restart. `Glide`/`Snap` carry concrete endpoints; `Reseed`/`Defer` are thin signals
    (a reseed mutates the window, so post-reseed geometry cannot be pre-computed — the freshly-seeded tabs are
    unmeasured, which is *why* those paths defer). Three stimuli (`SelectionChanged`, `TrackEnded`, `RecentreTap`)
    collapse the four old entry points. `IndicatorGeometry` and `MeasuredThrough` moved onto `StripLayout` so the
    resolver, the render's underscore, and the tests share one definition. Unit-tested headlessly in
    `Mobiorum.Material3.Tests` (`StripTransitionTests`) with hand-authored widths, like `StripLayout`. One
    behaviour change folded in: the rare "tap the selected tab after it scrolled off-window" path now reseeds-and-
    defers (routing through the one validated centre-on-measure path) instead of snapping off possibly-unmeasured
    widths with no self-correction — a latent-defect fix, not a feature change. `Defer` stays distinct from
    `Reseed` precisely to preserve the anti-flick rule (never re-centre the window on an in-window snap).

- **Surface finalised at seven props; everything else internal.** The full public surface is the seven props
  above and the `struct` constraint — **nothing more**. Deliberately kept off the surface: the **window radius /
  slide triggers** (internal constants — a caller can't reason about `R`, so it's defaulted, promoted to a prop
  only on a real need); all **M3 tokens** (selection/fling/stretch durations, easings, underscore height,
  state-layer alpha, stretch ramp) live in the control per ADR-0001's "controls read `MaterialScheme.Current`
  internally; callers never thread style through"; and **home-button visibility** is internal
  (`StripLayout.IsVisible(Home)`) with no callback (the host already holds `Home` and `Selected`). No
  speculative knobs are designed in ahead of a concrete need.

- **All persistent control state lives in `State`, never in instance fields (MauiReactor re-instantiation).**
  Device-found 2026-07-02 during the component port. When the **host** re-renders (e.g. its
  `SetState(Selected=…)` in response to `OnSelectedChanged`), MauiReactor builds a **new component instance**
  and migrates only the `State` object — plain instance fields reset to their defaults, and `OnMounted` does
  **not** re-run for the new instance. The port had first stored the materialised window, the width map, the
  measurement generation, and the last-selected marker in instance fields; they zeroed the instant a tab was
  selected (the empty window then made `HitTest` misfire, wrongly tripped the reseed path, and left the
  underscore/centre/Home-visibility reading stale coordinates — one bug, four symptoms). Fix: `TabStripState`
  is **generic** (`TabStripState<TItem>`) and holds `Window`, `Widths`, `LastSelected`, `MeasurementGeneration`,
  and `ViewportWidth`; only single-interaction transients (velocity samples, fling/stretch/selection endpoints,
  the tap-slop flag) stay in fields, because a gesture/animation never crosses a host re-render. Seeding is
  **lazy on first render** (guarded by a `Seeded` flag), not in `OnMounted`, so a migrated instance inherits the
  seeded window. Corollary — a *reseed* (`SeedWindow` around a new centre) moves the content-coordinate origin,
  so it must **snap** `Committed` into the new frame; the earlier "glide from the old `Committed`" was stale.
  This is the load-bearing MauiReactor constraint for the whole control and any future `TabbedPageView`.

## Decisions

- **`TabStrip<TItem>` is a standalone control; `TabbedPageView` is a later composite.** The v1 deliverable is
  `TabStrip<TItem>` — the freely-scrollable bar alone (scroll, centring, recall button, selection-on-tap),
  built and tested with no page. `TabbedPageView<TItem>` becomes a thin later composite (a `TabStrip` above a
  page body that follows its selection); it is **not** built yet. Internal vocabulary: **tab** (one label
  cell), **tab strip** (the scrollable row), **selected tab** (host-owned). This is library/implementation
  vocabulary and deliberately stays *out* of `CONTEXT.md`, which is the MMoney domain glossary only.

- **Lockstep is cut; the tab strip is designed and tested on its own, page deferred.** The first deliverable
  is the **tab strip alone** — a horizontal, freely-scrollable bar of labels ("tabs") over a navigable
  sequence. There is **no shared drag fraction**, no swipe-to-commit, no flick/rubber-band coupling to a body.
  This deletes the machinery that sank ADR-0002 (the self-distortion reconstruction on the body, the
  native-drive gesture desync, `PagerGesture.Decide`'s commit logic). When the page is added later, a body
  interaction will *change selection* — which animates the strip to re-centre — not drive a shared fraction.

- **Hand-driven direct-drag, no momentum for v1.** *(SUPERSEDED — fling momentum was added; see Spike
  outcomes.)* The strip is a hand-driven controlled coordinate (reusing
  the existing browse + window-growth logic from `StripPager`), **not** a native horizontal `ScrollView`. The
  range is semi-infinite (open-ended forward, bounded back at the edit lock), which a native scroller's
  finite-extent/index model fights — the same reason ADR-0002 rejected `CarouselView`. The cost is no fling
  momentum: drag is 1:1, stops on lift. Accepted for v1; a hand-rolled fling-decay on release is a contained,
  reversible addition if "scroll all through the range" proves tedious.

- **Cells are positioned by `Margin` (real layout), not `TranslationX`.** *(SUPERSEDED — `Margin` jittered on
  device; the row is moved by a single `TranslationX` and taps use position hit-testing. See Spike outcomes.)*
  This reverses `StripPager`'s approach. ADR-0002 documents the bug it was working around: on Android a tap is **not delivered through a
  translated ancestor** (`StripPager.cs` never translates the container and instead bakes the scroll offset
  into every cell's `TranslationX`). `Margin` is real layout, so hit-testing follows it and the whole
  translate-and-lose-the-tap class of bug disappears. The per-frame layout cost — the historic objection to
  `Margin` — is now negligible because the page is decoupled: a drag frame re-lays-out only a single row of
  light labels, not the heavy pages that caused the original stutter.
  - **Spike risk (confirm on device):** scrolling the row leftward needs a **negative** offset, and negative
    `Margin` is historically flaky on Android (clipping, inconsistent hit-region). Verify negative-margin
    leftward scroll *and* tap delivery on a real device first. **Fallback:** per-cell `TranslationX` (the
    proven `StripPager` path) if Android misbehaves on negatives.

- **A side-aware "return to selected" button.** *(SUPERSEDED 2026-07-02 — target changed from selected to a
  host-supplied Home item, and the affordance from a directional chevron to a fixed-left semantic icon. See
  "Home button" under Real-control design.)* When the selected tab is scrolled outside the viewport, a
  pinned button appears on **whichever edge the selected tab went off** (forward browse pushes it off the
  left; backward browse off the right), carrying a **bare directional chevron** (no destination label — kept
  minimal). Tapping it animates the strip to re-centre the selected tab with **no selection change** (the
  existing `Recentre()` glide). It hides whenever
  the selected tab is on-screen. A fixed far-left button was rejected: it points the wrong way and sits on the
  wrong side when the user has browsed backward.

- **Centre only when flanked by enough tabs; clamp at boundaries with no gutter.** (Revised 2026-06-29 after
  seeing M3 tabs in motion — reverses the earlier "always-centre with a gutter" call.) A tab scrolls to
  screen-centre **only when there are enough tabs on both sides to fill the viewport**. Near a bounded edge
  (the edit lock) the scroll **clamps** so the edge tab sits flush against the viewport edge — **no empty
  gutter** is ever shown; the boundary tab simply lands off-centre. This matches `StripPager`'s
  clamp-to-materialised-span behaviour and M3's own scrollable-tabs feel. The recall button still scrolls the
  selected tab back as far as the clamp allows (centred if interior, flush if at a bound).

- **Variable-width tabs, via a real stack offset by a single value (not per-cell positioning).** Tabs are
  content-sized, not fixed-width. The implementation deliberately avoids computing each cell's position every
  frame: tabs sit in a real `HorizontalStackLayout` (`HStart`, unconstrained, overflowing a clipped
  viewport), so the platform does all relative positioning, and the control moves the whole row with **one**
  offset value. The key property that keeps this simple is that **the hot path needs no widths**:
  - *Drag/browse* is `scroll += delta` — pure finger-follow, never reads a width.
  - *Recall* (centre the selected tab, offset 0) animates `scroll` back to a fixed reference — no widths.
  - Only *tap-to-centre a distant tab* and the *selected-visible?* test consume widths, and both act on tabs
    that are currently rendered, hence already measured.

  Each tab reports its rendered width via `OnSizeChanged` into an `offset → width` map, read **only** by the
  centring-target and visibility calculations — never to position cells. That arithmetic ("given this width
  map and the current scroll, what scroll centres tab *k*, and is tab 0 on-screen?") is a pure function over
  a dictionary and stays in the headless `Mobiorum.Material3.Tests` seam, like `PagerGesture`/`SelectionRouting`.
  - **Accepted costs:** (1) a two-pass settle — the first frame may render uncentred and snap centre one
    frame later (precompute via `Microsoft.Maui.Graphics` text measurement if it ever flashes); (2) the
    single-container negative `Margin` offset is now **load-bearing**, raising the stakes on the
    negative-margin spike above; (3) the width map must be invalidated and re-measured on font/theme/label
    changes.

- **Gesture model: spike a stationary pan-catcher first, fall back to per-tab pan.** *(SUPERSEDED — the final
  model is parent-`Grid` pan + position-based tap, neither model A nor per-tab pan. See Spike outcomes.)*
  Preferred arrangement is
  **pan on a stationary viewport background** (it never moves, so there's no self-distortion to reconstruct
  and no moving-view hazard) + `OnTapped` on each tab. This is exactly what `StripPager.cs:228` claims Android
  won't deliver (tappable children swallow the parent's pan), so it must be confirmed on device. **Fallback:**
  pan *and* tap on each tab (the proven `StripPager` arrangement) with the self-distortion reconstruction
  (`trueDelta = TotalX + appliedDelta`) and tabs rendered edge-to-edge so a drag anywhere on the content
  browses. Also part of the same spike: whether the per-frame `Margin` **layout pass survives an active pan**
  (a layout pass mid-gesture is more disruptive than a transform); deeper fallback is per-cell `TranslationX`.
  - **Spike result (2026-06-29, device `cb90e980`): model A rejected, going per-tab pan.** On device the
    stationary catcher received the pan **only from the background** — a drag starting on a tab was swallowed
    by the tab, exactly as `StripPager.cs:228` predicted. Since tabs cover almost the whole strip, that is
    unusable, so the **fallback (per-tab pan + self-distortion reconstruction) is now the chosen model.** The
    negative-`Margin` half of the spike **passed**: dragging the row scrolled it and taps still landed on the
    correct tab after scrolling (hit-testing follows `Margin`). Terminal-event reliability under the per-frame
    `Margin` layout pass is being confirmed in the per-tab-pan spike iteration.

- **M3 selection animation: a dynamically-sliding underscore + a fill-and-fade on the tab.** *(PARTLY
  SUPERSEDED — the shape (sliding content-width underscore + transient state-layer) is right, but it is driven
  by an `AnimationController` with M3-token durations + emphasized easing, not `WithAnimation` gating at
  "~220ms default easing". The underscore spans the tab's **text**, not full width. See Spike outcomes.)*
  (Revised 2026-06-29 after seeing M3 tabs in motion — reverses the earlier "indicator is static styling" call.) On
  selection the **underscore animates** from the previously-selected tab to the new one (a separately-animated
  element in stack coordinates, its target the new tab's position), and the tab shows an M3 **state-layer
  fill that fades** as the selection transition runs. Cutting swipe-commit still removes the old "underscore
  hop" — selection only changes on tap — but the indicator is now a *moving* element, not pure styling, so it
  needs `WithAnimation` gating (contained to the selection transition, never per drag frame). It consumes the
  measured width map (both tabs' positions); if the previously-selected tab is off-screen/unmaterialised
  (post-browse recall case), the slide degrades to a snap — detail to settle in implementation. The
  fill-and-fade is the **transient** form (A): a `primary` state-layer (~10%) fills the tapped tab and fades
  out over the selection transition; the persistent selected state is the underscore + bold `OnSurface` text
  (unselected `OnSurfaceVariant`), with **no** lasting filled background. The underscore is **content-width**
  (option b): it translates *and* resizes to the target tab's width over the same `SelectionDurationMs` as the
  scroll. The elastic M3 stretch (resize with overshoot mid-transit) is deferred as optional future polish.
  All selection animation runs at `SelectionDurationMs` (~220ms) with **default easing** for v1; M3's
  emphasized easing is a deferred swap-in.

- **Tap collapses to centre-it, and selection is reported immediately.** With the page gone, `StripPager`'s
  slide-vs-jump routing (`SelectionRouting`) is dropped: tapping a non-selected tab fires
  `OnSelectedChanged` **immediately** (a discrete state change) and animates the tab to centre as independent
  visual feedback; tapping the selected tab only re-centres it (no report). This reverses `StripPager`'s
  fire-at-animation-end timing (which existed for its seamless window-swap, now gone) so a future page can
  start switching the instant a tab is tapped. Overlapping centring animations from rapid taps are cancelled
  by the reused generation counter.

- **Tested without the page: pure unit seam + a synthetic dev sandbox.** Headless tests in
  `Mobiorum.Material3.Tests` cover the pure surface — windowing (reuse `PagerWindow.StripRange`) plus new pure
  functions for centring (width map + scroll → scroll that centres tab *k*), selected-visibility, and
  recall-side. Manual device testing uses a **dev-only sandbox page** hosting `TabStrip` alone (page area a
  plain placeholder), driven by a **synthetic sequence with deliberately variable-width labels and a
  configurable near bound** to stress the measurement path, the edit-lock gutter, and the recall button.
  Reuses the `scripts/` build-deploy-log loop. `MonthOnly`/Transactions wiring waits for `TabbedPageView`
  (uniform month labels under-test variable width and would drag the page back into scope).

- **API surface, and the old `Pager/` seam's disposition.** *(UPDATED 2026-07-02 — the surface is now **seven**
  props, not five: the Home button added `.Home(TItem?)` and `.HomeIcon(string)`. See "Home button" and the
  finalisation below.)* `TabStrip<TItem> where TItem : struct` exposes
  `.Selected(TItem)` (host-owned source of truth), `.Next`/`.Prev`
  (`Func<TItem, TItem?>`, null at a bound), `.Label(Func<TItem, string>)`, `.OnSelectedChanged(Action<TItem>)`
  (fired immediately on tap), `.Home(TItem?)` (nullable — absence *is* the "no home button" signal, one source
  of truth), and `.HomeIcon(string)` (M3 glyph, defaults to `MaterialSymbols.Home`). No `.Page(...)` — that is
  `TabbedPageView`'s later. The `struct` constraint
  stays (the null-neighbour signal needs `Nullable<TItem>`, per ADR-0002). `PagerWindow`/`PagerCell`
  windowing is **kept and reused** (rename to `Tabs/`, `TabWindow`/`TabCell` when the new control lands);
  `PagerGesture` (commit/flick/damp) and `SelectionRoute` (slide/jump) become dead and are removed once
  `TabStrip` replaces them. **`StripPager.cs` is kept in place until `TabStrip` is proven on device**, then
  deleted.

## TabbedPageView body composition (decided 2026-07-02)

`TabbedPageView<TItem>` is the composite MMoney needs: a `TabStrip` (top) over a swipeable page body, kept
in **selection sync only** (no shared drag fraction — that is what sank ADR-0002). It is a thin composite
that *contains* a `TabStrip`, not a reimplementation.

- **Body = a native MAUI `CarouselView` (`Loop=false`).** Horizontal swipe/pan changes the current item; each
  item's template is the host's page wrapped in a vertical `ScrollView`. The **axis arbitration** (horizontal
  swipe vs. vertical scroll) is the *native* control's job — horizontal-outer + vertical-inner is the
  well-supported nesting direction. This deletes the hand-rolled axis-lock + self-distortion pan that made
  `StripPager` unreliable. `Loop=false` so finite ranges rubber-band at their real ends and never wrap.
  *(Hand-rolled 3-page body rejected for v1: it reopens the ADR-0002 gesture swamp. A native `CarouselView`
  gets reliable gestures for free.)*

- **Feed = an append-on-demand buffer, not a fixed horizon and not a 3-item recycler.** `CarouselView` needs a
  materialised `IList`; the sequence is `Next`/`Prev` (back-bounded at the edit lock, forward-open). The body
  binds a buffer materialised from the back edge forward to a modest initial horizon; when the swiped
  `Position` nears the end, the buffer **appends** the next chunk. Because MMoney is *back-bounded + forward-open*,
  growth is **append-at-end only**, which never shifts an existing index — so it is unbounded forward (scroll
  arbitrarily far, no false edge) with **no re-anchor and no visible jump**, and the back never grows. Items
  are value-type structs (trivial memory); `CarouselView` virtualises the page *views*. *(Fixed bounded buffer
  rejected: leaves an artificial forward edge. 3-item prev/current/next recycler rejected for the CarouselView
  body: silent re-centring fights the native pager's opaque internal scroll — flicker risk; that recycler is
  clean only when you own the scroll, i.e. a hand-rolled body.)*

- **Selection sync + a settle-only commit seam.** Body swipe and tab tap both mirror `Selected`, but MAUI's
  `CarouselView` raises `PositionChanged`/`CurrentItemChanged` *optimistically* at the mid-drag crossover (no
  settle-only managed event), and its old Items `RecyclerView` snaps unreliably on a slow release. So commit runs
  off a **library-owned Android scroll-state seam** (`CarouselSettleObserver`): a non-clobbering
  `RecyclerView.OnScrollListener` (via `CarouselViewHandler.Mapper.AppendToMapping`) that commits **only on
  `ScrollStateIdle`** reading geometry (`FindFirstCompletelyVisibleItemPosition`), and **owns the snap** — if the
  release left the body straddling two pages it drives `SmoothScrollToPosition(nearest)` and waits for the clean
  idle. So the tab changes exactly once, at the point of no return. Tab tap → `Selected` → the body's `Position`
  is set to that item.
  - **Pure kernel behind the native adapter: `CarouselSettle` (added 2026-07-03).** The seam's *arithmetic* — the
    continuous drag position (`first + −left/width`, matching `ViewPager2.onPageScrolled`) and the idle decision
    (completely-visible ⇒ commit; straddle ⇒ snap to the nearest page; nothing laid out ⇒ ignore) — was written
    directly in `RecyclerView`/`child.Left`/`child.Width` terms behind `#if ANDROID`, so it could not be link-
    compiled into the headless tests and had **zero coverage** despite being the device-tuned, bug-historied core.
    It is now a pure, Android-free `CarouselSettle` (in `Controls/Tabs/`, link-compiled and unit-tested like
    `StripLayout`) returning a `SettleOutcome { Commit / Snap / Ignore }` DU — the same decision-then-thin-adapter
    shape as `StripTransition`, so both native resolvers read alike. `SettleListener` stays the adapter: it reads
    the RecyclerView, holds the stateful `_userDrag` latch, and *applies* the outcome (`Commit` reports up and ends
    the gesture; `Snap` drives `SmoothScrollToPosition`; `Ignore` waits). Behaviour is preserved exactly. The two
    string-keyed callback registries (settle + live-drag) were also unified into one `Register(id, onSettled,
    onScrolled)` + a `CarouselCallbacks` record, removing the half-registered failure mode.

- **Coupled drag-lock (strip tracks the body's live drag 1:1) — DONE, device-validated 2026-07-03.** The
  "panning lock". **Body-drives-strip only** (strip drag stays free-scroll + tap). The settle seam supplies the
  intra-swipe fraction that `CarouselView` hides: while a finger owns the scroll (`ScrollStateDragging` latched),
  `OnScrolled` reports a **continuous page position** (`firstVisible + −child.Left/width`, exactly Android's
  `ViewPager2.onPageScrolled`). `TabbedPageView` turns that into a clamped ±1 fraction and feeds `TabStrip` a new
  `.Track(double?)` prop; `TabStrip` lerps the underscore **and** the scroll-centre between the selected tab and
  its neighbour. It tracks *through* a snap-back (the seam keeps reporting as the body animates home) and commits
  only at idle. Load-bearing device-found rules: the commit clears the live fraction by **direct mutation before**
  reporting up, so the strip **snaps** (not glides) atomically onto the committed tab; the underscore width never
  lerps toward an **unmeasured** neighbour (it would vanish); the window grows via `MaybeSlide` (re-centring on
  every snap flicked the row); and a hard multi-page fling **commits where it lands** (a one-page clamp fought
  MAUI's `Position` binding and snapped the body back to origin). *(The earlier "hand-rolled body is the only
  guarantee" worry did not materialise — the RecyclerView seam gave a faithful `onPageScrolled` for free.)*
  - **Far-tap — RESOLVED (2026-07-03, was a deferred limitation).** Tapping a **far** tab **after free-scrolling
    the strip a long way** used to misbehave three ways: off-by-one hit-test, wrong/absent centre, and the body
    *scrolling* through every intervening page instead of jumping. The first two shared a root — distant tabs have
    **unmeasured widths** — closed by the **estimate-width hardening** (see the windowing bullet below): a far tab
    now carries a real-enough width, so hit-test and centre geometry hold. The third was MAUI's `CarouselView`
    animating a distant in-source `Position` change (`IsScrollAnimated(false)` is unreliable); closed by
    **rebuilding the body buffer around the target** on a far tap, so MAUI gets a *fresh ItemsSource* and
    initialises AT the target with no in-source scroll — a clean instant jump (near taps, ≤ one page, still animate
    the single page across). All three device-validated 2026-07-03. *(The original "measure/settle far tabs before
    acting" plan proved unnecessary — the estimate makes the geometry degrade gracefully without it.)*

- **Windowing hardened for the back edge and multi-page flings (2026-07-03).** Device testing at the edit-lock
  (back) edge and under fast flings surfaced a family of bugs — underscore parked a whole tab off its selection, a
  left gutter, the selection stranded off-screen with no recovery, and the strip freezing during a multi-page
  fling then snapping at the end. An **A/B against the pre-refactor build confirmed all of them are pre-existing**
  (present in the device-validated drag-lock build too — the back edge and multi-page flings were simply never
  stressed), *not* introduced by the `StripTransition`/`CarouselSettle` refactors. The shared root: the strip's
  geometry (`ContentLeft`, `CentreOffset`, the slide re-anchor) depended on the widths of tabs that are **off-screen
  and may never fire `OnSizeChanged`**, so a `0` there shorted `ContentLeft` by a full tab. The fixes:
  - **Estimated width for unmeasured tabs.** `Layout()` fills a not-yet-measured tab with the **mean measured
    width** (a label default before anything measures) instead of `0`, so off-screen tabs can't corrupt the
    geometry; the exact width refines it the instant a tab comes on-screen. This is the "seed-then-refine" the
    original ADR considered and rejected — reinstated because the rejection assumed *every materialised tab
    measures*, which is false for a front tab prepended off the left edge. One `EffectiveWidth` definition is now
    shared by `Layout`, `GrowFront`, and `EvictFront` so the slide re-anchor rebases by the **same** width the
    layout counts (a mismatch there was shoving the selection off-screen after a `GrowFront`).
  - **Re-centre immediately after a reseed.** `TryInitialCentre` centres off the estimate-filled layout rather than
    waiting on a measurement that may never come, so a reseed into an already-visited region no longer sticks the
    selection off-screen.
  - **Follow multi-page flings.** The live body-track fraction is **no longer clamped to ±1** (that clamp assumed
    "one page per gesture"); `TrackView` brackets whichever two tabs the continuous position falls between, so the
    strip scrolls/underlines across every page a fling crosses instead of freezing at the first neighbour. *(This
    supersedes the "clamped ±1 fraction" wording in the drag-lock bullet above.)*
  - **Grow the window during a track.** `EnsureTrackWindow` materialises tabs ahead of a live fling (**grow-only**;
    eviction deferred to the post-commit `MaybeSlide`) so the follow doesn't stutter at the old window edge.
  - **Residual limitations.** (1) The estimate is a *mean*, so off-screen centring is approximate to within a few
    px per unmeasured tab until it scrolls into view — invisible for MMoney's near-uniform month labels, but a
    control with wildly-varying label widths would see brief off-screen drift. (2) Track-window growth is grow-only,
    so a very long single fling temporarily bloats the window until the next commit evicts back to the cap (bounded
    by fling distance; not a leak). (3) **Body-level, not the strip:** a hard flick still flings the `CarouselView`
    several pages by momentum (by design — "commits where it lands"), and the *first* interaction right after a
    cold `dotnet run` launch can settle at position 0 (a MAUI `CarouselView` initial-`Position` timing quirk); a
    normal reopen starts on today correctly. These are `CarouselView` behaviours, out of the strip's scope.
