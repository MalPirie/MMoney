using MMoney.Core.Repeat;

namespace MMoney.Core;

/// <summary>
/// The sparse-overlay read model for one account. Month partitions hold only <em>facts</em> (one-off
/// transactions and carried-balance anchors) and <em>overrides</em> (tombstones for skipped/moved
/// occurrences, and stored transactions for individually-edited or moved-in occurrences). Repeating
/// sequences live in a registry; their unedited occurrences are projected on read, never stored.
/// </summary>
/// <remarks>
/// Nothing here stores a running balance: balances are computed on read from the carried-balance anchor
/// forward (see <see cref="BalanceOn"/>). The read model owns the <see cref="EarliestAllowedDate"/> edit lock
/// and derives the projection floor from it — in normal mode the floor is the lock, so closed months stay
/// collapsed; while browsing closed months the floor drops away so they reappear (still read-only).
/// </remarks>
internal sealed class TransactionCollection(bool ignoreMonthClosed)
{
    private const string CarriedBalanceDescription = "Balance carried";

    private readonly RepeatScheduler scheduler = new();

    // Facts and overrides, partitioned by month and kept in ascending TransactionId order within a month.
    private readonly SortedDictionary<MonthOnly, List<TransactionEntry>> partitions = [];

    // The repeating rules, keyed by their per-account sequence number.
    private readonly Dictionary<int, Sequence> sequences = [];

    /// <summary>The date before which the ledger is read-only. Advanced by <see cref="MonthClosed"/>.</summary>
    public DateOnly EarliestAllowedDate { get; private set; } = DateOnly.MinValue;

    /// <summary>
    /// The date occurrences are projected from. The edit lock in normal mode; nothing while browsing closed
    /// months, so they reappear.
    /// </summary>
    private DateOnly ProjectionFloor => ignoreMonthClosed ? DateOnly.MinValue : EarliestAllowedDate;

    // ---- Registry --------------------------------------------------------------------------------

    /// <summary>Registers a repeating sequence. Its occurrences are projected on read, not stored.</summary>
    public void AddSequence(Sequence sequence) => sequences[sequence.Number] = sequence;

    /// <summary>Returns the sequence for the given number, or <see langword="null"/> if none.</summary>
    public Sequence? FindSequence(int number) => sequences.GetValueOrDefault(number);

    /// <summary><see langword="true"/> when a sequence with the given number is registered.</summary>
    public bool IsSequence(int number) => sequences.ContainsKey(number);

    /// <summary>
    /// The active sequences, ordered by origin, excluding any whose schedule ends before the edit lock — a
    /// completed sequence sits entirely in the read-only region and is no longer relevant.
    /// </summary>
    public IEnumerable<Sequence> GetSequences() => sequences.Values
        .Where(s => scheduler.EndDate(s.Strategy, s.EndCondition, s.Origin) >= EarliestAllowedDate)
        .OrderBy(s => s.Id);

    // ---- Facts -----------------------------------------------------------------------------------

    /// <summary>Adds a one-off (non-repeating) transaction as a stored fact.</summary>
    public void AddFact(Transaction transaction) => PutStored(transaction);

    // ---- Reads -----------------------------------------------------------------------------------

    /// <summary>
    /// Resolves the transaction at <paramref name="id"/>: a stored fact/override wins, a tombstone resolves to
    /// nothing, otherwise a projected occurrence on or after the projection floor.
    /// </summary>
    public Transaction? Find(TransactionId id)
    {
        var entry = FindEntry(id);
        if (entry is TransactionEntry.Stored stored)
        {
            return stored.Transaction;
        }

        if (entry is TransactionEntry.Tombstone)
        {
            return null;
        }

        return IsProjectedOccurrence(id)
            ? new Transaction(id, sequences[id.Sequence].Amount, sequences[id.Sequence].Description)
            : null;
    }

    /// <summary>
    /// The month's transactions as display rows with running balances, opening from the balance carried into
    /// the month.
    /// </summary>
    public IReadOnlyList<LedgerEntry> GetMonth(MonthOnly month)
    {
        var balance = OpeningBalance(month);
        var rows = new List<LedgerEntry>();
        foreach (var transaction in EffectiveTransactionsForMonth(month))
        {
            balance += transaction.Amount;
            var kind = transaction.IsCarriedBalance
                ? LedgerEntryKind.CarriedBalance
                : IsSequence(transaction.Sequence) ? LedgerEntryKind.Occurrence : LedgerEntryKind.OneOff;
            rows.Add(new LedgerEntry(transaction, balance, kind));
        }

        return rows;
    }

    /// <summary>
    /// The running balance including every transaction dated on or before <paramref name="asOf"/>. Computation
    /// starts at the earliest content month (the carried-balance anchor after a close) so its cost is bounded by
    /// time since the last close, not by account age.
    /// </summary>
    public decimal BalanceOn(DateOnly asOf)
    {
        var start = EarliestContentMonth();
        if (start is null)
        {
            return 0m;
        }

        var asOfMonth = MonthOnly.FromDate(asOf);
        if (asOfMonth.CompareTo(start.Value) < 0)
        {
            return 0m;
        }

        var balance = 0m;
        foreach (var month in start.Value.To(asOfMonth))
        {
            foreach (var transaction in EffectiveTransactionsForMonth(month))
            {
                if (transaction.Date <= asOf)
                {
                    balance += transaction.Amount;
                }
            }
        }

        return balance;
    }

