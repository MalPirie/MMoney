# MMoney.Core — Architecture

How the ledger core works, the invariants that hold it together, the decisions behind it, and where it's
likely to change. Read alongside:

- **[CONTEXT.md](../CONTEXT.md)** — the domain glossary (Account, Sequence, Occurrence, edit lock, projection floor, …).
- **[model-b-read-model.md](model-b-read-model.md)** — the deep dive on the read model and the design session that produced it.

`MMoney.App` is, at the time of writing, still the stock MauiReactor template — none of the architecture below
is consumed by a UI yet. The read API (`GetMonth`, `BalanceOn`, `GetSequences`) is the surface it will bind to.

---

## 1. The shape in one picture

```
            commands (AddTransaction, CloseMonth, …)
                              │
                              ▼
        ┌──────────────────────────────────────────┐
        │                Account                    │   event-sourced aggregate
        │  validates → emits AccountEvent → Apply    │
        └───────┬───────────────────────┬───────────┘
                │ NewEvent (per event)   │ Apply(event)
                ▼                        ▼
   AccountPersistenceService     TransactionCollection        the read model
   (append NDJSON per account)   (sparse overlay; projects     (internal)
                                  occurrences; computes
                                  balances on read)
                                          │ uses
                                          ▼
                                   RepeatScheduler            the repeat engine
                                   (all date maths)

   AccountManager  ── owns ──▶ the set of Accounts, a TimeProvider, and the
                               ignore-closed-months toggle; wires each Account's
                               NewEvent to the persistence service.
```

Two ideas carry everything:

1. **The event log is the source of truth.** `Account` state is rebuilt by replaying `AccountEvent`s in
   order. Commands validate, emit an event, then apply it; `NewEvent` fires per event so persistence can
   append it. Reconstruction is deterministic and side-effect-free.
2. **Reads are projected, not stored.** Repeating occurrences and running balances are computed on demand
   over a *sparse overlay* of facts and overrides. Nothing is eagerly materialised.

---

## 2. The event-sourcing spine (`Account`, `AccountEvent`, codec)

- **`AccountEvent`** is a discriminated union (abstract record + sealed nested records) serialized as
  newline-delimited JSON via the `$type` discriminator (`AccountEventCodec`). Variants: `NameSet`,
  `TransactionAdded`, `TransactionAmountChanged`, `TransactionDateChanged`, `TransactionDescriptionChanged`,
  `TransactionRemoved`, `MonthClosed`, `SequenceRemoved`, `SequenceAmountChanged`, `SequenceDescriptionChanged`.
- **`Account`** is the only aggregate. Each command method: validates → `Update(event)` →
  `NewEvent?.Invoke` then `Apply(event)`. `Apply` is a switch that translates each event into read-model calls.
- **Replay** runs the same `Apply` path with no events emitted. The constructor takes the event list and an
  `ignoreMonthClosed` flag.

Command methods are deliberately thin; the interesting state lives in the read model. The split is the
event-sourcing seam: commands decide *what happened*, `Apply` decides *how state changes*.

---

## 3. The read model (`TransactionCollection`, internal)

This is the heart; [model-b-read-model.md](model-b-read-model.md) covers it in full. In brief:

- **Partitions** (per-month lists) hold only **facts** (one-off transactions, carried-balance anchors) and
  **overrides** (`Tombstone` = a skipped/moved-away occurrence; `Stored` = an individually-edited or moved-in
  occurrence). **Sequences** (the repeat rules) live in a registry. Unedited occurrences are **projected on
  read**, never stored.
- **A month view** = overlay the partition's facts/overrides onto the projected occurrences for that month,
  then stamp a running balance (`GetMonth` → `LedgerEntry`s).
- **Balances are computed**, never stored. `BalanceOn(asOf)` sums effective transactions from the earliest
  content month up to `asOf`. The **carried-balance anchor** written at each month-close is the checkpoint
  that bounds this cost (see §5).
- **The read model owns the projection floor and the edit lock.** It holds `ignoreMonthClosed` + the
  `EarliestAllowedDate` and derives the projection floor internally; callers pass neither (see §6 decision).

---

## 4. The repeat engine (`RepeatScheduler` + the DUs)

