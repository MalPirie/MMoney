# Model B — Sparse-Projection Read Model

Status: **implemented** (`MMoney.Core`; verified by `MMoney.Core.Tests`, 17 tests green)
Scope: `MMoney.Core` read model only (in-memory projection + types). The event log and
persistence layer are **out of scope and unchanged**.

## 1. The problem (as originally framed, and as reframed)

The UI presents an open-ended, into-the-future scrolling list of month/year headers. Selecting a
month shows that month's transactions with running balances. The stated worry was that selecting a
far-future month would require "walking back through all prior months" to compute balances.

**Reframe (agreed):** rendering month *M* depends on prior months through exactly **one scalar** —
the opening balance of *M* (= the closing balance of *M−1*). Everything else is local: *M*'s own
occurrences come from the repeat templates via the scheduler, and each row's running balance is just
`opening + cumulative-within-M`. There is no genuine walk over prior *transactions*, only the
accumulation of a single cumulative sum, which `BalanceOn(asOf)` already produces.

**The real problem (agreed):** the read-side balance cost is negligible (a far jump is a few ms of
arithmetic, stateless and correct). The genuine risk is that the **forward projection /
materialisation path is untested and faulty** across month spans — and that path is precisely what an
open-ended-future UI exercises constantly.

Demonstrated fault in the current code: `MaterialiseMonth` walks `last.Add(1).To(month)` calling
`AddRange(GenerateAllOccurrencesForMonth(m))` without first creating `partitions[m]`; `AddRange` does
`var partition = partitions[month];` (a direct index) and throws `KeyNotFoundException` the moment it
must populate a not-yet-created month that an active template produces occurrences in. This includes
simply adding a transaction into a new month while any sequence is active. It has not surfaced only
because the core has never been run against the multi-month-plus-sequence case.

## 2. Core idea

Stop eagerly materialising projected occurrences. Partitions become a **sparse overlay of facts and
overrides**; everything else is **projected and computed on read**.

- **Facts** — real one-off transactions, and the carried-balance anchor written by a month close.
- **Overrides** — per-occurrence exceptions to a projected series: skip, move, or modify.
- **Templates/sequences** — the recurrence rules, in the registry.
- **Everything else** (unedited occurrences, running balances) — **not stored**; produced on read.

This is the consistent endpoint of the read-side decisions below: if display projects on the fly,
then storing projected occurrences is paying to persist data we have chosen not to read, and that
redundancy is the entire bug surface (the throw, unbounded fill from a far one-off, gap-rebalancing
fragility, contiguity assumptions).

## 3. Type model

Three types replace today's single overloaded `Transaction`:

- **`Sequence`** — a recurrence rule. Origin date, sequence number, amount, description, `Strategy`,
  `EndCondition`. Lives in the registry. (Today these fields ride on `Transaction` but are only valid
  on a template; occurrences silently carry `Never`/`Forever`, which is a latent trap.)
- **`Transaction`** — a fact or a projected occurrence: `Id` (date + sequence), amount, description.
  **No `Strategy`, no `EndCondition`, no `Balance`.**
- **`LedgerEntry`** — a display row: a `Transaction` + the **computed** `Balance`, plus a kind
  discriminator (one-off vs occurrence) and, for occurrences, the sequence number to resolve back to
  its `Sequence`. The rule itself is **not** copied onto the row.

Rationale: both `Balance` and `Strategy`/`EndCondition` are today phantom fields — meaningful only on
some instances, default-valued (and misleading) on others. Splitting the types turns a
documentation-and-discipline problem into a compiler-enforced invariant: only the read layer can
produce a balance-bearing row, and only the registry holds a rule.

## 4. Balances are computed, anchored on month close

There are two computed balances; **neither is stored**:

1. **Scalar opening/closing balance** — `BalanceOn(asOf)`: the running balance including every
   transaction (fact or overlaid occurrence) dated on or before `asOf`. Sums amounts; needs no
   per-row balance.
2. **Per-row running balance** — stamped onto each `LedgerEntry` only when a month's rows are
   returned. The month-view computes `opening = BalanceOn(month.FirstDay.AddDays(-1))`, then walks
   the month's overlaid occurrences + facts in `(date, sequence)` order, accumulating and stamping.

Because nothing is stored, a naive `BalanceOn` would sum from account genesis. The **closed-month
carried balance is the anchor that bounds this**: `CloseMonth` writes a sequence-0 `Balance carried`
fact at the start of the first open month — a known-good checkpoint. So:

```
BalanceOn(asOf) = carriedBalanceAnchor + Σ(facts + overlaid occurrences) for dates in (anchor, asOf]
```

Computation cost is bounded by **time since the last close**, not by account age. Closing a month is
therefore **load-bearing for performance**, not merely tidiness — it is the prefix cache, falling out
of an existing feature with no separate invalidatable memo to maintain.

Ignore-month-closed (the temporary "browse history") mode has no anchor; it computes from genesis,
which is affordable for an occasional browsing mode.

