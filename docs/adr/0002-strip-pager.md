# Strip + pager (`StripPager`) is a hand-built pan-driven pager, not `CarouselView`

The generic "synced strip + pager" control (`Mobiorum.Material3`) — a horizontal label
**strip** with a moving **underscore** indicator above a swipeable **pager** body, kept in
lockstep — is built as a custom pan-driven windowed pager rather than wrapping MAUI's
`CarouselView`. The Transactions month ledger (`app-design.md` §5) is the first consumer.
The decisions below set the pattern for any later pager-like control, so they are recorded.

## Decisions

- **Windowed pan-driven pager, not `CarouselView`.** Only `Prev`/`Current`/`Next` are
  materialised; a `PanGestureRecognizer` translates the row and `pan.TotalX / pageWidth` is
  a fraction in `[-1, +1]` that drives **both** the page translation and the underscore
  position — that single shared fraction *is* the "in step" coupling. `CarouselView` exposes
  no clean fractional-scroll signal to sync the underscore mid-drag, and its index/extent
  model fights the no-index source below.

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

- **Edges are symmetric and the control assumes no infinity.** A `null` neighbour in the drag
  direction — `Prev` at the back edge (the edit lock) *or* `Next` at a forward edge — produces
  a **damped rubber-band bounce** (resisted drag, springs back, never commits), and the strip
  **pins the active cell at that edge** instead of centring. "Open-ended forward" is merely the
  host's delegate never returning `null` (as `MonthOnly` does); the control supports a finite
  bound in either direction for free. A hard wall was rejected as feeling broken, and the edit
  lock is a real domain boundary worth making *feel* like a firm-but-soft edge.

- **The control owns the vertical scroller; the pan sits on its content.** Each page body is a
  real vertical scroller (native fling/momentum/overscroll), but it is provided by the *control*
  wrapping the host's page content — not by the host. This is forced by Android touch dispatch,
  confirmed on device: a vertical `ScrollView` intercepts vertical moves but passes horizontal
  ones to its children, so a `PanGestureRecognizer` placed *outside* the scroller never sees the
  horizontal drag (the scroller swallows the touch stream — the first on-device build paged not at
  all). Putting the pan on the scroller's **content** lets the platform arbitrate: vertical scrolls,
  horizontal reaches the pan. A secondary **axis-lock** (dominant axis past ~8–12dp) guards diagonal
  drags. Hand-rolling vertical scroll through the same recogniser was rejected (re-creating
  momentum/overscroll is worse UX and far more code). Consequence: `Page` returns scroll *content*,
  not a scroller, so a per-page virtualised `CollectionView` isn't possible here — fine for a month
  ledger (bounded rows), revisit if an unbounded page ever appears. Horizontal overscroll is our
  rubber-band; the inner vertical overscroll stays Android's native edge-stretch (different axes).

- **Host owns selection; the control owns transient state.** `Selected` is the host's single
  source of truth; the control reports commits via `OnSelectedChanged` and re-derives its window
  (the navbar principle from ADR-0001, extended). All transient state — drag fraction, the
  materialised window, in-flight animations — lives in the control's own `Component<…State>`, so
  a finger-drag re-renders only the pager, never the host shell.

- **Selected (committed) vs Current (candidate).** `Selected` is the host-owned committed item:
  always **centred with the underscore fully seated** under it, set only on swipe-completion or
  tap. `Current` is transient control state — the neighbour the in-progress swipe is heading
  toward. While dragging, `Current` is **rendered in the full selected visual state immediately**
  (label colour/weight), *decoupled* from the underscore, which **slides partially** from under
  `Selected` toward `Current` "in step" with the finger. The **strip itself does not scroll during
  a drag** (it stays anchored on `Selected`); the underscore is the only moving element. On commit,
  `Current` *becomes* `Selected` and the strip eases to re-centre it. At a finite edge there is no
  `Current` (no neighbour) — the drag just rubber-bands. This supersedes an earlier "always centre
  the active cell" idea, which with uniform cells would have pinned the underscore stationary —
  the opposite of the required "moves in step".

- **One selection-animation duration governs every path.** A single `SelectionDuration`
  (~200ms, echoing the nav bar's selection pill). Tapping an **adjacent** cell (offset ±1)
  reuses the swipe-commit **slide**; tapping a **distant** cell (|offset| > 1) does **not**
  slide through the intermediate pages — the body **crossfades** while the underscore **glides**
  along the strip, both running **concurrently over the same duration** (fixed, independent of
  jump distance). The underscore never teleports; it is the constant across every selection.

- **Pure logic sits in a MauiReactor-free seam.** Window materialisation + offset tagging, the
  commit decision (`fraction`/velocity/`null`-neighbour → commit-next / commit-prev /
  rubber-back), and tap routing (slide vs jump) are pure functions over the sequence and
  numbers, unit-tested in `Mobiorum.Material3.Tests` (both bounds, including the forward bound
  the `MonthOnly` demonstrator never exercises). Gestures, animation, and rendering are
  on-device manual — they cannot be meaningfully unit-tested.

## Considered and rejected

- **`CarouselView` / paging `ScrollView`** — less code, but no fractional-scroll signal for the
  underscore and an index/extent model that fights the no-index navigable sequence.
- **Single pan recogniser owning both axes (hand-rolled vertical scroll)** — trivial
  arbitration, but re-creating native fling/momentum/overscroll on a long ledger is worse UX and
  much more code.
- **Unconstrained item type via a try-pattern delegate** — keeps reference-type items possible,
  but reads worse at every host call site; the `struct` constraint can be relaxed later if a
  reference-type sequence ever appears.
- **Hard stop at a bound** — simplest, but reads as an unresponsive wall; the damped bounce gives
  honest "nothing more here" feedback at a real domain boundary.
