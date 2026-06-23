namespace MMoney.Core;

/// <summary>What a <see cref="LedgerEntry"/> represents in the ledger.</summary>
public enum LedgerEntryKind
{
    /// <summary>A standalone, non-repeating transaction.</summary>
    OneOff,

    /// <summary>An occurrence projected from a <see cref="Sequence"/> (possibly individually edited).</summary>
    Occurrence,

    /// <summary>The "Balance carried" opening entry created by closing a month.</summary>
    CarriedBalance,
}

/// <summary>
/// A single row as displayed for a month: a <see cref="Transaction"/> together with its running
/// <see cref="Balance"/>. The balance is computed at read time and is valid only on entries returned by the
/// month view — it is the one place a running balance is ever manifested.
/// </summary>
/// <param name="Transaction">The underlying transaction (one-off, occurrence, or carried-balance entry).</param>
/// <param name="Balance">The running balance up to and including this transaction.</param>
/// <param name="Kind">Whether the row is a one-off, a sequence occurrence, or a carried balance.</param>
/// <remarks>
/// When <see cref="Kind"/> is <see cref="LedgerEntryKind.Occurrence"/>, the owning <see cref="Sequence"/>'s
/// number is the transaction's own <see cref="MMoney.Core.Transaction.Sequence"/> — occurrences share it —
/// so the rule is resolved with <c>entry.Transaction.Sequence</c>; no separate field is carried.
/// </remarks>
public sealed record LedgerEntry(
    Transaction Transaction,
    decimal Balance,
    LedgerEntryKind Kind)
{
    /// <summary>The date of the underlying transaction.</summary>
    public DateOnly Date => Transaction.Date;

    /// <summary>The signed amount of the underlying transaction.</summary>
    public decimal Amount => Transaction.Amount;

    /// <summary>The description of the underlying transaction.</summary>
    public string Description => Transaction.Description;
}
