# Admin account export / import (raw event-log backup + restore)

A hidden **admin mode** in Settings adds raw-event-log **Export** and **Import** for the current account — a
backup/restore + move-between-installs tool. The Core already had the mechanism (`AccountManager.ExportAccount`
reads the raw log lines; `ImportAccount` → `AccountPersistenceService.ReplaceLog` validates by decode+replay into a
throwaway `Account`, backs the old log up, and swaps atomically), so this is largely a UI + file-contract decision.
The decisions worth recording are the **backup file contract** and the **import identity model** — both hard to
change once backups exist in the wild.

## Decision

- **Admin mode is a hidden, in-memory, session-scoped unlock.** Five quick taps on the Settings **About** box reveal
  an **Admin** section (snackbar on unlock, no visible counter). The flag lives in memory for the app session — it
  survives leaving/re-entering Settings but **re-locks on app restart**, and is never persisted. It gates a
  destructive action (Import), so it should not sit permanently armed, but re-tapping on every Settings visit within
  a session would be tedious.

- **The export is the *pure* raw event log; the account id rides in the filename.** File name **`<accountId>.jsonl`**
  (id in `"N"` form, matching the on-disk file name); content is exactly the stored newline-delimited `AccountEvent`
  log, unchanged. Delivered through the Android **share sheet** (as the CSV export is). The event log has no place
  for an id — its lines are `AccountEvent`s and the id is the aggregate identity, stored only as the on-disk file
  name — so keeping the export a *pure* log (a hard requirement) forces the id into the file name rather than an
  embedded header line.

- **Import reads the id from the file name and is id-guarded, but adoptable.** A file picker supplies the file; the
  id is parsed from its name. The file is **validated and summarised first** (decode + replay into a throwaway
  account): invalid → an immediate error, nothing touched; valid → a confirm dialog carrying a summary (event count
  + recency) and the destructive warning.
  - **Matching id** (the account's own backup) → a plain "revert this account to its backup" confirm → overwrite in
    place (Core keeps the id and writes a timestamped `.bak`).
  - **Mismatching id** (a foreign backup, or a fresh install / new device whose default account has a different id)
    → a **stronger** confirm ("this backup is from a different account/install; this replaces the current account
    entirely and adopts the backup"). On confirm the account **adopts the backup's id**; the current account's log is
    kept as a **recoverable deleted account** (the existing delete/restore mechanism), not destroyed.

- **After success:** stay on Settings, show a snackbar; the shell resets the shown month to today (clamped into the
  imported account's range) via the existing `OnChanged` hook, so it can never point at a now-missing month.

## Considered and rejected

- **Embed the id in a header line inside the file** — survives file renames, but stops the export being a pure raw
  event log (export/import must add/strip the header, and it won't replay as-is through the existing Core path).
  Rejected to keep "raw nljson" literally true; the cost is limitation (1) below.
- **Hard-reject a mismatching id** — safest against clobbering the wrong account, but it locks you out of your own
  backup on a fresh install / new device (the default account there has a new id), which is a primary backup use
  case. Rejected in favour of asking with a pointed warning and adopting the id on confirm.
- **Always adopt the id with a single confirm** (no mismatch special-casing) — simplest, but drops the warning that
  tells you the backup isn't this account's own. Rejected: the mismatch case is exactly where an extra beat of
  friction is warranted.
- **Include the account name in the export file name** (`<name>-<date>.jsonl`) — friendlier, but the id is what
  import needs, and a name adds nothing it can use. Rejected: file name is **`<id>.jsonl`** only.

## Consequences

- **A renamed export cannot be imported.** Because the id lives only in the file name, `mybackup.jsonl` has no
  parseable id; import rejects it with a clear "the file name must be the account id" message. This is the price of
  keeping the content a pure log; revisit only if it bites (the fix would be the header-line format above).
- **Adopting an id replaces the single current account.** In today's single-account app, importing a foreign backup
  turns the current account into the backup (old data preserved as a recoverable deleted account). The id-guard's
  full value — routing a backup to *its* account without disturbing others — only materialises once multi-account
  exists; the file contract and guard are designed now so that future is a routing change, not a format change.
- **Small Core additions:** a validate-only `PreviewImport(lines)` that returns a summary without applying, and an
  "import under a new id" path (mark the current account deleted, create the account under the backup's id) beside
  the existing same-id `ImportAccount`.
- Import is destructive but **safe by construction**: validation happens before any disk write, and the prior state
  is always recoverable (a `.bak` on same-id overwrite, a deleted account on id adoption).
