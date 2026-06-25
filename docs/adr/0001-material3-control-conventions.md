# Mobiorum.Material3 control conventions

When extracting the first controls out of `MMoney.App` into the `Mobiorum.Material3`
library (the Navigation Bar and FAB), we set the conventions that every later control in
the library table (top app bar, list item, card, dialogs, snackbar, toggle, pager) will
follow. The cost of changing them grows with each control added, so they are recorded here.

## Decisions

- **Controls are fluent MauiReactor `Component` subclasses**, not static factory methods
  returning `VisualNode`. Factories grow unwieldy as parameters and child slots multiply;
  components give named fluent setters and can hold their own interaction/animation state.
- **Fluent setters are generated, not hand-written.** Control props are fields annotated with
  MauiReactor's `[Prop]` source-generator attribute (the class is therefore `partial`); the
  generator emits the fluent method (`_icon` → `.Icon(...)`). Hand-rolled setter methods are
  avoided. The generator targets components only, so plain config objects that are *not*
  components (e.g. `NavDestination`) keep manual fluent setters.
- **Controls read `MaterialScheme.Current` internally** rather than taking a `scheme`
  parameter — callers never thread colours through.
- **Selection is reported per item via callbacks, not via an index.** Each
  `NavDestination` carries its own `Selected` flag and `OnSelected` action; the
  `NavigationBar` is purely presentational and owns no selection state. This matches the
  MAUI `ToolbarItem` / Shell `TabBar` idiom (per-item command, no index) and keeps the
  single source of truth in the app (`ShellState.Tab`), from which each `Selected` is
  derived.
- **Item descriptors are fluent too.** `NavDestination` takes its stable identity
  (`Icon`, `Label`) in the constructor and its varying state (`Selected`, `OnSelected`)
  via fluent setters — keeping the whole call site in one voice and avoiding a positional
  `bool`/`Action` trap. It is a plain config object, not a `Component`.
- **Layout/placement stays in the app, not the control.** The control is
  margin/alignment-agnostic; the shell positions it. The FAB's hover-over-the-bar offset
  lives in `ShellPage`, and the Navigation Bar exposes an `Arrangement(Start | Fill)` knob
  (default `Fill`, the M3 even distribution) so the app can pack destinations to the start
  and leave the trailing gap for the floating FAB — without the bar ever knowing a FAB
  exists.
- **Elevation is a static `Elevation` token, not scheme-dependent.** M3 levels map to MAUI
  `Shadow` (navigation bar = Level 2 / 3dp, FAB = Level 3 / 6dp). M3's `shadow` colour role
  is pure black in both themes, so a single translucent-black shadow is shared across
  light/dark for now; refining dark-mode elevation (which M3 conveys largely through tonal
  overlay) is folded into the parked on-device colour review.

## Considered and rejected

- **Static factory methods** — simpler for one control, but they do not scale to the
  parameter counts and child slots of the later controls (list item with accent-bar slot,
  dialogs, pager).
- **Index-based selection (`SelectedIndex` + `OnSelected(int)`)** — fewer types, but
  couples the control to a numeric model the destinations do not naturally have, and makes
  the control a holder of selection state that actually belongs to the app.
- **Per-theme shadows** — more faithful to M3 dark elevation, but building them blind
  (before the on-device colour review) is guesswork; deferred.
