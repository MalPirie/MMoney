# TabbedPageView: a decoupled, freely-scrollable tab strip (supersedes the StripPager direction)

The `StripPager` (ADR-0002) is being removed. Its central thesis — a label **strip** and a swipeable
**pager** body kept in lockstep off one shared drag fraction — proved too complex and unreliable on device
(see ADR-0002's revert note). We are replacing it with `TabbedPageView`, a generic `Mobiorum.Material3`
control built in smaller, independently-testable pieces. This ADR captures the new control's decisions as
they are made.

> **Status:** accepted; design **validated on device** (2026-06-30). The throwaway spike
> (`MMoney.App/Components/Sandbox/TabStripSpike.cs`, dev-only **Sandbox** nav destination) ended up proving
> the *whole* interaction model, and reversed several decisions below. **Read "Spike outcomes" first — it is
> the authoritative current architecture; the "Decisions" section is kept as the design rationale/history, and
> bullets it supersedes are tagged.** Real `TabStrip<TItem>` implementation still pending.

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

- **A side-aware "return to selected" button.** When the selected tab is scrolled outside the viewport, a
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

- **API surface, and the old `Pager/` seam's disposition.** `TabStrip<TItem> where TItem : struct` exposes
  exactly five props — `.Selected(TItem)` (host-owned source of truth), `.Next`/`.Prev`
  (`Func<TItem, TItem?>`, null at a bound), `.Label(Func<TItem, string>)`, `.OnSelectedChanged(Action<TItem>)`
  (fired immediately on tap). No `.Page(...)` — that is `TabbedPageView`'s later. The `struct` constraint
  stays (the null-neighbour signal needs `Nullable<TItem>`, per ADR-0002). `PagerWindow`/`PagerCell`
  windowing is **kept and reused** (rename to `Tabs/`, `TabWindow`/`TabCell` when the new control lands);
  `PagerGesture` (commit/flick/damp) and `SelectionRoute` (slide/jump) become dead and are removed once
  `TabStrip` replaces them. **`StripPager.cs` is kept in place until `TabStrip` is proven on device**, then
  deleted.
