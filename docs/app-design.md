# MMoney — App Design

The design for the MMoney front-end (MauiReactor, Material 3, orange) and the reusable
`Mobiorum.Material3` library it sits on. Captures decisions reached in design discussion; the next step is
turning this into implementation tasks. Read alongside [core-architecture.md](core-architecture.md) (the
ledger core this binds to) and [CONTEXT.md](../CONTEXT.md) (domain glossary).

**Status:** design only — no code yet. **Parked:** CSV export, printing, and an on-device **colour review**
(the dark-mode chrome especially is provisional).

---

## 1. Projects and the library/app split

- **`Mobiorum.Material3`** — generic, seed-agnostic M3-styled controls + the colour/scheme system. Knows
  nothing about MMoney.
- **`MMoney.App`** — domain composites and screens; references the library and supplies the seed `#FFA500`
  plus all MMoney-specific layout and Core wiring.

A control is **generic** if it's reusable with no MMoney knowledge; **specific** if it touches MMoney's types
or domain layout.

| Generic — `Mobiorum.Material3` | Specific — `MMoney.App` |
|---|---|
| Top app bar (overflow menu, back arrow), Navigation Bar, FAB | Balance banner |
| List item (incl. **leading accent-bar** slot), card | Ledger row, repeating-list row |
| Dialogs: AlertDialog, radio-choice dialog, date-picker dialog | Add/Edit page, Repeat-strategy page, Settings page |
| Snackbar (with action) | Month-scroller configuration |
| **Labelled/coloured toggle** (on/off text + colours) | Income/expense sign logic, edit-scope logic |
| **Synced strip + pager** control | Splash config + Android resources |
| Scheme/token system incl. **extended colours**; system-bar-colour helper | Core command wiring |

---

## 2. Colour and theming

- **Seed:** `#FFA500`, with a **chroma-preserving** M3 scheme variant (not the muted default) for maximum
  orange. Both **light and dark** schemes generated from the seed.
- **Theme toggle:** **tristate** — System / Light / Dark. Persisted (MAUI `Preferences`); applied via
  `Application.UserAppTheme`.