    /// <summary>
    /// The earliest month that holds any effective transaction — a stored fact or a projected occurrence — or
    /// <see langword="null"/> when the account has none.
    /// </summary>
    public MonthOnly? EarliestContentMonth()
    {
        MonthOnly? earliest = null;

        foreach (var (month, list) in partitions)
        {
            if (list.Any(e => e is TransactionEntry.Stored))
            {
                earliest = month;
                break;
            }
        }

        var floor = ProjectionFloor;
        foreach (var sequence in sequences.Values)
        {
            var from = sequence.Origin > floor ? sequence.Origin : floor;
            var first = scheduler.NextOnOrAfter(sequence.Strategy, sequence.EndCondition, sequence.Origin, from);
            if (first is DateOnly date)
            {
                var month = MonthOnly.FromDate(date);
                if (earliest is null || month.CompareTo(earliest.Value) < 0)
                {
                    earliest = month;
                }
            }
        }

        return earliest;
    }

    // ---- Edits -----------------------------------------------------------------------------------

    /// <summary>Sets the amount of a single transaction (one-off or occurrence) via a stored override.</summary>
    public void ChangeAmount(TransactionId id, decimal newAmount)
    {
        var current = Find(id) ?? throw new InvalidOperationException("Transaction not found.");
        PutStored(current with { Amount = newAmount });
    }

    /// <summary>Sets the description of a single transaction (one-off or occurrence) via a stored override.</summary>
    public void ChangeDescription(TransactionId id, string newDescription)
    {
        var current = Find(id) ?? throw new InvalidOperationException("Transaction not found.");
        PutStored(current with { Description = newDescription });
    }

    /// <summary>Moves a single transaction to a new date: vacates the old id and stores it at the new one.</summary>
    public void ChangeDate(TransactionId id, DateOnly newDate)
    {
        var current = Find(id) ?? throw new InvalidOperationException("Transaction not found.");
        RemoveResolved(id);
        PutStored(current with { Id = current.Id with { Date = newDate } });
    }

    /// <summary>
    /// Removes a single transaction: a projected occurrence is suppressed with a tombstone (a skip), a stored
    /// fact or moved-in occurrence is deleted outright.
    /// </summary>
    public void RemoveTransaction(TransactionId id) => RemoveResolved(id);

    /// <summary>
    /// Sets the amount of a whole sequence in place, updating the rule and any occurrence overrides that still
    /// carry the old amount (individually-customised overrides are left untouched).
    /// </summary>
    public void ChangeSequenceAmount(int number, decimal newAmount)
    {
        var sequence = sequences[number];
        var oldAmount = sequence.Amount;
        sequences[number] = sequence with { Amount = newAmount };
        UpdateMatchingOverrides(number, t => t.Amount == oldAmount, t => t with { Amount = newAmount });
    }

    /// <summary>Sets the description of a whole sequence in place (see <see cref="ChangeSequenceAmount"/>).</summary>
    public void ChangeSequenceDescription(int number, string newDescription)
    {
        var sequence = sequences[number];
        var oldDescription = sequence.Description;
        sequences[number] = sequence with { Description = newDescription };
        UpdateMatchingOverrides(number, t => t.Description == oldDescription, t => t with { Description = newDescription });
    }

    /// <summary>
    /// Removes a sequence from <paramref name="fromDate"/> onwards. On or before the origin the whole sequence
    /// and all its overrides go; otherwise the rule is truncated and only overrides on or after the cut are removed.
    /// </summary>
    public void RemoveSequenceFrom(int number, DateOnly fromDate)
    {
        var sequence = sequences[number];
        if (fromDate <= sequence.Origin)
        {
            sequences.Remove(number);
            RemoveOverrides(number, _ => true);
        }
        else
        {
            sequences[number] = sequence with { EndCondition = new RepeatEndCondition.UntilDate(fromDate.AddDays(-1)) };
            RemoveOverrides(number, id => id.Date >= fromDate);
        }
    }

    /// <summary>
    /// Applies a month close: unless closed months are being shown, collapses the month into a carried-balance
    /// anchor at the start of the next month; then prunes completed sequences and advances the edit lock.
    /// </summary>
    /// <remarks>
    /// The carried balance is computed before the lock advances, so it captures the balance as of the closing
    /// month. This ordering is the reason a close must be a single operation.
    /// </remarks>
    public void MonthClosed(MonthOnly month)
    {
        if (!ignoreMonthClosed)
        {
            var carried = BalanceOn(month.LastDay);
            partitions.Remove(month);
            PutStored(new Transaction(new TransactionId(month.Add(1).FirstDay, 0), carried, CarriedBalanceDescription));
        }

        RemoveExpiredSequences(month.LastDay);
        EarliestAllowedDate = month.Add(1).FirstDay;
    }