## 5. Read paths

A single internal primitive is the source of truth for "what occurrences actually exist in month *M*
after overrides," consumed by **both** the month-view and `BalanceOn`:

- Project occurrences for every template active in *M* (scheduler).
- Apply overrides: drop tombstoned ids (skips); replace modified occurrences with their override
  values; account for moved occurrences at their new date (and not the old).
- Add real one-off facts in *M*.

`GetTransactionForMonth` (renamed/retyped) returns `IReadOnlyList<LedgerEntry>` with running balances
stamped. **Viewing never mutates** — no `MaterialiseMonth`, so it cannot regenerate occurrences into a
closed/collapsed region and break the earliest-allowed-date invariant.

## 6. Edit paths

Editing is the **only** thing that writes to partitions, and it materialises **one month**, never a
span:

- **Skip an occurrence** → `TransactionRemoved` event → tombstone override at that id.
- **Move an occurrence** → `TransactionDateChanged` event → redirect at the source id, fact/override
  at the target date.
- **Modify an occurrence's amount/description** → the corresponding event → a modified-occurrence
  override at that id.
- **Series-level edits** (amount/description/strategy from a date) → existing `Sequence*` events and
  the split decomposition (`SequenceRemoved` + `TransactionAdded`) already designed; they mutate the
  `Sequence` and, where a later-dated split is involved, mint a new sequence.

The override store is built by `Apply` from these events on replay, so it is never out of step with
the log.

## 7. What is removed

- Eager materialisation of projected occurrences (`MaterialiseMonth`'s span walk).
- Stored running balances and `Rebalance` (and with them the gap-contiguity fragility, e.g. the
  newest-first walk-back worked around in `StoredBalanceOnOrBefore`).
- Phantom `Balance` on `Transaction`; phantom `Strategy`/`EndCondition` on `Transaction`.

## 8. Scope / blast radius

**Unchanged:** event records, `AccountEventCodec`, `AccountPersistenceService`, `AccountManager`, and
the entire on-disk format. Existing logs replay into the new model with no migration.

**Rewritten:** `TransactionCollection` (projection + overlay + computed balances), the
`Transaction` / `Sequence` / `LedgerEntry` split, `Account.Apply` (build sparse state, not eager
materialisation), and `Account`'s read methods (`GetTransactionForMonth` → `LedgerEntry`s,
`BalanceOn`, `GetSequences`).

The existing events already encode every intent model B needs — **no new event types**. This makes the
work a read-model rewrite behind a stable event log: build it test-first by replaying hand-written
event sequences and asserting on projected month-views and balances, with zero persistence concerns
and a clean revert path if it goes wrong.

## 9. Decision log (from the design session)

1. Cross-month dependency for a month view collapses to one scalar: the opening balance.
2. Design for repeated-access cost, not single-jump; raw projection compute is negligible.
3. No balance memo for now — stateless recompute via `BalanceOn`. Revisit if usage shows heavy
   month-hopping.
4. Display is non-mutating projection; materialisation happens only on edit.
5. The real problem is projection/materialisation correctness across spans, not read-cost.
6. Adopt the sparse-overlay/projection model (B) rather than patching eager materialisation.
7. Drop stored running balances and `Rebalance`; the carried-balance checkpoint is the computation
   anchor; regular month-closing is load-bearing for performance.
8. Split the domain `Transaction` from a balance-bearing display row (`LedgerEntry`).
9. Lift `Strategy`/`EndCondition` onto a `Sequence` type; the row carries a kind + sequence-number
   link, not the rule.
10. This is a read-model rewrite behind a stable event log; persistence and event schema are out of
    scope.

## 10. Deferred / open

- **Balance memo** — a sparse month-boundary closing-balance cache extended as a frontier, invalidated
  forward from an edit point. Only if usage shows heavy month-hopping (Q3).
- **Far-future one-off transactions** — the previously-faulty case. Now verified: adding a far-future
  one-off no longer throws and projects alongside occurrences (`FarFutureOneOff_...`). Remaining open
  question is only product-level: how far ahead the UI should let users go.
- **First core tests** — done. `MMoney.Core.Tests` (58 tests) covers one-off balances, the
  before/in-range/projected-forward balance branches, all three schedules and both end conditions,
  every `DayInMonth` mode (incl. short-month clamping and first/last weekday), skip/move/modify
  overrides, in-place-vs-split sequence amount/description/strategy edits, month close + carried-balance
  anchor + closed-month-reads-zero, ignore-mode browsing (visible but read-only), the close guards,
  completed-sequence pruning, replay determinism, validation guards, and the `AccountManager` +
  `AccountPersistenceService` layer (save/load round-trip, delete/restore, import/export, ignore-mode
  toggle reload) using an in-memory `MockFileSystem` and a fixed clock. The audit also caught and fixed
  a real bug: changing a sequence to `Never` was registering a phantom `Never`-sequence.

  Yearly leap-day drift (29 Feb origin losing the 29th) was found and fixed at the same time.
