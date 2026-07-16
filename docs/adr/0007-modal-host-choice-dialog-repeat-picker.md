# Shared `ModalHost` + `ChoiceDialog`, and a two-tier repeat picker

§8's repeat strategy is chosen in two surfaces: a **preset dialog** (an M3 single-choice radio dialog raised from
the Add page's Repeat field) whose "Custom…" option pushes a **`RepeatStrategyPage`** editor. Building these brings
the modal-dialog count to the point ADR-0006 named as the trigger to extract shared plumbing: the `Calendar`
(built), this radio dialog, the imminent §8 **occurrence scope dialog** (This / This-and-following / All — the same
radio shape), and the §7 discard AlertDialog. So we extract now.

## Decision

- **`Mobiorum.Material3.ModalHost` — the generic modal mechanism.** A dimmed scrim + a centred content child +
  dismiss-on-scrim-tap + **hardware-back-to-dismiss** (moved out of `AddTransactionPage`). Props: `IsOpen`, the
  content, `OnDismiss`. It generalises ADR-0006's hand-rolled per-page scrim into one control. The non-modal
  overflow `Menu` (transparent catcher, corner-anchored) stays as it is — `ModalHost` is for centred modal
  dialogs only. `ModalHost` itself is **platform-free**: it keeps a static stack of open dismiss handles and
  exposes `TryDismissTopmost()`; the back press is routed in by the page (below).

- **Back-to-dismiss hooks MAUI's page, not AndroidX's dispatcher.** `Mobiorum.Material3.ModalAwareContentPage` —
  a native `ContentPage` subclass overriding `OnBackButtonPressed` to return `ModalHost.TryDismissTopmost()`,
  surfaced to MauiReactor via `[Scaffold]` — replaces `ContentPage(...)` on any page hosting a `ModalHost`.
  **ADR-0006's `OnBackPressedDispatcher` approach does not work and never did:** MAUI pops the page without ever
  delegating to the dispatcher, so a callback registered there never fires however it is ordered or enabled
  (device-verified: the callback was armed and `HasEnabledCallbacks` true, yet `HandleOnBackPressed` never ran).
  MauiReactor's `ContentPage` exposes no back hook under a `NavigationPage`; the custom-page subclass is the
  workaround MauiReactor documents, and it correctly supersedes ADR-0006's "MauiReactor has no page back hook".

- **`Mobiorum.Material3.ChoiceDialog` — the radio-choice surface.** An optional title, a single-select radio list,
  and Cancel/OK. Presentational: the host supplies the options, the selected index, and the confirm/cancel
  callbacks (the `Menu`/`Calendar` split). Reused verbatim by §8's scope dialog.

- **`Calendar` is refactored onto `ModalHost`.** The hand-rolled scrim + `OnBackPressedDispatcher` code leaves
  `AddTransactionPage`; the Calendar dialog and the new dialogs then share one scrim/back implementation rather than
  drifting. This supersedes ADR-0006's "hand-rolled per page, extract later" stance — and, because the dispatcher
  never worked, it is what makes the Calendar's back-to-dismiss work for the first time.

- **The repeat picker is two-tier (dialog → page), not a dropdown.** The Repeat field opens a `ChoiceDialog` of
  presets — **Does not repeat · Every day · Every week · Every month · Every year · Custom…** — each preset being
  the type's *default* built from the origin date (`Never`, `Daily(1)`, `Weekly(1, origin-weekday)`,
  `Monthly(1, DayOfMonth)`, `Yearly(1)`, all `Forever`). **OK** applies the selected preset; **OK on "Custom…"**
  pushes `RepeatStrategyPage`. A configured custom rule is **pinned as the first radio** (labelled via
  `RepeatDescription`), updated in place, never removed, and **session-only** (not persisted across launches).

