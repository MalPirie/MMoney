using MMoney.Core.Repeat;

namespace MMoney.Core;

/// <summary>
/// A named financial account whose state is an event-sourced ledger. Reads project repeating occurrences and
/// compute balances on demand over a sparse overlay (see <see cref="TransactionCollection"/>).
/// </summary>
/// <remarks>
/// All mutations fire <see cref="NewEvent"/> before updating internal state, allowing subscribers
/// (e.g. <see cref="AccountPersistenceService"/>) to persist each change as it occurs.
/// </remarks>
public sealed class Account
{
    /// <summary>Raised immediately before each state-changing event is applied.</summary>
    public event EventHandler<AccountEvent>? NewEvent;

    private int sequence = 0;
    private readonly TransactionCollection transactions;
    private readonly bool ignoreMonthClosed;

    /// <summary>
    /// Reconstructs an account by replaying the supplied event log in order.
    /// </summary>
    /// <param name="ignoreMonthClosed">
    /// When <see langword="true"/>, the collapse performed by <see cref="AccountEvent.MonthClosed"/> is skipped
    /// so that closed months reappear, while the events still apply their <see cref="EarliestAllowedDate"/> lock —
    /// closed months become visible but read-only.
    /// </param>
    public Account(Guid id, IEnumerable<AccountEvent> events, bool ignoreMonthClosed = false)
    {
        this.ignoreMonthClosed = ignoreMonthClosed;
        transactions = new TransactionCollection(ignoreMonthClosed);
        Id = id;
        foreach (var e in events)
        {
            Apply(e);
        }
    }

    /// <summary>The account's unique identifier.</summary>
    public Guid Id { get; }

    /// <summary>The account's display name.</summary>
    public string Name { get; private set; } = "";

    /// <summary>
    /// The earliest date that may be assigned to a new or updated transaction. Always enforced, including while
    /// closed months are being shown — that is what keeps closed months read-only.
    /// </summary>
    public DateOnly EarliestAllowedDate => transactions.EarliestAllowedDate;

    /// <summary>
    /// The account's active repeating sequences, ordered by origin. Sequences that completed before the edit
    /// lock are excluded, as they are no longer relevant.
    /// </summary>
    public IReadOnlyList<Sequence> GetSequences() => [.. transactions.GetSequences()];

    /// <summary>
    /// The account's active sequences paired with their next due date on or after <paramref name="asOf"/>,
    /// excluding any with no upcoming occurrence, ordered by next due date. Backs the repeating-items list.
    /// </summary>
    public IReadOnlyList<UpcomingSequence> GetUpcomingSequences(DateOnly asOf) =>
        [.. transactions.GetUpcomingSequences(asOf)];

    /// <summary>The given month's transactions as display rows with running balances.</summary>
    public IReadOnlyList<LedgerEntry> GetMonth(MonthOnly month) => transactions.GetMonth(month);

    /// <summary>
    /// The running balance including every transaction dated on or before <paramref name="asOf"/> (inclusive).
    /// Pass the current date for the available balance, or any other date for a projected or historical balance.
    /// </summary>
    public decimal BalanceOn(DateOnly asOf) => transactions.BalanceOn(asOf);

    /// <summary>Adds a new transaction (optionally repeating) to the account.</summary>
    public Transaction AddTransaction(DateOnly date, decimal amount, string description,
        RepeatStrategy? strategy = null, RepeatEndCondition? endCondition = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ThrowIfDateTooEarly(date, nameof(date));

        var nextSequence = sequence + 1;
        Update(new AccountEvent.TransactionAdded(date, nextSequence, amount, description, strategy, endCondition));
        return FindTransaction(new TransactionId(date, nextSequence));
    }

    /// <summary>
    /// Changes the amount of a repeating sequence from <paramref name="fromDate"/> onwards. When
    /// <paramref name="fromDate"/> is the sequence origin, updates the rule and its occurrences in place.
    /// Otherwise splits the sequence: the old one is truncated and a new one starting at <paramref name="fromDate"/>
    /// is created with the new amount.
    /// </summary>
    public void ChangeSequenceAmount(Transaction transaction, DateOnly fromDate, decimal newAmount)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentOutOfRangeException.ThrowIfZero(newAmount);
        ThrowIfDateTooEarly(fromDate, nameof(fromDate));
        var sequenceRule = GetSequenceOrThrow(transaction);