- **`RepeatScheduler`** is the single deep module for all repeat date maths. Public interface:
  `DatesForMonth`, `NextOnOrAfter`, `EndDate`. Internally it dispatches a `RepeatStrategy` to a private
  per-strategy generator (daily/weekly/monthly/yearly; `Never` inlined), each yielding an infinite, lazy,
  ascending date sequence that the public methods clamp.
- **`RepeatStrategy`** (Never/Daily/Weekly/Monthly/Yearly) and **`RepeatEndCondition`**
  (Forever/AfterOccurrences/UntilDate) are **behaviour-free** serialized DUs — they hold configuration only.
- **`RepeatDescription`** renders a strategy/end-condition as human text. It switches over the same DU
  independently of the scheduler — a deliberate, separate presentation concern (see §7).
- **`DayInMonth`** / **`DaysOfWeek`** are config enums; note `DayInMonth.First`–`Fourth` are `1`–`4`
  on purpose (used as the nth-week index), and `DaysOfWeek` is Monday-bit-0 (unlike `System.DayOfWeek`).

---

## 5. Persistence and orchestration

- **`AccountPersistenceService(path, IFileSystem)`** — one append-only NDJSON file per account, named by the
  account's `Guid` (`"N"` format, no extension). Deleted accounts are renamed with a `.deleted` suffix and
  excluded from load. Admin restore (`ReplaceLog`) **validates before touching disk** (decode + replay into a
  throwaway account), keeps timestamped backups under `backups/`, and prunes to the newest few. Takes
  `IFileSystem` so it's testable against an in-memory file system.
- **`AccountManager(persistence, ignoreMonthClosed, TimeProvider)`** — loads all accounts on construction
  (creating a default "New Account" if storage is empty), wires each account's `NewEvent` to the persistence
  service, and owns the clock. `Today` is resolved live from the `TimeProvider`. `SetIgnoreMonthClosed`
  toggles the mode by **reloading every account from persistence** — there is no in-place toggle, which is
  why toggling can't corrupt state.

---

## 6. Decisions, and why

| Decision | Why | Where |
|---|---|---|
| **Event log is a pure-data contract; reads are a separate projection.** | Deterministic replay; the read model can be rebuilt or redesigned without touching persistence. | throughout; model-b §8 |
| **Sparse overlay, not eager materialisation.** | Eagerly storing projected occurrences was the source of a crash, unbounded fill, and gap-fragility. Store only facts + overrides; project the rest. | model-b §2 |
| **No stored running balances; compute from the carried-balance anchor.** | Density-dependent stored balances couldn't survive a sparse overlay; the close-anchor is a checkpoint that bounds compute by time-since-close, so no separate memo is needed. | model-b §4, §7 |
| **Reads never mutate.** | A read that materialised could regenerate occurrences into a closed region and break the edit lock. `BalanceOn`/`GetMonth` are pure. | model-b §5 |
| **Type split: `Transaction` (fact/occurrence) vs `Sequence` (rule) vs `LedgerEntry` (display row).** | `Balance` and `Strategy`/`EndCondition` were phantom fields — valid only on some instances. Splitting makes the compiler enforce where each is meaningful. | model-b §3 |
| **`LedgerEntry` carries `Kind` but not a sequence number.** | The sequence number is already the transaction's `Id.Sequence`; only `Kind` (one-off vs occurrence) needs the registry to determine, so only it earns a place. | this session |
| **The read model owns the projection floor + edit lock; floor dropped from every method.** | The floor was re-derived and threaded through ~7 methods. Owning it concentrates the rule and shrinks the interface. | architecture review #2 |
| **`RepeatScheduler` is the single engine; the per-strategy class hierarchy was collapsed in.** | Five adapter classes with one consumer and no external implementations — a hypothetical seam. Collapsing removes ceremony, allocation, and a public surface nobody used. | architecture review #1 |
| **Behaviour stays off `RepeatStrategy` (kept as data + an engine switch), not on the records.** | Behaviour-on-records would give compiler-enforced exhaustiveness but couples the serialized contract to the date engine; contract purity won. | this session |
| **Move = tombstone old + store new (no `Redirect`).** | Simpler moves and redirect-free reads. Trade-off: sequence-wide edits scan all partitions instead of following a forward pointer (acceptable while overrides are sparse). | this session |
| **`AfterOccurrences`/`UntilDate` clamp via the scheduler; `Never` (even explicit) is a one-off fact.** | A `Never` strategy must not register as a live sequence — doing so produced a phantom recurring rule (a bug the tests caught). | this session |

