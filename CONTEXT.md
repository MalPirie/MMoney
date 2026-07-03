# MMoney

A personal-finance ledger built as an event-sourced core. State is rebuilt by replaying an event log; reads
project repeating transactions and compute balances on demand over a sparse overlay. See
[docs/model-b-read-model.md](docs/model-b-read-model.md) for the read model in full.

## Language

**Account**:
The event-sourced ledger aggregate. All state changes are events; reads are projected from them.
_Avoid_: ledger (the whole app), wallet.

**Sequence**:
A repeating-transaction rule — an origin, an amount/description, and a Schedule. Lives in the registry; its
occurrences are projected, not stored.
_Avoid_: template, recurrence.

**Schedule**:
The recurrence definition that projects a Sequence's occurrences: its `RepeatStrategy` (the interval pattern),
`RepeatEndCondition`, and origin date. A value that knows its own occurrence dates.
_Avoid_: strategy (only the interval pattern, one part of a Schedule), scheduler (the date maths live on the
Schedule itself, not a separate engine).

**Occurrence**:
A single transaction projected from a Sequence on one date. Shares the Sequence's number.
_Avoid_: instance, repeat.

**Carried-balance anchor**:
The sequence-0 "Balance carried" entry written when a month is closed. The checkpoint every later balance is
computed forward from.
_Avoid_: opening balance (that is a computed figure, not the stored anchor).

**Edit lock**:
The date before which the ledger is read-only (the `EarliestAllowedDate`). Always enforced, including while
closed months are being browsed. Sequence relevance is tied to the edit lock: a Sequence that ended before it
is no longer listed, because nothing in the locked region can be acted on.
_Avoid_: earliest date (ambiguous with the projection floor).

**Projection floor**:
The date occurrences are projected from for display. Equals the edit lock in normal mode; drops to nothing
while closed months are being browsed, so they reappear (still read-only, via the edit lock).
_Avoid_: floor (ambiguous), earliest allowed date (that is the edit lock).
