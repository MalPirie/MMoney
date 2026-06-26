# Strip + pager (`StripPager`) is a hand-built pan-driven pager, not `CarouselView`

The generic "synced strip + pager" control (`Mobiorum.Material3`) — a horizontal label
**strip** with a moving **underscore** indicator above a swipeable **pager** body, kept in
lockstep — is built as a custom pan-driven windowed pager rather than wrapping MAUI's
`CarouselView`. The Transactions month ledger (`app-design.md` §5) is the first consumer.
The decisions below set the pattern for any later pager-like control, so they are recorded.

> **Reverted 2026-06-26 — native drive abandoned, back to pure MVU.** On-device, the native-drive design
> below desynced MAUI's pan-gesture state: translating the very view that owns the pan with no re-render made
> the platform **drop terminal events (Completed/Canceled) on fast flicks**, freezing the body mid-swipe (and
> a `PointerGestureRecognizer` fallback fired spuriously on fast motion; a timer watchdog couldn't tell a lift
> from a pause). Crucially, the stutter that motivated the native drive turned out to be **page weight** (the
> host's 30-row non-virtualised `MonthPage` × 3), not the render mechanism — so the redesign solved the wrong
> problem and introduced a worse one. The control was reverted to **pure-MVU** drive (translate via per-frame
> `SetState`), which keeps gesture state in sync and makes terminal events reliable. Kept from the redesign:
> the windowed release-velocity fix (single-event velocity was too noisy and committed mid-drag), the growing
> strip window (`StripRange`, browse loads more), per-cell `StripScroll` so taps land after a browse, and
> `.WithKey` page reuse. The native-drive decisions below are retained for the record but are **not** how the
> control currently works; see `Mobiorum.Material3/Controls/StripPager.cs` (search "AREA OF INTEREST") and
> `scripts/` for the build/deploy/log loop used to investigate. Open question handed back to the user: why does
> the control **remount on commit** (render counter resets, `OnMounted`/`OnWillUnmount` fire), and is that
> implicated in the residual gesture flakiness?
>
> **Revised 2026-06-26 — native-drive redesign.** The original build tracked the finger by
> re-rendering the MVU tree every frame, which **stuttered on a real device** with heavy pages
> (the "OPEN: live-peek performance" investigation, now resolved below). The control now
> finger-tracks the gesture by mutating **native control transforms directly** and only touches MVU
> state at rest points; commit/jump tweens stay declarative. That change reversed three of the
> original decisions (underscore motion, strip anchoring, and the active-cell-centring question) and
> dropped the MVU-era per-frame mitigations; the superseded versions and the reasons are kept in
> **Superseded earlier decisions** at the end so the history isn't lost.

## Decisions

- **Windowed pan-driven pager, not `CarouselView`.** Only the items around `Selected` are
  materialised; a `PanGestureRecognizer` drives the motion and `pan.TotalX / pageWidth` is a
  fraction in `[-1, +1]` that drives **both** the body translation and the strip translation —
  that single shared fraction *is* the "in step" coupling. `CarouselView` exposes no clean
  fractional-scroll signal to sync the strip mid-drag, and its index/extent model fights the
  no-index source below.

- **Source is a navigable sequence, no index and no count.** The host supplies `Selected`
  plus `Next`/`Prev` step delegates; the control steps out from `Selected` to materialise its
  window. There is no integer index, no item count, and **no equality requirement on the item
  type** — each materialised strip cell is *offset-tagged* at creation (`Current` = 0, its
  neighbours ±1, …), so a tap already knows its distance and direction.

- **Generic, constrained `where TItem : struct`.** "No neighbour" is signalled by
  `Func<TItem, TItem?>` returning `null`. For that `TItem?` to mean `Nullable<TItem>` (and so
  be able to hold `null`) the item must be a value type. Every realistic navigable-sequence
  item is a value object (`MonthOnly`, a date, an index), and relaxing a constraint later is
  non-breaking. The unconstrained try-pattern (`Func<TItem,(bool,TItem)>`) was rejected: it
  reads worse at every host call site. Verified the `[Prop]` source generator and MauiReactor
  instantiation work on an open generic `StripPager<TItem> : Component<StripPagerState<TItem>>`.

- **Native drive: MVU owns discrete state, native transforms own continuous motion.** Every
  MauiReactor `SetState` re-renders the whole control, and MauiReactor offers no memo /
  `ShouldRender`, so finger-tracking the body by re-rendering per frame saturates the UI thread
  and the gesture stalls (observed on device: the fraction advanced smoothly in logs while the
  screen froze). Instead the control captures the **native `MauiControls.View`** of the body
  track, the strip container, and the control root (the `Action`-ref pattern,
  <https://adospace.gitbook.io/mauireactor/components/accessing-native-controls>) and during a
  drag mutates their `TranslationX` / `ScaleX` **directly** — no `SetState`, no rebuild. MVU
  state changes only at **rest points** (an axis-lock, the release handoff, a settle), where one
  `SetState` records the resting declarative value (which equals the native value), so the next
  render reconciles cleanly. The rule: *native = finger-tracked motion, MVU = committed state and
  fixed tweens.*

- **Three pages, stacked then transformed, never clipped, current on top.** The body renders
  `Prev`/`Current`/`Next` as three full-width hosts in **one `Grid` cell** — so all three are
  laid out in-bounds and **rasterised at least once** — and `TranslationX` (a post-layout
  transform, not a layout position) then spreads them to `-width / 0 / +width`. The body is
  **not** clipped and `Current` is z-ordered **on top**. Rendering all three (not just the
  dragged-toward neighbour) costs nothing per frame now that the drag is native, and it makes a
  **direction reversal mid-drag smooth** — the opposite neighbour is already present and drawn.
  This *stacked-then-transform* order is the deliberate fix for the draw wall the first native
  attempt hit (see **Spike risks**): laying a host out **directly** off-screen left it blank;
  laying it out stacked at `x = 0` and *then* transforming it out is the bet that it stays drawn.

- **The control owns the vertical scroller; the pan sits on its content.** Each page body is a
  real vertical scroller (native fling/momentum/overscroll) provided by the *control* wrapping
  the host's page content — not by the host. This is forced by Android touch dispatch, confirmed
  on device: a vertical `ScrollView` intercepts vertical moves but passes horizontal ones to its
  children, so a `PanGestureRecognizer` placed *outside* the scroller never sees the horizontal
  drag. Putting the pan on the scroller's **content** lets the platform arbitrate: vertical
  scrolls, horizontal reaches the pan. A secondary **axis-lock** (dominant axis past ~8–12dp)
  guards diagonal drags; once horizontal locks, the inner scroller drops to `Orientation=Neither`
  for the rest of the gesture (one `SetState`, not per-frame). Consequence: `Page` returns scroll
  *content*, not a scroller, so a per-page virtualised `CollectionView` isn't possible here — fine
  for a month ledger (bounded rows), revisit if an unbounded page ever appears.

- **Live peek via gesture-distortion reconstruction (unchanged by the native drive).**
  Translating the body — natively or otherwise — moves the pan recogniser's own view, which
  *shrinks* MAUI's reported `TotalX` (a feedback loop; verified on device: a full-width swipe
  registered as ~half). The true offset is recovered as `TotalX + alreadyApplied`, so the control
  tracks the finger 1:1. The same reconstruction lets the **strip be browsed sideways**
  independently (its container translates, and its own pan would self-distort too).

- **The strip scrolls in proportion to the pan; the underscore is just selected styling.**
  During a drag the strip container translates so that at `fraction = ±1` the committed-toward
  cell is **centred** — body and strip move together off the one shared fraction. The underscore
  is **no longer an independently animated element**: it is part of the *selected cell's* styling
  (weight/colour + bar) and rides with the strip. Selection changes **on commit**, so the
  underscore **hops one cell** at the swap (the old selected cell is one slot off-centre when the
  new one lands centred). The hop is **accepted** for now (it may be animated later); pinning the
  underscore to screen-centre to avoid it was considered and deferred. This reverses the original
  "underscore is the only moving element / strip stays anchored" design (see **Superseded**).

- **Edges stretch the whole control, not just the body.** A `null` neighbour in the drag
  direction — `Prev` at the back edge (the edit lock) *or* `Next` at a forward edge — is a finite
  bound. Instead of sliding the body aside to reveal a gap, the drag drives a **damped `ScaleX`
  on the control root** (strip *and* body together), anchored at the trailing edge, off
  `PagerGesture.DampOverscroll`; release springs it back (`ScaleTo(1)`), no commit, no selection
  change, no underscore hop. Scaling the whole control reads as one elastic card hitting a wall
  and retires the separate "strip damps / body stretches" special case. The scale is capped low
  (~1.03–1.05) so strip labels flex rather than visibly warp. A hard wall was rejected as feeling
  broken; the edit lock is a real domain boundary worth making *feel* like a firm-but-soft edge.

- **Native drive is for the finger only; commit/jump stay on the MVU tween.** The native drive
  exists to kill the *per-frame, finger-tracked* rebuild — that, not animation, was the stutter.
  Commit, tap-jump, and strip re-centre are **fixed tweens** (one `SetState` sets the target,
  `WithAnimation` runs it over one `SelectionDuration`): they were never finger-tracked, so they
  never stuttered, and keeping them declarative is what makes the **commit swap seamless** (the
  render that shifts the window also resets `Fraction` to 0, so the committed cell lands centred
  with no native leftover to reconcile). The handoff at release **syncs the native drag value back
  into `State.Fraction`/`StripScroll`** (declared now equals native, no jump), primes
  `WithAnimation` for one frame, then tweens to the commit target and does the discrete swap.
  Consequently the `BodyAnimating`/`StripAnimating` gating flags and the one-frame priming step are
  **retained** — a native `TranslateTo` commit was considered but rejected: animating natively then
  swapping via MVU reintroduces a native⇄declared mismatch at the swap frame (a one-frame flash)
  that the declarative tween avoids for free. Release velocity is sampled across pan updates with a
  `Stopwatch` (the args carry no timestamp). The finite-edge spring-back has no swap, so it *does*
  animate natively (`ScaleTo(1)`).

- **Overlapping settles are cancelled by a generation counter.** Each commit/jump/recentre bumps
  a generation; a stale async continuation (e.g. from a second swipe landing mid-settle) checks the
  generation and bails, so rapid input always lands cleanly centred.

- **Edges are symmetric and the control assumes no infinity.** "Open-ended forward" is merely the
  host's delegate never returning `null` (as `MonthOnly` does); the control supports a finite bound
  in either direction for free, and the same stretch handles both.

- **Tap routing: adjacent slides, distant crossfades.** A single `SelectionDuration` (~220ms,
  echoing the nav bar's selection pill) governs every path. Tapping an **adjacent** cell (offset
  ±1) reuses the swipe-commit **slide** (same native `TranslateTo`). Tapping a **distant** cell
  (|offset| > 1) does **not** slide through the intermediate pages — the body **crossfades** while
  the strip **scrolls** the target to centre, both over the same duration. The strip never jumps;
  it always scrolls to re-centre.

- **The strip scrolls independently; selection always re-centres.** The strip container is a
  controlled coordinate, not a native `ScrollView` — the user can browse it sideways (its own pan,
  driven natively the same way), and any selection change (swipe-commit, tap-slide, or tap-jump)
  animates it back so the selected cell re-centres.

- **Host owns selection; the control owns transient state.** `Selected` is the host's single
  source of truth; the control reports commits via `OnSelectedChanged` and re-derives its window.
  All transient state lives in the control's own `Component<…State>`, so a commit re-renders only
  the pager, never the host shell. With the native drive, `State` shrank to the resting essentials
  (`PageWidth`, `HorizontalLock`, the jump's `BodyOpacity`); the continuous values (`Fraction`,
  strip offset, root scale) live as native transforms during a gesture, not in `State`.

- **Pure logic sits in a MauiReactor-free seam.** Window materialisation + offset tagging, the
  commit decision (`fraction`/velocity/`null`-neighbour → commit-next / commit-prev / rubber-back),
  the overscroll damping, and tap routing are pure functions over the sequence and numbers,
  unit-tested in `Mobiorum.Material3.Tests` (which link-compiles only `Controls\Pager\*.cs`, so the
  suite runs headless on plain net10.0 with no MAUI workload). **The native-drive layer is not
  unit-testable** — gestures, native transforms, and on-device draw are manual/device only.

## Spike risks (must be confirmed on a device)

The redesign is sound on paper but rests on two MAUI behaviours that can only be settled with a
finger on glass. Both were the rocks the *first* native attempt (reverted) ran aground on.

- **Off-screen draw.** The whole approach assumes a host **laid out in-bounds (stacked at `x = 0`)
  and then transformed to `±width`** keeps painting when translated back on-screen. The reverted
  attempt laid hosts out *directly* off-screen (`TranslationX(±width)` from rest) and MAUI **never
  drew** their content (blank peek). Dropping `IsClippedToBounds` plus the stacked-then-transform
  order is the bet that fixes it — but the original failure looked like **culling**, not clipping,
  so no-clip alone may be insufficient. **If the off-screen host still draws blank**, fall back to
  materialising the entering neighbour at the screen edge on the zero-crossing (one `SetState` per
  sign flip).

- **Native ⇄ MVU reconciliation.** MauiReactor diffs declared values between renders and only
  writes a native property when the *declared* value changed. Because the drag mutates native
  transforms **without** a render, the control must **reset the native transform itself** at each
  rest point (it can't rely on a no-op declared diff to do it), and the swap must shift content and
  reset the transform in the *same* frame to stay seamless. The scaled root also scales pan
  coordinates during an edge stretch; the scale is ≤5% and we never commit at an edge, so the
  feedback into the recogniser is negligible — recorded here as a known small effect.

## Superseded earlier decisions (MVU-era, kept for the rationale)

- **~~Underscore is the only moving element; the strip stays anchored during a drag.~~** Originally
  the strip held still on `Selected` and the underscore *drifted* toward the candidate "in step"
  with the finger, with the drift cancelling the strip re-centre on commit so the underscore stayed
  perfectly centred through the swap (no hop). Replaced because the new model moves the **strip**
  with the finger and folds the underscore into ordinary selected-cell styling; the hop at the swap
  is the accepted cost.

- **~~"Always centre the active cell" was rejected.~~** The MVU design argued that centring the
  active cell would pin the underscore stationary — "the opposite of the required *moves in step*."
  The redesign **embraces** a centred active cell: the strip scrolls so the committed cell lands
  centred, and a stationary-ish underscore is now fine because it's just styling, not the motion
  cue (the moving strip is).

- **~~Materialise only the dragged-toward neighbour + coalesce drag re-renders.~~** Two MVU-era
  mitigations for per-frame cost (render one neighbour, throttle `SetState` to a frame budget).
  Obsolete now that the drag does no `SetState` at all; all present neighbours are rendered once and
  moved natively. (The one-frame `WithAnimation` priming + `BodyAnimating`/`StripAnimating` flags are
  **kept** — see the commit decision above — because the commit/jump tweens are still declarative.)

## Considered and rejected

- **`CarouselView` / paging `ScrollView`** — less code, but no fractional-scroll signal for the
  strip and an index/extent model that fights the no-index navigable sequence. A native horizontal
  `ScrollView` body (the "Path A" spike) was also weighed: smooth and scalable, but it has no
  paging/scroll-ended/velocity events, so the precise 0.5 point-of-no-return and flick-to-commit
  would degrade to "snap to nearest page." The native-drive design keeps the precise commit.
- **Single pan recogniser owning both axes (hand-rolled vertical scroll)** — trivial arbitration,
  but re-creating native fling/momentum/overscroll on a long ledger is worse UX and much more code.
- **Unconstrained item type via a try-pattern delegate** — keeps reference-type items possible, but
  reads worse at every host call site; the `struct` constraint can be relaxed later.
- **Hard stop at a bound** — simplest, but reads as an unresponsive wall; the damped whole-control
  stretch gives honest "nothing more here" feedback at a real domain boundary.
- **Pin the underscore to screen-centre to avoid the commit hop** — clean and hop-free, but it
  decouples the underscore from "selected cell styling" and adds a special case; deferred in favour
  of the simpler "underscore is just styling, accept the hop" until the hop proves objectionable.