        if (fromDate == sequenceRule.Origin)
        {
            Update(new AccountEvent.SequenceAmountChanged(sequenceRule.Origin, sequenceRule.Number, newAmount));
        }
        else
        {
            SplitSequence(sequenceRule, fromDate, newAmount, sequenceRule.Description);
        }
    }

    /// <summary>
    /// Changes the description of a repeating sequence from <paramref name="fromDate"/> onwards
    /// (see <see cref="ChangeSequenceAmount"/> for the in-place-vs-split behaviour).
    /// </summary>
    public void ChangeSequenceDescription(Transaction transaction, DateOnly fromDate, string newDescription)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(newDescription);
        ThrowIfDateTooEarly(fromDate, nameof(fromDate));
        var sequenceRule = GetSequenceOrThrow(transaction);

        if (fromDate == sequenceRule.Origin)
        {
            Update(new AccountEvent.SequenceDescriptionChanged(sequenceRule.Origin, sequenceRule.Number, newDescription));
        }
        else
        {
            SplitSequence(sequenceRule, fromDate, sequenceRule.Amount, newDescription);
        }
    }

    /// <summary>
    /// Changes the repeat strategy and end condition of a transaction from <paramref name="fromDate"/> onwards.
    /// </summary>
    /// <remarks>
    /// Expressed as a decomposition into existing events rather than an in-place mutation, mirroring how a
    /// later-dated amount/description change splits a sequence. A repeating source is truncated at
    /// <paramref name="fromDate"/> (removed entirely when that is its origin); a non-repeating source is removed
    /// outright and ignores <paramref name="fromDate"/>, re-anchoring on its own date since it has a single
    /// occurrence. Either way a fresh sequence carrying the new schedule is added with a new number, so any
    /// overrides on the old sequence are left intact as history rather than reinterpreted under the new schedule.
    /// </remarks>
    public void ChangeSequenceStrategy(Transaction transaction, DateOnly fromDate, RepeatStrategy newStrategy, RepeatEndCondition newEndCondition)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(newStrategy);
        ArgumentNullException.ThrowIfNull(newEndCondition);
        ThrowIfDateTooEarly(fromDate, nameof(fromDate));
        ThrowIfTransactionDoesNotExist(transaction);

        var sequenceRule = transactions.FindSequence(transaction.Sequence);

        DateOnly newOrigin;
        decimal amount;
        string description;
        if (sequenceRule is null)
        {
            // Non-repeating source: a single stored fact.
            if (newStrategy is RepeatStrategy.Never)
            {
                return;
            }

            Update(new AccountEvent.TransactionRemoved(transaction.Id.Date, transaction.Id.Sequence));
            newOrigin = transaction.Date;
            amount = transaction.Amount;
            description = transaction.Description;
        }
        else
        {
            if (newStrategy == sequenceRule.Strategy && newEndCondition == sequenceRule.EndCondition)
            {
                return;
            }

            if (fromDate < sequenceRule.Origin)
            {
                throw new ArgumentOutOfRangeException(nameof(fromDate), fromDate,
                    "Cannot change a sequence's strategy from before its origin date.");
            }

            Update(new AccountEvent.SequenceRemoved(sequenceRule.Origin, sequenceRule.Number, fromDate));
            newOrigin = fromDate;
            amount = sequenceRule.Amount;
            description = sequenceRule.Description;
        }

        Update(new AccountEvent.TransactionAdded(newOrigin, sequence + 1, amount, description, newStrategy, newEndCondition));
    }

    /// <summary>
    /// Re-anchors a repeating sequence's dates atomically: truncates it at <paramref name="fromDate"/> and
    /// restarts the same schedule (amount, description, strategy, end condition) at <paramref name="newDate"/>.
    /// </summary>
    /// <remarks>
    /// Both dates are validated against the edit lock before anything changes, so the operation never half-applies.
    /// When <paramref name="fromDate"/> is the origin the whole sequence is replaced; when later it is truncated
    /// to end the day before <paramref name="fromDate"/>. The locked past is never touched — and a remnant
    /// truncated to end before the edit lock is dropped from <see cref="GetSequences"/> as completed. Always mints
    /// a new sequence (a date change cannot be applied in place). The caller maps scope to <paramref name="fromDate"/>:
    /// the origin (or the edit lock, if the origin is locked) for the whole series, or the occurrence for this-and-following.
    /// </remarks>
    public void ChangeSequenceDate(Transaction transaction, DateOnly fromDate, DateOnly newDate)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ThrowIfDateTooEarly(fromDate, nameof(fromDate));
        ThrowIfDateTooEarly(newDate, nameof(newDate));
        var sequenceRule = GetSequenceOrThrow(transaction);

        Update(new AccountEvent.SequenceRemoved(sequenceRule.Origin, sequenceRule.Number, fromDate));
        Update(new AccountEvent.TransactionAdded(newDate, sequence + 1, sequenceRule.Amount, sequenceRule.Description,
            sequenceRule.Strategy, sequenceRule.EndCondition));
    }

    /// <summary>Changes the amount of an existing transaction.</summary>
    public void ChangeTransactionAmount(Transaction transaction, decimal newAmount)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentOutOfRangeException.ThrowIfZero(newAmount);
        ThrowIfBeforeEarliestDate(transaction);
        ThrowIfTransactionDoesNotExist(transaction);

        if (transaction.Amount != newAmount)
        {
            Update(new AccountEvent.TransactionAmountChanged(transaction.Id.Date, transaction.Id.Sequence, newAmount));
        }
    }

    /// <summary>Changes the date of an existing transaction.</summary>
    public void ChangeTransactionDate(Transaction transaction, DateOnly newDate)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ThrowIfDateTooEarly(newDate, nameof(newDate));
        ThrowIfBeforeEarliestDate(transaction);
        ThrowIfTransactionDoesNotExist(transaction);

        if (transaction.Id.Date != newDate)
        {
            if (transactions.IsSequence(transaction.Sequence) && TransactionExists(newDate, transaction.Sequence))
            {
                throw new InvalidOperationException("Cannot move an occurrence onto another date in the same sequence.");
            }

            Update(new AccountEvent.TransactionDateChanged(transaction.Id.Date, transaction.Id.Sequence, newDate));
        }
    }

    /// <summary>Changes the description of an existing transaction.</summary>
    public void ChangeTransactionDescription(Transaction transaction, string newDescription)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(newDescription);
        ThrowIfBeforeEarliestDate(transaction);
        ThrowIfTransactionDoesNotExist(transaction);

        if (transaction.Description != newDescription)
        {
            Update(new AccountEvent.TransactionDescriptionChanged(transaction.Id.Date, transaction.Id.Sequence, newDescription));
        }
    }

    /// <summary>
    /// Closes the specified month: collapses its transactions into a "Balance carried" opening entry as the
    /// first transaction of the following month, and advances <see cref="EarliestAllowedDate"/>.
    /// </summary>
    /// <remarks>
    /// Only the oldest month with content, and never the month containing <paramref name="today"/> or a later
    /// one, may be closed. Closing also bounds balance computation: the carried-balance anchor is the point
    /// every later balance is computed forward from.
    /// </remarks>
    public void CloseMonth(MonthOnly month, DateOnly today)
    {
        if (ignoreMonthClosed)
        {
            throw new InvalidOperationException("Cannot close a month while closed months are being shown.");
        }

        var currentMonth = MonthOnly.FromDate(today);
        if (month.CompareTo(currentMonth) >= 0 || transactions.EarliestContentMonth() != month)
        {
            throw new InvalidOperationException("Cannot close month.");
        }

        Update(new AccountEvent.MonthClosed(month));
    }

    /// <summary>
    /// Removes a repeating sequence from <paramref name="fromDate"/> onwards. If <paramref name="fromDate"/>
    /// equals the origin, the sequence itself is removed.
    /// </summary>
    public void RemoveSequence(Transaction transaction, DateOnly fromDate)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ThrowIfDateTooEarly(fromDate, nameof(fromDate));
        var sequenceRule = GetSequenceOrThrow(transaction);

        Update(new AccountEvent.SequenceRemoved(sequenceRule.Origin, sequenceRule.Number, fromDate));
    }

    /// <summary>Removes an existing transaction from the account.</summary>
    public void RemoveTransaction(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ThrowIfBeforeEarliestDate(transaction);
        ThrowIfTransactionDoesNotExist(transaction);

        Update(new AccountEvent.TransactionRemoved(transaction.Id.Date, transaction.Id.Sequence));
    }

    /// <summary>Sets the account's display name.</summary>
    public void SetName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!string.Equals(Name, name, StringComparison.OrdinalIgnoreCase))
        {
            Update(new AccountEvent.NameSet(name));
        }
    }

    private void Apply(AccountEvent e)
    {
        switch (e)
        {
            case AccountEvent.NameSet nameSet:
                Name = nameSet.Name;
                break;
            case AccountEvent.TransactionAdded added:
                ApplyAddTransaction(added);
                break;
            case AccountEvent.TransactionAmountChanged amountChanged:
                transactions.ChangeAmount(new TransactionId(amountChanged.Date, amountChanged.Sequence), amountChanged.NewAmount);
                break;
            case AccountEvent.TransactionDateChanged dateChanged:
                transactions.ChangeDate(new TransactionId(dateChanged.Date, dateChanged.Sequence), dateChanged.NewDate);
                break;
            case AccountEvent.TransactionDescriptionChanged descriptionChanged:
                transactions.ChangeDescription(new TransactionId(descriptionChanged.Date, descriptionChanged.Sequence), descriptionChanged.NewDescription);
                break;
            case AccountEvent.TransactionRemoved removed:
                transactions.RemoveTransaction(new TransactionId(removed.Date, removed.Sequence));
                break;
            case AccountEvent.MonthClosed monthClosed:
                transactions.MonthClosed(monthClosed.Month);
                break;
            case AccountEvent.SequenceRemoved sequenceRemoved:
                transactions.RemoveSequenceFrom(sequenceRemoved.Sequence, sequenceRemoved.FromDate);
                break;
            case AccountEvent.SequenceAmountChanged sequenceAmountChanged:
                transactions.ChangeSequenceAmount(sequenceAmountChanged.Sequence, sequenceAmountChanged.NewAmount);
                break;
            case AccountEvent.SequenceDescriptionChanged sequenceDescriptionChanged:
                transactions.ChangeSequenceDescription(sequenceDescriptionChanged.Sequence, sequenceDescriptionChanged.NewDescription);
                break;
        }
    }

    private void ApplyAddTransaction(AccountEvent.TransactionAdded added)
    {
        var id = new TransactionId(added.Date, added.Sequence);
        if (added.Strategy is { } strategy and not RepeatStrategy.Never)
        {
            transactions.AddSequence(new Sequence(id, added.Amount, added.Description,
                strategy, added.EndCondition ?? new RepeatEndCondition.Forever()));
        }
        else
        {
            // A null or explicit-Never strategy is a one-off fact, not a repeating sequence.
            transactions.AddFact(new Transaction(id, added.Amount, added.Description));
        }

        sequence = added.Sequence;
    }

    private Transaction FindTransaction(TransactionId id) =>
        transactions.Find(id) ?? throw new InvalidOperationException("Transaction not found.");

    /// <summary>
    /// Truncates a sequence at <paramref name="fromDate"/> (removing it entirely when that is the origin) and
    /// starts a replacement sequence at <paramref name="fromDate"/> carrying the same schedule. The single owner
    /// of the sequence-split decomposition.
    /// </summary>
    private void SplitSequence(Sequence sequenceRule, DateOnly fromDate, decimal amount, string description)
    {
        Update(new AccountEvent.SequenceRemoved(sequenceRule.Origin, sequenceRule.Number, fromDate));
        Update(new AccountEvent.TransactionAdded(fromDate, sequence + 1, amount, description,
            sequenceRule.Strategy, sequenceRule.EndCondition));
    }

    private bool TransactionExists(DateOnly date, int sequence) =>
        transactions.Find(new TransactionId(date, sequence)) is not null;

    private void Update(AccountEvent e)
    {
        NewEvent?.Invoke(this, e);
        Apply(e);
    }

    private void ThrowIfDateTooEarly(DateOnly date, string paramName)
    {
        if (date < EarliestAllowedDate)
        {
            throw new ArgumentOutOfRangeException(paramName, date,
                $"Date cannot be before the earliest allowed date ({EarliestAllowedDate:yyyy-MM-dd}).");
        }
    }

    private void ThrowIfBeforeEarliestDate(Transaction transaction)
    {
        if (transaction.Id.Date < EarliestAllowedDate)
        {
            throw new ArgumentOutOfRangeException(nameof(transaction), transaction.Id.Date,
                $"Cannot change transaction before earliest allowed date ({EarliestAllowedDate:yyyy-MM-dd}).");
        }
    }

    private void ThrowIfTransactionDoesNotExist(Transaction transaction)
    {
        if (transactions.Find(transaction.Id) is null)
        {
            throw new InvalidOperationException("The specified transaction does not exist.");
        }
    }

    private Sequence GetSequenceOrThrow(Transaction transaction)
    {
        if (transactions.Find(transaction.Id) is null)
        {
            throw new InvalidOperationException("The specified transaction does not exist.");
        }

        return transactions.FindSequence(transaction.Sequence)
            ?? throw new InvalidOperationException("The specified transaction is not part of a sequence.");
    }
}