- **`primary` is the brand chrome**, used and **theme-adaptive** across: the **banner**, **page headers**
  (app bars), the **Android status bar**, and the **splash**.
  - Primary-coloured app bars are a deliberate step away from M3's default surface app bars — accepted.
  - **Dark mode ⇒ light-orange chrome** (M3 dark `primary` is light, tone ~80, with dark `on-primary`).
    **Provisional — to review on device.**
  - Status-bar **icon brightness flips** per theme for contrast.
  - **Splash:** a single fixed colour (light-mode deep orange); it will *not* match in dark mode — accepted
    (MAUI's splash is one static colour).
- **Banner orange:** white text on `primary`, with `primary` pushed to the most orange tone that still clears
  **4.5:1** against white (~HCT tone 48, ≈ `#B35A00`) — small labels on the banner force 4.5:1.
- **Extended semantic colours** (outside the orange palette), each with **light/dark** variants tuned for
  **text contrast on surface**:
  - **Income green** and **expense red** — *custom*, deliberately distinct from M3's `error` red so an
    expense never reads as a validation error.
  - **Grey** (future-income bar) reuses a **neutral role** (≈ text, e.g. `onSurfaceVariant`) — not custom.
  - These drive: the income/expense toggle's on/off colours, the signed amount text, and the income bar.

---

## 3. Navigation / shell

- **Root shell:** persistent **banner** (top) · swappable **central area** · bottom **Navigation Bar**
  (destinations: **Transactions**, **Repeating**) · floating **FAB** (bottom-end, hovering above the bar —
  *not* docked). **Only the central area swaps**; banner, nav bar, and FAB persist.
- **Pushed pages** (own back-arrow app bar, cover the shell): **Add/Edit transaction**, **Repeat strategy**,
  **Settings**.
- **Mechanism:** MauiReactor push-navigation for pushed pages; an in-shell content swap for the two
  destinations (the nav bar is our own control, not MAUI Shell tabs).
- The **FAB** runs **one unified add flow**, identical on both tabs (so adding from Repeating starts as "Does
  not repeat" until a repeat is set — accepted).
- A **secondary FAB** is planned, placed **to the left of the primary FAB** in the same bottom-end add-flow
  region. Both FABs are arranged by the shell (the generic `Fab` control is placement-agnostic); a secondary
  FAB is an M3 colour/size variant added fluently when needed (`.Role(...)` / `.Size(...)`).

---

## 4. Banner

Today-anchored and **static** (the month scroller never changes it). Amounts are **uncoloured** (`on-primary`).

- **Hero (large)** = `BalanceOn(Today)` — current balance.
- **Available** = `BalanceOn(endOfCurrentMonth)` — projected month-end.
- **Pending** = `Available − Hero`. (So `Available = Hero + Pending`.)
- **Overflow menu:** Export CSV *(parked)*, Print *(parked)*, Settings.

---

## 5. Transactions (month ledger)

- Built on the **synced strip + pager** (`Mobiorum.Material3`): a horizontal month strip driving a paged
  content area; tap-strip and swipe-page stay in lockstep.
  - Item source is a **navigable sequence**: `Selected` + `Next`/`Prev` (null at bounds). MMoney supplies
    `MonthOnly`, `Next = m => m.Add(1)`, `Prev = m => m == editLockMonth ? null : m.Add(-1)` (open-ended
    forward, bounded back at the edit lock).
- Per month: `Account.GetMonth(month)` → `LedgerEntry` rows, **day-grouped**.
- **Sort: date descending** (latest day on top), then **sequence descending** within a day. Implemented by
  **reversing** `GetMonth`'s ascending output — balances are computed ascending (correct), only display is
  reversed (presentation lives in the app). The carried-balance anchor lands at the **bottom**.
- **Ledger row:** `[income accent bar] description (+ trailing repeat icon) … signed amount / balance below`.
  - **Repeat icon** on occurrences (`Kind == Occurrence`), **trailing/inline**; description **wraps + truncates**
    and the icon may be clipped away — acceptable.
  - **Income accent bar** (amount > 0 only): **green if date ≤ today, grey if > today** (received vs upcoming).
    Expenses get no bar.
  - **Amount colour:** income green / expense red.

---

## 6. Repeating tab

- Source: **upcoming sequences** — sequence + next-due, **filtered** to those with an occurrence on/after
  today (no-upcoming ones are filtered out), **sorted by next-due ascending** (soonest on top).
- **Row:** description · recurrence summary (`RepeatDescription`) · signed amount · **next-due** ("next: 5 Jul").
- Tap → edit the **origin / whole series** (see §8).

---

## 7. Add/Edit transaction page

- **Fields:** Date (defaults to **today**, ≥ edit lock) · **Amount** (unsigned magnitude — any minus is
  ignored) · Description · **Repeat** field (shows summary/placeholder via `RepeatDescription`; opens the
  strategy page) · **income/expense toggle** (the labelled/coloured toggle; **defaults to Expense**; decides
  the sign).
- **App bar:** back arrow + **Save**.
- **Sign:** `signed = isIncome ? +magnitude : −magnitude`. On edit, derive toggle + magnitude from the stored
  sign.
- **Save:** **validate on save** — magnitude > 0, description non-blank, date ≥ edit lock — surfaced as **M3
  field errors** (error-coloured supporting text). The Core guards remain the backstop.
- **Back when dirty:** M3 **AlertDialog** ("Discard changes?" — Discard / Keep editing); clean ⇒ just pop.
- **Overflow (editing):**
  - **Delete:** confirm AlertDialog (transaction summary) → remove → pop to home → **Snackbar with Undo**.
    Undo **creates new**, not a true reversal — consequences: an occurrence-skip undo becomes a *detached
    one-off*; a truncation/whole-series undo recreates a *new* sequence; sequence numbers change. Acceptable.
  - **Copy:** date-picker dialog (OK relabelled **Copy**) → always a **one-off** on the chosen date.

---

## 8. Repeat strategy page

Single page, modelled on **Google Calendar**, editing our types directly. Maps ~1:1 onto the Core:

| Google Calendar | Core |
|---|---|
| Does not repeat | `Never` |
| Daily / every N days | `Daily(Interval)` |
| Weekly on «days» / every N weeks | `Weekly(Interval, DaysOfWeek)` |
| Monthly on day N / on the «nth» «weekday» | `Monthly(Interval, DayInMonth.DayOfMonth / First…Fourth / Last)` |
| Annually / **every N years** | `Yearly(Interval)` *(Interval is a Core addition — see §10)* |
| Ends: Never / On date / After N | `Forever` / `UntilDate` / `AfterOccurrences` |

The **end condition** lives on this page. Up to "Fourth"/"Last" only (no "Fifth"); `DayOfMonth` clamps to
month length.

### Editing a sequence — two paths

- **From the Repeating tab → the origin → whole series.** Applies from `max(origin, editLock)`: a true
  in-place change when the origin is editable, or a **split at the edit lock** when the origin is in a closed
  month (the locked past stays; the `GetSequences` "completed-before-lock" filter retires the truncated
  remnant). The origin **date is editable** (a clamped re-anchor).
- **From a ledger occurrence → that occurrence,** with a **scope dialog** (radio: This / This-and-following /
  All — *as appropriate*: if the occurrence is the origin, This-and-following collapses into All; a **date**
  change is **this-occurrence only**). Cancel + a context-labelled action button (Delete / Change).

**Scope → Core:**

| Scope | Change (amount/desc/strategy) | Delete | Date |
|---|---|---|---|
| This | `ChangeTransaction*` | `RemoveTransaction` (skip) | `ChangeTransactionDate` (move) |
| This & following | `ChangeSequence*(from = occurrence)` (split) | `RemoveSequence(from = occurrence)` | `ChangeSequenceDate(from = occurrence, newDate)` |
| All | `ChangeSequence*(from = origin)` (in-place) | `RemoveSequence(from = origin)` | `ChangeSequenceDate(from = max(origin, lock), newDate)` |

A single-occurrence **move onto a date already held by the same sequence** is **rejected** (the Core throws);
the UI surfaces the refusal.

---

## 9. Settings page

- **App bar:** back arrow.
- **Version / SHA / build date:** version + **SHA (debug builds only)** via **SourceLink** (read back from
  `AssemblyInformationalVersion`); **build date** via a small **MSBuild target** writing an
  `[AssemblyMetadata("BuildDate", …)]` attribute (deliberately non-deterministic — accepted). Read through a
  small `AppInfo` accessor (`Version`, `CommitSha?`, `BuildDate`).
- **Theme toggle:** tristate (System / Light / Dark), persisted.

---

## 10. Core changes this app needs

All additive; the event log stays a stable contract.

1. **`Yearly` gains an `Interval`** — to represent "every N years" (parity with the other strategies). Small
   additive change to the serialized shape.
2. **`ChangeSequenceDate(transaction, fromDate, newDate)`** — a thin, atomic re-anchor: validates both dates
   `≥ edit lock` up front, then emits `SequenceRemoved(origin, number, fromDate)` + `TransactionAdded(newDate,
   …)` carrying the rule's fields. Composes existing events; always mints a new sequence.
3. **An upcoming-sequences accessor** — e.g. `GetUpcomingSequences(asOf)` returning *(sequence, next-due)*,
   filtered to those with an occurrence on/after `asOf` and sorted by next-due. Keeps scheduling in the Core
   rather than re-hosting `RepeatScheduler` in the app.

---

## 11. Parked / to review

- **CSV export** and **printing** (banner overflow) — not yet designed.
- **Colour review on device** — the dark-mode chrome (light-orange banner/headers/status bar), the banner
  orange tone, and the income-green / expense-red.
- The unified-FAB default on the Repeating tab ("Does not repeat") — left as-is.
- **FAB press elevation** (M3 raises the FAB Level 3 → Level 4 on press) — deferred. Approach when revisited
  (likely alongside the secondary FAB): track a pressed state in the `Fab` control via pointer-pressed/released
  gestures and swap the Level 3↔4 shadow. **Verify first** that MauiReactor tweens a composite `Shadow`
  (its `.WithAnimation()` reliably tweens scalars like `Scale`/`Opacity` — the nav-bar pill proves that path —
  but a `Shadow` object may be swapped wholesale and pop rather than glide). Fallback: a small `Scale` bump on
  press, which is guaranteed to animate but is less M3-correct.