---

## 7. Invariants and things to look for

These are the load-bearing rules. Breaking one tends to corrupt balances or resurrect closed months.

- **The edit lock (`EarliestAllowedDate`) is always enforced, in both modes.** It's what makes closed
  months read-only even while they're being browsed. Every mutator guards against it.
- **Projection is clamped to the projection floor.** Nothing may generate an occurrence before the floor —
  that's the guard against resurrecting a closed, collapsed region. Any new read path must respect it.
- **The carried-balance anchor bounds balance cost.** Regularly closing old months is *load-bearing for
  performance*, not just tidiness — it's the checkpoint balances compute forward from.
- **`MonthClosed` ordering:** the carried balance is computed *before* the lock advances. It's a single
  operation for exactly this reason; don't split it.
- **Close only the oldest month with content, never the current/future month, never while browsing closed
  months.** All three guards live in `Account.CloseMonth`.
- **Occurrences share their sequence's number.** A one-off has its own unique number; the carried anchor is
  sequence `0`. Identity and "is this part of a sequence?" both hinge on the number + the registry.
- **Sequence-wide edits scan every partition** (`UpdateMatchingOverrides`, `RemoveOverrides`) — O(all stored
  entries), not O(this sequence's overrides). Fine while sparse; see §8 if it ever isn't.
- **Customisation is inferred by value, not flagged.** A sequence-wide amount/description change updates only
  overrides whose value still equals the rule's old value; individually-customised ones (already different)
  are left alone. There's no per-entry "customised" bit.
- **Yearly from a 29 Feb origin** computes each occurrence from the origin (`origin.AddYears(n)`), not
  cumulatively — so it clamps to 28 Feb in common years but *restores* the 29th in leap years. Don't
  "simplify" it back to stepping off the previous date.
- **The test surface is `Account`'s public API.** `TransactionCollection` and the scheduler internals are
  internal; correct refactors of them are invisible to the tests. If a refactor forces test changes, the seam
  probably moved.

---

## 8. Possible future changes

- **Wire the App layer.** `MMoney.App` is greenfield; the Core read API (`GetMonth` → `LedgerEntry`s,
  `BalanceOn`, `GetSequences`) is ready to bind. Inject `TimeProvider.System` into the app's DI and pass
  `AccountManager.Today` into `CloseMonth`.
- **Per-sequence override index.** If many overrides or hot sequence-wide edits ever make the
  all-partition scan matter, add `Dictionary<int, HashSet<TransactionId>>` (sequence number → its override
  ids), maintained as overrides are written. Restores footprint-locality without bringing back `Redirect`'s
  read-path indirection. Not worth it at current scale.
- **Balance memo.** A sparse month-boundary closing-balance cache, extended as a frontier and invalidated
  forward on edits — only if usage shows heavy month-hopping. Deferred: the close-anchor already bounds cost.
- **Far-future one-off horizon.** Adding a one-off far in the future is the only thing that lengthens the
  computation window. It works correctly today; whether/how far the UI should let users go is a product call.
- **Unify the two `RepeatStrategy` switches** (`RepeatScheduler` engine vs `RepeatDescription` text) —
  architecture-review candidate #3, consciously left as defensible presentation locality. Reopen only if the
  duplication starts to drift.
- **`AccountManager` does I/O in its constructor** (loads on construction). If lazy/async loading is ever
  wanted, that's the place to change.
- **No ADRs yet.** When a decision here gets seriously challenged, capture it as an ADR under `docs/adr/` so
  reviews don't re-litigate it.

---

## 9. Tests

`MMoney.Core.Tests` (61 tests) exercises the Core through `Account`'s public API plus the persistence/manager
layer (via an in-memory `MockFileSystem` and a fixed `TimeProvider`). Coverage: one-off balances; the
before/in-range/projected-forward balance branches; all three schedules and both end conditions; every
`DayInMonth` mode incl. short-month clamping, first/last weekday, and the leap-day case; skip/move/modify
overrides; in-place-vs-split sequence amount/description/strategy edits; month close + carried anchor +
closed-month-reads-zero; ignore-mode browsing (visible but read-only); the close guards; completed-sequence
pruning; replay determinism; validation guards; and the manager's save/load, delete/restore, import/export,
and ignore-mode toggle. Two real bugs were caught by this suite while building it (the phantom `Never`-
sequence and the yearly leap-day drift).