    // ---- Projection + overlay --------------------------------------------------------------------

    private IEnumerable<Transaction> EffectiveTransactionsForMonth(MonthOnly month)
    {
        var floor = ProjectionFloor;
        var result = new SortedDictionary<TransactionId, Transaction>();
        var tombstoned = new HashSet<TransactionId>();

        if (partitions.TryGetValue(month, out var list))
        {
            foreach (var entry in list)
            {
                switch (entry)
                {
                    case TransactionEntry.Stored stored:
                        result[stored.Id] = stored.Transaction;
                        break;
                    case TransactionEntry.Tombstone tombstone:
                        tombstoned.Add(tombstone.Id);
                        break;
                }
            }
        }

        foreach (var sequence in sequences.Values)
        {
            foreach (var date in scheduler.DatesForMonth(sequence.Strategy, sequence.EndCondition, sequence.Origin, month))
            {
                if (date < floor)
                {
                    continue;
                }

                var id = new TransactionId(date, sequence.Number);
                if (result.ContainsKey(id) || tombstoned.Contains(id))
                {
                    continue;
                }

                result[id] = new Transaction(id, sequence.Amount, sequence.Description);
            }
        }

        return result.Values;
    }

    private decimal OpeningBalance(MonthOnly month)
    {
        var firstDay = month.FirstDay;
        return firstDay > DateOnly.MinValue ? BalanceOn(firstDay.AddDays(-1)) : 0m;
    }

    private bool IsProjectedOccurrence(TransactionId id) =>
        id.Date >= ProjectionFloor
        && sequences.TryGetValue(id.Sequence, out var sequence)
        && scheduler.NextOnOrAfter(sequence.Strategy, sequence.EndCondition, sequence.Origin, id.Date) == id.Date;

    private void RemoveExpiredSequences(DateOnly lastDay)
    {
        var expired = sequences.Values
            .Where(s => scheduler.EndDate(s.Strategy, s.EndCondition, s.Origin) <= lastDay)
            .Select(s => s.Number)
            .ToList();

        foreach (var number in expired)
        {
            sequences.Remove(number);
        }
    }

    // ---- Partition primitives --------------------------------------------------------------------

    private void RemoveResolved(TransactionId id)
    {
        if (IsProjectedOccurrence(id))
        {
            PutTombstone(id);
        }
        else
        {
            RemoveEntry(id);
        }
    }

    private void PutStored(Transaction transaction) => PutEntry(new TransactionEntry.Stored(transaction));

    private void PutTombstone(TransactionId id) => PutEntry(new TransactionEntry.Tombstone(id));

    private void PutEntry(TransactionEntry entry)
    {
        var month = MonthOnly.FromDate(entry.Id.Date);
        if (!partitions.TryGetValue(month, out var list))
        {
            list = [];
            partitions[month] = list;
        }

        var index = FindPartitionIndex(list, entry.Id);
        if (index >= 0)
        {
            list[index] = entry;
        }
        else
        {
            list.Insert(~index, entry);
        }
    }

    private void RemoveEntry(TransactionId id)
    {
        var month = MonthOnly.FromDate(id.Date);
        if (!partitions.TryGetValue(month, out var list))
        {
            return;
        }

        var index = FindPartitionIndex(list, id);
        if (index < 0)
        {
            return;
        }

        list.RemoveAt(index);
        if (list.Count == 0)
        {
            partitions.Remove(month);
        }
    }

    private TransactionEntry? FindEntry(TransactionId id)
    {
        var month = MonthOnly.FromDate(id.Date);
        if (!partitions.TryGetValue(month, out var list))
        {
            return null;
        }

        var index = FindPartitionIndex(list, id);
        return index >= 0 ? list[index] : null;
    }

    private void UpdateMatchingOverrides(int number, Func<Transaction, bool> predicate, Func<Transaction, Transaction> update)
    {
        foreach (var list in partitions.Values)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] is TransactionEntry.Stored stored
                    && stored.Transaction.Sequence == number
                    && predicate(stored.Transaction))
                {
                    list[i] = new TransactionEntry.Stored(update(stored.Transaction));
                }
            }
        }
    }

    private void RemoveOverrides(int number, Func<TransactionId, bool> predicate)
    {
        var emptyMonths = new List<MonthOnly>();
        foreach (var (month, list) in partitions)
        {
            list.RemoveAll(e => e.Id.Sequence == number && predicate(e.Id));
            if (list.Count == 0)
            {
                emptyMonths.Add(month);
            }
        }

        foreach (var month in emptyMonths)
        {
            partitions.Remove(month);
        }
    }

    private static int FindPartitionIndex(List<TransactionEntry> list, TransactionId id)
    {
        var left = 0;
        var right = list.Count - 1;
        while (left <= right)
        {
            var mid = left + (right - left) / 2;
            var comparison = list[mid].Id.CompareTo(id);
            if (comparison == 0)
            {
                return mid;
            }

            if (comparison < 0)
            {
                left = mid + 1;
            }
            else
            {
                right = mid - 1;
            }
        }

        return ~left;
    }
}
