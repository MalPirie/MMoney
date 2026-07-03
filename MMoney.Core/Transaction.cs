namespace MMoney.Core;

/// <summary>Uniquely identifies a transaction by its date and a per-account sequence number.</summary>
/// <param name="Date">The date on which the transaction occurred.</param>
/// <param name="Sequence">A 1-based sequence number incremented per account for each new transaction
/// (0 is reserved for the "Balance carried" opening entry).</param>
public readonly record struct TransactionId(DateOnly Date, int Sequence) : IComparable<TransactionId>
{
    /// <summary>Compares first by date, then by sequence number.</summary>
    public int CompareTo(TransactionId other) =>
        Date != other.Date
            ? Date.CompareTo(other.Date)
            : Sequence.CompareTo(other.Sequence);
}

/// <summary>
/// An immutable, value-typed representation of a single financial transaction — either a one-off fact or a
/// projected occurrence of a <see cref="Sequence"/>. It carries no running balance and no repeat rule:
/// a balance is a read-time decoration (see <see cref="LedgerEntry"/>), and the repeat rule lives on the
/// owning <see cref="Sequence"/>.
/// </summary>
/// <remarks>
/// Equality and ordering are based solely on <see cref="Id"/>; <see cref="Amount"/> and
/// <see cref="Description"/> are not considered.
/// </remarks>
public sealed record Transaction(TransactionId Id, decimal Amount, string Description)
    : IComparable<Transaction>
{
    /// <summary>Creates a transaction using discrete date and sequence values.</summary>
    public Transaction(DateOnly date, int sequence, decimal amount, string description)
        : this(new TransactionId(date, sequence), amount, description) { }

    /// <summary>The date on which the transaction occurred.</summary>
    public DateOnly Date => Id.Date;

    /// <summary>The per-account sequence number of this transaction.</summary>
    public int Sequence => Id.Sequence;

    /// <summary>
    /// <see langword="true"/> when this is a "Balance carried" opening entry created by closing a month.
    /// Identified by <see cref="Sequence"/> == 0.
    /// </summary>
    public bool IsCarriedBalance => Sequence == 0;

    /// <inheritdoc cref="IComparable{T}.CompareTo"/>
    public int CompareTo(Transaction? other) => other is null ? 1 : Id.CompareTo(other.Id);

    /// <inheritdoc/>
    public bool Equals(Transaction? other) => other is not null && CompareTo(other) == 0;

    /// <inheritdoc/>
    public override int GetHashCode() => Id.GetHashCode();
}
