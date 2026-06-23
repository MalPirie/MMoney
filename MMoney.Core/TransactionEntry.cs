namespace MMoney.Core;

/// <summary>
/// An entry physically held in a month partition. In the sparse-overlay model a partition stores only
/// <em>facts</em> and <em>overrides</em>; unedited occurrences are projected on read and never stored.
/// </summary>
internal abstract record TransactionEntry
{
    private TransactionEntry() { }

    public abstract TransactionId Id { get; }

    /// <summary>
    /// A concrete transaction present at <see cref="Transaction.Id"/>: a one-off, a carried-balance anchor,
    /// or an occurrence that has been individually edited or moved here. Overrides any projection at that id.
    /// </summary>
    public sealed record Stored(Transaction Transaction) : TransactionEntry
    {
        public override TransactionId Id => Transaction.Id;
    }

    /// <summary>
    /// Suppresses a projected occurrence at <see cref="Deleted"/> — a skip, or the vacated origin of a moved
    /// occurrence — so the projection does not reappear on read.
    /// </summary>
    public sealed record Tombstone(TransactionId Deleted) : TransactionEntry
    {
        public override TransactionId Id => Deleted;
    }
}