- **`RepeatStrategyPage` — a pushed editor (ADR-0005).** Back discards (the §7 discard-confirmation AlertDialog is
  a later slice); a `TopAppBar` **Done** validates and returns `(RepeatStrategy, RepeatEndCondition)` via a props
  callback (the date picker's `OnClosed` pattern). Rows: interval **input field** + unit **`ComboBox`**
  (Day/Week/Month/Year); Weekly → **`DayOfWeekPicker`**; Monthly → **`ComboBox`** of day-of-month / nth-weekday /
  last-weekday; Ends radios **Never / After [N] / On [date]**, the date **tap-to-pick** via `Calendar`.

- **`ComboBox` — an M3 exposed-dropdown, anchored popover.** Outlined trigger + `ArrowDropDown`. On tap the trigger
  measures its own on-screen rect via MauiReactor's **native-control ref** (`new Border(nativeBorder => …)`, then
  Android `GetLocationOnScreen` → DIPs) and reports it; a new **`PopoverHost`** anchors the list directly under the
  field at the field's width. `PopoverHost` converts the screen-space anchor into its own local space using its
  cached on-screen origin — measured with a **retrying dispatched call after layout** (measuring on the render path
  or in `OnSizeChanged` returns null, since the overlay's native view isn't realized yet), cached because the
  page's top-left is stable. It falls back to centring until the origin is known, so the list is never lost.
  (An earlier inline-expand form, and a centred-modal form, were both rejected on the user's feedback.)

- **Origin-relative rules need no rebuild-on-date-change plumbing.** `Monthly(DayOfMonth)` and `Yearly` derive
  their day/month from the transaction date at schedule time, and `RepeatDescription.Describe` takes the origin —
  so changing the Add-page date re-derives the summary and occurrences automatically. Only `Weekly.Days` is stored
  explicitly (its initial default comes from the origin weekday, then it is fixed).

- **Numbers are lenient.** The interval and "after N" fields are plain numeric inputs (no steppers); on **Done**,
  empty / invalid / < 1 coerces to **1** — no page error UI. Weekly keeps ≥ 1 day by making a clear-the-last tap a
  no-op; the end-date calendar floors at the origin. So Done is always valid.

- **Pure logic extracted and headless-tested:** the **Monthly day-in-month options** for an origin (day-of-month
  always; the nth `First`…`Fourth` where `nth = (day-1)/7 + 1 ≤ 4`; `Last` when the origin is its weekday's final
  occurrence, `day + 7 > daysInMonth`), the **preset construction** from an origin, and the **number coercion**.
  `RepeatDescription` gains a public single-option `DescribeMonthlyOption` for the Monthly combo labels. The
  controls themselves are visual and untested, like the existing ones.

## Considered and rejected

- **A dropdown `Menu` for the preset field** (Google's shape) — rejected for the M3 single-choice dialog the user
  chose; it also reuses the scope-dialog surface.
- **Extracting a generic `DateField` now** for the "ends on date" input — deferred (ADR-0006). A stop date is
  low-value to type, so it is **tap-to-pick** straight into `Calendar`; the `DateField` extraction waits for a
  field that genuinely needs manual entry again.
- **Steppers for the numbers** — the user chose plain inputs with lenient coercion.

## Consequences

- `ModalHost` centralises back-handling; a page can host several dialogs, and back always hits the innermost open
  one (the stack is ordered by open, not by declaration). The cost is that a page hosting dialogs **must** root a
  `ModalAwareContentPage` — a plain `ContentPage` silently falls back to popping.
- **MauiReactor trap (device-verified):** a component's plain instance fields do **not** survive re-render —
  MauiReactor builds a new instance each render and migrates only `State`, and `OnMounted` runs once, on the
  first instance. Anything that must outlive a render (here the dismiss handle) belongs in `State`, and any
  closure over a `[Prop]` must be re-pointed each render or it will invoke the discarded instance's prop.
- Dialog open/close motion stays minimal for now (functional-first), tracked with the ADR-0004 menu-animation and
  ADR-0006 calendar-animation follow-ups.
- If the `ComboBox` falls back to inline-expand, only that control changes; the page layout is unaffected.
