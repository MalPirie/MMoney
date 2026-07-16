# A custom M3 date field + Calendar dialog — the app's first modal overlay

The transaction Add/Edit date input moves off MAUI's native `DatePicker` (which opened the OS calendar
dialog) to a **custom, dual-mode date field**: the existing outlined `TextField` for manual typing plus a
trailing calendar button that raises a **hand-rolled M3 `Calendar` dialog**. This is the first *modal* overlay
in the app (the overflow `Menu`, ADR-0004, was non-modal), and it fulfils the "date-picker dialog" follower
that ADR-0004 anticipated.

## Decision

- **Native `DatePicker` retired for transactions.** The OS calendar is Material on Android but uses the system
  accent, not the app's orange seed, and it can't be typed into. We own both paths instead: type or pick.

- **Dual-mode field, app-owned, assembled in `AddTransactionPage`.** The field is the outlined `TextField` in
  text mode (not `.Content()`), canonical **`dd/MM/yyyy`**, with a trailing **`CalendarMonth`** icon button.
  Tapping the *entry* types; tapping the *button* dismisses the keyboard and opens the dialog. The weekday is
  shown as the field's **supporting text** ("Monday"), which naturally yields to the error line. We do **not**
  extract a generic `DateField` yet — §8's "ends on date" will be the second use that justifies it (ADR-0004's
  "don't extract speculatively" discipline).

- **Deterministic digits mask, exact parse.** A numeric keyboard plus a **digits-model auto-mask**: the value is
  up to 8 typed digits reformatted to `dd/MM/yyyy` on every change (backspace/caps handled uniformly, capped at
  8). Because input is deterministic `DDMMYYYY`, the commit parse is an **exact** `dd/MM/yyyy` parse on 8 digits
  (en-GB) — which also validates the real date, so 31/02 is rejected. (This refines the grill's "lenient
  TryParse": leniency only mattered for hand-typed separators, which the mask removes.) Known limitation: pasting
  a non-zero-padded string ("6/7/2026") mis-groups; the calendar is the escape hatch.

- **Live commit, last-good retained.** Each parseable, in-range keystroke commits `Date` immediately (so the
  weekday hint and the calendar seed track live); incomplete/invalid input keeps the last-good `Date` and does
  not nag. Errors follow the page's **validate-on-save + live-clear** pattern (mirrors Amount/Description): a
  bad/empty/below-lock date blocks Save; the error clears once the text parses in range.

- **Edit-lock is the only bound.** `Account.EarliestAllowedDate` is the floor — a manual date below it is an
  error (no commit, blocks Save), and in the calendar those days render disabled with the prev-month arrow
  disabled at the lock's month. **No upper bound** (future/scheduled dates are legitimate).

- **First modal overlay, hand-rolled on the pushed page.** The dialog is a conditionally-rendered full-bleed
  layer in `AddTransactionPage`'s **own** root `Grid` (dim scrim + tap-to-cancel + centred card) — generalising
  ADR-0004's overlay pattern from "shell-only" to *any* page. This is the first overlay with the **dimmed scrim**
  ADR-0004 reserved for modal dialogs. A shared `Dialog`/scrim-host is **deferred to §8** (the discard-changes
  AlertDialog and the scope radio dialog will be the second/third modals that reveal the real shared shape —
  again, not extracted speculatively).

- **Android hardware-back cancels the dialog, not the page.** When the dialog is open, back is intercepted to
  close it (leaving the half-entered transaction intact) rather than popping the whole Add page. If the
  MauiReactor hook proves awkward, we fall back to scrim-tap + Cancel only and note it (as ADR-0004 deferred
  back-to-dismiss for the menu).

  > **Superseded by [ADR-0007](0007-modal-host-choice-dialog-repeat-picker.md).** This shipped against AndroidX's
  > `OnBackPressedDispatcher` and **never actually worked on device** — MAUI pops the page without delegating to
  > the dispatcher, so the callback never fired; the behaviour above was documented but not device-verified. The
  > claim here that "MauiReactor 4.0.15 has no page back hook" is also wrong: MauiReactor documents a custom
  > `ContentPage` subclass overriding `OnBackButtonPressed`, which is what ADR-0007's `ModalAwareContentPage`
  > uses to make back-to-dismiss work for real.

- **`Calendar` is a generic `Mobiorum.Material3` control; the app owns the modality.** Same split as
  `Menu`/`MenuItem`: the library owns the presentational **surface** (month grid, Monday-start single-letter
  header, blank leading/trailing days, `Primary`-filled selected cell / `Primary`-ring today, "July 2026" +
  prev/next arrows, Cancel/OK); the app owns the scrim, open state, placement, and OK/Cancel wiring. Props:
  `SelectedDate`, `MinDate`, **`Today` (injected)**, `OnConfirm(DateOnly)`, `OnCancel`. The **year-list view is
  deferred** — arrows only for now.

- **The `Calendar` control owns its draft and its visible month (transient presentational state).** Per
  ADR-0001 (host owns the committed value, the control owns transient state — like `TextField.Focused` /
  `LabeledToggle.DragX`), tapping a day sets an in-control **draft**, month paging is in-control, and only **OK**
  fires `OnConfirm`. Because the dialog is conditionally rendered, `Calendar` mounts fresh each open and seeds
  its draft from `SelectedDate`, so Cancel/back is simply "unmount without confirming" and the app state stays
  just `Date` + `DateText` + `CalendarOpen` (no draft field).

- **Library stays Core-free.** `Calendar` and its grid maths use only BCL `DateOnly`/`DateTime.DaysInMonth`,
  never `MonthOnly` (a `MMoney.Core` type) — the library keeps its zero dependency on the app.

- **Pure logic extracted and headless-tested.** The **month-grid computation** (`CalendarGrid`, in
  `Mobiorum.Material3`) and the **mask/parse/format** logic (`DateEntry`, in `MMoney.App`) are MauiReactor-free
  and link-compiled into the existing test projects (the `MonthLedger` / strip-seam pattern).

## Considered and rejected

- **Trigger the native `DatePickerDialog` from our own field + button.** Cheapest, and Material on Android, but
  the system accent clashes with the orange seed and we don't control its chrome. The whole point of this
  library is M3 fidelity on our terms (same reasoning as hand-rolling `TabStrip`/`Menu`).

- **A full M3 modal date picker up front** (year-grid view, big hero-header line). Substantial; the arrows cover
  a personal-finance app's needs (you rarely pick dates years out), so the year view is deferred, not built.

- **Extracting a generic `Dialog`/`OverlayHost` now.** Tempting since three modals are coming, but this is a
  sample of one; ADR-0004 says extract when the later dialogs prove the shared plumbing, not speculatively.

- **A `CalendarDraft` in app state.** Placing the draft in `AddTransactionState` works, but the draft is
  transient presentational state and belongs in the control (ADR-0001), which also shrinks the app state and
  makes Cancel a no-op unmount.

## Consequences

- The overlay is clipped to the `ContentPage` bounds and z-ordered manually (fine for a centred modal).
- Open/close **motion is minimal for now** (functional first; a fade/scale enter is a polish follow-up, tracked
  alongside the still-open `Menu` open-animation review from ADR-0004).
- When a generic `DateField` and shared `Dialog` host are extracted at §8, only the app-owned assembly moves;
  `Calendar` (anchor/modality-agnostic) is unaffected.
